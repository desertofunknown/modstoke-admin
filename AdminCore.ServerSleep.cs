using System;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _blockServerSleepEvents;
        private string _serverSleepPolicyStatus = "Sleep event blocking is off.";
        private int _serverSleepRequestsSeen;
        private int _serverSleepRequestsRejected;
        private DateTime _lastServerSleepRequestUtc = DateTime.MinValue;
        private string _lastServerSleepDecision = "No request observed.";

#if NET11_0_OR_GREATER
        private bool LoaderSleepHookInstalled => OpenACAISonsServerSleep.State.Status == OpenACAISonsSleepHookStatus.Installed;
        private string LoaderSleepHookStatus => OpenACAISonsServerSleep.State.Status.ToString();
#else
        private bool LoaderSleepHookInstalled => false;
        private string LoaderSleepHookStatus => "Unsupported target";
#endif

        private void SetServerSleepBlocking(bool block)
        {
            if (!block)
            {
                StopServerSleepPolicy("Sleep event blocking is off. Previously rejected events are not replayed.");
                ActionOk(_serverSleepPolicyStatus);
                return;
            }
            if (!HasHostSession())
            {
                StopServerSleepPolicy("Sleep event blocking requires the multiplayer host in a loaded world.");
                ActionFail(_serverSleepPolicyStatus);
                return;
            }
            if (!LoaderSleepHookInstalled)
            {
                StopServerSleepPolicy("Sleep event blocking is unavailable: the shared sleep hook is not installed.");
                ActionFail(_serverSleepPolicyStatus);
                return;
            }
#if NET11_0_OR_GREATER
            if (!_blockServerSleepEvents)
            {
                OpenACAISonsServerSleep.Receiving += ReceiveServerSleep;
                _blockServerSleepEvents = true;
            }
            _serverSleepPolicyStatus = "Subsequent server Sleep events will be blocked in this host session.";
            ActionOk(_serverSleepPolicyStatus);
#endif
        }

        private void TickServerSleepPolicy()
        {
            if (!_blockServerSleepEvents) return;
            if (!_hostActionsAvailable)
                StopServerSleepPolicy("Sleep event blocking was cleared because host authority or the local world became unavailable.");
            else if (!LoaderSleepHookInstalled)
                StopServerSleepPolicy("Sleep event blocking was cleared because the shared hook became unavailable.");
        }

#if NET11_0_OR_GREATER
        private void ReceiveServerSleep(OpenACAISonsSleepRequest request)
        {
            if (!_blockServerSleepEvents || request == null || request.IsRejected) return;
            _serverSleepRequestsSeen = IncrementRequestCount(_serverSleepRequestsSeen);
            _lastServerSleepRequestUtc = request.ReceivedAt.UtcDateTime;
            if (!HasHostSession())
            {
                StopServerSleepPolicy("Sleep event blocking was cleared because the host world is no longer available.");
                _lastServerSleepDecision = "Continued native handling after host authority was lost.";
                return;
            }

            // This is a host-wide delivery rule. The event source is not an individual sleep voter.
            // Aborted is a raw payload flag, not a cancellation command.
            if (request.Reject())
            {
                _serverSleepRequestsRejected = IncrementRequestCount(_serverSleepRequestsRejected);
                _lastServerSleepDecision = "Blocked this server Sleep dispatch; active sleep and waiting indicators were not changed.";
            }
            else
            {
                _lastServerSleepDecision = "Could not block this Sleep event: its rejection window had closed.";
            }
        }
#endif

        private void StopServerSleepPolicy(string detail)
        {
#if NET11_0_OR_GREATER
            if (_blockServerSleepEvents)
                OpenACAISonsServerSleep.Receiving -= ReceiveServerSleep;
#endif
            _blockServerSleepEvents = false;
            _serverSleepPolicyStatus = detail;
        }

        private void ResetServerSleepPolicy()
        {
            StopServerSleepPolicy("Sleep event blocking is off.");
            _serverSleepRequestsSeen = 0;
            _serverSleepRequestsRejected = 0;
            _lastServerSleepRequestUtc = DateTime.MinValue;
            _lastServerSleepDecision = "No request observed.";
        }
    }
}
