using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
#if NET11_0_OR_GREATER
        private readonly List<TrackedModerationNotice> _trackedModerationNotices = new List<TrackedModerationNotice>();
        private DateTime _nextModerationNoticeReadUtc;

        private sealed class TrackedModerationNotice
        {
            public string SteamId;
            public string Source;
            public OpenACAISonsNoticeDelivery Delivery;
            public OpenACAISonsNoticeDeliveryStatus LastStatus;
        }
#endif

        // False permits the existing native route only when no SDK payload was attempted.
        private bool TrySendLoaderModerationNotice(string steamId, string message, string source)
        {
            return TrySendLoaderNotice(steamId, message, source, out _, out _);
        }

        // Handled and queued are separate: a handled request may have failed or have an unknown outcome.
        private bool TrySendLoaderNotice(string steamId, string message, string source, out bool queued, out string outcome)
        {
            queued = false;
            outcome = "No SDK notice was attempted.";
#if NET11_0_OR_GREATER
            if (!HasHostSession() || !OpenACAISonsServerNotices.HooksInstalled || !OpenACAISonsServerSessions.HooksInstalled
                || String.Equals(steamId, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal)) return false;
            if (!UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
                || !TryResolveServerIdentity(id, out var identity, out _))
            {
                outcome = "No unique current remote identity; no notice was sent.";
                RecordModerationNotice(steamId, source, "SDK recipient lookup", outcome);
                return true;
            }

            string text = FormatLoaderNotice(message);
            if (String.IsNullOrWhiteSpace(text))
            {
                outcome = "No plain text remains; no notice was sent.";
                RecordModerationNotice(steamId, source, "SDK notice", outcome);
                return true;
            }
            bool plainSend = false;
            OpenACAISonsNoticeDelivery delivery = null;
            OpenACAISonsNoticeSendResult result;
            try
            {
                result = OpenACAISonsServerNotices.SendTrackedToSession(identity.Session, text, out delivery);
                if (result == OpenACAISonsNoticeSendResult.ReceiptsUnsupported)
                {
                    // Receipt negotiation failed before sending. A plain notice still targets the same lifetime.
                    plainSend = true;
                    result = OpenACAISonsServerNotices.SendToSession(identity.Session, text);
                }
            }
            catch (Exception ex)
            {
                LogWarning("Moderation notice request failed: " + ex.GetType().Name);
                outcome = "Send outcome unknown; no retry or native fallback.";
                RecordModerationNotice(steamId, source, "SDK notice", outcome);
                TrackModerationNotice(steamId, source, delivery);
                return true;
            }

            if (result == OpenACAISonsNoticeSendResult.PeerUnavailable)
            {
                outcome = "No negotiated live peer; native notice route will be attempted.";
                RecordModerationNotice(steamId, source, "SDK notice", outcome);
                return false;
            }
            switch (result)
            {
                case OpenACAISonsNoticeSendResult.Queued:
                    queued = true;
                    outcome = plainSend ? "Queued without receipts; display is unconfirmed." : "Queued with receipt tracking; display is unconfirmed."; break;
                case OpenACAISonsNoticeSendResult.SendUncertain:
                    outcome = "Send uncertain; retaining available receipt tracking without retry or native fallback."; break;
                case OpenACAISonsNoticeSendResult.Failed:
                    outcome = plainSend ? "Plain send failed with an uncertain outcome; no retry or native fallback."
                        : "Tracked request failed before sending; no retry or native fallback."; break;
                case OpenACAISonsNoticeSendResult.PendingLimitReached:
                    outcome = "The peer's receipt queue is full; no notice sent and no fallback."; break;
                case OpenACAISonsNoticeSendResult.Unavailable:
                    outcome = "Notice services became unavailable; no fallback."; break;
                case OpenACAISonsNoticeSendResult.NotServer:
                    outcome = "Host authority is unavailable; no notice sent."; break;
                case OpenACAISonsNoticeSendResult.WrongThread:
                    outcome = "The notice must be sent on the game's thread; no notice sent."; break;
                case OpenACAISonsNoticeSendResult.InvalidMessage:
                    outcome = "The notice service rejected the text; no notice sent."; break;
                default:
                    outcome = "Unrecognized notice result; no retry or native fallback."; break;
            }
            RecordModerationNotice(steamId, source, "SDK " + (plainSend ? "plain" : "tracked") + " notice: " + text, outcome);
            TrackModerationNotice(steamId, source, delivery);
            return true;
#else
            return false;
#endif
        }

        private static string FormatLoaderNotice(string message)
        {
            return FormatLoaderNotice(message, out _);
        }

        private static string FormatLoaderNotice(string message, out bool shortened)
        {
            var text = new StringBuilder();
            int bytes = 0;
            shortened = false;
            foreach (Rune original in (message ?? "").EnumerateRunes())
            {
                Rune rune = original;
                if (Rune.IsControl(rune) || rune.Value == 0x2028 || rune.Value == 0x2029) rune = new Rune(' ');
                else if (rune.Value == '<') rune = new Rune('\uFF1C');
                else if (rune.Value == '>') rune = new Rune('\uFF1E');
                if (bytes + rune.Utf8SequenceLength > 256)
                {
                    shortened = true;
                    break;
                }
                text.Append(rune.ToString());
                bytes += rune.Utf8SequenceLength;
            }
            return text.ToString().Trim();
        }

        private void RecordModerationNotice(string steamId, string source, string observation, string outcome)
        {
            RecordGuardEvidence(steamId, "Actions", source, observation,
                "Notification only; transport results and client claims do not change moderation decisions.", outcome);
        }

        private bool SendNativeModerationNotice(string steamId, string message, string source, object connection, object entity)
        {
            if (!HasLiveConnectionProof(connection, steamId)) connection = null;
            if (!HasLiveEntityProof(entity, steamId)) entity = null;
            if (UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            {
                if (connection == null)
                {
                    object current = GetConnectionFromSteamId(id);
                    if (HasLiveConnectionProof(current, steamId)) connection = current;
                }
                if (entity == null)
                {
                    object current = GetEntityFromSteamId(id);
                    if (HasLiveEntityProof(current, steamId)) entity = current;
                }
            }
            bool sent = SendNetworkMessageTo(message, connection, entity, false);
            RecordModerationNotice(steamId, source, "Native notice: " + message,
                sent ? "Native dispatch returned; delivery and display are unconfirmed."
                    : "Native dispatch could not be confirmed; no second send was attempted.");
            return sent;
        }

#if NET11_0_OR_GREATER
        private void TrackModerationNotice(string steamId, string source, OpenACAISonsNoticeDelivery delivery)
        {
            if (delivery == null) return;
            var state = delivery.State;
            RecordModerationNotice(steamId, source, "Notice " + delivery.Id.ToString("D"), DescribeNoticeDelivery(state));
            if (!state.IsTerminal)
                _trackedModerationNotices.Add(new TrackedModerationNotice
                {
                    SteamId = steamId, Source = source, Delivery = delivery, LastStatus = state.Status
                });
        }

        private static string DescribeNoticeDelivery(OpenACAISonsNoticeDeliveryState state)
        {
            string detail;
            switch (state.Status)
            {
                case OpenACAISonsNoticeDeliveryStatus.ClientAccepted:
                    detail = "Client reports accepting the notice into its queue; display is unconfirmed."; break;
                case OpenACAISonsNoticeDeliveryStatus.ClientHudInvoked:
                    detail = "Client reports that its HUD call returned; rendering and reading are unconfirmed."; break;
                case OpenACAISonsNoticeDeliveryStatus.ClientExpired:
                    detail = "Client reports that the queued notice expired."; break;
                case OpenACAISonsNoticeDeliveryStatus.ClientQueueEvicted:
                    detail = "Client reports evicting the notice from its queue."; break;
                case OpenACAISonsNoticeDeliveryStatus.ClientDisplayFailed:
                    detail = "Client reports a display failure."; break;
                case OpenACAISonsNoticeDeliveryStatus.TimedOut:
                    detail = "Receipt tracking timed out; delivery remains unconfirmed."; break;
                case OpenACAISonsNoticeDeliveryStatus.PeerUnavailable:
                    detail = "Peer became unavailable; prior delivery remains unconfirmed."; break;
                case OpenACAISonsNoticeDeliveryStatus.Unavailable:
                    detail = "Receipt service became unavailable; prior delivery remains unconfirmed."; break;
                default:
                    detail = state.Status + "; display is unconfirmed."; break;
            }
            return detail + " Observed " + state.ObservedAt.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }
#endif

        private void TickModerationNoticeTracking()
        {
#if NET11_0_OR_GREATER
            if (!_hostActionsAvailable)
            {
                ClearModerationNoticeTracking();
                return;
            }
            if (DateTime.UtcNow < _nextModerationNoticeReadUtc) return;
            _nextModerationNoticeReadUtc = DateTime.UtcNow.AddSeconds(1);
            for (int index = _trackedModerationNotices.Count - 1; index >= 0; index--)
            {
                var tracked = _trackedModerationNotices[index];
                var state = tracked.Delivery.State;
                if (state.Status != tracked.LastStatus)
                {
                    RecordModerationNotice(tracked.SteamId, tracked.Source, "Notice " + tracked.Delivery.Id.ToString("D"), DescribeNoticeDelivery(state));
                    tracked.LastStatus = state.Status;
                }
                if (state.IsTerminal) _trackedModerationNotices.RemoveAt(index);
            }
#endif
        }

        private void ClearModerationNoticeTracking()
        {
#if NET11_0_OR_GREATER
            _trackedModerationNotices.Clear();
            _nextModerationNoticeReadUtc = DateTime.MinValue;
#endif
        }
    }
}
