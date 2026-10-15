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
        private string _lastServerItemGrant = "";

        private void DrawServerItemGrant()
        {
            PlayerEntry target = GetSelectedPlayer();
            string targetId = CleanSteamId(target?.SteamId);
            GUILayout.Space(6f);
            GUILayout.Label("Give to selected player", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(new GUIContent(target == null ? "No player selected" : Shorten(DisplayPlayerName(target), 32),
                null, targetId), _contextValueStyle, GUILayout.Width(290f), GUILayout.Height(32f));
            if (DrawModerationButton("Give Target", _primaryButtonStyle,
                _hostActionsAvailable && ServerItemGrantsAvailable && LooksLikeSteamId(targetId),
                GUILayout.Width(112f), GUILayout.Height(32f))) GrantServerItem(target, targetId);
            if (GUILayout.Button("Choose Player", _quietButtonStyle, GUILayout.Width(126f), GUILayout.Height(32f)))
                _activeTab = "Players";
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Host only. Uses Selected and Count above. Grants approve that item for this player's current session; quantity, range and jail rules still apply.", _hintStyle);
            int approvals = GetServerItemApprovalCount(targetId);
            if (approvals > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Session-approved item types: " + approvals.ToString(CultureInfo.InvariantCulture), _hintStyle, GUILayout.Width(290f));
                if (DrawModerationButton(new GUIContent("Clear Approvals", null, "Restore normal rules for future item checks. Does not remove items or recheck earlier observations."),
                    _quietButtonStyle, _hostActionsAvailable, GUILayout.Width(142f), GUILayout.Height(28f)))
                {
                    if (RequireHostAction("Clear item approvals"))
                    {
                        RemoveServerItemApprovals(targetId);
                        RecordPlayerActivity(target, targetId, "Host cleared session item approvals");
                        ActionOk("Session item approvals cleared for " + DisplayPlayerName(target));
                    }
                    _lastServerItemGrant = _lastAction;
                }
                GUILayout.EndHorizontal();
            }
            if (!String.IsNullOrEmpty(_lastServerItemGrant)) GUILayout.Label(_lastServerItemGrant, _hintStyle);
        }

#if NET11_0_OR_GREATER
        private sealed class ServerItemApproval
        {
            internal OpenACAISonsServerSession Session;
            internal readonly HashSet<int> Items = new HashSet<int>();
        }

        private readonly Dictionary<string, ServerItemApproval> _serverItemApprovals = new Dictionary<string, ServerItemApproval>();
        private bool ServerItemGrantsAvailable => OpenACAISonsServerSessions.HooksInstalled;

        private void GrantServerItem(PlayerEntry target, string targetId)
        {
            if (!RequireHostAction("Give item"))
            {
                _lastServerItemGrant = _lastAction;
                return;
            }
            if (!LooksLikeSteamId(targetId) || !UInt64.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId))
            {
                ServerItemGrantFailed("Give item unavailable: select a player with a resolved Steam64 ID");
                return;
            }
            if (String.Equals(targetId, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                ServerItemGrantFailed("Use Add for your own inventory. Give Target requires a remote player.");
                return;
            }
            if (!Int32.TryParse(_debugItemCountInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || count < 1 || count > 50)
            {
                ServerItemGrantFailed("Enter a whole-number Count from 1 to 50");
                return;
            }
            ItemCatalogEntry selectedItem = FindSelectedCatalogItem();
            int itemId;
            if (selectedItem != null) itemId = selectedItem.Id;
            else if (!Int32.TryParse((_debugItemInput ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out itemId))
            {
                ServerItemGrantFailed("Select an exact catalog item or enter its numeric ID");
                return;
            }
            if (itemId <= 0 || itemId > OpenACAISonsServerInventory.MaxItemId)
            {
                ServerItemGrantFailed("Give item requires an ID from 1 to " + OpenACAISonsServerInventory.MaxItemId.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (!TryResolveServerIdentity(steamId, out var identity, out string failure))
            {
                ServerItemGrantFailed("Give item unavailable: " + failure);
                return;
            }

            // The SDK checks the current item definition and exact recipient again for the send.
            // An uncertain send is never retried: one additional item may already be queued.
            OpenACAISonsServerSession session = identity.Session;
            OpenACAISonsItemGrantResult result = OpenACAISonsServerInventory.QueueItemGrant(session, itemId, count);
            if (result.QueuedCount > 0 || result.HasUncertainSend)
            {
                if (!_serverItemApprovals.TryGetValue(targetId, out var approval) || !ReferenceEquals(approval.Session, session))
                {
                    approval = new ServerItemApproval { Session = session };
                    _serverItemApprovals[targetId] = approval;
                }
                approval.Items.Add(itemId);
            }
            string itemLabel = selectedItem == null ? itemId.ToString(CultureInfo.InvariantCulture)
                : selectedItem.Name + " (" + itemId.ToString(CultureInfo.InvariantCulture) + ")";
            string outcome = "Give " + itemLabel + " to " + DisplayPlayerName(target) + ": "
                + result.QueuedCount.ToString(CultureInfo.InvariantCulture) + " of " + result.RequestedAmount.ToString(CultureInfo.InvariantCulture)
                + " item requests queued";
            if (result.HasUncertainSend)
                outcome += "; one additional request may have queued. Check the player's inventory before sending more.";
            else if (result.Status != OpenACAISonsItemGrantStatus.Queued)
                outcome += "; stopped: " + DescribeItemGrantStatus(result.Status) + ". Earlier requests were not rolled back.";
            outcome += " Delivery and inventory acceptance are unconfirmed.";
            _lastServerItemGrant = outcome;
            RecordPlayerActivity(target, targetId, outcome);
            ActionResult(result.Status == OpenACAISonsItemGrantStatus.Queued, outcome, outcome);
        }

        private bool IsServerApprovedItem(string steamId, int itemId)
        {
            if (itemId <= 0 || !_serverItemApprovals.TryGetValue(steamId, out var approval) || !approval.Items.Contains(itemId)) return false;
            if (!approval.Session.IsCurrent)
            {
                _serverItemApprovals.Remove(steamId);
                return false;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(approval.Session, out var identity, out _)) return false;
            return identity.GameSteamId.ToString(CultureInfo.InvariantCulture) == steamId
                && (!identity.RegisteredSteamId.HasValue || identity.RegisteredSteamId.Value == identity.GameSteamId);
        }

        private int GetServerItemApprovalCount(string steamId)
        {
            return _serverItemApprovals.TryGetValue(steamId, out var approval) && approval.Session.IsCurrent ? approval.Items.Count : 0;
        }

        private void RemoveServerItemApprovals(string steamId) => _serverItemApprovals.Remove(steamId);
        private void ClearServerItemApprovals()
        {
            _serverItemApprovals.Clear();
            _lastServerItemGrant = "";
        }

        private static string DescribeItemGrantStatus(OpenACAISonsItemGrantStatus status)
        {
            switch (status)
            {
                case OpenACAISonsItemGrantStatus.Unavailable: return "loader inventory service unavailable";
                case OpenACAISonsItemGrantStatus.WrongThread: return "game-thread dispatch unavailable";
                case OpenACAISonsItemGrantStatus.NotServer: return "host authority lost";
                case OpenACAISonsItemGrantStatus.InvalidItemId: return "item ID is outside the supported range";
                case OpenACAISonsItemGrantStatus.InvalidAmount: return "unsupported amount";
                case OpenACAISonsItemGrantStatus.SessionUnavailable: return "the selected session ended or is unavailable";
                case OpenACAISonsItemGrantStatus.PlayerUnavailable: return "the remote player is not ready";
                case OpenACAISonsItemGrantStatus.ItemUnavailable: return "the current item definition is unavailable";
                default: return "request preparation failed";
            }
        }
#else
        private bool ServerItemGrantsAvailable => false;
        private void GrantServerItem(PlayerEntry target, string targetId) => ServerItemGrantFailed("Give Target requires the current Modstoke Loader runtime");
        private bool IsServerApprovedItem(string steamId, int itemId) => false;
        private int GetServerItemApprovalCount(string steamId) => 0;
        private void RemoveServerItemApprovals(string steamId) { }
        private void ClearServerItemApprovals() { _lastServerItemGrant = ""; }
#endif

        private void ServerItemGrantFailed(string message)
        {
            _lastServerItemGrant = message;
            ActionFail(message);
        }
    }
}
