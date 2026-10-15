using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawNpcTab()
        {
            BeginPanel("NPC Control");
            DrawWorldActionAvailability();
            bool canRelease = _possession.Active || _possession.NeedsRelease;
            string label = _possession.Active ? "Stop Kelvin" : _possession.NeedsRelease ? "Retry Release" : "Possess Kelvin";
            if (DrawModerationButton(label, canRelease ? _dangerButtonStyle : _primaryButtonStyle,
                canRelease || (_experimentalPossession && _worldActionsAvailable), GUILayout.Width(148f), GUILayout.Height(34f)))
            {
                TogglePossession();
            }
            if (!_experimentalPossession)
            {
                GUILayout.Label("Kelvin possession is disabled in settings.ini.", _hintStyle);
            }
            GUILayout.Label(_possession.LastStatus, _labelStyle);
            GUILayout.Label("Close the menu to move. WASD: move | Space: up | Ctrl / C: down | Shift: faster", _hintStyle);
            EndPanel();
        }

        private void TogglePossession()
        {
            if (_possession.Active || _possession.NeedsRelease)
            {
                bool released = _possession.Stop();
                if (released) ActionOk("Kelvin: " + _possession.LastStatus);
                else ActionFail("Kelvin release incomplete: " + _possession.LastStatus);
                return;
            }
            if (!_experimentalPossession)
            {
                ActionFail("Kelvin possession is disabled in settings.ini");
                return;
            }
            if (!HasWorldAuthority())
            {
                ActionFail("Kelvin possession requires single-player or the multiplayer host");
                return;
            }
            if (_possession.Start()) ActionOk("Kelvin possession on: " + _possession.LastStatus);
            else ActionFail("Kelvin possession failed: " + _possession.LastStatus);
        }

        internal void StopCompanionControl()
        {
            if (_possession != null && (_possession.Active || _possession.NeedsRelease)
                && !_possession.Stop("Admin driver destroyed"))
            {
                LogWarning("Kelvin release incomplete during admin driver teardown: " + _possession.LastStatus);
            }
        }
    }
}
