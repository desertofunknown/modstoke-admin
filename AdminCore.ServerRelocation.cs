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
        private void SummonSelectedPlayer()
        {
            if (!RequireHostAction("Summon")) return;
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Summon requires a selected player with a resolved Steam64 ID");
                return;
            }
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            if (!TryGetSummonPose(out Vector3 target, out Quaternion rotation))
            {
                ActionFail("Summon unavailable: your in-world position or facing could not be read");
                return;
            }
#if NET11_0_OR_GREATER
            if (!TryResolveServerMoveTarget(id, out var identity, out _, out string failure))
            {
                ActionFail("Summon unavailable: " + failure);
                return;
            }
            var status = QueueServerMove(identity.Session, target, rotation, out string detail);
            if (ServerMoveMayBeQueued(status)) AllowMovementSpike(id, 8f, "host summon");
            string outcome = "Summon " + id + ": " + detail;
            RecordPlayerActivity(entry, id, outcome);
            ActionResult(status == OpenACAISonsPlayerSpawnStatus.Queued, outcome, outcome);
#else
            ActionFail("Summon requires the current Modstoke Loader runtime");
#endif
        }

        private bool QueueJailMove(string steamId)
        {
            steamId = CleanSteamId(steamId);
            if (!_jailedPlayers.TryGetValue(steamId, out var jail) || jail == null) return false;
            if (!TryGetEffectiveJailPosition(out Vector3 target))
            {
                SetJailMovement(jail, "Unavailable", "Jail position is unavailable; set a finite jail point in the current world");
                return false;
            }
#if NET11_0_OR_GREATER
            if (!TryResolveServerMoveTarget(steamId, out var identity, out var pose, out string failure))
            {
                SetJailMovement(jail, "Unavailable", "Jail movement unavailable: " + failure);
                return false;
            }
            Vector3 position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            if (!jail.ReturnCaptureClosed && !jail.HasReturnPosition && Vector3.Distance(position, target) > 12f)
            {
                jail.ReturnPosition = position;
                jail.HasReturnPosition = true;
            }
            if (Vector3.Distance(position, target) <= 1f)
            {
                jail.ReturnCaptureClosed = true;
                jail.PendingMove = null;
                SetJailMovement(jail, "Near jail", "Replicated position observed within one metre of the jail point; no move requested");
                return false;
            }

            var pending = jail.PendingMove;
            if (pending != null && ReferenceEquals(pending.Session, identity.Session)
                && Vector3.Distance(pending.Target, target) <= 0.01f
                && Vector3.Distance(pending.Origin, position) <= 0.5f)
            {
                SetJailMovement(jail, pending.Uncertain ? "Send uncertain" : "Awaiting move",
                    "Previous jail assignment " + (pending.Uncertain ? "may have queued" : "was queued")
                    + "; observed position is unchanged, so no duplicate request was sent");
                return false;
            }

            Quaternion rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            var status = QueueServerMove(identity.Session, target, rotation, out string detail);
            if (ServerMoveMayBeQueued(status))
            {
                jail.ReturnCaptureClosed = true;
                jail.PendingMove = new PendingJailMove
                {
                    Session = identity.Session,
                    Origin = position,
                    Target = target,
                    Uncertain = status == OpenACAISonsPlayerSpawnStatus.SendUncertain
                };
                AllowMovementSpike(steamId, 8f, "jail move");
            }
            bool queued = status == OpenACAISonsPlayerSpawnStatus.Queued;
            if (queued) jail.LastMoveQueuedUtc = DateTime.UtcNow;
            SetJailMovement(jail, queued ? "Queued" : status == OpenACAISonsPlayerSpawnStatus.SendUncertain ? "Send uncertain" : "Not queued", detail);
            return queued;
#else
            SetJailMovement(jail, "Unavailable", "Jail movement requires the current Modstoke Loader runtime");
            return false;
#endif
        }

        private JailReturnStatus QueueJailReturn(string steamId, Vector3 position, out string detail)
        {
            detail = "saved return position is not finite; no movement requested";
            if (!IsFiniteEventPosition(position)) return JailReturnStatus.NoPosition;
#if NET11_0_OR_GREATER
            if (!TryResolveServerMoveTarget(steamId, out var identity, out var pose, out string failure))
            {
                detail = "return not queued: " + failure;
                return JailReturnStatus.NotQueued;
            }
            Quaternion rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            var status = QueueServerMove(identity.Session, position, rotation, out detail);
            if (ServerMoveMayBeQueued(status))
                AllowMovementSpike(steamId, Math.Max(8f, _legalTeleportGraceSeconds), "jail release return");
            detail = "return movement: " + detail;
            return status == OpenACAISonsPlayerSpawnStatus.Queued ? JailReturnStatus.Queued
                : status == OpenACAISonsPlayerSpawnStatus.SendUncertain ? JailReturnStatus.Uncertain
                : JailReturnStatus.NotQueued;
#else
            detail = "return not queued: requires the current Modstoke Loader runtime";
            return JailReturnStatus.NotQueued;
#endif
        }

        private static void SetJailMovement(JailEntry jail, string status, string detail)
        {
            jail.MovementStatus = status;
            jail.MovementDetail = detail;
        }

        private string GetJailMovementDetail(string steamId)
        {
            return _jailedPlayers.TryGetValue(CleanSteamId(steamId), out var jail) && jail != null
                ? jail.MovementDetail : "no jail restriction is active";
        }

        private string DescribeJailPosition()
        {
            return TryGetEffectiveJailPosition(out Vector3 position) ? FormatVector(position) : "Unavailable";
        }

        private bool TryGetEffectiveJailPosition(out Vector3 target)
        {
            target = _jailPosition;
            if (!IsFiniteEventPosition(target)) return false;
            if (!IsLegacyUnsafeJailPosition(target)) return true;
            if (!TryGetLocalJailFallback(out target)) return false;
            if (!_warnedLegacyJailPosition)
            {
                _warnedLegacyJailPosition = true;
                LogWarning("Legacy off-map jail position " + FormatVector(_jailPosition)
                    + " replaced at runtime with host-side holding point " + FormatVector(target)
                    + ". Use Set Jail Here to save a permanent jail point.");
            }
            return true;
        }

        private static bool IsLegacyUnsafeJailPosition(Vector3 value) => value.y < -100f;

        private bool TryGetLocalJailFallback(out Vector3 target)
        {
            target = Vector3.zero;
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (ReflectionTools.GetStaticMemberValue(localPlayer, "IsInWorld") is not true
                || !TryGetObjectWorldPosition(GetLocalPlayerTransformForAdmin(), out Vector3 position)) return false;
            target = position + Vector3.up * 0.5f;
            return IsFiniteEventPosition(target);
        }

        private bool TryGetSummonPose(out Vector3 target, out Quaternion rotation)
        {
            target = Vector3.zero;
            rotation = Quaternion.identity;
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (ReflectionTools.GetStaticMemberValue(localPlayer, "IsInWorld") is not true) return false;
            try
            {
                Transform transform = GetLocalPlayerTransformForAdmin() as Transform;
                if (transform == null || !TryGetObjectWorldPosition(transform, out Vector3 position)) return false;
                Transform camera = GetLocalCameraTransformForAdmin() as Transform;
                Vector3 forward = camera == null ? transform.forward : camera.forward;
                if (!IsFiniteEventPosition(forward)) return false;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
                target = position + forward * 2f + Vector3.up * 0.5f;
                rotation = transform.rotation;
                return IsFiniteEventPosition(target);
            }
            catch { return false; }
        }

#if NET11_0_OR_GREATER
        private sealed class PendingJailMove
        {
            internal OpenACAISonsServerSession Session;
            internal Vector3 Origin;
            internal Vector3 Target;
            internal bool Uncertain;
        }

        private bool TryResolveServerMoveTarget(string steamId, out OpenACAISonsServerIdentitySnapshot identity,
            out OpenACAISonsEntitySnapshot pose, out string failure)
        {
            identity = null;
            pose = null;
            failure = "an active multiplayer host session is required";
            if (!HasHostSession()) return false;
            string id = CleanSteamId(steamId);
            if (!LooksLikeSteamId(id) || !UInt64.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
            {
                failure = "a resolved Steam64 ID is required";
                return false;
            }
            if (String.Equals(id, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                failure = "the local host cannot be a remote movement target";
                return false;
            }
            if (!TryResolveServerIdentity(parsed, out identity, out failure)) return false;
            if (!OpenACAISonsServerWorld.TryReadPlayer(identity.Session, out pose, out var status))
            {
                failure = "current player position unavailable (" + status + ")";
                return false;
            }
            if (!IsFiniteEventPosition(new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z)))
            {
                failure = "current player position is not finite";
                return false;
            }
            return true;
        }

        private OpenACAISonsPlayerSpawnStatus QueueServerMove(OpenACAISonsServerSession session,
            Vector3 target, Quaternion rotation, out string detail)
        {
            var status = OpenACAISonsServerPlayerSpawns.QueueSpawnAssignment(session,
                new System.Numerics.Vector3(target.x, target.y, target.z),
                new System.Numerics.Quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
            switch (status)
            {
                case OpenACAISonsPlayerSpawnStatus.Queued:
                    detail = "spawn assignment queued to " + FormatVector(target) + "; client arrival is unconfirmed";
                    break;
                case OpenACAISonsPlayerSpawnStatus.SendUncertain:
                    detail = "spawn assignment may have queued to " + FormatVector(target) + "; send result and arrival are unconfirmed";
                    break;
                case OpenACAISonsPlayerSpawnStatus.RaceUnavailable:
                    detail = "movement not queued: the player's current race could not be preserved";
                    break;
                case OpenACAISonsPlayerSpawnStatus.Unavailable:
                    detail = "movement not queued: Modstoke Loader's player-spawn service is unavailable";
                    break;
                case OpenACAISonsPlayerSpawnStatus.SessionUnavailable:
                    detail = "movement not queued: the selected session is no longer current";
                    break;
                case OpenACAISonsPlayerSpawnStatus.PlayerUnavailable:
                    detail = "movement not queued: the selected player's attached entity is unavailable";
                    break;
                case OpenACAISonsPlayerSpawnStatus.InvalidPosition:
                case OpenACAISonsPlayerSpawnStatus.InvalidRotation:
                    detail = "movement not queued: the destination position or rotation is invalid";
                    break;
                case OpenACAISonsPlayerSpawnStatus.NotServer:
                    detail = "movement not queued: an active multiplayer host is required";
                    break;
                case OpenACAISonsPlayerSpawnStatus.WrongThread:
                    detail = "movement not queued: the request must run on the game thread";
                    break;
                default:
                    detail = "movement not queued (" + status + ")";
                    break;
            }
            return status;
        }

        private static bool ServerMoveMayBeQueued(OpenACAISonsPlayerSpawnStatus status)
        {
            return status == OpenACAISonsPlayerSpawnStatus.Queued || status == OpenACAISonsPlayerSpawnStatus.SendUncertain;
        }
#endif
    }
}
