using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using System.Collections.Generic;
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderChatRequestsSeen;
        private int _loaderChatRequestsRejected;
        private int _loaderChatRequestsUnresolved;
        private DateTime _lastLoaderChatRequestUtc = DateTime.MinValue;
        private string _lastLoaderChatDecision = "No request observed";

#if NET11_0_OR_GREATER
        internal bool LoaderChatSubscribed { get; private set; }
        internal bool LoaderChatHookInstalled => LoaderChatSubscribed
            && OpenACAISonsServerChat.State.Status == OpenACAISonsServerChatHookStatus.Installed;
        internal string LoaderChatStatus => LoaderChatSubscribed ? OpenACAISonsServerChat.State.Status.ToString() : "Not subscribed";

        private void InitializeLoaderChatIntegration()
        {
            if (LoaderChatSubscribed) return;
            OpenACAISonsServerChat.Receiving += ReceiveLoaderChat;
            LoaderChatSubscribed = true;
        }

        private void StopLoaderChatIntegration()
        {
            if (!LoaderChatSubscribed) return;
            LoaderChatSubscribed = false;
            OpenACAISonsServerChat.Receiving -= ReceiveLoaderChat;
            foreach (var state in _inventorySignals.Values) state.PendingAuditSessionId = Guid.Empty;
        }

        private void ReceiveLoaderChat(OpenACAISonsChatRequest request)
        {
            if (!LoaderChatSubscribed || request == null || request.IsRejected) return;
            _loaderChatRequestsSeen = IncrementRequestCount(_loaderChatRequestsSeen);
            _lastLoaderChatRequestUtc = request.ReceivedAt.UtcDateTime;
            string message = request.Message;
            if (String.IsNullOrEmpty(message))
            {
                RejectLoaderChat(request, "Rejected an empty message");
                return;
            }

            bool auditReply = message.StartsWith(AuditResponsePrefix, StringComparison.Ordinal);
            // Inventory replies are private control traffic, including malformed or unresolvable replies.
            if (auditReply && !RejectLoaderChat(request, "Consumed a private inventory reply")) return;
            if (!request.SourceConnectionId.HasValue)
            {
                _lastLoaderChatDecision = auditReply ? "Ignored a server-local inventory reply" : "Native handling: server-local message";
                return;
            }
            if (!OpenACAISonsServerSessions.TryReadIdentity(request.SourceSession, out var identity, out var readStatus))
            {
                DeferLoaderChat(auditReply, "Source identity " + readStatus);
                return;
            }
            if (!ReferenceEquals(identity.Session, request.SourceSession)
                || (request.RegisteredSteamId.HasValue && request.RegisteredSteamId.Value != identity.GameSteamId)
                || (identity.RegisteredSteamId.HasValue && identity.RegisteredSteamId.Value != identity.GameSteamId))
            {
                DeferLoaderChat(auditReply, "Source identity and registration disagree");
                return;
            }

            string steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            if (!LooksLikeSteamId(steamId))
            {
                DeferLoaderChat(auditReply, "Source Steam64 ID is unavailable");
                return;
            }
            if (auditReply)
            {
                if (request.Origin == OpenACAISonsChatRequestOrigin.GameChat)
                    ReceiveLoaderInventoryAudit(request, steamId);
                else
                    IgnoreLoaderInventoryAudit(steamId, "the command channel is not an inventory reply route");
                return;
            }
            if (IsLoaderAuditCommand(message))
            {
                if (IsPendingLoaderAuditCommand(request, identity))
                    _lastLoaderChatDecision = "Continued a pending private inventory reply for command validation";
                else
                    RejectLoaderChat(request, "Rejected an unmatched private inventory command");
                return;
            }
            if (!_mutedPlayers.ContainsKey(steamId))
            {
                _lastLoaderChatDecision = "Continued " + request.Origin + "; native and command permission checks still apply";
                return;
            }
            if (!RejectLoaderChat(request, "Rejected muted player's " + request.Origin)) return;
            RecordGuardEvidence(steamId, "Server", "Loader chat / exact session",
                request.Origin + " (" + message.Length.ToString(CultureInfo.InvariantCulture) + " characters)",
                "Host mute list; source connection identity", _lastLoaderChatDecision);

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            if (entry != null) TryRefreshPlayerNetworkHandles(entry, identity.GameSteamId);
            SendMuteNotice(entry, steamId, false);
        }

        private bool RejectLoaderChat(OpenACAISonsChatRequest request, string decision)
        {
            if (!request.Reject())
            {
                _lastLoaderChatDecision = "Chat decision window unavailable";
                return false;
            }
            _loaderChatRequestsRejected = IncrementRequestCount(_loaderChatRequestsRejected);
            _lastLoaderChatDecision = decision;
            return true;
        }

        private void DeferLoaderChat(bool auditReply, string reason)
        {
            _loaderChatRequestsUnresolved = IncrementRequestCount(_loaderChatRequestsUnresolved);
            _lastLoaderChatDecision = (auditReply ? "Private reply ignored: " : "Native handling: ") + reason + "; no attributed punishment";
        }

        private bool BindInventoryAuditSession(string steamId, InventorySignalState state, out string failure)
        {
            state.PendingAuditSessionId = Guid.Empty;
            failure = "remote audits require an active player-host session";
            if (!HasHostSession()) return false;
            if (!LoaderChatHookInstalled)
            {
                failure = "the shared server chat reply route is unavailable";
                return false;
            }
            if (!UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)) return false;
            if (!TryResolveServerIdentity(id, out var identity, out failure)) return false;
            state.PendingAuditSessionId = identity.Session.SessionId;
            return true;
        }

        private void ReceiveLoaderInventoryAudit(OpenACAISonsChatRequest request, string steamId)
        {
            if (_pendingLoaderAudits.ContainsKey(steamId))
            {
                IgnoreLoaderInventoryAudit(steamId, "the pending SDK request requires a complete command-channel report");
                return;
            }
            if (!_clientInventoryAudit || !HasHostSession())
            {
                IgnoreLoaderInventoryAudit(steamId, "inventory audits are disabled or host authority is unavailable");
                return;
            }
            string[] parts = request.Message.Substring(AuditResponsePrefix.Length).Split(new[] { '|' }, 4);
            if (parts.Length != 4 || !String.Equals(parts[2], steamId, StringComparison.Ordinal))
            {
                IgnoreLoaderInventoryAudit(steamId, "the payload is malformed or its claimed player does not match the source");
                return;
            }
            if (!_inventorySignals.TryGetValue(steamId, out var state)
                || state.PendingAuditSessionId != request.SourceSession.SessionId
                || state.LastAuditRequestUtc == DateTime.MinValue
                || String.IsNullOrEmpty(state.LastAuditNonce)
                || !String.Equals(parts[0], state.LastAuditNonce, StringComparison.Ordinal))
            {
                IgnoreLoaderInventoryAudit(steamId, "no pending request matches this session and nonce");
                return;
            }
            if (!TryParseLoaderAuditItems(parts[3], out var items))
            {
                IgnoreLoaderInventoryAudit(steamId, "item counts are malformed; previous observations retained");
                return;
            }
            state.PendingAuditSessionId = Guid.Empty;
            _lastLoaderChatDecision = "Accepted a requested client-reported inventory snapshot; receipt is not inventory verification";
            RecordInventoryAuditResponse(steamId, parts[0], CleanConfigText(parts[1], 24), "client", items, false);
        }

        private void IgnoreLoaderInventoryAudit(string steamId, string reason)
        {
            _lastLoaderChatDecision = "Private inventory reply ignored: " + reason;
            RecordGuardEvidence(steamId, "Items", "Loader chat / exact session", "Private inventory reply",
                "Matching source identity, pending session and nonce required", _lastLoaderChatDecision);
        }

        private static bool TryParseLoaderAuditItems(string text, out Dictionary<int, int> items)
        {
            items = new Dictionary<int, int>();
            if (text.Length == 0) return true;
            var seen = new HashSet<int>();
            foreach (string pair in text.Split(','))
            {
                string[] fields = pair.Split(':');
                if (fields.Length != 2
                    || !Int32.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int itemId) || itemId <= 0
                    || !Int32.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0
                    || !seen.Add(itemId)) return false;
                if (count > 0) items.Add(itemId, count);
            }
            return true;
        }
#else
        internal bool LoaderChatSubscribed => false;
        internal bool LoaderChatHookInstalled => false;
        internal string LoaderChatStatus => "Unsupported target";
        private void InitializeLoaderChatIntegration() { }
        private void StopLoaderChatIntegration() { }
        private bool BindInventoryAuditSession(string steamId, InventorySignalState state, out string failure)
        {
            failure = "";
            return true;
        }
#endif

        private void ClearPendingInventoryAudit(string steamId)
        {
            if (_inventorySignals.TryGetValue(steamId, out var state) && state.PendingAuditSessionId != Guid.Empty)
            {
                state.PendingAuditSessionId = Guid.Empty;
                state.LastAuditStatus = "Source session ended or became unavailable; previous observations retained";
            }
#if NET11_0_OR_GREATER
            _pendingLoaderAudits.Remove(steamId);
#endif
        }

        private void ResetLoaderChatEvidence()
        {
            _loaderChatRequestsSeen = 0;
            _loaderChatRequestsRejected = 0;
            _loaderChatRequestsUnresolved = 0;
            _lastLoaderChatRequestUtc = DateTime.MinValue;
            _lastLoaderChatDecision = "No request observed";
        }
    }
}
