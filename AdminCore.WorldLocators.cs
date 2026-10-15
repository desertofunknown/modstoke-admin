using System;
using System.Globalization;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _locatorIdInput = "";
        private int _locatorStateChoice = 0;
        private Guid _locatorReadInputId;
        private Guid _locatorNativeKey;
        private bool _locatorRequestPrepared;
        private string _locatorReadStatus = "Enter a known locator GUID and read its local entries.";
        private string _locatorNativeId = "Not read";
        private string _locatorRegisteredState = "Not read";
        private string _locatorRecordedState = "Not read";
        private string _locatorUnappliedState = "Not read";
        private string _locatorPosition = "Not read";
        private string _locatorObservedAt = "Not read";
        private string _locatorRequestStatus = "No state request recorded.";

        private bool CanRequestWorldLocatorState
        {
            get
            {
#if NET11_0_OR_GREATER
                return _hostActionsAvailable && _locatorRequestPrepared
                    && Guid.TryParse((_locatorIdInput ?? "").Trim(), out Guid id) && id == _locatorReadInputId;
#else
                return false;
#endif
            }
        }

        private void ClearWorldLocatorRead()
        {
            _locatorRequestPrepared = false;
            _locatorReadInputId = Guid.Empty;
            _locatorNativeKey = Guid.Empty;
            _locatorNativeId = "Not read";
            _locatorRegisteredState = "Not read";
            _locatorRecordedState = "Not read";
            _locatorUnappliedState = "Not read";
            _locatorPosition = "Not read";
            _locatorObservedAt = "Not read";
        }

        private void ResetWorldLocatorInspector()
        {
            ClearWorldLocatorRead();
            _locatorReadStatus = "Enter a known locator GUID and read its local entries.";
            _locatorRequestStatus = "No state request recorded.";
        }

        private void ReadWorldLocator()
        {
            ClearWorldLocatorRead();
            if (!Guid.TryParse((_locatorIdInput ?? "").Trim(), out Guid id))
            {
                _locatorReadStatus = "Enter a locator GUID, not a network entity ID.";
                ActionFail(_locatorReadStatus);
                return;
            }
            if (!IsLocalPlayerInWorld())
            {
                _locatorReadStatus = "Join a world before reading a locator.";
                ActionFail(_locatorReadStatus);
                return;
            }
#if NET11_0_OR_GREATER
            try
            {
                if (!OpenACAISonsWorldLocatorReads.TryRead(id, out var snapshot, out var status) || snapshot == null)
                {
                    _locatorReadStatus = "Locator read unavailable: " + status + ". No current entries are displayed.";
                    ActionFail(_locatorReadStatus);
                    return;
                }
                _locatorReadInputId = id;
                _locatorNativeKey = snapshot.LocatorId;
                _locatorNativeId = snapshot.LocatorId.ToString("D");
                _locatorIdInput = id.ToString("D");
                _locatorRegisteredState = FormatWorldLocatorState(snapshot.RegisteredState, snapshot.KnownRegisteredState?.ToString());
                _locatorRecordedState = FormatWorldLocatorState(snapshot.RecordedState, snapshot.KnownRecordedState?.ToString());
                _locatorUnappliedState = FormatWorldLocatorState(snapshot.UnappliedState, snapshot.KnownUnappliedState?.ToString());
                if (snapshot.RegisteredPosition.HasValue)
                {
                    var position = snapshot.RegisteredPosition.Value;
                    _locatorPosition = IsFiniteDebugValue(position.X) && IsFiniteDebugValue(position.Y) && IsFiniteDebugValue(position.Z)
                        ? position.X.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                            + position.Y.ToString("0.##", CultureInfo.InvariantCulture) + ", "
                            + position.Z.ToString("0.##", CultureInfo.InvariantCulture)
                        : "Nonfinite coordinates reported";
                }
                else _locatorPosition = "No registered position";
                _locatorObservedAt = snapshot.ObservedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
                _locatorRequestPrepared = snapshot.HasAnyObservation && HasHostSession();
                _locatorReadStatus = snapshot.HasAnyObservation
                    ? "Local entries observed. Each source is shown separately."
                    : "No entries observed in these sources; this does not prove the object is absent.";
                ActionOk(_locatorReadStatus);
            }
            catch (Exception ex)
            {
                ClearWorldLocatorRead();
                _locatorReadStatus = "Locator read failed; no current entries are displayed.";
                LogWarning("Locator read failed: " + ex.GetType().Name);
                ActionFail(_locatorReadStatus);
            }
#else
            _locatorReadStatus = "Locator inspection requires the net11 Modstoke SDK.";
            ActionFail(_locatorReadStatus);
#endif
        }

        private static string FormatWorldLocatorState(int? raw, string known)
        {
            return !raw.HasValue ? "No entry" : (known ?? "Unrecognized") + " (" + raw.Value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private void RequestWorldLocatorState()
        {
            if (!HasHostSession())
            {
                _locatorRequestPrepared = false;
                ReportWorldLocatorRequest(false, "State requests require the multiplayer host in a loaded world.");
                return;
            }
            if (!CanRequestWorldLocatorState)
            {
                ReportWorldLocatorRequest(false, "Read the entered locator ID before requesting a state.");
                return;
            }
            if (_locatorStateChoice < -1 || _locatorStateChoice > 4)
            {
                ReportWorldLocatorRequest(false, "Choose one of the listed native states.");
                return;
            }
            // A new read is required for another request, including after an uncertain send.
            _locatorRequestPrepared = false;
#if NET11_0_OR_GREATER
            var requested = (OpenACAISonsWorldLocatorState)_locatorStateChoice;
            string target = _locatorNativeKey.ToString("D") + " -> " + requested;
            OpenACAISonsWorldLocatorChangeStatus status;
            try { status = OpenACAISonsServerWorldLocators.RequestState(_locatorNativeKey, requested); }
            catch (Exception ex)
            {
                LogWarning("Locator state request failed: " + ex.GetType().Name);
                ReportWorldLocatorRequest(false, target + ": outcome unknown. No retry was made; read again before another request.");
                return;
            }
            string detail;
            switch (status)
            {
                case OpenACAISonsWorldLocatorChangeStatus.Queued:
                    detail = "queued; policy acceptance, application, client delivery and persistence are unconfirmed."; break;
                case OpenACAISonsWorldLocatorChangeStatus.SendUncertain:
                    detail = "send uncertain; some recipients may have received it. No retry was made."; break;
                case OpenACAISonsWorldLocatorChangeStatus.Unavailable:
                    detail = "the loader's locator hook is unavailable."; break;
                case OpenACAISonsWorldLocatorChangeStatus.WrongThread:
                    detail = "state requests must run on the game's thread."; break;
                case OpenACAISonsWorldLocatorChangeStatus.InvalidState:
                    detail = "Modstoke SDK did not recognize the requested state."; break;
                case OpenACAISonsWorldLocatorChangeStatus.NotServer:
                    detail = "host authority is no longer available."; break;
                case OpenACAISonsWorldLocatorChangeStatus.NotReady:
                    detail = "the save manager is unavailable or a world is loading."; break;
                case OpenACAISonsWorldLocatorChangeStatus.ReadUnavailable:
                    detail = "Modstoke SDK could not refresh the locator's local entries."; break;
                case OpenACAISonsWorldLocatorChangeStatus.LocatorNotObserved:
                    detail = "no locator entry was observed at request time; no request was sent."; break;
                case OpenACAISonsWorldLocatorChangeStatus.Failed:
                    detail = "preflight failed before the native network request."; break;
                default:
                    detail = "unrecognized result; the outcome is unknown."; break;
            }
            ReportWorldLocatorRequest(status == OpenACAISonsWorldLocatorChangeStatus.Queued,
                target + ": " + detail + " Read again before another request.");
#else
            ReportWorldLocatorRequest(false, "Locator state requests require the net11 Modstoke SDK.");
#endif
        }

        private void ReportWorldLocatorRequest(bool queued, string detail)
        {
            _locatorRequestStatus = "Last request (" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + "): " + detail;
            ActionResult(queued, detail, detail);
        }
    }
}
