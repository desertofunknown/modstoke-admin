using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _localCameraFovStatus = "Local player camera FOV unavailable";
        private string _observedAiOverlayMode;
        private string _observedAiRadarMode;
        private bool _aiOverlayAvailable;
        private bool _aiRadarAvailable;
        private static MethodInfo _freeCameraControllerReader;
        private static readonly string[] AiOverlayModes =
        {
            "None", "Health", "Thoughts", "Stats", "SurvivalStats", "AnimStates",
            "AnimClips", "PlayerInfluences", "EventMemory", "AnimVailTags", "Audio"
        };
        private static readonly string[] AiOverlayLabels =
        {
            "Off", "Health", "Thoughts", "Actor Stats", "Survival", "Anim States",
            "Anim Clips", "Influences", "Event Memory", "Anim Tags", "Audio"
        };
        private static readonly string[] AiRadarModes = { "None", "Standard", "Verbose", "SimpleAnimal" };
        private static readonly string[] AiRadarLabels = { "Off", "Standard", "Verbose", "Animals" };

        private void RefreshVisualDebugState()
        {
            ResetVisualDebugState();
            if (!_worldReady) return;
            RefreshLocalCameraFov();
            object console;
            object mode;
            string filter;
            float distance;
            if (TryReadAiDisplay(false, out console, out mode, out filter, out distance))
            {
                _observedAiOverlayMode = mode.ToString();
                _aiOverlayAvailable = true;
            }
            if (TryReadAiDisplay(true, out console, out mode, out filter, out distance))
            {
                _observedAiRadarMode = mode.ToString();
                _aiRadarAvailable = true;
            }
        }

        private void ResetVisualDebugState()
        {
            _localCameraFovStatus = "Local player camera FOV unavailable";
            _observedAiOverlayMode = null;
            _observedAiRadarMode = null;
            _aiOverlayAvailable = false;
            _aiRadarAvailable = false;
        }

        private bool TryReadLocalCameraFov(out float fov)
        {
            fov = 0f;
            if (!IsLocalPlayerInWorld()) return false;
            Camera camera = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("TheForest.Utils.LocalPlayer"), "MainCam") as Camera;
            if (camera == null) return false;
            try
            {
                fov = camera.fieldOfView;
                if (!IsFiniteDebugValue(fov)) return false;
                _localCameraFovStatus = "Local player camera FOV: " + fov.ToString("0.##", CultureInfo.InvariantCulture)
                    + (camera.enabled && camera.gameObject.activeInHierarchy ? "" : " (camera inactive)");
                return true;
            }
            catch { return false; }
        }

        private void RefreshLocalCameraFov()
        {
            _localCameraFovStatus = "Local player camera FOV unavailable";
            float fov;
            TryReadLocalCameraFov(out fov);
        }

        private void CaptureLocalCameraFov()
        {
            float fov;
            if (!TryReadLocalCameraFov(out fov))
            {
                _localCameraFovStatus = "Local player camera FOV unavailable";
                ActionFail(_localCameraFovStatus);
                return;
            }
            _debugFovInput = fov.ToString("G9", CultureInfo.InvariantCulture);
            ActionOk("Captured " + _localCameraFovStatus);
        }

        private static bool TryReadVisualProperty(object instance, string name, Type expectedType, out object value)
        {
            value = null;
            if (instance == null || expectedType == null) return false;
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanRead || property.PropertyType != expectedType) return false;
                value = property.GetValue(instance);
                return value != null || !expectedType.IsValueType;
            }
            catch { return false; }
        }

        private static bool TryReadAiDisplay(bool radar, out object console, out object mode, out string filter, out float distance)
        {
            console = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("TheForest.DebugConsole"), "Instance");
            mode = null;
            filter = null;
            distance = 0f;
            if (!(console is UnityEngine.Object nativeObject) || nativeObject == null) return false;
            Type modeType = ReflectionTools.FindType(radar ? "Sons.Ai.Vail.VailRadarType" : "Sons.Ai.Vail.VailOverlayType");
            object filterValue;
            object distanceValue;
            if (modeType == null || !modeType.IsEnum
                || !TryReadVisualProperty(console, radar ? "_showVailRadar" : "_showVailActorOverlay", modeType, out mode)
                || !TryReadVisualProperty(console, radar ? "_radarTypeFilter" : "_showVailOverlayTypeFilter", typeof(string), out filterValue)
                || !TryReadVisualProperty(console, radar ? "_radarMaxDistance" : "_vailOverlayMaxDistance", typeof(float), out distanceValue)) return false;
            filter = (string)filterValue;
            distance = (float)distanceValue;
            return IsFiniteDebugValue(distance);
        }

        private void SetAiDisplayMode(bool radar, string modeName)
        {
            string label = radar ? "AI radar" : "AI actor overlay";
            if (!IsLocalPlayerInWorld())
            {
                ActionFail(label + " unavailable: join a world first");
                return;
            }
            object console;
            object currentMode;
            string filter;
            float distance;
            if (!TryReadAiDisplay(radar, out console, out currentMode, out filter, out distance))
            {
                ActionFail(label + " unavailable: current mode, filter or distance could not be read");
                return;
            }
            Type modeType = currentMode.GetType();
            object requested;
            if (!Enum.TryParse(modeType, modeName, false, out requested) || !Enum.IsDefined(modeType, requested))
            {
                ActionFail(label + " mode is unavailable in this game build");
                return;
            }

            object result;
            bool sent = InvokeMethod(console, console.GetType(), radar ? "ShowVailRadar" : "ShowVailActorOverlay",
                new object[] { requested, filter, distance }, false, out result) && NativeActionAccepted(result);
            RefreshVisualDebugState();
            _nextDebugToggleReadUtc = DateTime.MinValue;
            string observed = radar ? _observedAiRadarMode : _observedAiOverlayMode;
            ActionResult(sent, label + ": " + modeName + " requested; observed " + (observed ?? "unavailable"),
                label + " request failed; observed " + (observed ?? "unavailable"));
        }

        private void DrawAiOverlayControls()
        {
            GUILayout.Label("AI Actor Overlay", _sectionTitleStyle);
            GUILayout.Label("Observed mode: " + (_observedAiOverlayMode ?? "unavailable"), _hintStyle);
            for (int start = 0; start < AiOverlayModes.Length; start += 4)
            {
                GUILayout.BeginHorizontal(_rowStyle);
                for (int i = start; i < Math.Min(start + 4, AiOverlayModes.Length); i++)
                {
                    GUIStyle style = _observedAiOverlayMode == AiOverlayModes[i] ? _primaryButtonStyle : _buttonStyle;
                    if (DrawModerationButton(AiOverlayLabels[i], style, _worldReady && _aiOverlayAvailable, GUILayout.Width(126f), GUILayout.Height(30f)))
                        SetAiDisplayMode(false, AiOverlayModes[i]);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("AI Radar", _sectionTitleStyle);
            GUILayout.Label("Observed mode: " + (_observedAiRadarMode ?? "unavailable"), _hintStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            for (int i = 0; i < AiRadarModes.Length; i++)
            {
                GUIStyle style = _observedAiRadarMode == AiRadarModes[i] ? _primaryButtonStyle : _buttonStyle;
                if (DrawModerationButton(AiRadarLabels[i], style, _worldReady && _aiRadarAvailable, GUILayout.Width(126f), GUILayout.Height(30f)))
                    SetAiDisplayMode(true, AiRadarModes[i]);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Mode changes keep the current filter and distance. Actor Stats and the AI Stats switch are separate displays.", _hintStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("AI Stats", "_aiShowStats", "on", "off", "aishowstats on", "aishowstats off", 116f, _primaryButtonStyle, _buttonStyle, "Toggle the AI manager's statistics display.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static bool HasLiveVisualManager(Type type)
        {
            object instance = ReflectionTools.GetStaticMemberValue(type, "_instance");
            return instance is UnityEngine.Object nativeObject && nativeObject != null;
        }

        private static bool? ReadVisualToggleState(string methodName)
        {
            string command = GetDebugCommandName(methodName);
            if (command == "freecamera") return ReadFreeCameraState();
            if (command == "aishowstats")
            {
                Type type = ReflectionTools.FindType("Sons.Ai.Vail.VailActorManager");
                if (!HasLiveVisualManager(type)) return null;
                object value = ReflectionTools.GetStaticMemberValue(type, "VailStats");
                return value is bool state ? state : (bool?)null;
            }
            if (command == "virtualpickupdebug")
            {
                Type type = ReflectionTools.FindType("Sons.Gameplay.VirtualPickupManager");
                if (!HasLiveVisualManager(type)) return null;
                object applied = ReflectionTools.GetStaticMemberValue(type, "_appliedDebugState");
                if (!(ReflectionTools.GetMemberValue(applied, "HasValue") is bool hasValue) || !hasValue) return null;
                object value = ReflectionTools.GetMemberValue(applied, "Value");
                return value is bool state ? state : (bool?)null;
            }
            return null;
        }

        private static bool? ReadFreeCameraState()
        {
            if (!IsLocalPlayerInWorld()) return null;
            try
            {
                Camera camera = ReflectionTools.GetStaticMemberValue(
                    ReflectionTools.FindType("TheForest.Utils.LocalPlayer"), "MainCam") as Camera;
                if (camera == null || !camera.gameObject.activeInHierarchy) return null;
                Transform parent = camera.transform.parent;
                if (parent == null) return null;

                Type controllerType = ReflectionTools.FindType("Sons.Gui.FreeCameraController");
                if (controllerType == null || !typeof(Behaviour).IsAssignableFrom(controllerType)) return null;
                if (_freeCameraControllerReader == null)
                {
                    foreach (MethodInfo method in typeof(Component).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (method.Name == "GetComponentInChildren" && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)
                        {
                            _freeCameraControllerReader = method.MakeGenericMethod(controllerType);
                            break;
                        }
                    }
                }

                // Match the native command's lookup after it reparents the camera on entry or exit.
                Behaviour controller = _freeCameraControllerReader?.Invoke(parent, null) as Behaviour;
                if (controller == null || !controller.gameObject.activeInHierarchy) return null;
                return controller.enabled;
            }
            catch { return null; }
        }

        private bool? TryRunVisualDebugCommand(string methodName, string argument)
        {
            string command = GetDebugCommandName(methodName);
            if (command != "aishowstats" && command != "virtualpickupdebug") return null;
            bool enabled;
            if (String.Equals(argument, "on", StringComparison.OrdinalIgnoreCase)) enabled = true;
            else if (String.Equals(argument, "off", StringComparison.OrdinalIgnoreCase)) enabled = false;
            else
            {
                _lastDebugCommandFailure = "Choose on or off";
                return false;
            }
            Type type = ReflectionTools.FindType(command == "aishowstats" ? "Sons.Ai.Vail.VailActorManager" : "Sons.Gameplay.VirtualPickupManager");
            if (!HasLiveVisualManager(type))
            {
                _lastDebugCommandFailure = "The diagnostic manager is not ready";
                return false;
            }
            object result;
            bool sent = InvokeMethod(null, type, command == "aishowstats" ? "ShowVailStats" : "SetShowDebug",
                new object[] { enabled }, true, out result) && NativeActionAccepted(result);
            if (!sent) _lastDebugCommandFailure = "The diagnostic command is unavailable or failed";
            return sent;
        }

        private string GetVisualCommandObservation(string command)
        {
            if (command == "camerafov")
            {
                RefreshLocalCameraFov();
                return "; " + _localCameraFovStatus;
            }
            if (command == "aishowstats" || command == "virtualpickupdebug" || command == "freecamera")
            {
                bool? state = ReadVisualToggleState(command);
                string label = command == "virtualpickupdebug" ? "applied state "
                    : command == "freecamera" ? "observed camera controller " : "observed state ";
                return "; " + label
                    + (state.HasValue ? (state.Value ? "on" : "off") : "unavailable");
            }
            return "";
        }
    }
}
