using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private Type _boltNetworkType;
        private DateTime _nextRuntimeStatusUtc;
        private bool _worldReady;
        private bool? _networkRunning;
        private bool? _networkServer;
        private string _sessionStatus = "Waiting for game";
        private string _runtimePatchStatus = "Not initialized";
        private float _windowScale = 1f;

        internal void SetRuntimePatchStatus(string status)
        {
            _runtimePatchStatus = status;
        }

        private void RefreshRuntimeStatus()
        {
            if (DateTime.UtcNow < _nextRuntimeStatusUtc) return;
            _nextRuntimeStatusUtc = DateTime.UtcNow.AddSeconds(1);
            _boltNetworkType ??= ReflectionTools.FindType("BoltNetwork");
            _worldReady = IsLocalPlayerInWorld();
            _networkRunning = ReadNetworkFlag("isRunning");
            _networkServer = _networkRunning == true ? ReadNetworkFlag("isServer") : null;
            _hostActionsAvailable = _worldReady && _networkRunning == true && _networkServer == true;
            _worldActionsAvailable = _hostActionsAvailable || _worldReady && _networkRunning == false && IsSinglePlayerSession();
            _serverCommandsAvailable = CanSendServerCommands();
            _sessionStatus = _networkRunning == true
                ? (_networkServer == true ? "Multiplayer host" : _networkServer == false ? "Multiplayer client" : "Network role unknown")
                : _networkRunning == false
                    ? (_worldReady ? "Local world" : "Menu / loading")
                    : "Network unavailable";
            TickServerSleepPolicy();
            TickServerAdmissionAvailability();
            RefreshLoaderPolicySnapshot();
        }

        private bool? ReadNetworkFlag(string name)
        {
            object value = ReflectionTools.GetStaticMemberValue(_boltNetworkType, name);
            return value is bool flag ? flag : null;
        }

        private bool HasHostSession()
        {
            _boltNetworkType ??= ReflectionTools.FindType("BoltNetwork");
            return ReadNetworkFlag("isRunning") == true && ReadNetworkFlag("isServer") == true && IsLocalPlayerInWorld();
        }

        private string GetGuardBadge()
        {
            if (!_disallowCheats) return "GUARD OFF";
            if (!_worldReady || _networkRunning != true) return "GUARD IDLE";
            if (_networkServer != true) return "HOST ONLY";
            if (LoaderAdminRequestHookInstalled && _antiCheatPatchCount == 0) return "PARTIAL";
            return _runtimePatchesApplied && _antiCheatPatchCount > 0 ? "HOOKS ON" : "LIMITED";
        }

        private string GetGuardStatusText()
        {
            if (!_disallowCheats) return "Guard policy is off.";
            if (!_worldReady || _networkRunning != true) return "Guard policy is enabled; waiting for a multiplayer world.";
            if (_networkServer != true) return "Guard policy is enabled; host authority is unavailable in this session.";
            if (LoaderAdminRequestHookInstalled)
                return "Native admin-command policy is attached through Modstoke Loader. Other event rules have separate coverage; review the runtime panel.";
            if (!_runtimePatchesApplied || _antiCheatPatchCount == 0)
                return "Limited coverage: polling can run, but server event guards are unavailable.";
            return "Server event hooks are installed. Enabled checks and observed evidence determine enforcement.";
        }

        private void DrawOverviewTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(630f));
            BeginPanel("Session");
            GUILayout.Label(_sessionStatus, _contextValueStyle, GUILayout.Height(28f));
            GUILayout.Label(GetGuardStatusText(), _hintStyle, GUILayout.Height(44f));
            DrawOverviewValue("Online", CountPlayers(true).ToString(CultureInfo.InvariantCulture));
            DrawOverviewValue("Offline history", CountPlayers(false).ToString(CultureInfo.InvariantCulture));
            DrawOverviewValue("Presence unknown", CountUnresolvedPlayers().ToString(CultureInfo.InvariantCulture));
            DrawOverviewValue("Jailed", _jailedPlayers.Count.ToString(CultureInfo.InvariantCulture));
            DrawOverviewValue("Roster refresh", _autoRosterRefresh ? "Every " + _rosterRefreshSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds" : "Manual");
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Player Roster", _primaryButtonStyle, GUILayout.Height(34f))) _activeTab = "Players";
            if (GUILayout.Button("Host Controls", _buttonStyle, GUILayout.Height(34f))) _activeTab = "Admin";
            if (GUILayout.Button("Refresh Roster", _quietButtonStyle, GUILayout.Height(34f))) RefreshPlayers();
            GUILayout.EndHorizontal();
            EndPanel();

            BeginPanel("Administration");
            DrawOverviewValue("Guard policy", _disallowCheats ? "Enabled" : "Off");
            DrawOverviewValue("Automatic jail", _autoJailCheaters ? "Enabled in policy" : "Off");
            DrawOverviewValue("External review", _externalAiReviewEnabled && _codexBridgeEnabled ? "Enabled" : "Off");
            DrawOverviewValue("Learned policy", _learnedSelfAdminPolicyEnabled ? "Enabled" : "Off");
            GUILayout.Label("Policy choices do not prove hook coverage. Review the runtime panel before relying on event filtering.", _hintStyle, GUILayout.Height(40f));
            EndPanel();
            GUILayout.EndVertical();

            GUILayout.Space(16f);
            GUILayout.BeginVertical(GUILayout.Width(630f));
            BeginPanel("Runtime");
            DrawOverviewValue("Modstoke Admin", Plugin.Version);
            DrawOverviewValue("Managed runtime", ".NET " + Environment.Version);
            DrawOverviewValue("Modstoke Loader menu", LoaderIntegrationStatus);
            DrawOverviewValue("Modstoke Loader policy", LoaderPolicyStatus);
            DrawOverviewValue("Session lifecycle", LoaderSessionStatus);
            DrawOverviewValue("Native admin commands", LoaderAdminRequestStatus);
            DrawOverviewValue("Shared server chat", LoaderChatStatus);
            DrawOverviewValue("Shared player-hit hook", LoaderPlayerHitStatus);
            DrawOverviewValue("Shared structure-hit hook", LoaderStructureHitStatus);
            DrawOverviewValue("Shared blueprint hook", LoaderBlueprintStatus);
            DrawOverviewValue("Shared structure relocation", LoaderStructureRelocationStatus);
            DrawOverviewValue("Shared ingredient requests", LoaderIngredientStatus);
            DrawOverviewValue("Shared completion requests", LoaderCompletionStatus);
            DrawOverviewValue("Shared storage-add hook", LoaderStorageAddStatus);
            DrawOverviewValue("Shared storage-remove hook", LoaderStorageRemoveStatus);
            DrawOverviewValue("Shared NPC-hit hook", LoaderVailHitStatus);
            DrawOverviewValue("Shared dismemberment hook", LoaderDismemberHitStatus);
            DrawOverviewValue("Shared container-open hook", LoaderContainerOpenStatus);
            DrawOverviewValue("Shared object-break hook", LoaderObjectBreakStatus);
            DrawOverviewValue("World", _worldReady ? "Local player available" : "Waiting for local player");
            GUILayout.Space(10f);
            GUILayout.Label("Admin-owned hooks", _sectionTitleStyle);
            GUILayout.Label(_runtimePatchStatus, _hintStyle, GUILayout.Height(24f));
            DrawOverviewValue("Legacy chat", FormatHookCount(_chatPatchCount));
            DrawOverviewValue("Roster / movement events", FormatHookCount(_networkPatchCount));
            DrawOverviewValue("Server event guards", FormatHookCount(_antiCheatPatchCount));
            DrawOverviewValue("Modstoke SDK audit", LoaderAuditTransportAvailable ? "Registered" : "Unavailable");
            DrawOverviewValue("Legacy audit receiver", FormatHookCount(_inventoryAuditPatchCount));
            DrawOverviewValue("Vehicle spawns", FormatHookCount(_vehicleSpawnPatchCount));
            GUILayout.Label(_runtimePatchesApplied
                ? "Counts show installed patches, not complete protection or gameplay verification."
                : "These hooks are unavailable. Modstoke Loader's shared routes are reported separately above.",
                _hintStyle, GUILayout.Height(46f));
            EndPanel();

            BeginPanel("Console");
            DrawOverviewValue("Open / close", _toggleMenuKey);
            DrawOverviewValue("Reload settings", _reloadKey);
            DrawOverviewValue("Display scale", Math.Round(_windowScale * 100f).ToString(CultureInfo.InvariantCulture) + "%");
            GUILayout.Label("The console fits the current display. Search the roster by player name, alias or Steam64 ID.", _hintStyle, GUILayout.Height(40f));
            EndPanel();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawOverviewValue(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _hintStyle, GUILayout.Width(200f), GUILayout.Height(24f));
            GUILayout.Label(value, _labelStyle, GUILayout.Height(24f));
            GUILayout.EndHorizontal();
        }

        private static string FormatHookCount(int count)
        {
            return count > 0 ? count.ToString(CultureInfo.InvariantCulture) + " installed" : "Unavailable";
        }

        private static GUILayoutOption MinimumHeight(float height)
        {
            // Restored GUILayout.MinHeight boxes a CLR float instead of an IL2CPP value.
            Il2CppSystem.Object nativeHeight = height;
            return new GUILayoutOption(GUILayoutOption.Type.minHeight, nativeHeight);
        }

        private void DrawScaledWindow()
        {
            EnsureStyles();
            float scale = Math.Min(1f, Math.Min(Math.Max(1f, Screen.width - 24f) / WindowWidth,
                Math.Max(1f, Screen.height - 24f) / WindowHeight));
            float viewportWidth = Screen.width / scale;
            float viewportHeight = Screen.height / scale;
            _windowRect.width = WindowWidth;
            _windowRect.height = WindowHeight;
            if (!_windowPositionInitialized)
            {
                _windowRect.x = (viewportWidth - WindowWidth) * 0.5f;
                _windowRect.y = (viewportHeight - WindowHeight) * 0.5f;
                _windowPositionInitialized = true;
            }
            else if (scale != _windowScale)
            {
                _windowRect.x *= _windowScale / scale;
                _windowRect.y *= _windowScale / scale;
            }
            _windowScale = scale;
            ClampWindowToViewport(viewportWidth, viewportHeight);

            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            Color oldBackground = GUI.backgroundColor;
            Color oldContent = GUI.contentColor;
            int oldDepth = GUI.depth;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f)) * oldMatrix;
                GUI.depth = -10000;
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                _windowRect = GUI.Window(46071, _windowRect, (GUI.WindowFunction)DrawWindow, "", _windowStyle);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
                GUI.depth = oldDepth;
            }
            ClampWindowToViewport(viewportWidth, viewportHeight);
        }

        private void ClampWindowToViewport(float width, float height)
        {
            _windowRect.x = Clamp(_windowRect.x, 0f, Math.Max(0f, width - WindowWidth));
            _windowRect.y = Clamp(_windowRect.y, 0f, Math.Max(0f, height - WindowHeight));
        }
    }
}
