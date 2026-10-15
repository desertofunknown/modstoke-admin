using System;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawDebugBuildSubTab()
        {
            DrawWorldActionAvailability();
            GUILayout.Label("Build / Resources", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("Builder", "_buildermode", "on", "off", "buildermode on", "buildermode off", 116f, _primaryButtonStyle, _buttonStyle, "Toggle builder mode.");
            DrawDebugToggleButton("Instant Build", "_instantBuild", "on", "off", "", "", 156f, _primaryButtonStyle, _buttonStyle, "Toggle the structure crafting system's instant-build mode.");
            DrawDebugToggleButton("Creative Mode", "_creativeMode", "on", "off", "", "", 166f, _primaryButtonStyle, _buttonStyle, "Toggle the game's creative mode setting.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("Logs", "_loghack", "on", "off", "loghack on", "loghack off", 98f, _primaryButtonStyle, _buttonStyle, "Toggle infinite log resources.");
            DrawDebugToggleButton("Stone", "_stonehack", "on", "off", "stonehack on", "stonehack off", 104f, _primaryButtonStyle, _buttonStyle, "Toggle infinite stone resources.");
            DrawDebugToggleButton("Slap Chop", "_slapchop", "on", "off", "slapchop on", "slapchop off", 142f, _primaryButtonStyle, _buttonStyle, "Send the native slapchop on/off command. Its gameplay effects have not been verified.");
            DrawDebugToggleButton("Ghosts", "_enableStructureGhosts", "on", "off", "enablestructureghosts on", "enablestructureghosts off", 112f, _primaryButtonStyle, _buttonStyle, "Activate or deactivate structure ghosts.");
            DrawDebugToggleButton("Traps", "_playersTriggerTraps", "on", "off", "playerstriggertraps on", "playerstriggertraps off", 104f, _dangerButtonStyle, _buttonStyle, "Toggle whether players trigger traps.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Save / Death Flow", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugActionButton(_iconSave, "World Save", _primaryButtonStyle, 116f, 34f, ForceSave, _worldActionsAvailable);
            DrawDebugMethodButton("Instant Respawn", "_instantRespawnHere", "", "instantrespawnhere", 142f, _buttonStyle);
            DrawDebugToggleButton("Block Final Death", "_blockPlayerFinalDeath", "on", "off", "blockplayerfinaldeath on", "blockplayerfinaldeath off", 190f, _primaryButtonStyle, _buttonStyle, "Toggle final-death blocking for local debug testing.");
            DrawDebugMethodButton("Refill Containers", "_refillContainers", "", "refillcontainers", 146f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            DrawSaveActivitySummary();
        }

        private bool? TryRunConstructionDebugCommand(string methodName, string argument)
        {
            string command = GetDebugCommandName(methodName);
            if (command != "instantbuild" && command != "creativemode" && command != "enablestructureghosts") return null;

            bool enabled;
            if (String.Equals(argument, "on", StringComparison.OrdinalIgnoreCase)) enabled = true;
            else if (String.Equals(argument, "off", StringComparison.OrdinalIgnoreCase)) enabled = false;
            else
            {
                _lastDebugCommandFailure = "Choose on or off";
                return false;
            }

            object instance = null;
            Type type;
            string setter;
            bool staticOnly = command == "creativemode";
            if (staticOnly)
            {
                type = ReflectionTools.FindType("Sons.Gameplay.GameSetup.GameSetupManager");
                setter = "SetCreativeModeSetting";
            }
            else
            {
                instance = GetLiveStructureCraftingSystem();
                if (instance == null)
                {
                    _lastDebugCommandFailure = "Structure crafting system is not active";
                    return false;
                }
                type = instance.GetType();
                setter = command == "instantbuild" ? "set_InstantBuild" : "ActivateAllGhosts";
            }

            object result;
            if (!InvokeMethod(instance, type, setter, new object[] { enabled }, staticOnly, out result) || !NativeActionAccepted(result))
            {
                _lastDebugCommandFailure = "Construction command " + command + " is unavailable or failed";
                return false;
            }
            bool? observed = ReadConstructionToggleState(command);
            if (!observed.HasValue)
            {
                _lastDebugCommandFailure = "Construction command " + command + " was sent, but its state could not be read";
                return false;
            }
            if (observed.Value != enabled)
            {
                _lastDebugCommandFailure = "Construction command " + command + " did not reach the requested state";
                return false;
            }
            return true;
        }

        private static bool? ReadConstructionToggleState(string command)
        {
            command = GetDebugCommandName(command);
            object value;
            if (command == "creativemode")
            {
                Type type = ReflectionTools.FindType("Sons.Gameplay.GameSetup.GameSetupManager");
                value = ReflectionTools.InvokeStaticNoArgMethod(type, "GetCreativeModeSetting");
            }
            else if (command == "instantbuild" || command == "enablestructureghosts")
            {
                object instance = GetLiveStructureCraftingSystem();
                if (instance == null) return null;
                value = ReflectionTools.GetMemberValue(instance, command == "instantbuild" ? "InstantBuild" : "_ghostsAreActive");
            }
            else return null;
            return value is bool state ? state : (bool?)null;
        }

        private static object GetLiveStructureCraftingSystem()
        {
            Type type = ReflectionTools.FindType("Sons.Crafting.Structures.StructureCraftingSystem");
            object instance = ReflectionTools.GetStaticMemberValue(type, "_instance");
            Component component = instance as Component;
            if (component == null) return null;
            try
            {
                GameObject gameObject = component.gameObject;
                return gameObject != null && gameObject.activeInHierarchy ? instance : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
