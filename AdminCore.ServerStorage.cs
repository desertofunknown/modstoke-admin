using System;
using System.Globalization;
using UnityEngine;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderStorageAddsSeen;
        private int _loaderStorageAddsRejected;
        private int _loaderStorageAddsUnresolved;
        private int _loaderStorageRemovesSeen;
        private int _loaderStorageRemovesRejected;
        private int _loaderStorageRemovesUnresolved;
        private DateTime _lastLoaderStorageAddUtc = DateTime.MinValue;
        private DateTime _lastLoaderStorageRemoveUtc = DateTime.MinValue;
        private string _lastLoaderStorageAddDecision = "No request observed";
        private string _lastLoaderStorageRemoveDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderStorageSubscribed { get; private set; }
        internal bool LoaderStorageAddHookInstalled => LoaderStorageSubscribed
            && OpenACAISonsServerStorage.State.AddStatus == OpenACAISonsStorageHookStatus.Installed;
        internal bool LoaderStorageRemoveHookInstalled => LoaderStorageSubscribed
            && OpenACAISonsServerStorage.State.RemoveStatus == OpenACAISonsStorageHookStatus.Installed;
        internal string LoaderStorageAddStatus => LoaderStorageSubscribed
            ? OpenACAISonsServerStorage.State.AddStatus.ToString() : "Not subscribed";
        internal string LoaderStorageRemoveStatus => LoaderStorageSubscribed
            ? OpenACAISonsServerStorage.State.RemoveStatus.ToString() : "Not subscribed";

        private void InitializeLoaderStorageIntegration()
        {
            if (LoaderStorageSubscribed) return;
            OpenACAISonsServerStorage.Receiving += ReceiveLoaderStorage;
            LoaderStorageSubscribed = true;
        }

        private void StopLoaderStorageIntegration()
        {
            if (!LoaderStorageSubscribed) return;
            LoaderStorageSubscribed = false;
            OpenACAISonsServerStorage.Receiving -= ReceiveLoaderStorage;
        }

        private void ReceiveLoaderStorage(OpenACAISonsStorageRequest request)
        {
            if (!LoaderStorageSubscribed || request == null || request.IsRejected) return;
            bool adding;
            string eventName;
            switch (request.Operation)
            {
                case OpenACAISonsStorageOperation.Add:
                    if (!LoaderStorageAddHookInstalled) return;
                    adding = true;
                    eventName = "ItemStorageAddItemEvent";
                    _loaderStorageAddsSeen = IncrementRequestCount(_loaderStorageAddsSeen);
                    _lastLoaderStorageAddUtc = request.ReceivedAt.UtcDateTime;
                    break;
                case OpenACAISonsStorageOperation.Remove:
                    if (!LoaderStorageRemoveHookInstalled) return;
                    adding = false;
                    eventName = "ItemStorageRemoveItemEvent";
                    _loaderStorageRemovesSeen = IncrementRequestCount(_loaderStorageRemovesSeen);
                    _lastLoaderStorageRemoveUtc = request.ReceivedAt.UtcDateTime;
                    break;
                default:
                    return;
            }

            bool unresolved = false;
            if (!_disallowCheats || !HasHostSession())
            {
                SetLoaderStorageDecision(adding, "Native handling: guard is off or no local host world is available");
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                SetLoaderStorageDecision(adding, "Native handling: server-local request");
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderStorage(adding, "Source identity " + readStatus, ref unresolved);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderStorage(adding, "Source identity and registration disagree", ref unresolved);
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderStorage(adding, "Source Steam64 ID is unavailable", ref unresolved);
                return;
            }
            int itemId = Math.Max(0, request.RequestedInstanceItemId.GetValueOrDefault());
            int count = Math.Max(0, request.RequestedCount);
            string descriptor = DescribeStorageItemEvent(eventName, itemId, count, request.RequestedCount, request.RequestedItemId);
            PlayerEntry entry = FindPlayerBySteamId(steamId);
            bool roleExempt = IsSteamIdAdminOrOwner(steamId, entry);
            if (roleExempt || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderStorageDecision(adding, eventName, steamId, itemId, count, descriptor, false,
                    roleExempt ? "admin/owner exemption" : "configured exemption", "Not evaluated");
                return;
            }

            string reason = "";
            string validation = _nativeServerValidationGuard ? "Not evaluated" : "Disabled";
            bool blocked = _jailedWorldEventGuard && IsJailedSteamId(steamId);
            if (blocked)
                reason = "Jailed player attempted blocked server event: " + descriptor;
            else if (_nativeServerValidationGuard)
            {
                blocked = TryLoaderStorageRangeViolation(request, eventName, identity.GameSteamId,
                    out reason, out validation, out bool rangeUnavailable);
                if (rangeUnavailable) MarkLoaderStorageUnresolved(adding, ref unresolved);
            }

            bool ReadStoryPosition(out Vector3 position, out string source)
            {
                bool available = TryReadLoaderStoragePosition(request, out position, out source);
                if (!available) MarkLoaderStorageUnresolved(adding, ref unresolved);
                return available;
            }

            if (!blocked && _strictItemEventGuard)
            {
                PlayerEntry policyEntry = entry;
                if (policyEntry == null && _registeredPlayers.TryGetValue(steamId, out var registered)
                    && registered != null && registered.FirstSeenUtc != DateTime.MinValue)
                {
                    // The private event path uses zero age for an attached sender missing from the roster.
                    if (OpenACAISonsServerWorld.TryReadPlayer(request.SourceSession, out _, out _))
                        policyEntry = new PlayerEntry();
                    else
                        MarkLoaderStorageUnresolved(adding, ref unresolved);
                }
                blocked = ShouldBlockItemPolicy(eventName, steamId, policyEntry, itemId, count, descriptor,
                    request.RequestedCount < 0, ReadStoryPosition, out reason);
            }
            if (!blocked)
            {
                RecordLoaderStorageDecision(adding, eventName, steamId, itemId, count, descriptor, false, "allowed", validation);
                return;
            }
            if (LearnedPolicyAllowsBlockedEvent(steamId, eventName, reason, descriptor, out var learnedNote))
            {
                RecordLoaderStorageDecision(adding, eventName, steamId, itemId, count, descriptor, false, "learned allow: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Allowed " + eventName + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy allowed " + eventName + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderStorage(adding, "The rejection window has closed", ref unresolved);
                return;
            }
            if (adding)
                _loaderStorageAddsRejected = IncrementRequestCount(_loaderStorageAddsRejected);
            else
                _loaderStorageRemovesRejected = IncrementRequestCount(_loaderStorageRemovesRejected);

            if (LearnedPolicySuppressesSelfAdmin(steamId, eventName, reason, descriptor, out learnedNote))
            {
                RecordLoaderStorageDecision(adding, eventName, steamId, itemId, count, descriptor, true, "learned review: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Blocked without punishment " + eventName + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy suppressed self-admin action for " + eventName + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, eventName, reason, descriptor, out learnedNote))
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);

            RecordLoaderStorageDecision(adding, eventName, steamId, itemId, count, descriptor, true, reason, validation);
            AnnounceGuardBlock(entry, steamId, eventName, reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat " + eventName, false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", null, null);
            }
            LogWarning("Anti-cheat blocked " + eventName + " from " + steamId + " (" + reason + ")");
        }

        private static bool TryLoaderStorageRangeViolation(OpenACAISonsStorageRequest request, string eventName,
            ulong steamId, out string reason, out string validation, out bool unresolved)
        {
            reason = "";
            unresolved = false;
            if (!OpenACAISonsServerWorld.TryReadEntity(request.RequestedTargetNetworkId, out var target, out var readStatus))
            {
                validation = "Unavailable: live Target " + readStatus;
                unresolved = true;
                return false;
            }

            // This checks a copied target point and exact source, not storage ownership or a completed transfer.
            int distance = GetServerValidationDistance(eventName);
            var result = OpenACAISonsServerWorld.ValidatePositionRange(request.SourceSession, steamId, target.Position, distance);
            if (result.Status != OpenACAISonsNativeRangeStatus.Accepted && result.Status != OpenACAISonsNativeRangeStatus.Rejected)
            {
                validation = "Unavailable: SDK native range " + result.Status;
                unresolved = true;
                return false;
            }
            var position = new Vector3(target.Position.X, target.Position.Y, target.Position.Z);
            bool rejected = result.Status == OpenACAISonsNativeRangeStatus.Rejected;
            validation = (rejected ? "Native range check rejected" : "Native range check accepted")
                + "; live Target " + FormatVector(position) + "; limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            if (rejected)
                reason = eventName + " rejected by native range validation at " + FormatVector(position)
                    + " via live Target, limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            return rejected;
        }

        private static bool TryReadLoaderStoragePosition(OpenACAISonsStorageRequest request, out Vector3 position, out string source)
        {
            position = Vector3.zero;
            source = "live Target";
            if (!OpenACAISonsServerWorld.TryReadEntity(request.RequestedTargetNetworkId, out var target, out _)) return false;
            position = new Vector3(target.Position.X, target.Position.Y, target.Position.Z);
            return IsFiniteEventPosition(position);
        }

        private void SetLoaderStorageDecision(bool adding, string decision)
        {
            if (adding)
                _lastLoaderStorageAddDecision = CleanConfigText(decision, 320);
            else
                _lastLoaderStorageRemoveDecision = CleanConfigText(decision, 320);
        }

        private void MarkLoaderStorageUnresolved(bool adding, ref bool unresolved)
        {
            if (unresolved) return;
            unresolved = true;
            if (adding)
                _loaderStorageAddsUnresolved = IncrementRequestCount(_loaderStorageAddsUnresolved);
            else
                _loaderStorageRemovesUnresolved = IncrementRequestCount(_loaderStorageRemovesUnresolved);
        }

        private void DeferLoaderStorage(bool adding, string detail, ref bool unresolved)
        {
            MarkLoaderStorageUnresolved(adding, ref unresolved);
            SetLoaderStorageDecision(adding, "Native handling: " + detail + "; no Admin punishment");
        }

        private void RecordLoaderStorageDecision(bool adding, string eventName, string steamId, int itemId, int count,
            string descriptor, bool blocked, string reason, string validation)
        {
            SetLoaderStorageDecision(adding, (blocked ? "Blocked: " : "Native handling: ") + reason + "; " + validation);
            RecordInventorySignal(steamId, eventName, itemId, count, descriptor,
                CaptureEventGuardPolicy(eventName, null), "Loader storage / exact session", blocked, reason, validation);
        }
#else
        internal bool LoaderStorageSubscribed => false;
        internal bool LoaderStorageAddHookInstalled => false;
        internal bool LoaderStorageRemoveHookInstalled => false;
        internal string LoaderStorageAddStatus => "Unsupported target";
        internal string LoaderStorageRemoveStatus => "Unsupported target";
#endif

        private void ResetLoaderStorageEvidence()
        {
            _loaderStorageAddsSeen = 0;
            _loaderStorageAddsRejected = 0;
            _loaderStorageAddsUnresolved = 0;
            _loaderStorageRemovesSeen = 0;
            _loaderStorageRemovesRejected = 0;
            _loaderStorageRemovesUnresolved = 0;
            _lastLoaderStorageAddUtc = DateTime.MinValue;
            _lastLoaderStorageRemoveUtc = DateTime.MinValue;
            _lastLoaderStorageAddDecision = "No request observed";
            _lastLoaderStorageRemoveDecision = "No request observed";
        }
    }
}
