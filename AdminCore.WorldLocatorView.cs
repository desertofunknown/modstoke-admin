using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawWorldLocatorInspector()
        {
            GUILayout.Space(6f);
            GUILayout.Label("World Locator Inspector", _sectionTitleStyle);
#if NET11_0_OR_GREATER
            bool canRead = _worldReady;
            bool canSelectState = _hostActionsAvailable;
            GUILayout.Label("Read a known GUID in any loaded world. State changes require the multiplayer host. Nearby and radius discovery are unavailable.", _hintStyle);
#else
            bool canRead = false;
            bool canSelectState = false;
            GUILayout.Label("World locator inspection and state requests require the Modstoke SDK build.", _hintStyle);
#endif

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Known GUID", _contextLabelStyle, GUILayout.Width(88f));
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && canRead;
                _locatorIdInput = GUILayout.TextField(_locatorIdInput ?? "", _inputStyle, GUILayout.Width(330f), GUILayout.Height(32f));
            }
            finally
            {
                GUI.enabled = enabled;
            }
            if (DrawModerationButton(new GUIContent("Read", null, "Read the known world locator GUID in the current world."),
                _buttonStyle, canRead, GUILayout.Width(76f), GUILayout.Height(32f)))
            {
                ReadWorldLocator();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(_locatorReadStatus ?? "", _hintStyle);
            DrawWorldLocatorValue("Native key", _locatorNativeId);
            DrawWorldLocatorValue("Registered", _locatorRegisteredState);
            DrawWorldLocatorValue("Recorded", _locatorRecordedState);
            DrawWorldLocatorValue("Unapplied", _locatorUnappliedState);
            DrawWorldLocatorValue("Stored position", _locatorPosition);
            DrawWorldLocatorValue("Read at", _locatorObservedAt);
            GUILayout.Label("Sources have no assumed priority. Recorded is an in-memory entry, not proof of a disk save. Stored position is locator data, not a live transform.", _hintStyle);

            GUILayout.Label("Choose requested state (highlight shows your selection):", _hintStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawWorldLocatorStateChoice("None", -1, canSelectState);
            DrawWorldLocatorStateChoice("Default", 0, canSelectState);
            DrawWorldLocatorStateChoice("Cleared", 1, canSelectState);
            DrawWorldLocatorStateChoice("Damaged", 2, canSelectState);
            DrawWorldLocatorStateChoice("Destroyed", 3, canSelectState);
            DrawWorldLocatorStateChoice("Burning", 4, canSelectState);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton(new GUIContent("Request State", null,
                "Read a known GUID first and keep the original input unchanged. A fresh host check is required."),
                _buttonStyle, CanRequestWorldLocatorState, GUILayout.Width(132f), GUILayout.Height(32f)))
            {
                RequestWorldLocatorState();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(_locatorRequestStatus ?? "", _hintStyle);
            GUILayout.Label("Read again before each request. State names do not establish valid transitions for this object. Default does not guarantee restoration or undo prior drops and save effects. Queued is not proof of application or persistence.", _hintStyle);
        }

        private void DrawWorldLocatorValue(string label, string value)
        {
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(label, _contextLabelStyle, GUILayout.Width(120f));
            GUILayout.Label(value ?? "", _labelStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        private void DrawWorldLocatorStateChoice(string label, int value, bool available)
        {
            if (DrawModerationButton(new GUIContent(label, null, "Select requested state " + label + " (" + value + ")."),
                _locatorStateChoice == value ? _primaryButtonStyle : _buttonStyle,
                available, GUILayout.Width(90f), GUILayout.Height(32f)))
            {
                _locatorStateChoice = value;
            }
        }
    }
}
