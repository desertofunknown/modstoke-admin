using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawSaveActivity()
        {
            SaveActivitySnapshot activity = GetSaveActivitySnapshot();
            GUILayout.Space(8f);
            GUILayout.Label("Save Activity", _sectionTitleStyle);
            GUILayout.Label("Last console request: " + activity.RequestStatus, _labelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Recorded: " + FormatSaveActivityUtc(activity.RequestUtc), _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Last observed save call: " + activity.ObservationStatus, _labelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Observed: " + FormatSaveActivityUtc(activity.ObservationUtc), _hintStyle, GUILayout.ExpandWidth(true));
            if (!String.IsNullOrEmpty(activity.ObservationDetail))
                GUILayout.Label(activity.ObservationDetail, _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Observation hooks: " + activity.HookStatus, _labelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Callbacks since console startup: before " + activity.BeforeCount.ToString(CultureInfo.InvariantCulture)
                + " | After " + activity.AfterCount.ToString(CultureInfo.InvariantCulture), _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Observations include other game saves and remain until exit. They are not linked to the console request. Entry and return counts do not confirm that a save finished writing.", _hintStyle, GUILayout.ExpandWidth(true));
        }

        private void DrawSaveActivitySummary()
        {
            SaveActivitySnapshot activity = GetSaveActivitySnapshot();
            string tooltip = "Last console request: " + activity.RequestStatus
                + "\nRecorded UTC: " + FormatSaveActivityUtc(activity.RequestUtc)
                + "\nLast observed save entrypoint event: " + activity.ObservationStatus
                + "\nObserved UTC: " + FormatSaveActivityUtc(activity.ObservationUtc)
                + (String.IsNullOrEmpty(activity.ObservationDetail) ? "" : "\n" + activity.ObservationDetail)
                + "\nObservation hooks: " + activity.HookStatus
                + "\nObservations are not a receipt for the console request or proof of disk completion. History spans world changes until console shutdown.";
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Save request: " + Shorten(activity.RequestStatus, 52)
                + " | Observed: " + Shorten(activity.ObservationStatus, 52), null, tooltip), _hintStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Save details", _quietButtonStyle, GUILayout.Width(100f)))
            {
                _activeTab = "Utility";
                _contentScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();
        }

        private static string FormatSaveActivityUtc(DateTime utc)
        {
            return utc == DateTime.MinValue ? "None recorded" : utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }
    }
}
