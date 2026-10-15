using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _worldActionsAvailable;
        private string _lastDebugCommandFailure = "";

        internal bool HasWorldAuthority()
        {
            if (!IsLocalPlayerInWorld()) return false;
            _boltNetworkType ??= ReflectionTools.FindType("BoltNetwork");
            bool? running = ReadNetworkFlag("isRunning");
            return running == true ? ReadNetworkFlag("isServer") == true
                : running == false && IsSinglePlayerSession();
        }

        internal bool PossessionInputAllowed { get { return !_menuOpen; } }

        private static bool IsSinglePlayerSession()
        {
            object value = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("TheForest.Utils.GameSetup"), "IsSinglePlayer");
            return value is bool singlePlayer && singlePlayer;
        }

        private static string GetDebugCommandName(string command)
        {
            string value = (command ?? "").Trim();
            int separator = value.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
            if (separator >= 0) value = value.Substring(0, separator);
            return value.TrimStart('_').ToLowerInvariant();
        }

        private static bool IsSharedWorldCommand(string command)
        {
            switch (command)
            {
                case "settimeofday":
                case "locktimeofday":
                case "jumptimeofday":
                case "setcurrentday":
                case "setgametimespeed":
                case "season":
                case "lockseason":
                case "unlockseason":
                case "instantbuild":
                case "creativemode":
                case "enablestructureghosts":
                case "forcerain":
                case "regrowalltrees":
                case "restoreallworldlocators":
                case "treeradius":
                case "clearbushradius":
                case "diggingclear":
                case "playerstriggertraps":
                case "refillcontainers":
                case "aidisable":
                case "aigodmode":
                case "aidummy":
                case "animalsenabled":
                case "aithoughtnocooldown":
                case "killradius":
                case "igniteradius":
                case "shockradius":
                case "burnbodyradius":
                case "dismemberradius":
                case "gibradius":
                case "damageradius":
                case "creepyattackparty":
                case "creepyvillage":
                case "robbycarry":
                case "removedead":
                case "removeliving":
                case "animallimitmult":
                case "save":
                case "forcesave":
                    return true;
                default:
                    return false;
            }
        }

        private string GetDebugCommandUnavailableReason(string command, bool current)
        {
            string name = GetDebugCommandName(command);
            if (name == "help" || name == "clear" || name == "dumplobbyinfo" || name == "gccollect") return "";
            if (!(current ? IsLocalPlayerInWorld() : _worldReady)) return "Join a world first";
            if (name == "kickplayers")
                return (current ? HasHostSession() : _hostActionsAvailable) ? "" : "Requires the multiplayer host";
            if (IsSharedWorldCommand(name) && !(current ? HasWorldAuthority() : _worldActionsAvailable))
                return "Requires a single-player world or the multiplayer host";
            return "";
        }

        private bool CheckDebugCommandAccess(string command)
        {
            _lastDebugCommandFailure = GetDebugCommandUnavailableReason(command, true);
            return String.IsNullOrEmpty(_lastDebugCommandFailure);
        }

        private string DebugCommandFailure(string label)
        {
            return label + " failed: " + (String.IsNullOrEmpty(_lastDebugCommandFailure)
                ? "the game could not dispatch the request" : _lastDebugCommandFailure);
        }

        private bool DrawDebugCommandButton(Texture2D icon, string label, GUIStyle style, float width, float height, string command, string tooltip = null)
        {
            string reason = GetDebugCommandUnavailableReason(command, false);
            GUIContent content = new GUIContent(label, icon, String.IsNullOrEmpty(reason) ? tooltip ?? label : reason);
            return DrawModerationButton(content, style, String.IsNullOrEmpty(reason), GUILayout.Width(width), GUILayout.Height(height));
        }

        private void DrawWorldActionAvailability()
        {
            GUILayout.Label(_worldActionsAvailable
                ? "World and companion changes available. Command requests may take time to appear in game."
                : "World and companion changes require single-player or the host. Local tools remain available in a loaded world.", _hintStyle);
        }

        private static object GetWorldSimulation()
        {
            Type type = ReflectionTools.FindType("Sons.Ai.Vail.VailWorldSimulation");
            return ReflectionTools.GetStaticMemberValue(type, "_instance");
        }

        private bool? TryRunTypedWorldDebugCommand(string methodName, string argument)
        {
            string command = GetDebugCommandName(methodName);
            if (command != "aidisable" && command != "animalsenabled" && command != "animallimitmult") return null;

            Type type;
            string method;
            object[] args;
            if (command == "animallimitmult")
            {
                float value;
                if (!Single.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || Single.IsNaN(value) || Single.IsInfinity(value) || value <= 0f || value > 50f)
                {
                    _lastDebugCommandFailure = "Animal multiplier must be greater than zero and at most 50";
                    return false;
                }
                type = ReflectionTools.FindType("Sons.Ai.Vail.VailActorManager");
                method = "SetAnimalLimitMult";
                args = new object[] { value };
                if (ReflectionTools.GetStaticMemberValue(type, "_instance") == null)
                {
                    _lastDebugCommandFailure = "Animal manager is not ready";
                    return false;
                }
            }
            else
            {
                bool enabled;
                if (String.Equals(argument, "on", StringComparison.OrdinalIgnoreCase)) enabled = true;
                else if (String.Equals(argument, "off", StringComparison.OrdinalIgnoreCase)) enabled = false;
                else
                {
                    _lastDebugCommandFailure = "Choose on or off";
                    return false;
                }
                if (GetWorldSimulation() == null)
                {
                    _lastDebugCommandFailure = "World simulation is not ready";
                    return false;
                }
                type = ReflectionTools.FindType("Sons.Ai.Vail.VailWorldSimulation");
                method = command == "aidisable" ? "SetDisabled" : "SetAnimalsEnabled";
                args = new object[] { enabled };
            }
            object result;
            bool invoked = InvokeMethod(null, type, method, args, true, out result);
            if (!invoked) _lastDebugCommandFailure = "World command is unavailable or failed";
            return invoked && NativeActionAccepted(result);
        }

        private string GetWorldCommandObservation(string methodName)
        {
            switch (GetDebugCommandName(methodName))
            {
                case "aidisable":
                case "animalsenabled":
                case "instantbuild":
                case "creativemode":
                case "enablestructureghosts":
                    bool? value = ReadDebugToggleState(methodName);
                    return value.HasValue ? "; observed " + (value.Value ? "on" : "off") : "; current state unavailable";
                case "animallimitmult":
                    object multiplier = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("Sons.Ai.Vail.VailActorManager"), "_animalLimitMult");
                    return multiplier is float number && !Single.IsNaN(number) && !Single.IsInfinity(number)
                        ? "; observed multiplier " + number.ToString("0.##", CultureInfo.InvariantCulture) : "; multiplier unavailable";
                default:
                    string command = GetDebugCommandName(methodName);
                    string observation = GetVisualCommandObservation(command);
                    return observation.Length > 0 ? observation : GetEnvironmentCommandObservation(command);
            }
        }

        private bool ValidateDebugActionArgument(string methodName, ref string argument)
        {
            string command = GetDebugCommandName(methodName);
            bool? worldArgument = ValidateWorldCommandArgument(command, ref argument);
            if (worldArgument.HasValue) return worldArgument.Value;
            float minimum;
            float maximum;
            switch (command)
            {
                case "gotocoords":
                    string[] coordinates = argument.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    float x;
                    float y;
                    float z;
                    if (coordinates.Length != 3
                        || !Single.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        || !Single.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                        || !Single.TryParse(coordinates[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)
                        || !IsFiniteDebugValue(x) || !IsFiniteDebugValue(y) || !IsFiniteDebugValue(z))
                    {
                        ActionFail("Coordinates must contain three finite numbers: X Y Z");
                        return false;
                    }
                    argument = x.ToString("G9", CultureInfo.InvariantCulture) + " " + y.ToString("G9", CultureInfo.InvariantCulture)
                        + " " + z.ToString("G9", CultureInfo.InvariantCulture);
                    return true;
                case "gravity": minimum = -30f; maximum = 5f; break;
                case "timescale":
                case "setgametimespeed": minimum = 0.05f; maximum = 8f; break;
                case "camerafov": minimum = 40f; maximum = 120f; break;
                default: return true;
            }
            float value;
            if (!TryParseLocalDebugFloat(argument, command, minimum, maximum, out value)) return false;
            argument = value.ToString("G9", CultureInfo.InvariantCulture);
            return true;
        }
    }
}
