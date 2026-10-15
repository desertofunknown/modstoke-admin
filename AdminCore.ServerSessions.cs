using System;
#if NET11_0_OR_GREATER
using System.Collections.Generic;
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private int _loaderSessionsOpened;
        private int _loaderSessionsClosed;
        private int _loaderSessionRegistrationsChanged;
        private int _loaderSessionRecordsCleared;
        private DateTime _lastLoaderSessionNoticeUtc = DateTime.MinValue;
        private string _lastLoaderSessionNotice = "No notification observed";

#if NET11_0_OR_GREATER
        private DateTime _nextLoaderSessionSweepUtc = DateTime.MinValue;
        private bool _loaderSessionTrackingAvailable;

        internal bool LoaderSessionsSubscribed { get; private set; }
        internal bool LoaderSessionHooksAvailable => LoaderSessionsSubscribed && OpenACAISonsServerSessions.HooksInstalled;
        internal string LoaderSessionStatus => !LoaderSessionsSubscribed ? "Not subscribed"
            : OpenACAISonsServerSessions.HooksInstalled ? "Registered" : "Tracking unavailable";

        private void InitializeLoaderSessionIntegration()
        {
            if (LoaderSessionsSubscribed) return;
            OpenACAISonsServerSessions.Opened += ReceiveLoaderSessionOpened;
            OpenACAISonsServerSessions.Closed += ReceiveLoaderSessionClosed;
            OpenACAISonsServerSessions.RegistrationChanged += ReceiveLoaderSessionRegistration;
            LoaderSessionsSubscribed = true;
            _nextLoaderSessionSweepUtc = DateTime.MinValue;
        }

        private void StopLoaderSessionIntegration()
        {
            if (!LoaderSessionsSubscribed) return;
            LoaderSessionsSubscribed = false;
            OpenACAISonsServerSessions.Opened -= ReceiveLoaderSessionOpened;
            OpenACAISonsServerSessions.Closed -= ReceiveLoaderSessionClosed;
            OpenACAISonsServerSessions.RegistrationChanged -= ReceiveLoaderSessionRegistration;
            ReconcileLoaderSessionState(null);
            _loaderSessionTrackingAvailable = false;
        }

        private void ReceiveLoaderSessionOpened(OpenACAISonsServerSession session)
        {
            if (!LoaderSessionsSubscribed || session == null) return;
            _loaderSessionsOpened = IncrementRequestCount(_loaderSessionsOpened);
            RecordLoaderSessionNotice(session.IsCurrent ? "Opened notification received"
                : "Opened notification received after this lifetime ended or tracking became unavailable");
        }

        private void ReceiveLoaderSessionClosed(OpenACAISonsServerSession session)
        {
            if (!LoaderSessionsSubscribed || session == null) return;
            _loaderSessionsClosed = IncrementRequestCount(_loaderSessionsClosed);
            RecordLoaderSessionNotice("Closed notification received; only matching lifetime state is cleared");
            ReconcileLoaderSessionState(session);
        }

        private void ReceiveLoaderSessionRegistration(OpenACAISonsServerSession session)
        {
            if (!LoaderSessionsSubscribed || session == null) return;
            _loaderSessionRegistrationsChanged = IncrementRequestCount(_loaderSessionRegistrationsChanged);
            RecordLoaderSessionNotice("Registration change observed; roster identity will be read during refresh");
        }

        private void RecordLoaderSessionNotice(string detail)
        {
            _lastLoaderSessionNoticeUtc = DateTime.UtcNow;
            _lastLoaderSessionNotice = detail;
            _nextRosterRefreshUtc = DateTime.MinValue;
            _nextLoaderSessionSweepUtc = DateTime.MinValue;
        }

        private void TickLoaderSessionIntegration()
        {
            if (!LoaderSessionsSubscribed) return;
            bool available = OpenACAISonsServerSessions.HooksInstalled;
            if (_loaderSessionTrackingAvailable != available)
            {
                _loaderSessionTrackingAvailable = available;
                _nextRosterRefreshUtc = DateTime.MinValue;
                _nextLoaderSessionSweepUtc = DateTime.MinValue;
            }
            DateTime now = DateTime.UtcNow;
            if (now < _nextLoaderSessionSweepUtc) return;
            _nextLoaderSessionSweepUtc = now.AddSeconds(1);

            // Tracking can stop without delivering Closed. Managed handles and the membership snapshot
            // are sufficient for cleanup; a native connection must not be read after its retirement.
            if (ReconcileLoaderSessionState(null) > 0) _nextRosterRefreshUtc = DateTime.MinValue;
        }

        private int ReconcileLoaderSessionState(OpenACAISonsServerSession closedSession)
        {
            bool releaseAll = !LoaderSessionHooksAvailable;
            HashSet<Guid> currentSessions = null;
            if (closedSession == null)
            {
                currentSessions = new HashSet<Guid>();
                if (!releaseAll)
                {
                    foreach (var session in OpenACAISonsServerSessions.Sessions)
                        if (session.IsCurrent) currentSessions.Add(session.SessionId);
                }
            }

            int cleared = 0;
            var expiredApprovals = new List<string>();
            foreach (var pair in _serverItemApprovals)
            {
                if (ShouldReleaseLoaderSession(pair.Value.Session, closedSession, releaseAll))
                    expiredApprovals.Add(pair.Key);
            }
            foreach (string steamId in expiredApprovals)
            {
                _serverItemApprovals.Remove(steamId);
                cleared++;
            }

            foreach (var pair in _inventorySignals)
            {
                InventorySignalState signal = pair.Value;
                if (signal == null) continue;
                if (ShouldReleaseLoaderSession(signal.PassiveSession, closedSession, releaseAll))
                {
                    ResetPassiveEquipmentBaseline(pair.Key);
                    signal.LastPassiveReadStatus = releaseAll
                        ? "Session tracking unavailable; last successful snapshot retained"
                        : "Source session ended; last successful snapshot retained";
                    cleared++;
                }
                if (signal.PendingAuditSessionId != Guid.Empty
                    && (closedSession != null ? signal.PendingAuditSessionId == closedSession.SessionId
                        : !currentSessions.Contains(signal.PendingAuditSessionId)))
                {
                    ClearPendingInventoryAudit(pair.Key);
                    cleared++;
                }
            }

            cleared += RemoveEndedMovementSamples(_movementSignals, closedSession, releaseAll);
            cleared += RemoveEndedMovementSamples(_movementTelemetrySignals, closedSession, releaseAll);
            cleared += InvalidateEndedConnectionObservations(closedSession, releaseAll);
            _loaderSessionRecordsCleared = (int)Math.Min(Int32.MaxValue, (long)_loaderSessionRecordsCleared + cleared);
            return cleared;
        }

        private static bool ShouldReleaseLoaderSession(OpenACAISonsServerSession session,
            OpenACAISonsServerSession closedSession, bool releaseAll)
        {
            if (session == null) return false;
            // Steam IDs and connection numbers can be reused before an old notification is delivered.
            return closedSession != null ? ReferenceEquals(session, closedSession) : releaseAll || !session.IsCurrent;
        }

        private static int RemoveEndedMovementSamples(Dictionary<string, MovementSignalState> states,
            OpenACAISonsServerSession closedSession, bool releaseAll)
        {
            var ended = new List<string>();
            foreach (var pair in states)
            {
                if (ShouldReleaseLoaderSession(pair.Value?.SampleSession, closedSession, releaseAll))
                    ended.Add(pair.Key);
            }
            foreach (string steamId in ended) states.Remove(steamId);
            return ended.Count;
        }
#else
        internal bool LoaderSessionsSubscribed => false;
        internal bool LoaderSessionHooksAvailable => false;
        internal string LoaderSessionStatus => "Unsupported target";
        private void TickLoaderSessionIntegration() { }
#endif

        private void ResetLoaderSessionEvidence()
        {
            _loaderSessionsOpened = 0;
            _loaderSessionsClosed = 0;
            _loaderSessionRegistrationsChanged = 0;
            _loaderSessionRecordsCleared = 0;
            _lastLoaderSessionNoticeUtc = DateTime.MinValue;
            _lastLoaderSessionNotice = "No notification observed";
#if NET11_0_OR_GREATER
            _nextLoaderSessionSweepUtc = DateTime.MinValue;
#endif
        }
    }
}
