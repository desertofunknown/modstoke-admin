using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int RemoveUnprovenPlayers(Dictionary<string, PlayerEntry> entries, bool inWorld, Dictionary<string, PlayerEntry> lobbyMembers)
        {
            var removedKeys = new List<string>();
            foreach (var pair in entries)
            {
                if (HasFreshNetworkProof(pair.Value, pair.Key)) continue;
                if (!inWorld && pair.Value != null && lobbyMembers.ContainsKey(pair.Key))
                {
                    ClearPlayerHandles(pair.Value);
                    continue;
                }
                removedKeys.Add(pair.Key);
            }
            foreach (string key in removedKeys) entries.Remove(key);
            return removedKeys.Count;
        }

        private bool HasFreshNetworkProof(PlayerEntry entry, string steamId)
        {
            if (entry != null && IsLocalPlayerEntry(entry)) return IsLocalPlayerInWorld();
            if (ReadNetworkFlag("isRunning") != true) return false;

            steamId = CleanSteamId(entry?.SteamId ?? steamId);
            if (!LooksLikeSteamId(steamId)) return false;

            object connection = entry?.BoltConnection;
            object entity = entry?.BoltEntity;
            bool connected = HasLiveConnectionProof(connection, steamId);
            bool attached = HasLiveEntityProof(entity, steamId);

            // A cached identity can outlive its connection. Resolve current handles before reusing it.
            if (entry != null && entry.ConnectionId >= 0)
            {
                if (!connected)
                {
                    connection = GetConnectionFromConnectionId(entry.ConnectionId);
                    connected = HasLiveConnectionProof(connection, steamId);
                }
                if (!attached)
                {
                    entity = GetEntityFromConnectionId(entry.ConnectionId);
                    attached = HasLiveEntityProof(entity, steamId);
                }
            }

            if (UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
            {
                if (!connected)
                {
                    connection = GetConnectionFromSteamId(id);
                    connected = HasLiveConnectionProof(connection, steamId);
                }
                if (!attached)
                {
                    entity = GetEntityFromSteamId(id);
                    attached = HasLiveEntityProof(entity, steamId);
                }
            }

            if (!connected && !attached) return false;
            if (entry != null)
            {
                entry.BoltConnection = connected ? connection : null;
                entry.BoltEntity = attached ? entity : null;
                entry.SourceObject = attached ? entity : connection;
                entry.Transform = attached ? ExtractTransform(entity) : null;
                entry.NetworkId = attached ? ExtractNetworkId(entity) : null;
                entry.ConnectionId = connected ? ExtractConnectionId(connection) : -1;
            }
            return true;
        }

        private bool HasLiveConnectionProof(object connection, string steamId)
        {
            object udpConnection = ReflectionTools.GetMemberValue(connection, "udpConnection");
            return ReflectionTools.GetMemberValue(udpConnection, "IsConnected") is true
                && String.Equals(CleanSteamId(GetSteamIdFromConnection(connection)), steamId, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasLiveEntityProof(object entity, string steamId)
        {
            if (ReflectionTools.GetMemberValue(entity, "isAttached") is not true) return false;
            object source = ReflectionTools.GetMemberValue(entity, "source");
            object udpConnection = ReflectionTools.GetMemberValue(source, "udpConnection");
            if (ReflectionTools.GetMemberValue(udpConnection, "IsConnected") is false) return false;
            return String.Equals(CleanSteamId(GetSteamIdFromEntity(entity)), steamId, StringComparison.OrdinalIgnoreCase);
        }

        private static void ClearPlayerHandles(PlayerEntry entry)
        {
            if (entry == null) return;
            entry.BoltConnection = null;
            entry.BoltEntity = null;
            entry.Transform = null;
            entry.SourceObject = null;
            entry.ConnectionId = -1;
            entry.NetworkId = null;
        }

        private void MarkPlayerDisconnected(string steamId, string source)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId)) return;

            bool alreadyRecorded = _recentDisconnects.ContainsKey(steamId);
            _registeredPlayers.Remove(steamId);
            _movementSignals.Remove(steamId);
            _movementTelemetrySignals.Remove(steamId);
            ResetPassiveEquipmentBaseline(steamId);
            RemoveServerItemApprovals(steamId);
            RemoveServerRoleObservation(steamId);
            InvalidateServerConnectionObservation(steamId);
            ClearPendingInventoryAudit(steamId);
            _recentDisconnects[steamId] = GetSettingsDeadline(DateTime.UtcNow, Math.Max(5f, _offlineRetentionMinutes) * 60.0);

            _sessionPlayers.TryGetValue(steamId, out PlayerEntry session);
            if (session != null)
            {
                session.Online = false;
                session.PresenceUnresolved = false;
                session.PresenceDetail = null;
                ClearPlayerHandles(session);
            }
            if (_manualTarget != null && String.Equals(_manualTarget.SteamId, steamId, StringComparison.OrdinalIgnoreCase))
            {
                _manualTarget.Online = false;
                _manualTarget.PresenceUnresolved = false;
                _manualTarget.PresenceDetail = null;
                ClearPlayerHandles(_manualTarget);
            }
            if (alreadyRecorded) return;

            string reason = "Disconnected: " + CleanConfigText(source, 96);
            if (session != null) AddPlayerSource(session, reason);
            RecordPlayerActivity(session, steamId, reason);
            _nextRosterRefreshUtc = DateTime.MinValue;
            AppendEventLog("ROSTER", "Offline " + steamId + " via " + CleanConfigText(source, 60));
        }
    }
}
