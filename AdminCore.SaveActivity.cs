using System;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly object _saveActivitySync = new object();
        private string _lastSaveRequestStatus = "No console save request recorded";
        private DateTime _lastSaveRequestUtc = DateTime.MinValue;
        private string _lastSaveObservation = "No save callback observed";
        private string _lastSaveObservationDetail = "";
        private DateTime _lastSaveObservationUtc = DateTime.MinValue;
        private int _saveBeforeCount = 0;
        private int _saveAfterCount = 0;

#if NET11_0_OR_GREATER
        private bool _saveLifecycleSubscribed;

        internal void InitializeSaveActivity()
        {
            lock (_saveActivitySync)
            {
                if (_saveLifecycleSubscribed) return;
                OpenACAISonsSaveLifecycle.Lifecycle += ObserveSaveActivity;
                _saveLifecycleSubscribed = true;
            }
        }

        internal void StopSaveActivity()
        {
            lock (_saveActivitySync)
            {
                _saveLifecycleSubscribed = false;
                OpenACAISonsSaveLifecycle.Lifecycle -= ObserveSaveActivity;
            }
        }

        private void ObserveSaveActivity(OpenACAISonsSaveLifecycleEvent observation)
        {
            if (observation == null || observation.Operation != "Save") return;
            bool before = observation.Phase == "Before";
            if (!before && observation.Phase != "After") return;

            // The callback retains managed observations only; it never reads game objects or updates UI.
            lock (_saveActivitySync)
            {
                if (!_saveLifecycleSubscribed) return;
                if (before) _saveBeforeCount = Math.Min(_saveBeforeCount, Int32.MaxValue - 1) + 1;
                else _saveAfterCount = Math.Min(_saveAfterCount, Int32.MaxValue - 1) + 1;
                _lastSaveObservation = before ? "Save entrypoint entered" : "Save entrypoint returned";
                _lastSaveObservationUtc = observation.TimestampUtc.UtcDateTime;
                _lastSaveObservationDetail = "Game: " + (String.IsNullOrWhiteSpace(observation.GameName)
                    ? "unavailable" : CleanConfigText(observation.GameName, 96))
                    + " | Player-only: " + (observation.SavePlayerOnly.HasValue
                        ? observation.SavePlayerOnly.Value ? "yes" : "no" : "unknown");
            }
        }

        private static string GetSaveHookStatus(bool subscribed)
        {
            if (!subscribed) return "Not subscribed";
            var state = OpenACAISonsSaveLifecycle.State;
            foreach (string target in state.InstalledTargets)
                if (target == "SaveGameManager.Save") return "Registered";
            return "Unavailable (" + CleanConfigText(state.PatchStatus, 40) + ")";
        }
#else
        internal void InitializeSaveActivity() { }
        internal void StopSaveActivity() { }
#endif

        private void ReportSaveRequest(bool accepted, string message)
        {
            message = CleanConfigText(message, 420);
            lock (_saveActivitySync)
            {
                _lastSaveRequestUtc = DateTime.UtcNow;
                _lastSaveRequestStatus = message;
            }
            if (accepted) ActionOk(message);
            else ActionFail(message);
        }

        private SaveActivitySnapshot GetSaveActivitySnapshot()
        {
            SaveActivitySnapshot snapshot;
#if NET11_0_OR_GREATER
            bool subscribed;
#endif
            lock (_saveActivitySync)
            {
                snapshot = new SaveActivitySnapshot
                {
                    RequestStatus = _lastSaveRequestStatus,
                    RequestUtc = _lastSaveRequestUtc,
                    ObservationStatus = _lastSaveObservation,
                    ObservationUtc = _lastSaveObservationUtc,
                    ObservationDetail = _lastSaveObservationDetail,
                    BeforeCount = _saveBeforeCount,
                    AfterCount = _saveAfterCount
                };
#if NET11_0_OR_GREATER
                subscribed = _saveLifecycleSubscribed;
#endif
            }
#if NET11_0_OR_GREATER
            snapshot.HookStatus = GetSaveHookStatus(subscribed);
#else
            snapshot.HookStatus = "Unavailable on this runtime";
#endif
            return snapshot;
        }

        private sealed class SaveActivitySnapshot
        {
            public string HookStatus;
            public string RequestStatus;
            public DateTime RequestUtc;
            public string ObservationStatus;
            public DateTime ObservationUtc;
            public string ObservationDetail;
            public int BeforeCount;
            public int AfterCount;
        }
    }
}
