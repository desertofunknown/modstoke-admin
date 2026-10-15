using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _rosterSearch = "";
        private RosterFilter _rosterFilter;

        private enum RosterFilter
        {
            All,
            Online,
            Offline,
            Moderated
        }

        private void DrawRosterWorkspacePanel()
        {
            BeginPanel("Player Roster", GUILayout.Height(254f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", _hintStyle, GUILayout.Width(46f), GUILayout.Height(28f));
            string search = GUILayout.TextField(_rosterSearch, _inputStyle, GUILayout.Width(218f), GUILayout.Height(28f));
            if (!String.Equals(search, _rosterSearch, StringComparison.Ordinal))
            {
                _rosterSearch = search;
                _playerScroll = Vector2.zero;
            }
            DrawRosterFilterButton(RosterFilter.All, 46f);
            DrawRosterFilterButton(RosterFilter.Online, 66f);
            DrawRosterFilterButton(RosterFilter.Offline, 66f);
            DrawRosterFilterButton(RosterFilter.Moderated, 94f);
            if (GUILayout.Button("Clear", _quietButtonStyle, GUILayout.Width(58f), GUILayout.Height(28f)))
            {
                _rosterSearch = "";
                _rosterFilter = RosterFilter.All;
                _playerScroll = Vector2.zero;
            }

            string query = _rosterSearch.Trim();
            int visibleCount = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (MatchesRosterFilter(_players[i], query)) visibleCount++;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(visibleCount.ToString(CultureInfo.InvariantCulture) + " / " + _players.Count.ToString(CultureInfo.InvariantCulture) + " shown", _hintStyle, GUILayout.Width(118f), GUILayout.Height(28f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("PLAYER NAME", _tableHeaderStyle, GUILayout.Width(240f));
            GUILayout.Label("STEAM64 ID", _tableHeaderStyle, GUILayout.Width(190f));
            GUILayout.Label("STATUS", _tableHeaderStyle, GUILayout.Width(112f));
            GUILayout.Label("SOURCE", _tableHeaderStyle, GUILayout.Width(170f));
            GUILayout.Label("ACTIONS", _tableHeaderStyle, GUILayout.Width(98f));
            GUILayout.EndHorizontal();

            _playerScroll.x = 0f;
            _playerScroll = GUILayout.BeginScrollView(_playerScroll, false, false, GUILayout.Height(120f));
            GUILayout.BeginVertical(GUILayout.Width(846f));
            if (visibleCount == 0)
            {
                GUILayout.BeginVertical(_emptyStateStyle, GUILayout.Height(100f));
                GUILayout.Label(_players.Count == 0 ? "No players in the roster" : "No matching players", _contextValueStyle, GUILayout.Height(24f));
                GUILayout.Label(_players.Count == 0 ? "Roster source: " + Shorten(_lastRosterDiagnostics, 90) : "Search names, aliases or Steam64 IDs, or clear the filters. Your target stays selected.", _hintStyle, GUILayout.Height(32f));
                GUILayout.EndVertical();
            }
            else
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    if (MatchesRosterFilter(_players[i], query)) DrawPlayerRow(_players[i]);
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            EndPanel();
        }

        private void DrawRosterFilterButton(RosterFilter filter, float width)
        {
            if (GUILayout.Button(filter.ToString(), _rosterFilter == filter ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(width), GUILayout.Height(28f)))
            {
                _rosterFilter = filter;
                _playerScroll = Vector2.zero;
            }
        }

        private bool MatchesRosterFilter(PlayerEntry entry, string query)
        {
            if (entry == null) return false;
            if (_rosterFilter == RosterFilter.Online && (entry.PresenceUnresolved || !entry.Online)) return false;
            if (_rosterFilter == RosterFilter.Offline && (entry.PresenceUnresolved || entry.Online)) return false;
            if (_rosterFilter == RosterFilter.Moderated && !IsPlayerBanned(entry) && !IsPlayerMuted(entry) && !IsRosterPlayerJailed(entry)) return false;
            if (query.Length == 0) return true;
            if (ContainsRosterQuery(entry.Name, query) || ContainsRosterQuery(entry.Alias, query) || ContainsRosterQuery(entry.SteamId, query)) return true;
            for (int i = 0; i < entry.ObservedNames.Count; i++)
            {
                if (ContainsRosterQuery(entry.ObservedNames[i], query)) return true;
            }
            return false;
        }

        private static bool ContainsRosterQuery(string value, string query)
        {
            return !String.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsRosterPlayerJailed(PlayerEntry entry)
        {
            return entry != null && !String.IsNullOrEmpty(entry.SteamId) && _jailedPlayers.ContainsKey(entry.SteamId);
        }

        private void DrawSelectedPlayerDetailsPanel()
        {
            BeginPanel("Selected Player", MinimumHeight(390f));
            PlayerEntry entry = GetSelectedPlayer();
            if (entry == null)
            {
                GUILayout.Label("No target selected", _contextValueStyle, GUILayout.Height(24f));
                GUILayout.Label("Select a roster player or enter a manual target below.", _hintStyle, GUILayout.Height(40f));
                EndPanel();
                return;
            }

            string name = DisplayPlayerName(entry);
            GUILayout.Label(new GUIContent(Shorten(name, 46), null, name), _contextValueStyle, GUILayout.Height(22f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(String.IsNullOrEmpty(entry.SteamId) ? "Steam64 ID unavailable" : entry.SteamId, _monoStyle, GUILayout.Height(28f));
            if (GUILayout.Button("Copy ID", _quietButtonStyle, GUILayout.Width(82f), GUILayout.Height(28f))) CopySteamId(entry);
            GUILayout.EndHorizontal();

            string status = GetPlayerPresenceText(entry);
            if (HasSavedBanRecord(entry)) status += " | Ban record";
            else if (entry.NativeBan == true) status += " | Ban observed";
            if (IsPlayerMuted(entry)) status += " | Muted";
            string movementDetail = "";
            JailEntry jail = null;
            if (!String.IsNullOrEmpty(entry.SteamId) && _jailedPlayers.TryGetValue(entry.SteamId, out jail) && jail != null)
            {
                status += " | Jail " + jail.MovementStatus;
                movementDetail = jail.MovementDetail;
            }
            string statusDetail = entry.PresenceDetail ?? "";
            if (!String.IsNullOrEmpty(movementDetail)) statusDetail += (statusDetail.Length == 0 ? "" : "\n") + movementDetail;
            GUILayout.Label(new GUIContent(status, null, statusDetail), _hintStyle, GUILayout.Height(20f));
            if (jail != null) DrawJailCareStatus(jail);
            DrawSelectedJailReturn(entry.SteamId);
            DrawNativeBanObservation(entry);
            DrawServerRoleObservation(entry);
            DrawSelectedConnectionObservation(entry);
            string lastSeen = BuildLastSeenStatus(entry).Trim(' ', '|');
            GUILayout.Label((entry.PresenceUnresolved ? "" : "Last seen: ") + (lastSeen.Length == 0 ? "Unknown" : lastSeen), _hintStyle, GUILayout.Height(18f));
            string activity = String.IsNullOrEmpty(entry.LastActivity) ? "No activity recorded" : entry.LastActivity;
            GUILayout.Label(new GUIContent("Activity: " + Shorten(activity, 72), null, activity), _hintStyle, GUILayout.Height(32f));
            if (GUILayout.Button("Inspect Evidence", _primaryButtonStyle, GUILayout.Height(32f)))
            {
                _activeTab = "Evidence";
                _contentScroll = Vector2.zero;
            }
            EndPanel();
        }
    }
}
