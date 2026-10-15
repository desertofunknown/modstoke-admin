using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawServerSleepControls()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Server Sleep Events", _sectionTitleStyle);
            bool available = _hostActionsAvailable && LoaderSleepHookInstalled;
            GUILayout.Label(available ? "Multiplayer host controls available."
                : _hostActionsAvailable ? "The shared sleep hook is unavailable." : "Available to the multiplayer host only.", _hintStyle);

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton(new GUIContent(_blockServerSleepEvents ? "Allow Sleep Events" : "Block Sleep Events", null,
                "Control subsequent server Sleep event delivery."), _buttonStyle,
                _blockServerSleepEvents || available, GUILayout.Width(200f), GUILayout.Height(32f)))
            {
                SetServerSleepBlocking(!_blockServerSleepEvents);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(_serverSleepPolicyStatus, _hintStyle);
            GUILayout.Label("Sleep hook registration: " + LoaderSleepHookStatus, _hintStyle);
            GUILayout.Label("Events observed while blocking: " + _serverSleepRequestsSeen.ToString(CultureInfo.InvariantCulture)
                + " | Rejected: " + _serverSleepRequestsRejected.ToString(CultureInfo.InvariantCulture), _hintStyle);
            if (_lastServerSleepRequestUtc != DateTime.MinValue)
            {
                GUILayout.Label("Last observed: " + _lastServerSleepRequestUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                    + " | " + _lastServerSleepDecision, _hintStyle);
            }
            GUILayout.Label("Blocks subsequent server Sleep events. Active sleep and existing votes are unaffected; waiting indicators can remain visible.", _hintStyle);
            GUILayout.Label("Off by default; resets on observed host loss or session reset. Not saved.", _hintStyle);
        }
    }
}
