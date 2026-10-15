using System;
using System.Collections.Generic;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool TryCollectLoaderRoster(Dictionary<string, PlayerEntry> entries,
            Dictionary<string, string> unresolved, List<string> diagnostics)
        {
#if NET11_0_OR_GREATER
            if (!HasHostSession()) return false;

            var confirmed = new Dictionary<string, OpenACAISonsServerIdentitySnapshot>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var failures = new Dictionary<OpenACAISonsServerSession, string>();
            int unreadable = 0;
            foreach (var session in OpenACAISonsServerSessions.Sessions)
            {
                if (!OpenACAISonsServerSessions.TryReadIdentity(session, out var identity, out var status))
                {
                    failures[session] = "Identity read: " + status;
                    unreadable++;
                    continue;
                }

                string id = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
                if (!LooksLikeSteamId(id))
                {
                    failures[session] = "The session did not report a valid Steam64 ID";
                    unreadable++;
                    continue;
                }
                if (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId)
                {
                    ambiguous[id] = "Game identity and registration disagree";
                    confirmed.Remove(id);
                    failures[session] = ambiguous[id];
                    continue;
                }
                if (confirmed.ContainsKey(id))
                {
                    ambiguous[id] = "More than one session reports this Steam64 ID";
                    confirmed.Remove(id);
                    continue;
                }
                if (!ambiguous.ContainsKey(id)) confirmed.Add(id, identity);
            }

            var remove = new List<string>();
            foreach (var pair in entries)
            {
                if (IsLocalPlayerEntry(pair.Value) && HasFreshNetworkProof(pair.Value, pair.Key)) continue;
                if (!confirmed.ContainsKey(pair.Key)) remove.Add(pair.Key);
            }
            foreach (string key in remove) entries.Remove(key);

            int observed = 0;
            foreach (var pair in confirmed)
            {
                var identity = pair.Value;
                if (entries.TryGetValue(pair.Key, out var local) && IsLocalPlayerEntry(local)) continue;
                // Native handles supply presentation only. Presence comes from the exact SDK read.
                AddLiveNetworkPlayer(entries, pair.Key, null, "Modstoke Loader session identity", null, null, null);
                var entry = entries[pair.Key];
                entry.Key = pair.Key;
                entry.SteamId = pair.Key;
                if (!HasFreshNetworkProof(entry, pair.Key)) ClearPlayerHandles(entry);
                if (!identity.Session.IsCurrent)
                {
                    entries.Remove(pair.Key);
                    continue;
                }
                entry.RosterSession = identity.Session;
                entry.LastSeenUtc = identity.ObservedAt.UtcDateTime;
                observed++;
            }

            foreach (var entry in _sessionPlayers.Values)
            {
                if (entry == null || !entry.Online || IsLocalPlayerEntry(entry)) continue;
                string id = CleanSteamId(entry.SteamId);
                if (entries.ContainsKey(id)) continue;
                if (ambiguous.TryGetValue(id, out string reason))
                {
                    unresolved[id] = reason;
                    continue;
                }
                // A disabled service or failed read is not an observed end of this lifetime.
                if (entry.RosterSession?.ClosedAt != null) continue;
                if (!OpenACAISonsServerSessions.HooksInstalled)
                    reason = "Modstoke Loader session tracking is unavailable";
                else if (entry.RosterSession != null && failures.TryGetValue(entry.RosterSession, out var failure))
                    reason = failure;
                else
                    reason = "The previous session identity could not be confirmed";
                unresolved[id] = reason;
            }

            diagnostics.Add("Modstoke Loader identities " + observed.ToString(CultureInfo.InvariantCulture)
                + ", unreadable sessions " + unreadable.ToString(CultureInfo.InvariantCulture)
                + ", ambiguous identities " + ambiguous.Count.ToString(CultureInfo.InvariantCulture)
                + ", presence unknown " + unresolved.Count.ToString(CultureInfo.InvariantCulture)
                + (OpenACAISonsServerSessions.HooksInstalled ? "" : ", tracking unavailable"));
            return true;
#else
            return false;
#endif
        }

        private static bool HasRosterSessionChanged(PlayerEntry previous, PlayerEntry current)
        {
#if NET11_0_OR_GREATER
            return previous.RosterSession != null && current.RosterSession != null
                && !ReferenceEquals(previous.RosterSession, current.RosterSession);
#else
            return false;
#endif
        }

        private static void CopyRosterObservation(PlayerEntry target, PlayerEntry source, DateTime now)
        {
            target.PresenceUnresolved = false;
            target.PresenceDetail = null;
            target.LastSeenUtc = now;
#if NET11_0_OR_GREATER
            target.RosterSession = source.RosterSession;
            if (source.RosterSession != null) target.LastSeenUtc = source.LastSeenUtc;
#endif
        }

        private static string GetPlayerPresenceText(PlayerEntry entry)
        {
            return entry != null && entry.PresenceUnresolved ? "Presence unknown"
                : entry != null && entry.Online ? "Online" : "Offline";
        }

        private int CountUnresolvedPlayers()
        {
            int count = 0;
            foreach (var entry in _players)
                if (entry != null && entry.PresenceUnresolved) count++;
            return count;
        }
    }
}
