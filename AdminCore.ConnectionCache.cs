using System;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        internal void ForgetRegisteredClient(object connection, string source)
        {
            InvalidateCachedNetworkReferences(connection, true, source);
        }

        internal void ForgetAttachedPlayerEntity(object entity, string source)
        {
            InvalidateCachedNetworkReferences(entity, false, source);
        }

        private void InvalidateCachedNetworkReferences(object retired, bool isConnection, string source)
        {
            if (retired == null) return;

            bool changed = false;
            foreach (PlayerEntry entry in _registeredPlayers.Values)
                changed |= InvalidateCachedNetworkReference(entry, retired, isConnection);
            foreach (PlayerEntry entry in _sessionPlayers.Values)
                changed |= InvalidateCachedNetworkReference(entry, retired, isConnection);
            changed |= InvalidateCachedNetworkReference(_manualTarget, retired, isConnection);

            // A callback may use a different wrapper for the same native object. Refresh still runs
            // through the normal presence checks; the callback alone does not prove a disconnect.
            _nextRosterRefreshUtc = DateTime.MinValue;
            if (changed)
                LogInfo("Roster " + (isConnection ? "connection" : "attachment")
                    + " cache invalidated via " + CleanConfigText(source, 96));
        }

        private static bool InvalidateCachedNetworkReference(PlayerEntry entry, object retired, bool isConnection)
        {
            if (entry == null) return false;

            // Do not ask a retired wrapper for its identity or native equality. Neither a Steam ID
            // nor a reused connection number can identify the lifetime that produced this callback.
            if (isConnection && ReferenceEquals(entry.BoltConnection, retired))
            {
                ClearPlayerHandles(entry);
                return true;
            }
            if (!isConnection && ReferenceEquals(entry.BoltEntity, retired))
            {
                entry.BoltEntity = null;
                entry.Transform = null;
                entry.NetworkId = null;
                if (ReferenceEquals(entry.SourceObject, retired)) entry.SourceObject = null;
                return true;
            }
            if (ReferenceEquals(entry.SourceObject, retired))
            {
                entry.SourceObject = null;
                return true;
            }
            return false;
        }
    }
}
