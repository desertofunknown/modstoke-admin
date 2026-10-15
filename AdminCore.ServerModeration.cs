using System;
using System.Globalization;
using UnityEngine;
#if NET11_0_OR_GREATER
using System.Collections.Generic;
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool TryGetSelectedModerationTarget(out PlayerEntry entry, out ulong steamId)
        {
            entry = GetSelectedPlayer();
            string id = CleanSteamId(entry?.SteamId);
            steamId = 0;
            return LooksLikeSteamId(id) && UInt64.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out steamId);
        }

        private void ChangeSelectedPlayerRole(string role)
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Role change requires a selected player with a resolved Steam64 ID");
                return;
            }
            bool sent = SetPlayerRole(role, steamId, out string outcome);
            RecordPlayerActivity(entry, steamId.ToString(CultureInfo.InvariantCulture), outcome);
            ActionResult(sent, outcome, outcome);
        }

        private void DrawServerRoleObservation(PlayerEntry entry)
        {
            string id = CleanSteamId(entry.SteamId);
            GUILayout.BeginHorizontal();
            string rosterRole = "Roster role: " + (String.IsNullOrEmpty(entry.Role) ? "Unknown" : entry.Role);
            GUILayout.Label(new GUIContent(Shorten(rosterRole, 34), null, rosterRole),
                _hintStyle, GUILayout.Height(28f));
            if (DrawModerationButton(new GUIContent("Check Role", null,
                "Read the current world's stored role by Steam64, including offline and manual targets. Host only. Stored roles do not establish identity, effective permissions or owner-whitelist membership."),
                _quietButtonStyle, _hostActionsAvailable && StoredRoleReadsSupported && LooksLikeSteamId(id),
                GUILayout.Width(104f), GUILayout.Height(28f)))
            {
                bool read = ObserveServerRole(UInt64.Parse(id, CultureInfo.InvariantCulture), out string observation);
                string outcome = "Role check for " + id + ": " + observation;
                RecordPlayerActivity(entry, id, outcome);
                ActionResult(read, outcome, outcome);
            }
            GUILayout.EndHorizontal();
            string observedRole = GetServerRoleObservationText(id);
            GUILayout.Label(new GUIContent(Shorten(observedRole, 94), null, observedRole), _hintStyle, GUILayout.Height(40f));
        }

        private bool SendKickRequest(ulong steamId, out string outcome)
        {
#if NET11_0_OR_GREATER
            OpenACAISonsServerSession observedSession = null;
            if (HasHostSession() && TryResolveServerIdentity(steamId, out var identity, out _))
                observedSession = identity.Session;
#endif
            // Keep the game's target permissions and notification handling in the command path.
            bool sent = SendAdminPlayerCommand("Command_playerkick", "playerkick", steamId, out string dispatch);
            outcome = "Kick request " + dispatch;
#if NET11_0_OR_GREATER
            if (observedSession != null && observedSession.ClosedAt.HasValue)
                outcome += "; the previously observed session is now closed";
            else
#endif
                outcome += "; disconnect remains unconfirmed";
            return sent;
        }

#if NET11_0_OR_GREATER
        private readonly Dictionary<string, string> _serverRoleObservations = new Dictionary<string, string>();
        private bool StoredRoleReadsSupported => true;

        private bool TryResolveServerIdentity(ulong steamId, out OpenACAISonsServerIdentitySnapshot identity, out string failure)
        {
            identity = null;
            failure = "no current remote session matches this Steam64 ID";
            if (!OpenACAISonsServerSessions.HooksInstalled)
            {
                failure = "Modstoke Loader session tracking is unavailable";
                return false;
            }
            foreach (var candidate in OpenACAISonsServerSessions.Sessions)
            {
                if (!OpenACAISonsServerSessions.TryReadIdentity(candidate, out var current, out _)
                    || current.GameSteamId != steamId) continue;
                if (current.RegisteredSteamId.HasValue && current.RegisteredSteamId.Value != steamId)
                {
                    failure = "game identity and registration disagree";
                    return false;
                }
                if (identity != null)
                {
                    identity = null;
                    failure = "more than one current session matches this Steam64 ID";
                    return false;
                }
                identity = current;
            }
            return identity != null;
        }

        private bool ObserveServerRole(ulong steamId, out string observation)
        {
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            if (!HasHostSession())
            {
                observation = "Stored role unavailable: an active player-host session is required";
                _serverRoleObservations[id] = observation;
                return false;
            }
            if (!OpenACAISonsServerRoles.TryRead(steamId, out var snapshot, out var status))
            {
                observation = "Stored role unavailable: " + DescribeStoredRoleReadStatus(status);
                _serverRoleObservations[id] = observation;
                return false;
            }

            string role = !snapshot.HasRecord ? "no stored record"
                : Enum.IsDefined(typeof(OpenACAISonsGameRole), snapshot.StoredRole.Value) ? snapshot.StoredRole.Value.ToString()
                : "unrecognized value " + ((int)snapshot.StoredRole.Value).ToString(CultureInfo.InvariantCulture);
            string description = "Observed stored role: " + role + " | "
                + snapshot.ObservedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            observation = description + ". Stored record only; identity, effective permissions and save state are unconfirmed.";
            _serverRoleObservations[id] = observation;
            return true;
        }

        private static string DescribeStoredRoleReadStatus(OpenACAISonsStoredRoleReadStatus status)
        {
            switch (status)
            {
                case OpenACAISonsStoredRoleReadStatus.NotReady: return "the multiplayer save is not ready or is loading";
                case OpenACAISonsStoredRoleReadStatus.RoleDataUnavailable: return "the game's role data is unavailable";
                case OpenACAISonsStoredRoleReadStatus.NotServer: return "a running multiplayer server is required";
                case OpenACAISonsStoredRoleReadStatus.InvalidSteamId: return "invalid Steam64 ID";
                case OpenACAISonsStoredRoleReadStatus.WrongThread: return "lookup must run on the game thread";
                case OpenACAISonsStoredRoleReadStatus.Unavailable: return "the loader role service is not initialized";
                default: return "the role read failed (" + status + ")";
            }
        }

        private string GetServerRoleObservationText(string steamId)
        {
            if (!_serverRoleObservations.TryGetValue(steamId, out var observation))
                return "Stored role not checked. Check Role works for online, offline and manual targets.";
            return observation;
        }

        private void RemoveServerRoleObservation(string steamId) => _serverRoleObservations.Remove(steamId);
        private void ClearServerRoleObservations() => _serverRoleObservations.Clear();
#else
        private bool StoredRoleReadsSupported => false;
        private bool ObserveServerRole(ulong steamId, out string observation)
        {
            observation = "Stored-role reads require the current Modstoke Loader runtime";
            return false;
        }
        private string GetServerRoleObservationText(string steamId) => "Stored-role reads require the current Modstoke Loader runtime";
        private void RemoveServerRoleObservation(string steamId) { }
        private void ClearServerRoleObservations() { }
#endif
    }
}
