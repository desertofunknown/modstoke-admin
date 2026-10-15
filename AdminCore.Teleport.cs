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
        private void TeleportToSelectedPlayer()
        {
            PlayerEntry entry = GetSelectedPlayer();
            string id = CleanSteamId(entry?.SteamId);
            if (!LooksLikeSteamId(id) || !UInt64.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId))
            {
                ActionFail("Go To requires a selected player with a resolved Steam64 ID");
                return;
            }
            if (String.Equals(id, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                ActionFail("Go To requires another player; you are the selected target");
                return;
            }

            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            object localTransform = GetLocalPlayerTransformForAdmin();
            if (ReflectionTools.GetStaticMemberValue(localPlayer, "IsInWorld") is not true
                || !TryGetObjectWorldPosition(localTransform, out _))
            {
                ActionFail("Go To unavailable: your in-world position is not ready");
                return;
            }
            if (!TryReadTeleportTarget(steamId, out Vector3 position, out Quaternion rotation, out string failure)
                || !TryCreateTeleportDestination(position, rotation, out Vector3 destination, out failure))
            {
                ActionFail("Go To unavailable for " + id + ": " + failure);
                return;
            }

            // Use the native teleport once; direct transform writes bypass its movement handling.
            bool requested = InvokeMethod(null, localPlayer, "TeleportTo",
                new object[] { destination, rotation }, true, out _);
            string outcome = "Go To " + DisplayPlayerName(entry) + " -> " + FormatVector(destination);
            bool observed = false;
            if (!requested)
            {
                outcome += ": native teleport unavailable or failed; movement is unconfirmed";
            }
            else if (ReflectionTools.GetStaticMemberValue(localPlayer, "IsInWorld") is not true
                || !ObjectsSame(localTransform, GetLocalPlayerTransformForAdmin())
                || !TryGetObjectWorldPosition(localTransform, out Vector3 actual))
            {
                outcome += ": requested; local position readback is unavailable";
            }
            else
            {
                observed = Vector3.Distance(actual, destination) <= 0.5f;
                outcome += observed ? ": local position observed at destination"
                    : ": requested; immediate local position is " + FormatVector(actual) + "; arrival is unconfirmed";
            }
            RecordPlayerActivity(entry, id, outcome);
            ActionResult(observed, outcome, outcome);
        }

        private bool TryReadTeleportTarget(ulong steamId, out Vector3 position, out Quaternion rotation, out string failure)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            failure = "the selected player has no current attached entity";
            _boltNetworkType ??= ReflectionTools.FindType("BoltNetwork");
            if (ReadNetworkFlag("isRunning") != true)
            {
                failure = "a running multiplayer session is required";
                return false;
            }
#if NET11_0_OR_GREATER
            if (ReadNetworkFlag("isServer") == true)
            {
                if (!TryResolveServerIdentity(steamId, out var identity, out failure)) return false;
                if (!OpenACAISonsServerWorld.TryReadPlayer(identity.Session, out var pose, out var status))
                {
                    failure = "the loader could not read the current player position (" + status + ")";
                    return false;
                }
                position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
                return true;
            }
#endif
            // Clients and older runtimes resolve a live entity afresh, without cached roster transforms.
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            object entity = GetEntityFromSteamId(steamId);
            if (!HasLiveEntityProof(entity, id)) return false;
            Transform transform = ExtractTransform(entity) as Transform;
            try
            {
                if (transform == null) return false;
                position = transform.position;
                rotation = transform.rotation;
                return true;
            }
            catch
            {
                failure = "the selected player's position could not be read";
                return false;
            }
        }

        private static bool TryCreateTeleportDestination(Vector3 position, Quaternion rotation, out Vector3 destination, out string failure)
        {
            destination = Vector3.zero;
            failure = "the selected player has an invalid position or rotation";
            if (!IsFiniteEventPosition(position)
                || !IsFiniteDebugValue(rotation.x) || !IsFiniteDebugValue(rotation.y)
                || !IsFiniteDebugValue(rotation.z) || !IsFiniteDebugValue(rotation.w)) return false;
            float rotationLength = Quaternion.Dot(rotation, rotation);
            if (!IsFiniteDebugValue(rotationLength) || Math.Abs(rotationLength - 1f) > 0.01f) return false;

            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            destination = position - forward * 2f + Vector3.up * 0.35f;
            if (!IsFiniteEventPosition(destination)) return false;
            failure = "";
            return true;
        }
    }
}
