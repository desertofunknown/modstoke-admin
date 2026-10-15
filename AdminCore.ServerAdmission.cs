using System;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _serverAdmissionHasRead;
        private bool _serverAdmissionIncomingEnabled;
        private bool _serverAdmissionAutomaticAcceptance;
        private int _serverAdmissionConnectionLimit;
        private DateTime _serverAdmissionReadUtc = DateTime.MinValue;
        private string _serverAdmissionReadStatus = "Read the native settings to enable request controls.";
        private DateTime _serverAdmissionRequestUtc = DateTime.MinValue;
        private string _serverAdmissionRequestStatus = "No incoming-request change recorded.";

#if NET11_0_OR_GREATER
        private bool ServerAdmissionSupported => true;
#else
        private bool ServerAdmissionSupported => false;
#endif

        private void ReadServerAdmissionSettings()
        {
            bool read = TryReadServerAdmissionSettings();
            ActionResult(read, _serverAdmissionReadStatus, _serverAdmissionReadStatus);
        }

        private bool TryReadServerAdmissionSettings()
        {
            ClearServerAdmissionRead("Native admission settings are unavailable.");
            if (!HasHostSession())
            {
                _serverAdmissionReadStatus = "Admission settings require a multiplayer host in a loaded world.";
                return false;
            }
#if NET11_0_OR_GREATER
            try
            {
                var status = OpenACAISonsServerAdmission.TryRead(out var snapshot);
                if (status != OpenACAISonsServerAdmissionStatus.Success)
                {
                    _serverAdmissionReadStatus = DescribeServerAdmissionFailure(status);
                    return false;
                }
                if (!HasHostSession())
                {
                    _serverAdmissionReadStatus = "Host authority changed during the admission read.";
                    return false;
                }
                _serverAdmissionIncomingEnabled = snapshot.IncomingRequestsEnabled;
                _serverAdmissionAutomaticAcceptance = snapshot.AutomaticAcceptanceEnabled;
                _serverAdmissionConnectionLimit = snapshot.ConnectionLimit;
                _serverAdmissionReadUtc = snapshot.ObservedAt.UtcDateTime;
                _serverAdmissionHasRead = true;
                _serverAdmissionReadStatus = "Native settings read; join availability still depends on the game's other checks.";
                return true;
            }
            catch (Exception ex)
            {
                LogWarning("Admission settings read failed: " + ex.GetType().Name);
                _serverAdmissionReadStatus = "Could not read the native admission settings.";
                return false;
            }
#else
            _serverAdmissionReadStatus = "Admission controls require the current Modstoke SDK.";
            return false;
#endif
        }

        private void SetServerIncomingRequests(bool enabled)
        {
            if (!_serverAdmissionHasRead)
            {
                ReportServerAdmissionRequest(false, "Read the native settings before changing incoming requests.");
                return;
            }
            // Refresh authority and configuration before using a retained UI observation.
            if (!TryReadServerAdmissionSettings())
            {
                ReportServerAdmissionRequest(false, "Incoming-request flag was not changed: " + _serverAdmissionReadStatus);
                return;
            }
            ClearServerAdmissionRead("Waiting for a new observation after the request.");
#if NET11_0_OR_GREATER
            string requested = enabled ? "enabled" : "disabled";
            OpenACAISonsServerAdmissionStatus status;
            try { status = OpenACAISonsServerAdmission.SetIncomingRequestsEnabled(enabled); }
            catch (Exception ex)
            {
                LogWarning("Incoming-request change failed: " + ex.GetType().Name);
                ReportServerAdmissionRequest(false, "Incoming-request change has an unknown outcome; no retry was made.");
                TryReadServerAdmissionSettings();
                return;
            }

            bool confirmed = status == OpenACAISonsServerAdmissionStatus.Success
                || status == OpenACAISonsServerAdmissionStatus.AlreadySet;
            string detail;
            switch (status)
            {
                case OpenACAISonsServerAdmissionStatus.Success:
                    detail = "Incoming requests " + requested + " in local configuration readback; transport-worker behavior is unconfirmed.";
                    break;
                case OpenACAISonsServerAdmissionStatus.AlreadySet:
                    detail = "Incoming requests were already " + requested + "; no write was attempted.";
                    break;
                case OpenACAISonsServerAdmissionStatus.ApplyUncertain:
                    detail = "Incoming-request write was attempted, but its outcome is uncertain; no retry or restoration was made.";
                    break;
                default:
                    detail = DescribeServerAdmissionFailure(status);
                    break;
            }
            ReportServerAdmissionRequest(confirmed, detail);
            // This independent observation cannot turn an uncertain write into a confirmed operation.
            TryReadServerAdmissionSettings();
#else
            ReportServerAdmissionRequest(false, "Admission controls require the current Modstoke SDK.");
#endif
        }

        private void ReportServerAdmissionRequest(bool accepted, string detail)
        {
            _serverAdmissionRequestUtc = DateTime.UtcNow;
            _serverAdmissionRequestStatus = detail;
            ActionResult(accepted, detail, detail);
        }

        private void ClearServerAdmissionRead(string status)
        {
            _serverAdmissionHasRead = false;
            _serverAdmissionIncomingEnabled = false;
            _serverAdmissionAutomaticAcceptance = false;
            _serverAdmissionConnectionLimit = 0;
            _serverAdmissionReadUtc = DateTime.MinValue;
            _serverAdmissionReadStatus = status;
        }

        private void TickServerAdmissionAvailability()
        {
            if (!_hostActionsAvailable && _serverAdmissionHasRead)
                ClearServerAdmissionRead("Host authority is unavailable; read the settings again after joining as host.");
        }

        private void ResetServerAdmissionObservations()
        {
            // Clearing console observations does not restore the native gate.
            ClearServerAdmissionRead("Read the native settings to enable request controls.");
            _serverAdmissionRequestUtc = DateTime.MinValue;
            _serverAdmissionRequestStatus = "No incoming-request change recorded.";
        }

#if NET11_0_OR_GREATER
        private static string DescribeServerAdmissionFailure(OpenACAISonsServerAdmissionStatus status)
        {
            switch (status)
            {
                case OpenACAISonsServerAdmissionStatus.Unavailable:
                    return "The loader's admission service is unavailable.";
                case OpenACAISonsServerAdmissionStatus.WrongThread:
                    return "Admission settings must be accessed on the game's thread.";
                case OpenACAISonsServerAdmissionStatus.NotServer:
                    return "The network server is no longer running.";
                case OpenACAISonsServerAdmissionStatus.TransportUnavailable:
                    return "The live host transport configuration is unavailable.";
                case OpenACAISonsServerAdmissionStatus.Failed:
                    return "Native admission settings could not be accessed; no gate write was attempted.";
                default:
                    return "Admission service returned " + status + "; no confirmed outcome is available.";
            }
        }
#endif
    }
}
