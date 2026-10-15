using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawServerAdmissionControls()
        {
            GUILayout.Space(6f);
            GUILayout.Label("New Connection Requests", _sectionTitleStyle);
            bool available = ServerAdmissionSupported && _hostActionsAvailable;
            GUILayout.Label(!ServerAdmissionSupported
                ? "Connection request controls are unavailable in this Modstoke SDK build. A supported build is required."
                : _hostActionsAvailable ? "Multiplayer host controls available." : "Available to the multiplayer host only.", _hintStyle);

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton(new GUIContent("Read Settings", null, "Read the current transport admission settings."),
                _quietButtonStyle, available, GUILayout.Width(120f), GUILayout.Height(32f)))
            {
                ReadServerAdmissionSettings();
            }
            if (DrawModerationButton(new GUIContent("Allow New Requests", null, "Enable new connection requests, subject to game admission checks."),
                _buttonStyle, available && _serverAdmissionHasRead && !_serverAdmissionIncomingEnabled,
                GUILayout.Width(170f), GUILayout.Height(32f)))
            {
                SetServerIncomingRequests(true);
            }
            if (DrawModerationButton(new GUIContent("Block New Requests", null, "Disable new connection requests without changing existing players or pending joins."),
                _buttonStyle, available && _serverAdmissionHasRead && _serverAdmissionIncomingEnabled,
                GUILayout.Width(170f), GUILayout.Height(32f)))
            {
                SetServerIncomingRequests(false);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_serverAdmissionReadUtc != DateTime.MinValue)
            {
                GUILayout.Label("Settings observed: " + _serverAdmissionReadUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), _hintStyle);
            }
            GUILayout.Label("Read status: " + _serverAdmissionReadStatus, _hintStyle);
            if (_serverAdmissionHasRead)
            {
                GUILayout.Label("New request gate: " + (_serverAdmissionIncomingEnabled ? "Enabled" : "Disabled")
                    + " | Automatic acceptance (read-only): " + (_serverAdmissionAutomaticAcceptance ? "On" : "Off"), _hintStyle);
                string limit = _serverAdmissionConnectionLimit == -1 ? "Unlimited (-1)"
                    : _serverAdmissionConnectionLimit >= 0 ? _serverAdmissionConnectionLimit.ToString(CultureInfo.InvariantCulture)
                    : "Unknown (raw " + _serverAdmissionConnectionLimit.ToString(CultureInfo.InvariantCulture) + ")";
                GUILayout.Label(new GUIContent("Transport connection limit (read-only): " + limit, null,
                    "Native transport capacity includes active and pending connections. It does not establish gameplay or lobby player capacity."), _hintStyle);
            }
            else
            {
                GUILayout.Label("Read settings before changing the new request gate.", _hintStyle);
            }
            GUILayout.Label("Read observations may change. Each change checks host authority and reads the current settings again.", _hintStyle);

            string requestTime = _serverAdmissionRequestUtc == DateTime.MinValue ? string.Empty
                : " (" + _serverAdmissionRequestUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + ")";
            GUILayout.Label("Change request" + requestTime + ": " + _serverAdmissionRequestStatus, _hintStyle);
            GUILayout.Label("This gate affects only new connection requests. Existing players and pending joins are unaffected. Enabling requests does not bypass game admission checks.", _hintStyle);
            GUILayout.Label("Changes are not saved by this console and are not automatically restored.", _hintStyle);
        }
    }
}
