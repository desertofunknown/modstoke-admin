using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawDebugVisualSubTab()
        {
            GUILayout.Label("HUD / Camera", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("HUD", "_showHud", "on", "off", "showhud on", "showhud off", 88f, _primaryButtonStyle, _buttonStyle, "Toggle the game HUD.", true);
            DrawDebugToggleButton("UI", "_showui", "on", "off", "showui on", "showui off", 78f, _primaryButtonStyle, _buttonStyle, "Toggle game UI rendering.", true);
            DrawDebugToggleButton("Free Cam", "_freecamera", "on", "off", "freecamera on", "freecamera off", 118f, _primaryButtonStyle, _buttonStyle, "Toggle free camera. On/Off reflects the local camera controller's enabled state.");
            DrawDebugToggleButton("Player Cam", "_playerDebugCamera", "on", "off", "playerdebugcamera on", "playerdebugcamera off", 132f, _primaryButtonStyle, _buttonStyle, "Toggle player debug camera.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("FOV", ref _debugFovInput, 40f, 120f, 70f, "Camera field of view.", 34f, 150f);
            if (DrawDebugCommandButton(_iconVisual, "Set FOV", _buttonStyle, 90f, 32f, "_cameraFov", "Apply camera field of view."))
            {
                RunDebugConsoleAction("Camera FOV", "_cameraFov", _debugFovInput, "camerafov " + (_debugFovInput ?? ""));
            }
            if (DrawModerationButton(new GUIContent("Use Current", null, "Read the current local camera field of view."), _quietButtonStyle, _worldReady, GUILayout.Width(112f), GUILayout.Height(32f)))
            {
                CaptureLocalCameraFov();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(_localCameraFovStatus, _hintStyle);

            GUILayout.Space(6f);
            DrawAiOverlayControls();

            GUILayout.Space(6f);
            GUILayout.Label("Other Overlays", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("AI Debug", "_aiShowDebug", "on", "off", "aishowdebug on", "aishowdebug off", 116f, _primaryButtonStyle, _buttonStyle, "Request the native aishowdebug command.");
            DrawDebugToggleButton("Spawn Stats", "_spawnedObjectStats", "on", "off", "spawnedobjectstats on", "spawnedobjectstats off", 136f, _primaryButtonStyle, _buttonStyle, "Toggle spawned object statistics.");
            DrawDebugToggleButton("World Objects", "_showWorldObjects", "on", "off", "showworldobjects on", "showworldobjects off", 146f, _primaryButtonStyle, _buttonStyle, "Toggle world object markers.");
            DrawDebugToggleButton("GolfCart Net", "_golfCartNetworkDebug", "on", "off", "golfcartnetworkdebug on", "golfcartnetworkdebug off", 142f, _primaryButtonStyle, _buttonStyle, "Toggle golf cart network diagnostics.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("Pickup Debug", "_virtualPickupDebug", "on", "off", "virtualpickupdebug on", "virtualpickupdebug off", 144f, _primaryButtonStyle, _buttonStyle, "Toggle virtual pickup diagnostics.");
            DrawDebugToggleButton("Audio Debug", "_audioDebug", "on", "off", "audiodebug on", "audiodebug off", 132f, _primaryButtonStyle, _buttonStyle, "Toggle audio diagnostics.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Object Inspect", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Object", _contextLabelStyle, GUILayout.Width(54f), GUILayout.Height(32f));
            _debugObjectInput = GUILayout.TextField(_debugObjectInput ?? "", _inputStyle, GUILayout.Width(240f), GUILayout.Height(32f));
            if (DrawDebugCommandButton(_iconVisual, "Show Location", _buttonStyle, 136f, 32f, "_showObjectLocation"))
            {
                RunDebugConsoleAction("Object location", "_showObjectLocation", _debugObjectInput, "showobjectlocation " + (_debugObjectInput ?? ""));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
