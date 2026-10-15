using System;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawDebugWorldSubTab()
        {
            DrawWorldActionAvailability();
            GUILayout.Label(_worldClockStatus, _hintStyle);
            GUILayout.Label(_worldWeatherStatus, _hintStyle);
            GUILayout.Space(6f);
            GUILayout.Label("Position / Time", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Coords", _contextLabelStyle, GUILayout.Width(54f), GUILayout.Height(32f));
            _debugCoordInput = GUILayout.TextField(_debugCoordInput ?? "", _inputStyle, GUILayout.Width(210f), GUILayout.Height(32f));
            DrawDebugActionButton(_iconGoTo, "Use Here", _quietButtonStyle, 96f, 32f, SetDebugCoordsFromLocalPosition);
            if (DrawDebugCommandButton(_iconGoTo, "Go", _primaryButtonStyle, 66f, 32f, "_gotoCoords")) RunDebugConsoleAction("Coordinate teleport", "_gotoCoords", _debugCoordInput, "gotocoords " + (_debugCoordInput ?? ""));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Time", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugTimeInput = GUILayout.TextField(_debugTimeInput ?? "", _inputStyle, GUILayout.Width(52f), GUILayout.Height(32f));
            if (DrawDebugCommandButton(_iconWorld, "Set", _buttonStyle, 64f, 32f, "_setTimeOfDay")) RunDebugConsoleAction("Set time", "_setTimeOfDay", _debugTimeInput, "settimeofday " + (_debugTimeInput ?? ""));
            if (DrawDebugCommandButton(_iconWorld, "Lock", _buttonStyle, 72f, 32f, "_lockTimeOfDay")) RunDebugConsoleAction("Time lock", "_lockTimeOfDay", _debugTimeInput, "locktimeofday " + (_debugTimeInput ?? ""));
            if (DrawDebugCommandButton(_iconWorld, "Jump", _buttonStyle, 74f, 32f, "_jumpTimeOfDay")) RunDebugConsoleAction("Time jump", "_jumpTimeOfDay", _debugTimeInput, "jumptimeofday " + (_debugTimeInput ?? ""));
            GUILayout.Label("Day", _contextLabelStyle, GUILayout.Width(36f), GUILayout.Height(32f));
            _debugDayInput = GUILayout.TextField(_debugDayInput ?? "", _inputStyle, GUILayout.Width(56f), GUILayout.Height(32f));
            if (DrawDebugCommandButton(_iconWorld, "Set Day", _buttonStyle, 90f, 32f, "_setCurrentDay")) RunDebugConsoleAction("Set day", "_setCurrentDay", _debugDayInput, "setcurrentday " + (_debugDayInput ?? ""));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Season", _contextLabelStyle, GUILayout.Width(58f), GUILayout.Height(32f));
            string[] seasons = { "Spring", "Summer", "Fall", "Winter" };
            for (int i = 0; i < seasons.Length; i++)
            {
                bool selected = String.Equals(_debugSeasonInput, seasons[i], StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(seasons[i], selected ? _primaryButtonStyle : _buttonStyle, GUILayout.Width(78f), GUILayout.Height(32f)))
                    _debugSeasonInput = seasons[i];
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawDebugCommandButton(_iconWorld, "Set Season", _buttonStyle, 114f, 32f, "_season")) RunDebugConsoleAction("Season " + _debugSeasonInput, "_season", _debugSeasonInput, "season " + (_debugSeasonInput ?? ""));
            if (DrawDebugCommandButton(_iconWorld, "Lock Season", _buttonStyle, 120f, 32f, "_lockSeason")) RunDebugConsoleAction("Lock season " + _debugSeasonInput, "_lockSeason", _debugSeasonInput, "");
            DrawDebugMethodButton("Unlock Season", "_unlockSeason", "", "unlockseason", 134f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Time Scale", ref _debugTimeScaleInput, 0.05f, 8f, 1f, "Runtime time scale. Keep this near 1 for normal play.", 78f, 170f);
            if (DrawDebugCommandButton(_iconSpeed, "Apply Scale", _buttonStyle, 112f, 32f, "_timescale", "Apply Unity time scale.")) RunDebugConsoleAction("Time scale", "_timescale", _debugTimeScaleInput, "timescale " + (_debugTimeScaleInput ?? ""));
            if (DrawDebugCommandButton(_iconSpeed, "Game Speed", _buttonStyle, 112f, 32f, "_setGameTimeSpeed", "Apply game-world time speed.")) RunDebugConsoleAction("Game time speed", "_setGameTimeSpeed", _debugTimeScaleInput, "setgametimespeed " + (_debugTimeScaleInput ?? ""));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Weather / Terrain", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("Rain", "_forcerain", "on", "off", "forcerain on", "forcerain off", 98f, _primaryButtonStyle, _buttonStyle, "Force rain on/off.");
            DrawDebugToggleButton("Clouds", "_cloudEnable", "on", "off", "cloudenable on", "cloudenable off", 112f, _primaryButtonStyle, _buttonStyle, "Enable or disable cloud rendering/weather layer.");
            DrawDebugMethodButton("Cloud 0.7", "_cloudFactor", "0.7", "cloudfactor 0.7", 92f, _buttonStyle);
            DrawDebugMethodButton("Regrow Trees", "_regrowAllTrees", "", "regrowalltrees", 112f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Radius", ref _debugRadiusInput, 1f, 80f, 15f, "Radius for tree and bush actions around the game's active world position.", 56f, 190f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Clear Trees", "_treeRadius", "clear " + _debugRadiusInput, "treeradius", 120f, _dangerButtonStyle);
            DrawDebugMethodButton("Damage Trees", "_treeRadius", "damage " + _debugRadiusInput, "treeradius", 128f, _dangerButtonStyle);
            DrawDebugMethodButton("Destroy Trees", "_treeRadius", "destroy " + _debugRadiusInput, "treeradius", 128f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugRadiusButton("Bushes", "_clearBushRadius", "clearbushradius");
            DrawDebugMethodButton("Restore Locators", "_restoreAllWorldLocators", "", "restoreallworldlocators", 128f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label("Dig Sites", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Clear Nearest Dig", "_diggingClear", "", "diggingclear", 156f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Activates the nearest dig site. This action has no radius limit.", _hintStyle);

            DrawWorldLocatorInspector();
        }
    }
}
