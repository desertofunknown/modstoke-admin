using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly Dictionary<string, bool> _debugToggleRequests = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _observedDebugToggles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextDebugToggleReadUtc;

        private void RefreshDebugToggleStates()
        {
            if (DateTime.UtcNow < _nextDebugToggleReadUtc) return;
            _nextDebugToggleReadUtc = DateTime.UtcNow.AddSeconds(1);
            _observedDebugToggles.Clear();
            if (!_hostActionsAvailable)
            {
                ResetServerEnvironmentRequests();
                _locatorRequestPrepared = false;
            }
            if (!_worldReady)
            {
                _debugToggleRequests.Clear();
                ResetWorldLocatorInspector();
                ResetWorldEnvironmentStatus();
                ResetVisualDebugState();
                return;
            }
            if (!_menuOpen) return;
            if (_activeTab == "Utility")
            {
                RefreshWorldEnvironmentStatus();
                return;
            }
            if (_activeTab != "Debug") return;

            RefreshWorldEnvironmentStatus();
            RefreshVisualDebugState();

            RecordDebugToggleState("_speedyrun");
            RecordDebugToggleState("_blockPlayerFinalDeath");
            RecordDebugToggleState("_spawnedObjectStats");
            RecordDebugToggleState("_aiDisable");
            RecordDebugToggleState("_animalsEnabled");
            RecordDebugToggleState("_instantBuild");
            RecordDebugToggleState("_creativeMode");
            RecordDebugToggleState("_enableStructureGhosts");
            RecordDebugToggleState("_aiShowStats");
            RecordDebugToggleState("_virtualPickupDebug");
            RecordDebugToggleState("_freecamera");
        }

        private void RecordDebugToggleState(string methodName)
        {
            bool? state = ReadDebugToggleState(methodName);
            if (state.HasValue) _observedDebugToggles[methodName] = state.Value;
        }

        private static bool? ReadDebugToggleState(string methodName)
        {
            object value;
            switch (methodName.ToLowerInvariant())
            {
                case "_speedyrun":
                    Type console = ReflectionTools.FindType("TheForest.DebugConsole");
                    object instance = ReflectionTools.GetStaticMemberValue(console, "Instance");
                    value = ReflectionTools.GetMemberValue(instance, "_speedyRunIsActive");
                    break;
                case "_blockplayerfinaldeath":
                    value = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("PlayerStats"), "_blockFinalDeath");
                    break;
                case "_spawnedobjectstats":
                    value = ReflectionTools.GetStaticMemberValue(ReflectionTools.FindType("Sons.Gameplay.SpawnedObjectManager"), "ShowStats");
                    break;
                case "_aidisable":
                    value = ReflectionTools.GetMemberValue(GetWorldSimulation(), "IsDisabled");
                    break;
                case "_animalsenabled":
                    value = ReflectionTools.GetMemberValue(GetWorldSimulation(), "_animalsEnabled");
                    break;
                case "_instantbuild":
                case "_creativemode":
                case "_enablestructureghosts":
                    return ReadConstructionToggleState(methodName);
                case "_aishowstats":
                case "_virtualpickupdebug":
                case "_freecamera":
                    return ReadVisualToggleState(methodName);
                default:
                    return null;
            }
            return value is bool state ? state : null;
        }

        private void ResetDebugToggleStates()
        {
            _debugToggleRequests.Clear();
            _observedDebugToggles.Clear();
            _nextDebugToggleReadUtc = DateTime.MinValue;
            ResetWorldEnvironmentStatus();
            ResetServerEnvironmentRequests();
            ResetWorldLocatorInspector();
            ResetVisualDebugState();
        }

        private void DrawDebugToggleButton(string label, string methodName, string onArg, string offArg, string onCommand, string offCommand, float width, GUIStyle onStyle, GUIStyle offStyle, string tooltip, bool defaultEnabled = false)
        {
            bool observed;
            bool hasObserved = _observedDebugToggles.TryGetValue(methodName, out observed);
            bool requested;
            bool hasRequest = _debugToggleRequests.TryGetValue(methodName, out requested);
            bool enabled = hasObserved ? observed : hasRequest ? requested : defaultEnabled;
            string state = hasObserved ? (observed ? "On" : "Off") : hasRequest ? (requested ? "On*" : "Off*") : "?";
            string detail = hasObserved ? "Observed in the game; refreshed once per second."
                : hasRequest ? "Last request: " + (requested ? "on" : "off") + ". Current state is unavailable."
                : "Current state is unavailable.";
            detail += " Click to request " + (enabled ? "off." : "on.");
            Texture2D icon = ResolveDebugButtonIcon(label, methodName, enabled ? offCommand : onCommand);
            GUIStyle style = hasObserved ? (observed ? onStyle : offStyle) : _quietButtonStyle;
            if (!DrawDebugCommandButton(icon, label + ": " + state, style,
                width + 8f, 32f, methodName, tooltip + " " + detail)) return;

            if (!IsLocalPlayerInWorld())
            {
                ActionFail(label + " unavailable: join a world first");
                return;
            }
            bool? current = ReadDebugToggleState(methodName);
            bool next = !(current ?? enabled);
            bool ok = InvokeDebugConsoleMethod(methodName, next ? onArg : offArg, next ? onCommand : offCommand);
            if (ok) _debugToggleRequests[methodName] = next;
            _observedDebugToggles.Remove(methodName);
            _nextDebugToggleReadUtc = DateTime.MinValue;
            ActionResult(ok, label + ": " + (next ? "on" : "off") + " requested" + GetWorldCommandObservation(methodName), DebugCommandFailure(label));
        }

        private bool InvokeDebugConsoleMethod(string methodName, string arg, string fallbackCommand)
        {
            if (!CheckDebugCommandAccess(String.IsNullOrEmpty(methodName) ? fallbackCommand : methodName)) return false;
            bool? typedResult = TryRunTypedWorldDebugCommand(methodName, arg);
            if (typedResult.HasValue) return typedResult.Value;
            typedResult = TryRunConstructionDebugCommand(methodName, arg);
            if (typedResult.HasValue) return typedResult.Value;
            typedResult = TryRunSeasonDebugCommand(methodName, arg);
            if (typedResult.HasValue) return typedResult.Value;
            typedResult = TryRunVisualDebugCommand(methodName, arg);
            if (typedResult.HasValue) return typedResult.Value;
            if (!String.IsNullOrEmpty(methodName))
            {
                Type type = ReflectionTools.FindType("TheForest.DebugConsole") ?? ReflectionTools.FindType("DebugConsole");
                if (type != null)
                {
                    object instance = FindRuntimeObject(type);
                    object[] args = new object[] { arg ?? "" };
                    bool? result = TryInvokeDebugCommand(instance, type, methodName, args, false);
                    if (result.HasValue) return result.Value;
                    result = TryInvokeDebugCommand(null, type, methodName, args, true);
                    if (result.HasValue) return result.Value;
                }
            }
            if (!String.IsNullOrEmpty(fallbackCommand)) return InvokeConsoleCommand(fallbackCommand);
            _lastDebugCommandFailure = "Command is unavailable in this game build";
            return false;
        }

        private bool InvokeConsoleCommand(string command)
        {
            if (!CheckDebugCommandAccess(command)) return false;
            string[] typeNames = new string[] { "TheForest.DebugConsole", "DebugConsole", "BoltConsole" };
            string[] methodNames = new string[] { "SendCommand", "RunCommand", "ExecuteCommand", "Execute", "Command", "SubmitCommand", "OnCommand" };
            object[] args = new object[] { command };
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = ReflectionTools.FindType(typeNames[i]);
                if (type == null) continue;
                object instance = FindRuntimeObject(type);
                for (int j = 0; j < methodNames.Length; j++)
                {
                    bool? result = TryInvokeDebugCommand(null, type, methodNames[j], args, true);
                    if (result.HasValue) return result.Value;
                    result = TryInvokeDebugCommand(instance, type, methodNames[j], args, false);
                    if (result.HasValue) return result.Value;
                }
            }
            _lastDebugCommandFailure = "Console command route is unavailable";
            return false;
        }

        // Only a missing method permits another route. A rejection or exception ends the request.
        private bool? TryInvokeDebugCommand(object instance, Type type, string methodName, object[] args, bool staticOnly)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                | (staticOnly ? BindingFlags.Static : BindingFlags.Instance);
            while (type != null)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); }
                catch (Exception ex)
                {
                    LogWarning("Command lookup failed for " + methodName + ": " + ex.Message);
                    _lastDebugCommandFailure = "Command lookup failed";
                    return false;
                }
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name != methodName || method.ContainsGenericParameters
                        || !ParametersMatch(method.GetParameters(), args)) continue;
                    if (!staticOnly && instance == null)
                    {
                        _lastDebugCommandFailure = "Game console is not ready";
                        return false;
                    }
                    try
                    {
                        object result = method.Invoke(instance, args);
                        bool accepted = NativeActionAccepted(result);
                        if (!accepted) _lastDebugCommandFailure = "The game rejected the request";
                        return accepted;
                    }
                    catch (Exception ex)
                    {
                        Exception cause = ex.InnerException ?? ex;
                        LogWarning("Command failed for " + methodName + ": " + cause.Message);
                        _lastDebugCommandFailure = "The game could not execute the request";
                        return false;
                    }
                }
                type = type.BaseType;
            }
            return null;
        }
    }
}
