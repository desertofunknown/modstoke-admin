using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string VehicleGuardStatus()
        {
            if (!_disallowCheats || !_vehicleSpawnGuard) return "disabled";
            if (!_hostActionsAvailable) return "host only";
            return _vehicleSpawnPatchCount == 0 ? "hooks unavailable"
                : "configured, " + _vehicleSpawnPatchCount.ToString(CultureInfo.InvariantCulture) + " hooks";
        }

        internal bool ShouldBlockBoltInstantiate(object[] args)
        {
            if (!_disallowCheats || !_vehicleSpawnGuard || !HasHostSession() || args == null || args.Length == 0)
            {
                return false;
            }

            int prefabId;
            if (!TryExtractPrefabId(args[0], out prefabId) || !IsGolfCartPrefab(prefabId))
            {
                return false;
            }

            Vector3 position = Vector3.zero;
            bool hasPosition = args.Length > 1 && TryConvertToVector3(args[1], out position)
                && IsFiniteDebugValue(position.x) && IsFiniteDebugValue(position.y) && IsFiniteDebugValue(position.z);
            if (!hasPosition)
            {
                position = Vector3.zero;
            }
            string prefabDescription = GetBoltPrefabName(prefabId) + " (" + prefabId.ToString(CultureInfo.InvariantCulture) + ")";
            string reason = "Blocked vehicle prefab spawn: " + prefabDescription
                + (hasPosition ? " at " + FormatVector(position) : "");
            RememberBlockedVehicleSpawnProbe(position, hasPosition, reason);
            AnnounceVehicleSpawnBlocked(reason);
            LogWarning("Vehicle spawn guard blocked BoltNetwork.Instantiate for " + prefabDescription);
            return true;
        }

        private static bool IsGolfCartPrefab(int prefabId)
        {
            int golfCartId;
            if (TryReadBoltPrefabId("GolfCart", out golfCartId) && prefabId == golfCartId)
            {
                return true;
            }
            int snowGolfCartId;
            return TryReadBoltPrefabId("SnowGolfCart", out snowGolfCartId) && prefabId == snowGolfCartId;
        }

        private void RememberBlockedVehicleSpawnProbe(Vector3 position, bool hasPosition, string reason)
        {
            VehicleSpawnProbe probe = new VehicleSpawnProbe();
            probe.Position = position;
            probe.HasPosition = hasPosition;
            probe.Reason = CleanConfigText(reason, 160);
            probe.ExpiresUtc = DateTime.UtcNow.AddSeconds(12.0);
            _blockedVehicleSpawnProbes.Add(probe);
            if (_blockedVehicleSpawnProbes.Count > 16)
            {
                _blockedVehicleSpawnProbes.RemoveAt(0);
            }
        }

        private void AnnounceVehicleSpawnBlocked(string reason)
        {
            DateTime now = DateTime.UtcNow;
            AppendEventLog("GUARD", Shorten("VehicleSpawn " + reason, 140));
            if (now.Subtract(_lastVehicleSpawnBlockNoticeUtc).TotalSeconds < 2.0)
            {
                return;
            }
            _lastVehicleSpawnBlockNoticeUtc = now;
            LogInfo("Vehicle spawn guard observation: " + Shorten(reason, 180));
        }

        private void TickVehicleSpawnGuard()
        {
            DateTime now = DateTime.UtcNow;
            _nextVehicleSpawnAuditUtc = now.AddSeconds(1.0);
            if (!_disallowCheats || !_vehicleSpawnGuard || !HasHostSession())
            {
                _blockedVehicleSpawnProbes.Clear();
                return;
            }

            // Rejected Instantiate calls return no new cart; nearby carts cannot be tied to these diagnostic probes.
            for (int i = _blockedVehicleSpawnProbes.Count - 1; i >= 0; i--)
            {
                if (_blockedVehicleSpawnProbes[i] == null || _blockedVehicleSpawnProbes[i].ExpiresUtc <= now)
                {
                    _blockedVehicleSpawnProbes.RemoveAt(i);
                }
            }
        }

        private static string RejectedPreSpawnedObjectStatus(object evnt)
        {
            object target = ReflectionTools.GetMemberValue(evnt, "PreSpawned");
            if (!(target is UnityEngine.Object nativeObject) || nativeObject == null) return "";
            // A client-provided entity reference does not authorize destruction of that entity.
            return "; pre-spawned object retained, cleanup unconfirmed";
        }
    }
}
