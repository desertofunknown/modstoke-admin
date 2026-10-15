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
        private const string StructureRelocationEvent = "DismantleScrewStructure";
        private int _loaderStructureRelocationsSeen;
        private int _loaderStructureRelocationsRejected;
        private int _loaderStructureRelocationsUnresolved;
        private DateTime _lastLoaderStructureRelocationUtc = DateTime.MinValue;
        private string _lastLoaderStructureRelocationDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderStructureRelocationSubscribed { get; private set; }
        internal bool LoaderStructureRelocationHookInstalled => LoaderStructureRelocationSubscribed
            && OpenACAISonsServerConstruction.State.StructureRelocationStatus == OpenACAISonsConstructionHookStatus.Installed;
        internal string LoaderStructureRelocationStatus => LoaderStructureRelocationSubscribed
            ? OpenACAISonsServerConstruction.State.StructureRelocationStatus.ToString() : "Not subscribed";

        private void InitializeLoaderStructureRelocationIntegration()
        {
            if (LoaderStructureRelocationSubscribed) return;
            OpenACAISonsServerConstruction.StructureRelocating += ReceiveLoaderStructureRelocation;
            LoaderStructureRelocationSubscribed = true;
        }

        private void StopLoaderStructureRelocationIntegration()
        {
            if (!LoaderStructureRelocationSubscribed) return;
            LoaderStructureRelocationSubscribed = false;
            OpenACAISonsServerConstruction.StructureRelocating -= ReceiveLoaderStructureRelocation;
        }

        private void ReceiveLoaderStructureRelocation(OpenACAISonsStructureRelocationRequest request)
        {
            if (!LoaderStructureRelocationHookInstalled || request == null || request.IsRejected) return;
            _loaderStructureRelocationsSeen = IncrementRequestCount(_loaderStructureRelocationsSeen);
            _lastLoaderStructureRelocationUtc = request.ReceivedAt.UtcDateTime;
            if (!_disallowCheats || !HasHostSession())
            {
                _lastLoaderStructureRelocationDecision = "Native handling: guard is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderStructureRelocationDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderStructureRelocation("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderStructureRelocation("Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderStructureRelocation("Source Steam64 ID is unavailable");
                return;
            }
            if (!TryDescribeLoaderConstructionTarget(StructureRelocationEvent, "TargetStructure",
                request.RequestedTargetNetworkId, null, out string descriptor, out string detail))
            {
                DeferLoaderStructureRelocation(detail);
                return;
            }

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            bool roleExempt = IsSteamIdAdminOrOwner(steamId, entry);
            if (roleExempt || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderStructureRelocationDecision(steamId, descriptor, false,
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
                blocked = TryLoaderStructureRelocationRangeViolation(request, identity.GameSteamId,
                    out reason, out validation, out bool unresolved);
                if (unresolved) _loaderStructureRelocationsUnresolved = IncrementRequestCount(_loaderStructureRelocationsUnresolved);
            }
            if (!blocked)
            {
                RecordLoaderStructureRelocationDecision(steamId, descriptor, false, "allowed", validation);
                return;
            }
            if (LearnedPolicyAllowsBlockedEvent(steamId, StructureRelocationEvent, reason, descriptor, out var learnedNote))
            {
                RecordLoaderStructureRelocationDecision(steamId, descriptor, false, "learned allow: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Allowed " + StructureRelocationEvent + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy allowed " + StructureRelocationEvent + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderStructureRelocation("The rejection window has closed");
                return;
            }
            _loaderStructureRelocationsRejected = IncrementRequestCount(_loaderStructureRelocationsRejected);

            if (LearnedPolicySuppressesSelfAdmin(steamId, StructureRelocationEvent, reason, descriptor, out learnedNote))
            {
                RecordLoaderStructureRelocationDecision(steamId, descriptor, true, "learned review: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Blocked without punishment " + StructureRelocationEvent + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy suppressed self-admin action for " + StructureRelocationEvent + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, StructureRelocationEvent, reason, descriptor, out learnedNote))
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);

            RecordLoaderStructureRelocationDecision(steamId, descriptor, true, reason, validation);
            AnnounceGuardBlock(entry, steamId, StructureRelocationEvent, reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat " + StructureRelocationEvent, false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", null, null);
            }
            LogWarning("Anti-cheat blocked " + StructureRelocationEvent + " from " + steamId + " (" + reason + ")");
        }

        private static bool TryLoaderStructureRelocationRangeViolation(OpenACAISonsStructureRelocationRequest request,
            ulong steamId, out string reason, out string validation, out bool unresolved)
        {
            reason = "";
            unresolved = false;
            if (!OpenACAISonsServerWorld.TryReadEntity(request.RequestedTargetNetworkId, out var target, out var readStatus))
            {
                validation = "Unavailable: live TargetStructure " + readStatus;
                unresolved = true;
                return false;
            }

            // The native validator checks this copied point and exact source, not target ownership or relocation success.
            int distance = GetServerValidationDistance(StructureRelocationEvent);
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
                + "; live TargetStructure " + FormatVector(position) + "; limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            if (rejected)
                reason = StructureRelocationEvent + " rejected by native range validation at " + FormatVector(position)
                    + " via live TargetStructure, limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            return rejected;
        }

        private void RecordLoaderStructureRelocationDecision(string steamId, string descriptor,
            bool blocked, string reason, string validation)
        {
            _lastLoaderStructureRelocationDecision = CleanConfigText((blocked ? "Blocked: " : "Native handling: ") + reason + "; " + validation, 320);
            RecordInventorySignal(steamId, StructureRelocationEvent, 0, 0,
                DescribeItemEventDetails(StructureRelocationEvent, descriptor, 0, 0, 0),
                CaptureEventGuardPolicy(StructureRelocationEvent, null), "Loader structure relocation / exact session",
                blocked, reason, validation);
        }

        private void DeferLoaderStructureRelocation(string detail)
        {
            _loaderStructureRelocationsUnresolved = IncrementRequestCount(_loaderStructureRelocationsUnresolved);
            _lastLoaderStructureRelocationDecision = "Native handling: " + detail + "; no Admin punishment";
        }
#else
        internal bool LoaderStructureRelocationSubscribed => false;
        internal bool LoaderStructureRelocationHookInstalled => false;
        internal string LoaderStructureRelocationStatus => "Unsupported target";
#endif

        private void ResetLoaderStructureRelocationEvidence()
        {
            _loaderStructureRelocationsSeen = 0;
            _loaderStructureRelocationsRejected = 0;
            _loaderStructureRelocationsUnresolved = 0;
            _lastLoaderStructureRelocationUtc = DateTime.MinValue;
            _lastLoaderStructureRelocationDecision = "No request observed";
        }
    }
}
