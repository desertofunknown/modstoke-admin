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
#if NET11_0_OR_GREATER
        private bool _loaderAuditIntegrationRegistered;
        private IDisposable _loaderAuditCommandRegistration;
        private readonly Dictionary<string, PendingLoaderAudit> _pendingLoaderAudits = new Dictionary<string, PendingLoaderAudit>();
        private long _nextLoaderAuditCheck;

        private sealed class PendingLoaderAudit
        {
            internal OpenACAISonsServerSession Session;
            internal Guid NoticeId;
            internal long ExpiresAt;
            internal string[] Chunks;
            internal string Version;
            internal int Received;
        }

        private bool LoaderAuditTransportAvailable => _loaderAuditIntegrationRegistered
            && LoaderChatHookInstalled && OpenACAISonsServerNotices.HooksInstalled && OpenACAISonsCommands.HooksInstalled;

        private void InitializeLoaderAuditIntegration()
        {
            if (_loaderAuditIntegrationRegistered) return;
            try
            {
                _loaderAuditCommandRegistration = OpenACAISonsCommands.Register(LoaderAuditCommandName,
                    AuthorizeLoaderAuditCommand, ReceiveLoaderAuditCommand);
                OpenACAISonsServerNotices.Received += ReceiveLoaderAuditNotice;
                _loaderAuditIntegrationRegistered = true;
            }
            catch (Exception ex)
            {
                _loaderAuditCommandRegistration?.Dispose();
                _loaderAuditCommandRegistration = null;
                LogWarning("SDK inventory audit registration failed: " + ex.Message);
            }
        }

        private void StopLoaderAuditIntegration()
        {
            _loaderAuditIntegrationRegistered = false;
            OpenACAISonsServerNotices.Received -= ReceiveLoaderAuditNotice;
            _loaderAuditCommandRegistration?.Dispose();
            _loaderAuditCommandRegistration = null;
            ClearLoaderAuditState();
        }

        // False permits the legacy route only before attempting an SDK send.
        private bool TryRequestLoaderInventoryAudit(string steamId, InventorySignalState state, out bool sent, out string detail)
        {
            sent = false;
            detail = "";
            _pendingLoaderAudits.Remove(steamId);
            if (!LoaderAuditTransportAvailable) return false;
            if (!HasHostSession() || !UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            {
                detail = "SDK audit requires a player-host world and a valid Steam64 ID";
                return true;
            }
            if (!TryResolveServerIdentity(id, out var identity, out string failure))
            {
                detail = "SDK audit not sent: " + failure;
                return true;
            }
            OpenACAISonsNoticePeer recipient = null;
            foreach (var peer in OpenACAISonsServerNotices.Peers)
            {
                if (!ReferenceEquals(peer.Session, identity.Session)) continue;
                if (peer.RegisteredSteamId.HasValue && peer.RegisteredSteamId.Value != id)
                {
                    detail = "SDK audit not sent: negotiated peer registration and game identity disagree";
                    return true;
                }
                if (peer.SupportsReceipts && peer.SupportsCommands) recipient = peer;
                break;
            }
            if (recipient == null) return false;

            OpenACAISonsNoticeDelivery delivery = null;
            OpenACAISonsNoticeSendResult result;
            try { result = OpenACAISonsServerNotices.SendTracked(recipient, LoaderAuditNoticeMessage, out delivery); }
            catch (Exception ex)
            {
                LogWarning("SDK inventory audit request failed: " + ex.GetType().Name);
                result = OpenACAISonsNoticeSendResult.SendUncertain;
            }
            if ((result == OpenACAISonsNoticeSendResult.Queued || result == OpenACAISonsNoticeSendResult.SendUncertain) && delivery != null)
            {
                state.LastAuditNonce = delivery.Id.ToString("N");
                state.PendingAuditSessionId = identity.Session.SessionId;
                _pendingLoaderAudits[steamId] = new PendingLoaderAudit
                {
                    Session = identity.Session,
                    NoticeId = delivery.Id,
                    ExpiresAt = Environment.TickCount64 + 45000
                };
                sent = result == OpenACAISonsNoticeSendResult.Queued;
                detail = sent ? "SDK audit request queued; waiting for a complete client report"
                    : "SDK audit send is uncertain; waiting for a possible report without retrying";
            }
            else
            {
                state.PendingAuditSessionId = Guid.Empty;
                detail = "SDK audit request " + result + "; no fallback or retry was made";
            }
            return true;
        }

        private bool AuthorizeLoaderAuditCommand(OpenACAISonsCommandContext context)
        {
            return context != null && TryGetPendingLoaderAudit(context.Identity, context.Arguments, out _, out _);
        }

        private bool TryGetPendingLoaderAudit(OpenACAISonsServerIdentitySnapshot identity, string arguments,
            out string steamId, out PendingLoaderAudit pending)
        {
            steamId = "";
            pending = null;
            if (!_loaderAuditIntegrationRegistered || !_clientInventoryAudit || !HasHostSession() || identity == null
                || !TryParseLoaderAuditChunk(arguments, out Guid noticeId, out _, out _, out _, out _)) return false;
            steamId = identity.GameSteamId.ToString(CultureInfo.InvariantCulture);
            return LooksLikeSteamId(steamId)
                && (!identity.RegisteredSteamId.HasValue || identity.RegisteredSteamId.Value == identity.GameSteamId)
                && _pendingLoaderAudits.TryGetValue(steamId, out pending)
                && ReferenceEquals(pending.Session, identity.Session)
                && pending.NoticeId == noticeId && Environment.TickCount64 < pending.ExpiresAt
                && OwnsPendingLoaderAudit(steamId, pending);
        }

        private bool IsPendingLoaderAuditCommand(OpenACAISonsChatRequest request, OpenACAISonsServerIdentitySnapshot identity)
        {
            return request.Origin == OpenACAISonsChatRequestOrigin.ClientCommand
                && request.Message.Length > LoaderAuditCommandName.Length + 2
                && TryGetPendingLoaderAudit(identity, request.Message.Substring(LoaderAuditCommandName.Length + 2), out _, out _);
        }

        private OpenACAISonsCommandResult ReceiveLoaderAuditCommand(OpenACAISonsCommandContext context)
        {
            if (!TryGetPendingLoaderAudit(context.Identity, context.Arguments, out string steamId, out var pending))
                return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.Denied);
            TryParseLoaderAuditChunk(context.Arguments, out _, out int index, out int total, out string version, out string payload);
            if (payload == "!unavailable" || payload == "!too-large")
            {
                if (index != 0 || total != 1 || pending.Chunks != null)
                    return RejectLoaderAuditReport(steamId, pending, "conflicting failure report");
                FinishLoaderAuditWithoutReport(steamId, pending, payload == "!unavailable"
                    ? "client could not read inventory; previous observations retained"
                    : "client report exceeds the transport limit; previous observations retained");
                return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.Succeeded);
            }
            if (!TryReadLoaderAuditChunkItems(payload, total, out _))
                return RejectLoaderAuditReport(steamId, pending, "malformed item counts");
            if (pending.Chunks == null)
            {
                pending.Chunks = new string[total];
                pending.Version = version;
            }
            else if (pending.Chunks.Length != total || !String.Equals(pending.Version, version, StringComparison.Ordinal))
                return RejectLoaderAuditReport(steamId, pending, "inconsistent report metadata");
            if (pending.Chunks[index] != null)
            {
                if (!String.Equals(pending.Chunks[index], payload, StringComparison.Ordinal))
                    return RejectLoaderAuditReport(steamId, pending, "conflicting report chunk");
                return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.Succeeded);
            }
            pending.Chunks[index] = payload;
            pending.Received++;
            if (pending.Received != total)
            {
                _inventorySignals[steamId].LastAuditStatus = "SDK report receiving " + pending.Received + "/" + total + "; previous observations retained";
                return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.Succeeded);
            }

            var items = new Dictionary<int, int>();
            foreach (string chunk in pending.Chunks)
            {
                if (!TryReadLoaderAuditChunkItems(chunk, total, out var part))
                    return RejectLoaderAuditReport(steamId, pending, "malformed report chunk");
                foreach (var pair in part)
                    if (!items.TryAdd(pair.Key, pair.Value))
                        return RejectLoaderAuditReport(steamId, pending, "duplicate item across report chunks");
            }
            _pendingLoaderAudits.Remove(steamId);
            RecordInventoryAuditResponse(steamId, pending.NoticeId.ToString("N"), version, "client via SDK", items, false);
            return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.Succeeded);
        }

        private static bool TryReadLoaderAuditChunkItems(string payload, int total, out Dictionary<int, int> items)
        {
            if (payload == "-")
            {
                items = new Dictionary<int, int>();
                return total == 1;
            }
            return TryParseLoaderAuditItems(payload, out items) && items.Count > 0 && items.Count == payload.Split(',').Length;
        }

        private OpenACAISonsCommandResult RejectLoaderAuditReport(string steamId, PendingLoaderAudit pending, string reason)
        {
            FinishLoaderAuditWithoutReport(steamId, pending, "SDK report ignored: " + reason + "; previous observations retained");
            return new OpenACAISonsCommandResult(OpenACAISonsCommandStatus.InvalidArguments);
        }

        private bool OwnsPendingLoaderAudit(string steamId, PendingLoaderAudit pending)
        {
            return _inventorySignals.TryGetValue(steamId, out var state)
                && state.PendingAuditSessionId == pending.Session.SessionId
                && String.Equals(state.LastAuditNonce, pending.NoticeId.ToString("N"), StringComparison.Ordinal);
        }

        private void FinishLoaderAuditWithoutReport(string steamId, PendingLoaderAudit pending, string status)
        {
            _pendingLoaderAudits.Remove(steamId);
            if (!OwnsPendingLoaderAudit(steamId, pending)) return;
            var state = _inventorySignals[steamId];
            state.PendingAuditSessionId = Guid.Empty;
            state.LastAuditStatus = status;
            RecordGuardEvidence(steamId, "Items", "SDK inventory audit", "No complete inventory report",
                "Exact session and notice ID; all chunks required", status);
        }

        private void TickLoaderInventoryAudits()
        {
            if (Environment.TickCount64 < _nextLoaderAuditCheck) return;
            _nextLoaderAuditCheck = Environment.TickCount64 + 1000;
            foreach (var pair in new List<KeyValuePair<string, PendingLoaderAudit>>(_pendingLoaderAudits))
            {
                var pending = pair.Value;
                if (!OwnsPendingLoaderAudit(pair.Key, pending)) _pendingLoaderAudits.Remove(pair.Key);
                else if (!_clientInventoryAudit || !_hostActionsAvailable || !LoaderAuditTransportAvailable || !pending.Session.IsCurrent)
                    FinishLoaderAuditWithoutReport(pair.Key, pending, "SDK audit no longer available; previous observations retained");
                else if (Environment.TickCount64 >= pending.ExpiresAt)
                    FinishLoaderAuditWithoutReport(pair.Key, pending, "SDK audit timed out without a complete report; previous observations retained");
            }
            if (!_worldReady) ClearLoaderAuditClientState();
        }

        private void ClearLoaderAuditState()
        {
            foreach (var pair in _pendingLoaderAudits)
                if (OwnsPendingLoaderAudit(pair.Key, pair.Value)) _inventorySignals[pair.Key].PendingAuditSessionId = Guid.Empty;
            _pendingLoaderAudits.Clear();
            ClearLoaderAuditClientState();
        }
#else
        private bool LoaderAuditTransportAvailable => false;
        private void InitializeLoaderAuditIntegration() { }
        private void StopLoaderAuditIntegration() { }
        private void TickLoaderInventoryAudits() { }
        private void ClearLoaderAuditState() { ClearLoaderAuditClientState(); }
        private bool TryRequestLoaderInventoryAudit(string steamId, InventorySignalState state, out bool sent, out string detail)
        {
            sent = false;
            detail = "";
            return false;
        }
#endif

        private static bool IsLoaderAuditCommand(string message)
        {
            string prefix = "/" + LoaderAuditCommandName;
            return message != null && message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (message.Length == prefix.Length || message[prefix.Length] == ' ' || message[prefix.Length] == '\t');
        }
    }
}
