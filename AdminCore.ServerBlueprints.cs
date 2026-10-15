using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private const string BlueprintPlacementEvent = "StructureCraftingNodeCreate";
        private int _loaderBlueprintsSeen;
        private int _loaderBlueprintsRejected;
        private int _loaderBlueprintsUnresolved;
        private DateTime _lastLoaderBlueprintUtc = DateTime.MinValue;
        private string _lastLoaderBlueprintDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderBlueprintsSubscribed { get; private set; }
        internal bool LoaderBlueprintHookInstalled => LoaderBlueprintsSubscribed
            && OpenACAISonsServerConstruction.State.BlueprintPlacementStatus == OpenACAISonsConstructionHookStatus.Installed;
        internal string LoaderBlueprintStatus => LoaderBlueprintsSubscribed
            ? OpenACAISonsServerConstruction.State.BlueprintPlacementStatus.ToString() : "Not subscribed";

        private void InitializeLoaderBlueprintIntegration()
        {
            if (LoaderBlueprintsSubscribed) return;
            OpenACAISonsServerConstruction.BlueprintPlacing += ReceiveLoaderBlueprint;
            LoaderBlueprintsSubscribed = true;
        }

        private void StopLoaderBlueprintIntegration()
        {
            if (!LoaderBlueprintsSubscribed) return;
            LoaderBlueprintsSubscribed = false;
            OpenACAISonsServerConstruction.BlueprintPlacing -= ReceiveLoaderBlueprint;
        }

        private void ReceiveLoaderBlueprint(OpenACAISonsBlueprintPlacementRequest request)
        {
            if (!LoaderBlueprintHookInstalled || request == null || request.IsRejected) return;
            _loaderBlueprintsSeen = IncrementRequestCount(_loaderBlueprintsSeen);
            _lastLoaderBlueprintUtc = request.ReceivedAt.UtcDateTime;
            if (!_disallowCheats || !HasHostSession())
            {
                _lastLoaderBlueprintDecision = "Native handling: guard is off or no local host world is available";
                return;
            }
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderBlueprintDecision = "Native handling: server-local request";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderBlueprint("Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderBlueprint("Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderBlueprint("Source Steam64 ID is unavailable");
                return;
            }
            if (!TryDescribeLoaderBlueprint(request, out string descriptor, out string detail))
            {
                DeferLoaderBlueprint(detail);
                return;
            }

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            bool roleExempt = IsSteamIdAdminOrOwner(steamId, entry);
            if (roleExempt || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderBlueprintDecision(request, steamId, descriptor, false,
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
                blocked = TryLoaderBlueprintRangeViolation(request, identity.GameSteamId,
                    out reason, out validation, out bool unresolved);
                if (unresolved) _loaderBlueprintsUnresolved = IncrementRequestCount(_loaderBlueprintsUnresolved);
            }
            // Blueprint requests are world mutations, not item acquisitions or resource quantities.
            if (!blocked)
            {
                RecordLoaderBlueprintDecision(request, steamId, descriptor, false, "allowed", validation);
                return;
            }
            if (LearnedPolicyAllowsBlockedEvent(steamId, BlueprintPlacementEvent, reason, descriptor, out var learnedNote))
            {
                RecordLoaderBlueprintDecision(request, steamId, descriptor, false, "learned allow: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Allowed " + BlueprintPlacementEvent + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy allowed " + BlueprintPlacementEvent + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderBlueprint("The rejection window has closed");
                return;
            }
            _loaderBlueprintsRejected = IncrementRequestCount(_loaderBlueprintsRejected);

            if (LearnedPolicySuppressesSelfAdmin(steamId, BlueprintPlacementEvent, reason, descriptor, out learnedNote))
            {
                RecordLoaderBlueprintDecision(request, steamId, descriptor, true, "learned review: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Blocked without punishment " + BlueprintPlacementEvent + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy suppressed self-admin action for " + BlueprintPlacementEvent + " from " + steamId + ": " + learnedNote);
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, BlueprintPlacementEvent, reason, descriptor, out learnedNote))
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);

            RecordLoaderBlueprintDecision(request, steamId, descriptor, true, reason, validation);
            AnnounceGuardBlock(entry, steamId, BlueprintPlacementEvent, reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat " + BlueprintPlacementEvent, false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", null, null);
            }
            LogWarning("Anti-cheat blocked " + BlueprintPlacementEvent + " from " + steamId + " (" + reason + ")");
        }

        private static bool TryLoaderBlueprintRangeViolation(OpenACAISonsBlueprintPlacementRequest request, ulong steamId,
            out string reason, out string validation, out bool unresolved)
        {
            reason = "";
            unresolved = false;
            var raw = request.RequestedPosition;
            if (!IsFiniteEventPosition(new Vector3(raw.X, raw.Y, raw.Z)))
            {
                reason = BlueprintPlacementEvent + " contains a nonfinite payload Position";
                validation = "Rejected nonfinite payload; native range check not run";
                return true;
            }
            if (!OpenACAISonsServerWorld.TryResolveBlueprintPose(request, out var pose, out var poseStatus))
            {
                validation = "Unavailable: blueprint world pose " + poseStatus;
                unresolved = true;
                return false;
            }

            string source = pose.ParentNetworkId == 0 ? "unparented blueprint Position" : "parent-transformed blueprint Position";
            var position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            int distance = GetServerValidationDistance(BlueprintPlacementEvent);
            var result = OpenACAISonsServerWorld.ValidatePositionRange(request.SourceSession, steamId, pose.Position, distance);
            if (result.Status != OpenACAISonsNativeRangeStatus.Accepted && result.Status != OpenACAISonsNativeRangeStatus.Rejected)
            {
                validation = "Unavailable: SDK native range " + result.Status;
                unresolved = true;
                return false;
            }
            bool rejected = result.Status == OpenACAISonsNativeRangeStatus.Rejected;
            validation = (rejected ? "Native range check rejected" : "Native range check accepted")
                + "; " + source + " " + FormatVector(position) + "; limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            if (rejected)
                reason = BlueprintPlacementEvent + " rejected by native range validation at " + FormatVector(position)
                    + " via " + source + ", limit " + distance.ToString(CultureInfo.InvariantCulture) + "m";
            return rejected;
        }

        private static bool TryDescribeLoaderBlueprint(OpenACAISonsBlueprintPlacementRequest request,
            out string descriptor, out string detail)
        {
            descriptor = "";
            detail = "Legacy blueprint prefab description is unavailable";
            try
            {
                // Saved rules include the native PrefabId type and text, not just its integer value.
                Type prefabType = ReflectionTools.FindType("Bolt.PrefabId");
                FieldInfo valueField = prefabType?.GetField("Value", BindingFlags.Public | BindingFlags.Instance);
                if (prefabType == null || !prefabType.IsValueType || valueField?.FieldType != typeof(int)) return false;
                object prefab = Activator.CreateInstance(prefabType);
                valueField.SetValue(prefab, request.RequestedPrefabId);
                var position = request.RequestedPosition;
                var parts = new List<string>();
                AppendEventField(parts, "PrefabId", prefab);
                AppendEventField(parts, "Position", new Vector3(position.X, position.Y, position.Z));
                descriptor = BlueprintPlacementEvent + " " + String.Join(", ", parts);
                return true;
            }
            catch (Exception ex)
            {
                detail += " (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private void RecordLoaderBlueprintDecision(OpenACAISonsBlueprintPlacementRequest request, string steamId,
            string descriptor, bool blocked, string reason, string validation)
        {
            _lastLoaderBlueprintDecision = CleanConfigText((blocked ? "Blocked: " : "Native handling: ") + reason + "; " + validation, 320);
            RecordInventorySignal(steamId, BlueprintPlacementEvent, 0, 0,
                DescribeItemEventDetails(BlueprintPlacementEvent, descriptor, 0, 0, request.RequestedPrefabId),
                CaptureEventGuardPolicy(BlueprintPlacementEvent, null), "Loader blueprint placement / exact session",
                blocked, reason, validation);
        }

        private void DeferLoaderBlueprint(string detail)
        {
            _loaderBlueprintsUnresolved = IncrementRequestCount(_loaderBlueprintsUnresolved);
            _lastLoaderBlueprintDecision = "Native handling: " + detail + "; no Admin punishment";
        }
#else
        internal bool LoaderBlueprintsSubscribed => false;
        internal bool LoaderBlueprintHookInstalled => false;
        internal string LoaderBlueprintStatus => "Unsupported target";
#endif

        private void ResetLoaderBlueprintEvidence()
        {
            _loaderBlueprintsSeen = 0;
            _loaderBlueprintsRejected = 0;
            _loaderBlueprintsUnresolved = 0;
            _lastLoaderBlueprintUtc = DateTime.MinValue;
            _lastLoaderBlueprintDecision = "No request observed";
        }
    }
}
