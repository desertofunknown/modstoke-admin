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
        private static float StructureHitRange(float radius)
        {
            return Math.Max(18f, Math.Min(90f, (radius > 0f ? radius : 5f) + 35f));
        }

        private static bool TryStructureHitPayloadViolation(float damage, float radius, Vector3? center,
            out string reason, out string validation)
        {
            reason = "";
            validation = "Not evaluated";
            if (!IsFiniteDebugValue(damage) || !IsFiniteDebugValue(radius))
            {
                reason = "Structure hit contains nonfinite damage or radius";
                validation = "Rejected nonfinite payload; native range check not run";
                return true;
            }
            if (damage > 250f || radius > 50f)
            {
                reason = damage > 250f ? "Structure hit damage over limit" : "Structure hit radius over limit";
                validation = "Rejected structure limits; native range check not run";
                return true;
            }
            if (center.HasValue && !IsFiniteEventPosition(center.Value))
            {
                reason = "Structure hit contains a nonfinite center";
                validation = "Rejected nonfinite payload; native range check not run";
                return true;
            }
            return false;
        }

#if NET11_0_OR_GREATER
        private void ValidateLoaderStructureHit(OpenACAISonsStructureHitRequest request,
            OpenACAISonsServerIdentitySnapshot identity, string steamId, ref bool unresolved)
        {
            if (!TryDescribeLoaderStructureHit(request, out string descriptor, out string detail))
            {
                DeferLoaderStructureHit(detail, ref unresolved);
                return;
            }

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            bool roleExempt = IsSteamIdAdminOrOwner(steamId, entry);
            if (roleExempt || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordLoaderStructureValidation(request, steamId, descriptor, false,
                    roleExempt ? "admin/owner exemption" : "configured exemption", "Not evaluated");
                return;
            }

            var center = request.RequestedCenter;
            bool blocked = TryStructureHitPayloadViolation(request.RequestedDamage, request.RequestedRadius,
                new Vector3(center.X, center.Y, center.Z), out string reason, out string validation);
            if (!blocked)
            {
                float range = StructureHitRange(request.RequestedRadius);
                var result = OpenACAISonsServerWorld.ValidateStructureHitRange(request.SourceSession,
                    identity.GameSteamId, request.RequestedTargetNetworkId, range);
                if (result.Status != OpenACAISonsNativeRangeStatus.Accepted
                    && result.Status != OpenACAISonsNativeRangeStatus.Rejected)
                {
                    validation = "Unavailable: SDK native range " + result.Status;
                    DeferLoaderStructureHit(validation, ref unresolved);
                    RecordLoaderStructureValidation(request, steamId, descriptor, false, "allowed", validation);
                    return;
                }
                blocked = result.Status == OpenACAISonsNativeRangeStatus.Rejected;
                validation = (blocked ? "Native range check rejected" : "Native range check accepted")
                    + "; exact source session and live Target; limit " + range.ToString("0.#", CultureInfo.InvariantCulture) + "m";
                if (blocked) reason = "Structure hit rejected by native range validation";
            }
            if (!blocked)
            {
                RecordLoaderStructureValidation(request, steamId, descriptor, false, "allowed", validation);
                return;
            }

            // Saved native-validation rules use the legacy descriptor, not the jail-lock descriptor.
            if (LearnedPolicyAllowsBlockedEvent(steamId, "StructureHitEvent", reason, descriptor, out var learnedNote))
            {
                RecordLoaderStructureValidation(request, steamId, descriptor, false, "learned allow: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Allowed StructureHitEvent for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy allowed StructureHitEvent from " + steamId + ": " + learnedNote);
                return;
            }
            if (!request.Reject())
            {
                DeferLoaderStructureHit("The rejection window has closed", ref unresolved);
                return;
            }
            _loaderStructureHitsRejected = IncrementRequestCount(_loaderStructureHitsRejected);

            if (LearnedPolicySuppressesSelfAdmin(steamId, "StructureHitEvent", reason, descriptor, out learnedNote))
            {
                RecordLoaderStructureValidation(request, steamId, descriptor, true, "learned review: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Blocked without punishment StructureHitEvent for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy suppressed self-admin action for StructureHitEvent from " + steamId + ": " + learnedNote);
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, "StructureHitEvent", reason, descriptor, out learnedNote))
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);

            RecordLoaderStructureValidation(request, steamId, descriptor, true, reason, validation);
            AnnounceGuardBlock(entry, steamId, "StructureHitEvent", reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat StructureHitEvent", false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", null, null);
            }
            LogWarning("Anti-cheat blocked StructureHitEvent from " + steamId + " (" + reason + ")");
        }

        private static bool TryDescribeLoaderStructureHit(OpenACAISonsStructureHitRequest request,
            out string descriptor, out string detail)
        {
            descriptor = "";
            detail = "Legacy structure target description is unavailable";
            try
            {
                // Target text is part of saved learned rules. Retain its native formatting until the SDK supplies it.
                Type networkIdType = ReflectionTools.FindType("Bolt.NetworkId");
                Type boltNetwork = ReflectionTools.FindType("BoltNetwork");
                if (networkIdType == null || boltNetwork == null) return false;
                var constructor = networkIdType.GetConstructor(new[] { typeof(ulong) });
                var findEntity = boltNetwork.GetMethod("FindEntity", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { networkIdType }, null);
                if (constructor == null || findEntity == null) return false;
                object networkId = constructor.Invoke(new object[] { request.RequestedTargetNetworkId });
                object target = findEntity.Invoke(null, new[] { networkId });
                var parts = new List<string>();
                AppendEventField(parts, "Target", target);
                AppendEventField(parts, "Radius", request.RequestedRadius);
                AppendEventField(parts, "Damage", request.RequestedDamage);
                descriptor = "StructureHitEvent " + String.Join(", ", parts);
                return true;
            }
            catch (Exception ex)
            {
                detail += " (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private void RecordLoaderStructureValidation(OpenACAISonsStructureHitRequest request, string steamId,
            string descriptor, bool blocked, string reason, string validation)
        {
            _lastLoaderStructureHitDecision = CleanConfigText((blocked ? "Blocked: " : "Native handling: ")
                + reason + "; " + validation, 320);
            RecordInventorySignal(steamId, "StructureHitEvent", 0, 0,
                descriptor + ", itemId=unresolved, quantity=unknown",
                CaptureStructureHitGuardPolicy(request.RequestedRadius), "Loader StructureHitEvent / exact session",
                blocked, reason, validation);
        }
#endif
    }
}
