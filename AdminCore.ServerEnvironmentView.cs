using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawServerEnvironmentControls()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Host Clock & Weather", _sectionTitleStyle);
#if NET11_0_OR_GREATER
            bool available = _hostActionsAvailable;
            GUILayout.Label(available ? "Multiplayer host controls available." : "Available to the multiplayer host only.", _hintStyle);
#else
            bool available = false;
            GUILayout.Label("Host clock and weather controls require the Modstoke SDK build.", _hintStyle);
#endif

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(new GUIContent("Time", null, "24-hour time: HH:mm or HH:mm:ss."), _hintStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && available;
                _serverTimeInput = GUILayout.TextField(_serverTimeInput, _inputStyle, GUILayout.Width(112f), GUILayout.Height(32f));
            }
            finally
            {
                GUI.enabled = enabled;
            }
            if (DrawModerationButton(new GUIContent("Set Time", null, "Set 24-hour time (HH:mm or HH:mm:ss), keeping the current day and clock speed."),
                _buttonStyle, available, GUILayout.Width(100f), GUILayout.Height(32f)))
            {
                SetServerTime();
            }
            if (DrawModerationButton(new GUIContent("Use Current", null, "Copy the observed world time into the time field."),
                _quietButtonStyle, available, GUILayout.Width(112f), GUILayout.Height(32f)))
            {
                CaptureServerTime();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(_worldClockStatus, _hintStyle);
            GUILayout.Label(_serverTimeStatus, _hintStyle);

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Precipitation", _hintStyle, GUILayout.Width(100f), GUILayout.Height(32f));
            if (DrawModerationButton(new GUIContent("Stop", null, "Request an end to precipitation. This does not keep skies permanently clear."),
                _buttonStyle, available, GUILayout.Width(76f), GUILayout.Height(32f)))
            {
                SetServerPrecipitation(0);
            }
            if (DrawModerationButton(new GUIContent("Light", null, "Request light precipitation; native weather conditions determine rain or snow."),
                _buttonStyle, available, GUILayout.Width(76f), GUILayout.Height(32f)))
            {
                SetServerPrecipitation(1);
            }
            if (DrawModerationButton(new GUIContent("Medium", null, "Request medium precipitation; native weather conditions determine rain or snow."),
                _buttonStyle, available, GUILayout.Width(86f), GUILayout.Height(32f)))
            {
                SetServerPrecipitation(2);
            }
            if (DrawModerationButton(new GUIContent("Heavy", null, "Request heavy precipitation; native weather conditions determine rain or snow."),
                _buttonStyle, available, GUILayout.Width(76f), GUILayout.Height(32f)))
            {
                SetServerPrecipitation(3);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(_worldWeatherStatus, _hintStyle);
            GUILayout.Label(_serverWeatherStatus, _hintStyle);
            GUILayout.Label("Time keeps the current day and clock speed. Weather can transition and return naturally. Client delivery is unconfirmed.", _hintStyle);
        }
    }
}
