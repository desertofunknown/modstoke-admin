using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    [BepInPlugin("com.openacai.sotf.admin", "Modstoke Admin", Version)]
    public sealed class Plugin : BasePlugin
    {
        public const string Version = "0.5.111";
        private const string EnableHarmonyPatchesEnvVar = "OPENACAI_ADMIN_ENABLE_HARMONY_PATCHES";
        internal static ManualLogSource LogSource;
        internal static AdminCore Core;
        private Harmony _harmony;

        public override void Load()
        {
            TraceAdmin("Plugin.Load entered");
            LogSource = Log;
            TraceAdmin("ResolveSettingsDirectory");
            string modDir = ResolveSettingsDirectory();
            TraceAdmin("Creating AdminCore");
            Core = new AdminCore(modDir);
            TraceAdmin("Core.LoadConfig");
            Core.LoadConfig();
            Core.InitializeLoaderServerIntegration();
            Core.InitializeSaveActivity();
            if (ShouldApplyRuntimePatches())
            {
                try
                {
                    TraceAdmin("Core.ApplyRuntimePatches");
                    _harmony = new Harmony("com.openacai.sotf.admin");
                    Core.ApplyRuntimePatches(_harmony);
                    Core.SetRuntimePatchStatus("Patch setup completed");
                    TraceAdmin("Core.ApplyRuntimePatches returned");
                }
                catch (Exception ex)
                {
                    TraceAdmin("Core.ApplyRuntimePatches managed exception: " + ex);
                    Log.LogWarning("Runtime patch setup skipped: " + ex.Message);
                    Core.SetRuntimePatchStatus("Patch setup failed; coverage may be partial");
                }
            }
            else
            {
                TraceAdmin("Core.ApplyRuntimePatches skipped");
                Core.SetRuntimePatchStatus("Disabled by runtime policy");
                Log.LogInfo("Admin runtime Harmony patches are disabled by default on " + CurrentDotnetLabel() + ". Loader/menu Harmony support is reported separately by Modstoke Loader. Set " + EnableHarmonyPatchesEnvVar + "=1 only for isolated admin-patch diagnostics.");
            }
            TraceAdmin("Core.RecordStartupEvents");
            Core.RecordStartupEvents();

            try
            {
                TraceAdmin("ClassInjector.RegisterTypeInIl2Cpp");
                ClassInjector.RegisterTypeInIl2Cpp<OpenACAIAdminDriver>();
                TraceAdmin("ClassInjector.RegisterTypeInIl2Cpp returned");
            }
            catch (Exception ex)
            {
                TraceAdmin("ClassInjector.RegisterTypeInIl2Cpp managed exception: " + ex);
            }

            TraceAdmin("Creating host GameObject");
            GameObject host = new GameObject("OpenACAI Admin Tool");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<OpenACAIAdminDriver>();
            Core.InitializeLoaderIntegration();
            TraceAdmin("Plugin.Load completed");
            Log.LogInfo("Modstoke Admin by OpenACAI loaded. F7 menu, F8 reload, F10 Kelvin/Robby possession.");
        }

        private static bool ShouldApplyRuntimePatches()
        {
            string value = Environment.GetEnvironmentVariable(EnableHarmonyPatchesEnvVar);
            if (!String.IsNullOrWhiteSpace(value))
            {
                return String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
            }

            return Environment.Version.Major < 10;
        }

        private static string CurrentDotnetLabel()
        {
            Version version = Environment.Version;
            return ".NET " + version.Major.ToString(CultureInfo.InvariantCulture) + " (" + version.ToString() + ")";
        }

        private static void TraceAdmin(string message)
        {
            try
            {
                string root = null;
                try { root = Paths.GameRootPath; } catch { }
                if (String.IsNullOrWhiteSpace(root))
                {
                    root = Directory.GetCurrentDirectory();
                }

                File.AppendAllText(Path.Combine(root, "openacai-admin-runtime.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string ResolveSettingsDirectory()
        {
            string pluginRoot;
            try
            {
                pluginRoot = Paths.PluginPath;
            }
            catch
            {
                pluginRoot = Path.Combine(Directory.GetCurrentDirectory(), "BepInEx", "plugins");
            }

            string settingsDir = Path.Combine(pluginRoot, "OpenACAIAdminTool");
            string legacyDir = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "ServerAdminTools");
            TryMigrateSettings(settingsDir, legacyDir);
            return settingsDir;
        }

        private static void TryMigrateSettings(string settingsDir, string legacyDir)
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                string current = Path.Combine(settingsDir, "settings.ini");
                string legacy = Path.Combine(legacyDir, "settings.ini");
                if (!File.Exists(current) && File.Exists(legacy))
                {
                    File.Copy(legacy, current, false);
                    LogSource.LogInfo("Migrated settings.ini to BepInEx plugin folder: " + current);
                }
            }
            catch (Exception ex)
            {
                LogSource.LogWarning("Settings migration skipped: " + ex.Message);
            }
        }
    }

    public sealed class OpenACAIAdminDriver : MonoBehaviour
    {
        public OpenACAIAdminDriver(IntPtr ptr) : base(ptr)
        {
        }

        private void Update()
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.Update();
            }
        }

        private void OnGUI()
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.OnGUI();
            }
        }

        private void OnDestroy()
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.StopCompanionControl();
                Plugin.Core.StopLoaderServerIntegration();
                Plugin.Core.ClearJailReturns();
                Plugin.Core.StopSaveActivity();
                Plugin.Core.StopLoaderIntegration();
                Plugin.Core.FlushPlayerHistory();
            }
        }
    }

    internal sealed partial class AdminCore
    {
        private const float WindowWidth = 1580f;
        private const float WindowHeight = 920f;
        private const string DefaultHighValueItemTokens = "354,355,356,358,361,365,367,374,375,376,377,378,394,431,478,485,522,566,567,568,572,630,663,727,NightVision,Pistol,Shotgun,Rifle,Revolver,Katana,Chainsaw,ModernAxe,FirefighterAxe,CompoundBow,Crossbow,RopeGun,Rebreather,Shovel,GoldenArmor,AncientArmor,MaintenanceKeycard,VIPKeycard,GuestKeycard,KnightV,PrintedSled,3DPrintedSled,Sled,GolfCart,SnowGolfCart,Pickaxe,SolafiteArmor";
        private const string BasicConstructionSalvageTokens = "Stick,StickPickup,StickPickupBundle,Stone,StonePickup,Rock,RockPickup,Log,LogPickup,LogPlank,Plank,Firewood,HalfLog,QuarterLog";
        private const string DefaultArtifactItemTokens = "542,543,544,667,668,669,689,708,712,Artifact,Artifact1,Artifact2,ArtifactPiece,CompleteArtifact,CorePiece";
        private const string DefaultStoryItemLocationRules = "";
        private const string AuditRequestPrefix = "[OpenACAI:AUDIT?]";
        private const string AuditResponsePrefix = "[OpenACAI:AUDIT!]";
        private readonly string _modDir;
        private readonly string _settingsPath;
        private readonly string _playerHistoryPath;
        private readonly string _automationStatePath;
        private readonly string _automationMemoryPath;
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastAuthorityNamePush = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModerationEntry> _bannedPlayers = new Dictionary<string, ModerationEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModerationEntry> _mutedPlayers = new Dictionary<string, ModerationEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModerationEntry> _antiCheatExemptPlayers = new Dictionary<string, ModerationEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JailEntry> _jailedPlayers = new Dictionary<string, JailEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _muteNoticeTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _antiCheatNoticeTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _globalCheatNoticeTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _recentDisconnects = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemEventWindow> _itemEventWindows = new Dictionary<string, ItemEventWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, InventorySignalState> _inventorySignals = new Dictionary<string, InventorySignalState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MovementSignalState> _movementSignals = new Dictionary<string, MovementSignalState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MovementSignalState> _movementTelemetrySignals = new Dictionary<string, MovementSignalState>(StringComparer.OrdinalIgnoreCase);
        private readonly LearnedSelfAdminPolicy _learnedSelfAdminPolicy = new LearnedSelfAdminPolicy();
        private readonly List<GuardedItemLocationRule> _storyItemLocationRules = new List<GuardedItemLocationRule>();
        private readonly List<VehicleSpawnProbe> _blockedVehicleSpawnProbes = new List<VehicleSpawnProbe>();
        private readonly Dictionary<string, int> _itemNameIdCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _itemIdNameCache = new Dictionary<int, string>();
        private readonly HashSet<int> _unresolvedItemIds = new HashSet<int>();
        private readonly Dictionary<string, PlayerEntry> _registeredPlayers = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlayerEntry> _sessionPlayers = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PlayerEntry> _playerHistory = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PlayerEntry> _players = new List<PlayerEntry>();
        private readonly List<string> _eventLog = new List<string>();
        private readonly PossessionController _possession;
        private string _loadedPlayerHistoryContext = "";
        private DateTime _nextPlayerHistorySaveUtc = DateTime.MinValue;

        private Rect _windowRect = new Rect(32f, 32f, WindowWidth, WindowHeight);
        private Vector2 _contentScroll;
        private Vector2 _playerScroll;
        private Vector2 _rosterScroll;
        private Vector2 _banScroll;
        private Vector2 _muteScroll;
        private Vector2 _eventLogScroll;
        private Vector2 _itemCatalogScroll;
        private string _activeTab = "Overview";
        private string _toggleMenuKey = "F7";
        private string _reloadKey = "F8";
        private string _possessKey = "F10";
        private string _localAlias = "";
        private string _localAliasInput = "";
        private string _manualSteamIdInput = "";
        private string _manualNameInput = "";
        private string _aliasKeyInput = "";
        private string _aliasNameInput = "";
        private string _adminReasonInput = "Removed by host";
        private string _muteMessageInput = "You are muted on this server.";
        private string _commandInput = "";
        private string _debugCommandInput = "";
        private string _debugActiveTab = "Self";
        private string _debugItemCategory = "All";
        private string _debugItemSearchInput = "";
        private string _debugItemInput = "Pistol";
        private string _debugItemCountInput = "1";
        private string _debugTagInput = "Weapon";
        private string _debugCoordInput = "";
        private string _debugTimeInput = "12";
        private string _debugRadiusInput = "15";
        private string _debugRadiusDamageInput = "";
        private string _debugEffectLifetimeInput = "";
        private string _debugPlayerCommandInput = "debug";
        private string _debugPlayerCommandDataInput = "godmode on";
        private string _debugWalkInput = "5";
        private string _debugRunInput = "8";
        private string _debugSwimInput = "4";
        private string _debugJumpInput = "1";
        private string _debugScaleInput = "1";
        private string _debugGravityInput = "-9.81";
        private string _debugTimeScaleInput = "1";
        private string _debugFovInput = "70";
        private string _debugDayInput = "1";
        private string _debugSeasonInput = "Summer";
        private string _debugObjectInput = "";
        private string _selectedPlayerKey = "";
        private string _lastAction = "Ready";
        private string _lastRosterDiagnostics = "";
        private string _lastRosterSummary = "";
        private string _lastRosterContext = "";
        private bool _menuOpen;
        private bool _serverPushAliases;
        private bool _experimentalPossession = true;
        private bool _runtimePatchesApplied;
        private bool _autoRosterRefresh = true;
        private bool _disallowCheats;
        private bool _autoJailCheaters = true;
        private bool _strictItemEventGuard = true;
        private bool _earlyItemGuard = true;
        private bool _itemBurstGuard = true;
        private bool _highValueItemGuard = true;
        private bool _artifactItemGuard = true;
        private bool _storyItemLocationGuard = true;
        private bool _jailedWorldEventGuard = true;
        private bool _serverGuardAnnouncements = true;
        private bool _clientInventoryAudit = true;
        private bool _passivePlayerStateAudit = true;
        private bool _nativeServerValidationGuard = true;
        private bool _movementGuard = true;
        private bool _movementTelemetryLog = true;
        private bool _codexBridgeEnabled = true;
        private bool _externalAiReviewEnabled;
        private bool _learnedSelfAdminPolicyEnabled = true;
        private bool _speedHackGuard = true;
        private bool _teleportGuard = true;
        private bool _noclipGuard = true;
        private bool _enforceServerCheatsOff = true;
        private bool _vehicleSpawnGuard = true;
        private bool _autoBanRepeatCheaters;
        private float _possessSpeed = 5f;
        private float _aliasSweepSeconds = 2f;
        private float _rosterRefreshSeconds = 3f;
        private float _offlineRetentionMinutes = 30f;
        private float _jailTickSeconds = 5f;
        private float _earlyItemGuardMinutes = 30f;
        private float _highValueGuardMinutes = 1440f;
        private float _itemBurstWindowSeconds = 12f;
        private float _passivePlayerStateAuditSeconds = 5f;
        private float _movementSampleSeconds = 0.75f;
        private float _movementTelemetrySampleSeconds = 1f;
        private float _codexBridgePollSeconds = 2f;
        private float _learnedPolicyRefreshSeconds = 20f;
        private float _maxHorizontalSpeed = 24f;
        private float _maxVerticalSpeed = 45f;
        private float _teleportDistanceThreshold = 70f;
        private float _golfCartHorizontalSpeed = 42f;
        private float _knightVHorizontalSpeed = 42f;
        private float _gliderHorizontalSpeed = 85f;
        private float _vehicleVerticalSpeed = 70f;
        private float _gliderVerticalSpeed = 95f;
        private float _gliderFallbackVerticalSpeed = 550f;
        private float _gliderFallbackTeleportDistance = 650f;
        private float _footClimbWindowSeconds = 8f;
        private float _footClimbLimitMeters = 12f;
        private float _legalTeleportGraceSeconds = 12f;
        private float _vehicleTransitionGraceSeconds = 5f;
        private float _joinMovementGraceSeconds = 20f;
        private float _noclipFloorY = -120f;
        private Vector3 _jailPosition = new Vector3(-378.1f, 21.6f, 1621.5f);
        private bool _warnedLegacyJailPosition;
        private DateTime _lastLocalMuteNoticeUtc = DateTime.MinValue;
        private int _lastAliasSweepTouches;
        private int _chatPatchCount;
        private int _networkPatchCount;
        private int _antiCheatPatchCount;
        private int _inventoryAuditPatchCount;
        private int _vehicleSpawnPatchCount;
        private int _itemEventCountThreshold = 25;
        private int _itemBurstCountThreshold = 10;
        private int _constructionSalvageBurstCountThreshold = 48;
        private int _autoBanTriggerCount = 3;
        private int _movementViolationThreshold = 2;
        private string _restrictedEarlyItemTokens = DefaultHighValueItemTokens;
        private string _highValueItemTokens = DefaultHighValueItemTokens;
        private string _artifactItemTokens = DefaultArtifactItemTokens;
        private string _storyItemLocationRulesText = DefaultStoryItemLocationRules;
        private int _visibleLobbyProbeLogs;
        private int _activeLobbyProbeLogs;
        private int _chatPlayerProbeLogs;
        private int _networkIdResolveLogs;
        private int _selectedPlayerIndex;
        private DateTime _nextAliasSweepUtc = DateTime.MinValue;
        private DateTime _nextRosterRefreshUtc = DateTime.MinValue;
        private DateTime _nextJailTickUtc = DateTime.MinValue;
        private DateTime _nextPolicyPushUtc = DateTime.MinValue;
        private DateTime _nextMovementSampleUtc = DateTime.MinValue;
        private DateTime _nextMovementTelemetryUtc = DateTime.MinValue;
        private DateTime _nextCodexBridgePollUtc = DateTime.MinValue;
        private DateTime _nextCodexAutomationStateWriteUtc = DateTime.MinValue;
        private DateTime _lastCodexBridgeCommandUtc = DateTime.MinValue;
        private DateTime _nextLearnedPolicyRefreshUtc = DateTime.MinValue;
        private DateTime _learnedPolicyMemoryWriteUtc = DateTime.MinValue;
        private DateTime _nextPassivePlayerStateAuditUtc = DateTime.MinValue;
        private DateTime _nextVehicleSpawnAuditUtc = DateTime.MinValue;
        private DateTime _lastVehicleSpawnBlockNoticeUtc = DateTime.MinValue;
        private bool _selfAdminFallback = true;
        private float _codexAdminQuietSeconds = 180f;
        private string _lastAdminControlMode = "";
        private long _learnedPolicyMemoryLength = -1L;
        private bool _hasRosterWorldState;
        private bool _lastRosterInWorld;
        private bool _windowPositionInitialized;
        private PlayerEntry _manualTarget;
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _headerMetaStyle;
        private GUIStyle _sideHeaderStyle;
        private GUIStyle _contextStyle;
        private GUIStyle _contextLabelStyle;
        private GUIStyle _contextValueStyle;
        private GUIStyle _smallHeaderStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _navStyle;
        private GUIStyle _navActiveStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _sidebarStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _goodPillStyle;
        private GUIStyle _warnPillStyle;
        private GUIStyle _dangerPillStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _quietButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _rowActiveStyle;
        private GUIStyle _emptyStateStyle;
        private GUIStyle _monoStyle;
        private Texture2D _whiteTex;
        private Texture2D _blueTex;
        private Texture2D _deepBlueTex;
        private Texture2D _paleBlueTex;
        private Texture2D _rowActiveTex;
        private Texture2D _panelTex;
        private Texture2D _sidebarTex;
        private Texture2D _subtleTex;
        private Texture2D _buttonTex;
        private Texture2D _buttonHoverTex;
        private Texture2D _inputTex;
        private Texture2D _inputFocusTex;
        private Texture2D _primaryTex;
        private Texture2D _primaryHoverTex;
        private Texture2D _goodTex;
        private Texture2D _warnTex;
        private Texture2D _dangerTex;
        private Texture2D _dangerHoverTex;
        private Texture2D _panelFrameTex;
        private Texture2D _sidebarFrameTex;
        private Texture2D _buttonFrameTex;
        private Texture2D _primaryFrameTex;
        private Texture2D _dangerFrameTex;
        private Texture2D _iconRoster;
        private Texture2D _iconHost;
        private Texture2D _iconPlayers;
        private Texture2D _iconWorld;
        private Texture2D _iconComms;
        private Texture2D _iconBans;
        private Texture2D _iconNpc;
        private Texture2D _iconDebug;
        private Texture2D _iconItems;
        private Texture2D _iconBuild;
        private Texture2D _iconVisual;
        private Texture2D _iconServer;
        private Texture2D _iconRaw;
        private Texture2D _iconHeal;
        private Texture2D _iconSpeed;
        private Texture2D _iconShield;
        private Texture2D _iconWarning;
        private Texture2D _iconSave;
        private Texture2D _iconSummon;
        private Texture2D _iconGoTo;
        private Texture2D _iconKick;
        private Texture2D _iconMute;
        private Texture2D _iconBan;
        private Texture2D _iconCopy;

        public AdminCore(string modDir)
        {
            _modDir = modDir;
            _settingsPath = Path.Combine(_modDir, "settings.ini");
            _playerHistoryPath = Path.Combine(_modDir, "player-history.tsv");
            _automationStatePath = Path.Combine(_modDir, "codex-automation-state.json");
            _automationMemoryPath = Path.Combine(_modDir, "codex-automation-memory.jsonl");
            _possession = new PossessionController(this);
        }

        internal float PossessSpeed
        {
            get { return _possessSpeed; }
        }

        public void RecordStartupEvents()
        {
            AppendEventLog("SYSTEM", "Modstoke Admin ready");
            AppendEventLog("SYSTEM", "Runtime patches chat=" + _chatPatchCount.ToString(CultureInfo.InvariantCulture)
                + " network=" + _networkPatchCount.ToString(CultureInfo.InvariantCulture)
                + " guard=" + _antiCheatPatchCount.ToString(CultureInfo.InvariantCulture)
                + " vehicle=" + _vehicleSpawnPatchCount.ToString(CultureInfo.InvariantCulture));
            AppendEventLog("SERVER", "Roster refresh " + (_autoRosterRefresh ? "auto" : "manual")
                + " every " + _rosterRefreshSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s");
        }

        public void Update()
        {
            RefreshRuntimeStatus();
            TickLoaderSessionIntegration();
            TickLoaderInventoryAudits();
            TickModerationNoticeTracking();
            UpdateItemCatalogAvailability();
            RefreshDebugToggleStates();
            TickLoaderIntegration();
            if (GetKeyDown(_toggleMenuKey))
            {
                ToggleMenu();
            }

            if (GetKeyDown(_reloadKey))
            {
                ReloadAliases();
            }

            if (GetKeyDown(_possessKey))
            {
                TogglePossession();
            }

            if (_serverPushAliases && DateTime.UtcNow >= _nextAliasSweepUtc)
            {
                _lastAliasSweepTouches = SweepAliasSurfaces();
                _nextAliasSweepUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(0.5f, _aliasSweepSeconds));
            }

            if (_autoRosterRefresh && DateTime.UtcNow >= _nextRosterRefreshUtc)
            {
                RefreshPlayers(false);
            }

            if (_jailedPlayers.Count > 0 && DateTime.UtcNow >= _nextJailTickUtc)
            {
                TickJailEnforcement();
            }

            if (_disallowCheats && _enforceServerCheatsOff && DateTime.UtcNow >= _nextPolicyPushUtc)
            {
                TickHostPolicyEnforcement();
            }

            if (_disallowCheats && _movementGuard && DateTime.UtcNow >= _nextMovementSampleUtc)
            {
                TickMovementGuard();
            }

            if (_movementTelemetryLog && DateTime.UtcNow >= _nextMovementTelemetryUtc)
            {
                TickMovementTelemetry();
            }

            if (_codexBridgeEnabled && _externalAiReviewEnabled && DateTime.UtcNow >= _nextCodexBridgePollUtc)
            {
                TickCodexBridge();
            }

            if (_learnedSelfAdminPolicyEnabled && DateTime.UtcNow >= _nextLearnedPolicyRefreshUtc)
            {
                TickLearnedSelfAdminPolicy(false);
            }

            TickAdminControlMode();

            if (_codexBridgeEnabled && _externalAiReviewEnabled && DateTime.UtcNow >= _nextCodexAutomationStateWriteUtc)
            {
                WriteCodexAutomationState(false);
            }

            if (_disallowCheats && _passivePlayerStateAudit && DateTime.UtcNow >= _nextPassivePlayerStateAuditUtc)
            {
                TickPassivePlayerStateAudit();
            }

            if (DateTime.UtcNow >= _nextVehicleSpawnAuditUtc)
            {
                TickVehicleSpawnGuard();
            }

            if (_possession != null)
            {
                _possession.Update();
            }
        }

        public void OnGUI()
        {
            if (!_menuOpen)
            {
                return;
            }

            DrawScaledWindow();
        }

        private void DrawWindowBackdrop(Rect rect)
        {
            FillRect(new Rect(rect.x, rect.y, WindowWidth, WindowHeight), new Color(0.95f, 0.98f, 1f, 1f));
            FillRect(new Rect(rect.x, rect.y, WindowWidth, 72f), new Color(0.015f, 0.16f, 0.32f, 1f));
            FillRect(new Rect(rect.x, rect.y + 66f, WindowWidth, 6f), new Color(0.03f, 0.35f, 0.68f, 1f));
            FillRect(new Rect(rect.x + 14f, rect.y + 86f, 164f, WindowHeight - 100f), new Color(0.98f, 0.995f, 1f, 1f));
            FillRect(new Rect(rect.x + 192f, rect.y + 86f, WindowWidth - 206f, 54f), Color.white);
        }

        private void DrawWindow(int id)
        {
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
            FillRect(new Rect(0f, 0f, WindowWidth, WindowHeight), new Color(0.012f, 0.014f, 0.013f, 1f));
            FillRect(new Rect(2f, 2f, WindowWidth - 4f, WindowHeight - 4f), new Color(0.044f, 0.047f, 0.043f, 1f));
            DrawSolidRect(new Rect(8f, 8f, WindowWidth - 16f, WindowHeight - 16f), _panelTex);
            DrawSolidRect(new Rect(14f, 14f, WindowWidth - 28f, 76f), _deepBlueTex);
            DrawSolidRect(new Rect(230f, 100f, WindowWidth - 248f, WindowHeight - 184f), _inputTex);
            FillRect(new Rect(14f, 90f, WindowWidth - 28f, 1f), new Color(0.20f, 0.24f, 0.20f, 1f));
            FillRect(new Rect(230f, 100f, WindowWidth - 248f, 1f), new Color(0.24f, 0.27f, 0.23f, 1f));
            FillRect(new Rect(230f, WindowHeight - 86f, WindowWidth - 248f, 1f), new Color(0.12f, 0.14f, 0.12f, 1f));
            FillRect(new Rect(14f, WindowHeight - 18f, WindowWidth - 28f, 1f), new Color(0.18f, 0.21f, 0.18f, 1f));
            DrawConsoleFrame();
            DrawReactiveConsoleGlow();

            GUILayout.BeginArea(new Rect(34f, 24f, 540f, 58f));
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("Modstoke Admin", _titleStyle, GUILayout.Height(30f));
            GUILayout.Label("by OpenACAI  |  Developer Alex Cooper  |  v" + Plugin.Version, _headerMetaStyle, GUILayout.Height(18f));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(WindowWidth - 528f, 31f, 468f, 38f));
            DrawContextBar();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(WindowWidth - 48f, 28f, 30f, 30f));
            if (GUILayout.Button("X", _quietButtonStyle, GUILayout.Width(30f), GUILayout.Height(30f)))
            {
                ToggleMenu();
            }
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(24f, 104f, 188f, WindowHeight - 190f), _sidebarStyle);
            GUILayout.Label("CONSOLE", _sideHeaderStyle, GUILayout.Height(24f));
            DrawNavButton("Overview", "Overview", _iconServer);
            DrawNavButton("Players", "Roster", _iconRoster);
            DrawNavButton("Admin", "Host", _iconHost);
            DrawNavButton("Evidence", "Evidence", _iconShield);
            DrawNavButton("Names", "Players", _iconPlayers);
            DrawNavButton("Utility", "World", _iconWorld);
            DrawNavButton("Mutes", "Comms", _iconComms);
            DrawNavButton("Bans", "Bans", _iconBans);
            DrawNavButton("NPC", "Companions", _iconNpc);
            DrawNavButton("Debug", "Debug / Cheating", _iconDebug);
            GUILayout.FlexibleSpace();
            GUILayout.Label("ADMIN MODE", _sideHeaderStyle, GUILayout.Height(20f));
            GUILayout.Label(_sessionStatus, _hintStyle, GUILayout.Height(20f));
            GUILayout.Space(6f);
            GUILayout.Label("Developer Alex Cooper", _hintStyle, GUILayout.Height(20f));
            GUILayout.Label("modstoke.com (planned)", _hintStyle, GUILayout.Height(20f));
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(236f, 104f, WindowWidth - 260f, WindowHeight - 190f));
            _contentScroll.x = 0f;
            _contentScroll = GUILayout.BeginScrollView(_contentScroll, false, false, GUILayout.Height(WindowHeight - 200f));
            DrawActiveTab();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            DrawFooterStatusStrip();

            GUI.DragWindow(new Rect(0f, 0f, WindowWidth - 60f, 88f));
        }

        private void DrawFooterStatusStrip()
        {
            Rect strip = new Rect(30f, WindowHeight - 48f, WindowWidth - 60f, 28f);
            DrawSolidRect(strip, _inputTex);
            FillRect(new Rect(strip.x, strip.y, strip.width, 1f), new Color(0.25f, 0.28f, 0.24f, 1f));
            FillRect(new Rect(strip.x, strip.yMax - 1f, strip.width, 1f), new Color(0.07f, 0.08f, 0.07f, 1f));

            string left = "STATUS  " + Shorten(String.IsNullOrEmpty(_lastAction) ? "Ready" : _lastAction, 72);
            if (!String.IsNullOrEmpty(_settingsProblem)) left = "SETTINGS  Needs attention (World tab)";
            string center =
                "PLAYERS: " + _players.Count.ToString(CultureInfo.InvariantCulture) +
                "   |   BANNED: " + _bannedPlayers.Count.ToString(CultureInfo.InvariantCulture) +
                "   |   MUTED: " + _mutedPlayers.Count.ToString(CultureInfo.InvariantCulture) +
                "   |   JAILED: " + _jailedPlayers.Count.ToString(CultureInfo.InvariantCulture);
            GUI.Label(new Rect(strip.x + 14f, strip.y + 5f, 410f, 20f), new GUIContent(left, null, _settingsProblem), _monoStyle);
            GUI.Label(new Rect(strip.x + 430f, strip.y + 5f, 420f, 20f), center, _monoStyle);
            GUI.Label(new Rect(strip.x + strip.width - 250f, strip.y + 5f, 232f, 20f), "Developer Alex Cooper", _hintStyle);
        }

        private void DrawReactiveConsoleGlow()
        {
            float pulse = 0.5f + (0.5f * (float)Math.Sin(Time.realtimeSinceStartup * 1.65f));
            float sweep = (Time.realtimeSinceStartup * 28f) % (WindowWidth - 290f);
            Color coolGlow = new Color(0.18f, 0.40f, 0.46f, 0.045f + (pulse * 0.035f));
            Color oliveGlow = new Color(0.48f, 0.62f, 0.28f, 0.055f + (pulse * 0.030f));

            FillRect(new Rect(34f, 86f, 190f, 1f), oliveGlow);
            FillRect(new Rect(WindowWidth - 570f, 70f, 500f, 1f), coolGlow);
            FillRect(new Rect(244f + sweep, 102f, 92f, 1f), new Color(0.42f, 0.66f, 0.72f, 0.090f));
            FillRect(new Rect(232f, 104f, 2f, WindowHeight - 206f), new Color(0.24f, 0.34f, 0.32f, 0.16f));
            FillRect(new Rect(WindowWidth - 22f, 104f, 2f, WindowHeight - 206f), new Color(0.24f, 0.34f, 0.32f, 0.12f));
        }

        private void DrawConsoleFrame()
        {
            Color outer = new Color(0.22f, 0.24f, 0.22f, 1f);
            Color mid = new Color(0.105f, 0.115f, 0.105f, 1f);
            Color inner = new Color(0.012f, 0.014f, 0.013f, 1f);
            Color highlight = new Color(0.36f, 0.38f, 0.34f, 1f);

            FillRect(new Rect(8f, 8f, WindowWidth - 16f, 3f), outer);
            FillRect(new Rect(8f, WindowHeight - 11f, WindowWidth - 16f, 3f), outer);
            FillRect(new Rect(8f, 8f, 3f, WindowHeight - 16f), outer);
            FillRect(new Rect(WindowWidth - 11f, 8f, 3f, WindowHeight - 16f), outer);

            FillRect(new Rect(14f, 14f, WindowWidth - 28f, 2f), highlight);
            FillRect(new Rect(14f, WindowHeight - 16f, WindowWidth - 28f, 2f), mid);
            FillRect(new Rect(14f, 14f, 2f, WindowHeight - 28f), highlight);
            FillRect(new Rect(WindowWidth - 16f, 14f, 2f, WindowHeight - 28f), mid);

            DrawCornerPlate(8f, 8f, false, false, outer, mid, inner);
            DrawCornerPlate(WindowWidth - 56f, 8f, true, false, outer, mid, inner);
            DrawCornerPlate(8f, WindowHeight - 56f, false, true, outer, mid, inner);
            DrawCornerPlate(WindowWidth - 56f, WindowHeight - 56f, true, true, outer, mid, inner);

            FillRect(new Rect(74f, 6f, 86f, 8f), mid);
            FillRect(new Rect(82f, 4f, 70f, 3f), outer);
            FillRect(new Rect(WindowWidth - 160f, 6f, 86f, 8f), mid);
            FillRect(new Rect(WindowWidth - 152f, 4f, 70f, 3f), outer);
        }

        private void DrawCornerPlate(float x, float y, bool right, bool bottom, Color outer, Color mid, Color inner)
        {
            FillRect(new Rect(x, y, 48f, 8f), outer);
            FillRect(new Rect(x, y, 8f, 48f), outer);
            FillRect(new Rect(x + 8f, y + 8f, 34f, 4f), mid);
            FillRect(new Rect(x + 8f, y + 8f, 4f, 34f), mid);
            FillRect(new Rect(x + (right ? 33f : 10f), y + (bottom ? 33f : 10f), 6f, 6f), inner);
            FillRect(new Rect(x + (right ? 34f : 11f), y + (bottom ? 34f : 11f), 4f, 4f), mid);
        }

        private void DrawActiveTab()
        {
            if (_activeTab == "Overview")
            {
                DrawOverviewTab();
            }
            else if (_activeTab == "Evidence")
            {
                DrawEvidenceTab();
            }
            else if (_activeTab == "Admin")
            {
                DrawAdminTab();
            }
            else if (_activeTab == "Bans")
            {
                DrawBansTab();
            }
            else if (_activeTab == "Mutes")
            {
                DrawMutesTab();
            }
            else if (_activeTab == "Names")
            {
                DrawNamesTab();
            }
            else if (_activeTab == "NPC")
            {
                DrawNpcTab();
            }
            else if (_activeTab == "Utility")
            {
                DrawUtilityTab();
            }
            else if (_activeTab == "Debug")
            {
                DrawDebugCheatingTab();
            }
            else
            {
                DrawPlayersTab();
            }
        }

        private void DrawContextBar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(34f));
            DrawPill("ONLINE " + CountPlayers(true).ToString(CultureInfo.InvariantCulture), _goodPillStyle, 98f);
            DrawPill("ROSTER " + _players.Count.ToString(CultureInfo.InvariantCulture), _pillStyle, 98f);
            int unresolved = CountUnresolvedPlayers();
            if (unresolved > 0) DrawPill("UNKNOWN " + unresolved.ToString(CultureInfo.InvariantCulture), _warnPillStyle, 110f);
            DrawPill(GetGuardBadge(), _disallowCheats ? _warnPillStyle : _pillStyle, 106f);
            DrawPill((_autoRosterRefresh ? "AUTO " : "MANUAL ") + _rosterRefreshSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s", _autoRosterRefresh ? _warnPillStyle : _pillStyle, 118f);
            GUILayout.EndHorizontal();
        }

        private static void DrawPill(string text, GUIStyle style, float width)
        {
            GUILayout.Label(text, style, GUILayout.Width(width), GUILayout.Height(28f));
        }

        private void DrawNavButton(string tab)
        {
            DrawNavButton(tab, tab);
        }

        private void DrawNavButton(string tab, string label)
        {
            DrawNavButton(tab, label, null);
        }

        private void DrawNavButton(string tab, string label, Texture2D icon)
        {
            bool active = String.Equals(_activeTab, tab, StringComparison.OrdinalIgnoreCase);
            GUIContent content = icon == null ? new GUIContent(label) : new GUIContent(label, icon, "");
            if (GUILayout.Button(content, active ? _navActiveStyle : _navStyle, GUILayout.Height(44f)))
            {
                _activeTab = tab;
                _contentScroll = Vector2.zero;
            }
        }

        private void DrawPlayersTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(920f));
            DrawRosterWorkspacePanel();
            DrawQuickActionsPanel();
            DrawEventLogPanel();
            GUILayout.EndVertical();

            GUILayout.Space(10f);
            GUILayout.BeginVertical(GUILayout.Width(388f));
            DrawSelectedPlayerDetailsPanel();
            DrawManualTargetPanel();
            DrawSessionSummaryPanel();
            DrawOperationalSourcePanel();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawQuickActionsPanel()
        {
            BeginPanel("Quick Actions", GUILayout.Height(160f));
            GUILayout.BeginHorizontal();
            DrawIconActionButton(_iconSummon, "Summon", _primaryButtonStyle, SummonSelectedPlayer, _hostActionsAvailable);
            DrawIconActionButton(_iconGoTo, "Go To", _buttonStyle, TeleportToSelectedPlayer);
            DrawIconActionButton(_iconWarning, "Jail", _dangerButtonStyle, JailSelectedPlayer, _hostActionsAvailable);
            DrawIconActionButton(_iconKick, "Kick", _dangerButtonStyle, KickSelectedPlayer, _serverCommandsAvailable);
            DrawIconActionButton(_iconMute, IsPlayerMuted(GetSelectedPlayer()) ? "Unmute" : "Mute", _buttonStyle, ToggleSelectedMute);
            DrawIconActionButton(_iconBan, "Ban", _dangerButtonStyle, BanSelectedPlayer, _serverCommandsAvailable);
            DrawIconActionButton(_iconCopy, "Copy ID", _quietButtonStyle, CopySelectedSteamId);
            GUILayout.EndHorizontal();
            GUILayout.Label("Selected: " + Shorten(FormatSelectedPlayer(GetSelectedPlayer()), 92), _hintStyle, GUILayout.Height(20f));
            DrawModerationAvailability();
            EndPanel();
        }

        private void DrawIconActionButton(Texture2D icon, string label, GUIStyle style, Action action, bool available = true)
        {
            GUIContent content = icon == null ? new GUIContent(label) : new GUIContent(label, icon, "");
            if (DrawModerationButton(content, style, available, GUILayout.Width(104f), GUILayout.Height(64f)) && action != null)
            {
                action();
            }
        }

        private void DrawEventLogPanel()
        {
            BeginPanel("Event Log", GUILayout.Height(286f));
            _eventLogScroll = GUILayout.BeginScrollView(_eventLogScroll, false, true, GUILayout.Height(232f));
            if (_eventLog.Count == 0)
            {
                GUILayout.Label("[--:--:--] SYSTEM  Ready", _monoStyle, GUILayout.Height(22f));
            }
            else
            {
                for (int i = 0; i < _eventLog.Count; i++)
                {
                    GUILayout.Label(Shorten(_eventLog[i], 148), _monoStyle, GUILayout.Height(20f));
                }
            }
            GUILayout.EndScrollView();
            EndPanel();
        }

        private void DrawManualTargetPanel()
        {
            BeginPanel("Manual Target", GUILayout.Height(142f));
            GUILayout.Label("Steam64 / Name", _hintStyle, GUILayout.Height(18f));
            _manualSteamIdInput = GUILayout.TextField(_manualSteamIdInput ?? "", _inputStyle, GUILayout.Height(30f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _hintStyle, GUILayout.Width(42f), GUILayout.Height(30f));
            _manualNameInput = GUILayout.TextField(_manualNameInput ?? "", _inputStyle, GUILayout.Height(30f));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("TARGET", _primaryButtonStyle, GUILayout.Height(34f)))
            {
                SelectManualTarget();
            }
            EndPanel();
        }

        private void DrawSessionSummaryPanel()
        {
            BeginPanel("Session Players", GUILayout.Height(120f));
            GUILayout.Label(_players.Count == 0 ? "No live players detected" : _players.Count.ToString(CultureInfo.InvariantCulture) + " known player(s)", _contextValueStyle, GUILayout.Height(24f));
            GUILayout.Label("Source: " + Shorten(_lastRosterDiagnostics, 48), _hintStyle, GUILayout.Height(32f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Roster", _primaryButtonStyle, GUILayout.Width(140f), GUILayout.Height(34f)))
            {
                RefreshPlayers();
            }
            if (GUILayout.Button(_autoRosterRefresh ? "Auto On" : "Auto Off", _autoRosterRefresh ? _primaryButtonStyle : _buttonStyle, GUILayout.Width(94f), GUILayout.Height(34f)))
            {
                _autoRosterRefresh = !_autoRosterRefresh;
                SaveSettingsFromConsole("Auto roster refresh " + (_autoRosterRefresh ? "enabled" : "disabled"));
            }
            GUILayout.EndHorizontal();
            EndPanel();
        }

        private void DrawOperationalSourcePanel()
        {
            BeginPanel("Operational Source", GUILayout.Height(88f));
            GUILayout.Label(Shorten(_lastRosterDiagnostics, 52), _hintStyle, GUILayout.Height(40f));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Details", _quietButtonStyle, GUILayout.Width(86f), GUILayout.Height(30f)))
            {
                ActionOk("Roster source: " + _lastRosterDiagnostics);
            }
            GUILayout.EndHorizontal();
            EndPanel();
        }

        private void DrawResponsibilityPanel()
        {
            BeginPanel("Command Audit", GUILayout.Height(82f));
            GUILayout.Label("All actions are logged.", _hintStyle, GUILayout.Height(22f));
            GUILayout.Label("Host operations only.", _hintStyle, GUILayout.Height(22f));
            EndPanel();
        }

        private void DrawPlayerRow(PlayerEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            bool selected = IsSelectedPlayer(entry);
            bool muted = IsPlayerMuted(entry);
            bool banned = IsPlayerBanned(entry);
            GUILayout.BeginVertical(selected ? _rowActiveStyle : _rowStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Shorten(DisplayPlayerName(entry), 38), _labelStyle, GUILayout.Width(240f));
            GUILayout.Label(Shorten(String.IsNullOrEmpty(entry.SteamId) ? "unknown" : entry.SteamId, 24), _monoStyle, GUILayout.Width(190f));
            string status = BuildPlayerStatus(entry, banned, muted);
            string statusDetail = status + "\n" + GetNativeBanObservationText(entry);
            if (!String.IsNullOrEmpty(entry.PresenceDetail)) statusDetail += "\n" + entry.PresenceDetail;
            GUILayout.Label(new GUIContent(Shorten(status, 22), null, statusDetail), _hintStyle, GUILayout.Width(112f));
            GUILayout.Label(Shorten(entry.Source ?? "unknown", 28), _hintStyle, GUILayout.Width(170f));
            if (GUILayout.Button(selected ? "Target" : "Select", selected ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(88f), GUILayout.Height(28f)))
            {
                SelectPlayer(entry, true);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Shorten(BuildNetworkStatus(entry) + BuildLastSeenStatus(entry) + BuildLastActivityStatus(entry), 142), _hintStyle, GUILayout.Width(724f));
            if (GUILayout.Button("Copy ID", _quietButtonStyle, GUILayout.Width(96f), GUILayout.Height(24f))) CopySteamId(entry);
            GUILayout.EndHorizontal();
            string observedNames = BuildObservedNamesText(entry);
            if (!String.IsNullOrEmpty(observedNames))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Shorten(observedNames, 142), _hintStyle, GUILayout.Width(838f));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawBansTab()
        {
            DrawModerationAvailability();
            BeginPanel("Ban Records");
            GUILayout.Label("Saved requests and imported bans. Use Check Ban to query the current game state.", _hintStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync From Game", _primaryButtonStyle, GUILayout.Width(134f), GUILayout.Height(34f))) RefreshBansFromGame();
            if (GUILayout.Button("Save", _quietButtonStyle, GUILayout.Width(80f), GUILayout.Height(34f))) SaveSettingsFromConsole();
            DrawPill("Known " + _bannedPlayers.Count.ToString(CultureInfo.InvariantCulture), _dangerPillStyle, 84f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Steam64", _labelStyle, GUILayout.Width(66f));
            _manualSteamIdInput = GUILayout.TextField(_manualSteamIdInput ?? "", _inputStyle, GUILayout.Width(190f), GUILayout.Height(32f));
            GUILayout.Label("Name", _labelStyle, GUILayout.Width(42f));
            _manualNameInput = GUILayout.TextField(_manualNameInput ?? "", _inputStyle, GUILayout.Width(150f), GUILayout.Height(32f));
            if (DrawModerationButton("Ban ID", _dangerButtonStyle, _serverCommandsAvailable, GUILayout.Width(82f), GUILayout.Height(32f))) BanManualSteamId();
            if (DrawModerationButton("Unban ID", _buttonStyle, _hostActionsAvailable, GUILayout.Width(92f), GUILayout.Height(32f))) UnbanManualSteamId();
            GUILayout.EndHorizontal();
            GUILayout.Label("Reason: " + Shorten(_adminReasonInput, 90), _hintStyle);

            List<ModerationEntry> bans = SortedModerationEntries(_bannedPlayers);
            if (bans.Count == 0)
            {
                DrawEmptyListState("No known banned players yet.", "Ban registry is ready.");
            }
            else
            {
                _banScroll.x = 0f;
                _banScroll = GUILayout.BeginScrollView(_banScroll, false, false, GUILayout.Height(330f));
                for (int i = 0; i < bans.Count; i++)
                {
                    DrawModerationRow(bans[i], true);
                }
                GUILayout.EndScrollView();
            }
            EndPanel();
        }

        private void DrawMutesTab()
        {
            BeginPanel("Muted Players");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mute notice", _labelStyle, GUILayout.Width(86f));
            _muteMessageInput = GUILayout.TextField(_muteMessageInput ?? "", _inputStyle, GUILayout.Height(32f));
            if (GUILayout.Button("Save", _quietButtonStyle, GUILayout.Width(80f), GUILayout.Height(32f))) SaveSettingsFromConsole();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawPill("Chat patches " + _chatPatchCount.ToString(CultureInfo.InvariantCulture), _pillStyle, 112f);
            DrawPill("Muted " + _mutedPlayers.Count.ToString(CultureInfo.InvariantCulture), _warnPillStyle, 82f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Steam64", _labelStyle, GUILayout.Width(66f));
            _manualSteamIdInput = GUILayout.TextField(_manualSteamIdInput ?? "", _inputStyle, GUILayout.Width(190f), GUILayout.Height(32f));
            GUILayout.Label("Name", _labelStyle, GUILayout.Width(42f));
            _manualNameInput = GUILayout.TextField(_manualNameInput ?? "", _inputStyle, GUILayout.Width(150f), GUILayout.Height(32f));
            if (GUILayout.Button("Mute ID", _buttonStyle, GUILayout.Width(82f), GUILayout.Height(32f))) MuteManualSteamId();
            if (GUILayout.Button("Unmute ID", _buttonStyle, GUILayout.Width(92f), GUILayout.Height(32f))) UnmuteManualSteamId();
            GUILayout.EndHorizontal();

            List<ModerationEntry> mutes = SortedModerationEntries(_mutedPlayers);
            if (mutes.Count == 0)
            {
                DrawEmptyListState("No muted players yet.", "Comms controls are ready.");
            }
            else
            {
                _muteScroll.x = 0f;
                _muteScroll = GUILayout.BeginScrollView(_muteScroll, false, false, GUILayout.Height(330f));
                for (int i = 0; i < mutes.Count; i++)
                {
                    DrawModerationRow(mutes[i], false);
                }
                GUILayout.EndScrollView();
            }
            EndPanel();
        }

        private void DrawModerationRow(ModerationEntry entry, bool bannedList)
        {
            if (entry == null)
            {
                return;
            }

            GUILayout.BeginVertical(_rowStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Shorten(String.IsNullOrEmpty(entry.Name) ? "(unknown)" : entry.Name, 24), _labelStyle, GUILayout.Width(160f));
            GUILayout.Label(entry.SteamId ?? "", _monoStyle, GUILayout.Width(178f));
            GUILayout.Label(Shorten(entry.Reason ?? "", 34), _hintStyle, GUILayout.Width(190f));
            if (GUILayout.Button("Copy", _buttonStyle, GUILayout.Width(58f), GUILayout.Height(28f)))
            {
                GUIUtility.systemCopyBuffer = entry.SteamId ?? "";
                ActionOk("Copied SteamID " + entry.SteamId);
            }
            if (bannedList)
            {
                if (DrawModerationButton("Unban", _primaryButtonStyle, _hostActionsAvailable, GUILayout.Width(72f), GUILayout.Height(28f))) UnbanSteamIdFromList(entry.SteamId);
                if (GUILayout.Button("Forget", _quietButtonStyle, GUILayout.Width(72f), GUILayout.Height(28f))) ForgetModerationEntry(_bannedPlayers, entry.SteamId, "ban");
            }
            else
            {
                if (GUILayout.Button("Unmute", _primaryButtonStyle, GUILayout.Width(78f), GUILayout.Height(28f))) SetSteamIdMuted(entry.SteamId, null, false, out _);
                if (GUILayout.Button("Forget", _quietButtonStyle, GUILayout.Width(72f), GUILayout.Height(28f))) ForgetModerationEntry(_mutedPlayers, entry.SteamId, "mute");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Shorten("Added: " + (entry.AddedUtc ?? "") + " | Source: " + (entry.Source ?? "manual"), 100), _hintStyle);
            GUILayout.EndVertical();
        }

        private void DrawEmptyListState(string title, string detail)
        {
            GUILayout.BeginVertical(_emptyStateStyle, GUILayout.Height(104f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(title, _contextValueStyle, GUILayout.Height(24f));
            GUILayout.Label(detail, _hintStyle, GUILayout.Height(22f));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private void DrawAdminTab()
        {
            BeginPanel("Host Controls");
            GUILayout.Label(GetGuardStatusText(), _hintStyle);
            DrawModerationAvailability();
            GUILayout.Label("Target: " + FormatSelectedPlayer(GetSelectedPlayer()), _contextValueStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Reason", _labelStyle, GUILayout.Width(70f));
            _adminReasonInput = GUILayout.TextField(_adminReasonInput ?? "", _inputStyle, GUILayout.Height(32f));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label("Player Actions", _sectionTitleStyle);
            GUILayout.BeginHorizontal();
            if (DrawModerationButton("Summon Player", _primaryButtonStyle, _hostActionsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) SummonSelectedPlayer();
            if (GUILayout.Button("Go To Player", _buttonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) TeleportToSelectedPlayer();
            if (DrawModerationButton("Kick Player", _dangerButtonStyle, _serverCommandsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) KickSelectedPlayer();
            if (DrawModerationButton("Ban Player", _dangerButtonStyle, _serverCommandsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) BanSelectedPlayer();
            if (DrawModerationButton("Unban Player", _buttonStyle, _hostActionsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) UnbanSelectedPlayer();
            if (GUILayout.Button(IsPlayerMuted(GetSelectedPlayer()) ? "Unmute Player" : "Mute Player", _buttonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) ToggleSelectedMute();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label("Access Level", _sectionTitleStyle);
            GUILayout.BeginHorizontal();
            if (DrawModerationButton("Make Admin", _buttonStyle, _serverCommandsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) MakeSelectedPlayerAdmin();
            if (DrawModerationButton("Make Trusted", _buttonStyle, _serverCommandsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) MakeSelectedPlayerTrusted();
            if (DrawModerationButton("Make Guest", _buttonStyle, _serverCommandsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) MakeSelectedPlayerGuest();
            if (GUILayout.Button("Check Ban", _buttonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) CheckSelectedPlayerBan();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label("Registry", _sectionTitleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy SteamID", _quietButtonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) CopySelectedSteamId();
            if (GUILayout.Button("Sync Bans", _primaryButtonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) RefreshBansFromGame();
            if (GUILayout.Button("Audit Inventory", _buttonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) RequestSelectedInventoryAudit();
            if (GUILayout.Button("Client Audit: " + (_clientInventoryAudit ? "On" : "Off"), _clientInventoryAudit ? _buttonStyle : _quietButtonStyle, GUILayout.Width(136f), GUILayout.Height(34f)))
            {
                _clientInventoryAudit = !_clientInventoryAudit;
                SaveSettingsFromConsole("Client inventory audit " + (_clientInventoryAudit ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("AI Review: " + (_externalAiReviewEnabled ? "On" : "Off"), _externalAiReviewEnabled ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(120f), GUILayout.Height(34f)))
            {
                _externalAiReviewEnabled = !_externalAiReviewEnabled;
                if (_externalAiReviewEnabled)
                {
                    _nextCodexBridgePollUtc = DateTime.MinValue;
                    _nextCodexAutomationStateWriteUtc = DateTime.MinValue;
                }
                SaveSettingsFromConsole("External AI learning/telemetry review " + (_externalAiReviewEnabled ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Learned Policy: " + (_learnedSelfAdminPolicyEnabled ? "On" : "Off"), _learnedSelfAdminPolicyEnabled ? _buttonStyle : _quietButtonStyle, GUILayout.Width(154f), GUILayout.Height(34f)))
            {
                _learnedSelfAdminPolicyEnabled = !_learnedSelfAdminPolicyEnabled;
                if (_learnedSelfAdminPolicyEnabled)
                {
                    TickLearnedSelfAdminPolicy(true);
                }
                SaveSettingsFromConsole("Local learned self-admin policy " + (_learnedSelfAdminPolicyEnabled ? "enabled" : "disabled"));
            }
            GUILayout.Label("Comms: chat " + _chatPatchCount.ToString(CultureInfo.InvariantCulture)
                + " | audit " + _inventoryAuditPatchCount.ToString(CultureInfo.InvariantCulture), _hintStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label("AI review: " + (_externalAiReviewEnabled ? "external Codex bridge active" : "off / local-only")
                + " | Learned self-admin: " + (_learnedSelfAdminPolicyEnabled ? _learnedSelfAdminPolicy.Summary : "disabled"), _hintStyle);
            GUILayout.Space(8f);
            GUILayout.Label("Integrity Policy", _sectionTitleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Disallow Cheats: " + (_disallowCheats ? "On" : "Off"), _disallowCheats ? _primaryButtonStyle : _buttonStyle, GUILayout.Width(164f), GUILayout.Height(34f)))
            {
                _disallowCheats = !_disallowCheats;
                if (_disallowCheats && _enforceServerCheatsOff)
                {
                    PushServerCheatsOff(false);
                }
                SaveSettingsFromConsole("Disallow cheats " + (_disallowCheats ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Auto Jail: " + (_autoJailCheaters ? "On" : "Off"), _autoJailCheaters ? _buttonStyle : _quietButtonStyle, GUILayout.Width(128f), GUILayout.Height(34f)))
            {
                _autoJailCheaters = !_autoJailCheaters;
                SaveSettingsFromConsole("Auto jail " + (_autoJailCheaters ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Item Guard: " + (_strictItemEventGuard ? "On" : "Off"), _strictItemEventGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(138f), GUILayout.Height(34f)))
            {
                _strictItemEventGuard = !_strictItemEventGuard;
                SaveSettingsFromConsole("Item guard " + (_strictItemEventGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Early Items: " + (_earlyItemGuard ? "On" : "Off"), _earlyItemGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(136f), GUILayout.Height(34f)))
            {
                _earlyItemGuard = !_earlyItemGuard;
                SaveSettingsFromConsole("Early item guard " + (_earlyItemGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Burst: " + (_itemBurstGuard ? "On" : "Off"), _itemBurstGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(104f), GUILayout.Height(34f)))
            {
                _itemBurstGuard = !_itemBurstGuard;
                SaveSettingsFromConsole("Item burst guard " + (_itemBurstGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("High Gear: " + (_highValueItemGuard ? "On" : "Off"), _highValueItemGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(128f), GUILayout.Height(34f)))
            {
                _highValueItemGuard = !_highValueItemGuard;
                SaveSettingsFromConsole("High-value gear guard " + (_highValueItemGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Artifacts: " + (_artifactItemGuard ? "On" : "Off"), _artifactItemGuard ? _dangerButtonStyle : _quietButtonStyle, GUILayout.Width(120f), GUILayout.Height(34f)))
            {
                _artifactItemGuard = !_artifactItemGuard;
                SaveSettingsFromConsole("Artifact guard " + (_artifactItemGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Story Locks: " + (_storyItemLocationGuard ? "On" : "Off"), _storyItemLocationGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(138f), GUILayout.Height(34f)))
            {
                _storyItemLocationGuard = !_storyItemLocationGuard;
                SaveSettingsFromConsole("Story item location guard " + (_storyItemLocationGuard ? "enabled" : "disabled"));
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cheats Off: " + (_enforceServerCheatsOff ? "On" : "Off"), _enforceServerCheatsOff ? _buttonStyle : _quietButtonStyle, GUILayout.Width(138f), GUILayout.Height(34f)))
            {
                _enforceServerCheatsOff = !_enforceServerCheatsOff;
                if (_disallowCheats && _enforceServerCheatsOff)
                {
                    PushServerCheatsOff(false);
                }
                SaveSettingsFromConsole("Server cheats-off enforcement " + (_enforceServerCheatsOff ? "enabled" : "disabled"));
            }
            if (DrawModerationButton("Push Cheats Off", _buttonStyle, _hostActionsAvailable, GUILayout.Width(130f), GUILayout.Height(34f))) PushServerCheatsOff(true);
            if (GUILayout.Button("Repeat Ban: " + (_autoBanRepeatCheaters ? "On" : "Off"), _autoBanRepeatCheaters ? _dangerButtonStyle : _quietButtonStyle, GUILayout.Width(130f), GUILayout.Height(34f)))
            {
                _autoBanRepeatCheaters = !_autoBanRepeatCheaters;
                SaveSettingsFromConsole("Repeat offender auto-ban " + (_autoBanRepeatCheaters ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Jail Lock: " + (_jailedWorldEventGuard ? "On" : "Off"), _jailedWorldEventGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(118f), GUILayout.Height(34f)))
            {
                _jailedWorldEventGuard = !_jailedWorldEventGuard;
                SaveSettingsFromConsole("Jail lock " + (_jailedWorldEventGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Alerts: " + (_serverGuardAnnouncements ? "On" : "Off"), _serverGuardAnnouncements ? _buttonStyle : _quietButtonStyle, GUILayout.Width(104f), GUILayout.Height(34f)))
            {
                _serverGuardAnnouncements = !_serverGuardAnnouncements;
                SaveSettingsFromConsole("Guard server alerts " + (_serverGuardAnnouncements ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(34f), GUILayout.Height(34f)))
            {
                _autoBanTriggerCount = Math.Max(2, _autoBanTriggerCount - 1);
                SaveSettingsFromConsole("Repeat ban threshold " + _autoBanTriggerCount.ToString(CultureInfo.InvariantCulture));
            }
            GUILayout.Label("Ban at " + _autoBanTriggerCount.ToString(CultureInfo.InvariantCulture), _hintStyle, GUILayout.Width(58f));
            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(34f), GUILayout.Height(34f)))
            {
                _autoBanTriggerCount = (int)Math.Min(20L, (long)_autoBanTriggerCount + 1);
                SaveSettingsFromConsole("Repeat ban threshold " + _autoBanTriggerCount.ToString(CultureInfo.InvariantCulture));
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Move Guard: " + (_movementGuard ? "On" : "Off"), _movementGuard ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(138f), GUILayout.Height(34f)))
            {
                _movementGuard = !_movementGuard;
                SaveSettingsFromConsole("Movement guard " + (_movementGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Speed: " + (_speedHackGuard ? "On" : "Off"), _speedHackGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(104f), GUILayout.Height(34f)))
            {
                _speedHackGuard = !_speedHackGuard;
                SaveSettingsFromConsole("Speed guard " + (_speedHackGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Teleport: " + (_teleportGuard ? "On" : "Off"), _teleportGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(122f), GUILayout.Height(34f)))
            {
                _teleportGuard = !_teleportGuard;
                SaveSettingsFromConsole("Teleport guard " + (_teleportGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Noclip: " + (_noclipGuard ? "On" : "Off"), _noclipGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(112f), GUILayout.Height(34f)))
            {
                _noclipGuard = !_noclipGuard;
                SaveSettingsFromConsole("Obvious noclip guard " + (_noclipGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("Vehicles: " + (_vehicleSpawnGuard ? "On" : "Off"), _vehicleSpawnGuard ? _buttonStyle : _quietButtonStyle, GUILayout.Width(120f), GUILayout.Height(34f)))
            {
                _vehicleSpawnGuard = !_vehicleSpawnGuard;
                SaveSettingsFromConsole("Vehicle spawn guard " + (_vehicleSpawnGuard ? "enabled" : "disabled"));
            }
            if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(34f), GUILayout.Height(34f)))
            {
                _maxHorizontalSpeed = Math.Max(15f, _maxHorizontalSpeed - 5f);
                SaveSettingsFromConsole("Max speed " + _maxHorizontalSpeed.ToString("0", CultureInfo.InvariantCulture) + " m/s");
            }
            GUILayout.Label("Max " + _maxHorizontalSpeed.ToString("0", CultureInfo.InvariantCulture) + " m/s", _hintStyle, GUILayout.Width(72f));
            if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(34f), GUILayout.Height(34f)))
            {
                _maxHorizontalSpeed = Math.Min(120f, _maxHorizontalSpeed + 5f);
                SaveSettingsFromConsole("Max speed " + _maxHorizontalSpeed.ToString("0", CultureInfo.InvariantCulture) + " m/s");
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(IsSelectedAntiCheatExempt() ? "Remove Exempt" : "Exempt Selected", _buttonStyle, GUILayout.Width(138f), GUILayout.Height(34f))) ToggleSelectedAntiCheatExempt();
            if (DrawModerationButton("Quarantine", _dangerButtonStyle, _hostActionsAvailable, GUILayout.Width(118f), GUILayout.Height(34f))) QuarantineSelectedPlayer();
            if (DrawModerationButton("Jail Selected", _dangerButtonStyle, _hostActionsAvailable, GUILayout.Width(118f), GUILayout.Height(34f))) JailSelectedPlayer();
            if (GUILayout.Button("Set Jail Here", _buttonStyle, GUILayout.Width(118f), GUILayout.Height(34f))) SetJailPointHere();
            if (DrawModerationButton("Release", _buttonStyle, _hostActionsAvailable, GUILayout.Width(88f), GUILayout.Height(34f))) ReleaseSelectedPlayer();
            if (DrawModerationButton("Release All", _quietButtonStyle, _hostActionsAvailable, GUILayout.Width(100f), GUILayout.Height(34f))) ReleaseAllJailedPlayers(true, out _);
            GUILayout.EndHorizontal();
            if (IsLegacyUnsafeJailPosition(_jailPosition))
            {
                GUILayout.Label("Legacy off-map jail position detected. Using a temporary safe host-side holding point until you set one.", _hintStyle);
            }
            GUILayout.Label("Jailed: " + _jailedPlayers.Count.ToString(CultureInfo.InvariantCulture)
                + " | Exempt: " + _antiCheatExemptPlayers.Count.ToString(CultureInfo.InvariantCulture)
                + " | Tick: " + _jailTickSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + "s | Patches: " + _antiCheatPatchCount.ToString(CultureInfo.InvariantCulture)
                + " | Jail: " + DescribeJailPosition(), _hintStyle);
            GUILayout.Label("Host policy: cheats-off " + (_enforceServerCheatsOff ? "enforced" : "manual")
                + " | repeat auto-ban " + (_autoBanRepeatCheaters ? "after " + _autoBanTriggerCount.ToString(CultureInfo.InvariantCulture) : "off"), _hintStyle);
            GUILayout.Label("Item policy: max " + _itemEventCountThreshold.ToString(CultureInfo.InvariantCulture)
                + " per event, burst " + _itemBurstCountThreshold.ToString(CultureInfo.InvariantCulture)
                + "/" + _itemBurstWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + "s, high gear " + _highValueGuardMinutes.ToString("0.#", CultureInfo.InvariantCulture)
                + "m, story locks " + (_storyItemLocationGuard ? _storyItemLocationRules.Count.ToString(CultureInfo.InvariantCulture) : "off"), _hintStyle);
            GUILayout.Label("Movement policy: sample " + _movementSampleSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + "s, speed " + _maxHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                + "m/s, teleport " + _teleportDistanceThreshold.ToString("0.#", CultureInfo.InvariantCulture)
                + "m, floor y " + _noclipFloorY.ToString("0.#", CultureInfo.InvariantCulture)
                + ", vehicle guard " + VehicleGuardStatus(), _hintStyle);
            if (GUILayout.Button("Inspect Player Evidence", _primaryButtonStyle, GUILayout.Height(32f)))
            {
                _activeTab = "Evidence";
                _contentScroll = Vector2.zero;
            }
            DrawSelectedInventorySignal();
            DrawSelectedMovementSignal();
            DrawJailedPlayersMiniList();
            DrawJailReturnList();
            GUILayout.Space(8f);
            GUILayout.Label("Event Log: " + Shorten(_lastAction, 110), _hintStyle);
            EndPanel();
        }

        private void DrawSelectedInventorySignal()
        {
            PlayerEntry selected = GetSelectedPlayer();
            string steamId = selected == null ? "" : CleanSteamId(selected.SteamId);
            if (!LooksLikeSteamId(steamId))
            {
                GUILayout.Label("Inventory audit: select a live SteamID to inspect server-side item signals.", _hintStyle);
                return;
            }

            InventorySignalState state;
            if (!_inventorySignals.TryGetValue(steamId, out state) || state == null)
            {
                GUILayout.Label("Inventory audit: no server-side item signals observed for selected player yet.", _hintStyle);
                return;
            }

            if (state.SignalCount <= 0 && String.IsNullOrEmpty(state.LastAuditStatus))
            {
                GUILayout.Label("Inventory audit: no server-side item signals observed for selected player yet.", _hintStyle);
                return;
            }

            GUILayout.Label("Inventory audit: " + FormatAuditStatus(state)
                + " | signals " + state.SignalCount.ToString(CultureInfo.InvariantCulture)
                + " | blocked " + state.BlockedCount.ToString(CultureInfo.InvariantCulture)
                + " | high " + state.HighValueSignals.ToString(CultureInfo.InvariantCulture)
                + " | artifacts " + state.ArtifactSignals.ToString(CultureInfo.InvariantCulture)
                + " | last " + Shorten(state.LastEventName, 28)
                + " item " + FormatInventorySignalItemLabel(state)
                + " quantity " + FormatEventItemCount(state.LastCount), _hintStyle);
            if (!String.IsNullOrEmpty(state.LastDescriptor))
            {
                GUILayout.Label("Last detail: " + Shorten(state.LastDescriptor, 140), _hintStyle);
            }
            GUILayout.Label("Observed/audited items: " + FormatInventoryDeltas(state, 10), _hintStyle);
        }

        private static string FormatAuditStatus(InventorySignalState state)
        {
            if (state == null || String.IsNullOrEmpty(state.LastAuditStatus))
            {
                return "server signals";
            }
            string age = state.LastAuditResponseUtc == DateTime.MinValue
                ? ""
                : "; last accepted report " + Math.Max(0.0, DateTime.UtcNow.Subtract(state.LastAuditResponseUtc).TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + "s ago";
            return "audit " + state.LastAuditStatus
                + age
                + " items " + state.LastAuditItemCount.ToString(CultureInfo.InvariantCulture)
                + " high " + state.LastAuditHighValueCount.ToString(CultureInfo.InvariantCulture)
                + " artifacts " + state.LastAuditArtifactCount.ToString(CultureInfo.InvariantCulture);
        }

        private string FormatInventoryDeltas(InventorySignalState state, int maxItems)
        {
            if (state == null || state.KnownItemDeltas.Count == 0)
            {
                return "none";
            }
            List<int> keys = new List<int>(state.KnownItemDeltas.Keys);
            keys.Sort();
            List<string> parts = new List<string>();
            for (int i = 0; i < keys.Count && parts.Count < maxItems; i++)
            {
                int value = state.KnownItemDeltas[keys[i]];
                parts.Add(FormatItemIdLabel(keys[i]) + ":" + value.ToString(CultureInfo.InvariantCulture));
            }
            if (keys.Count > maxItems)
            {
                parts.Add("+" + (keys.Count - maxItems).ToString(CultureInfo.InvariantCulture));
            }
            return String.Join(", ", parts.ToArray());
        }

        private string FormatItemIdLabel(int itemId)
        {
            if (itemId <= 0)
            {
                return "unknown";
            }
            string knownName = KnownItemNameForId(itemId);
            if (String.IsNullOrEmpty(knownName))
            {
                _itemIdNameCache.TryGetValue(itemId, out knownName);
            }
            if (String.IsNullOrEmpty(knownName))
            {
                knownName = ResolveItemNameById(itemId);
            }
            return itemId.ToString(CultureInfo.InvariantCulture) + (String.IsNullOrEmpty(knownName) ? "" : "/" + knownName);
        }

        private string FormatInventorySignalItemLabel(InventorySignalState state)
        {
            if (state == null)
            {
                return "unknown";
            }
            if (state.LastItemId > 0)
            {
                return FormatItemIdLabel(state.LastItemId);
            }
            string label = ExtractReadableItemLabelFromDescriptor(state.LastDescriptor);
            return String.IsNullOrEmpty(label) ? "unknown" : label;
        }

        private string ExtractReadableItemLabelFromDescriptor(string descriptor)
        {
            if (String.IsNullOrEmpty(descriptor))
            {
                return "";
            }

            string item = ExtractDescriptorValue(descriptor, "item=");
            if (!String.IsNullOrEmpty(item))
            {
                return item;
            }

            string prefabIdText = ExtractDescriptorValue(descriptor, "prefabId=");
            int slash = prefabIdText.IndexOf('/');
            if (slash >= 0 && slash < prefabIdText.Length - 1)
            {
                return ReadableItemNameFromPrefabName(prefabIdText.Substring(slash + 1));
            }

            int prefabId;
            if (TryParseLastPositiveInt(prefabIdText, out prefabId))
            {
                string alias = GetBoltPrefabName(prefabId);
                if (!String.IsNullOrEmpty(alias))
                {
                    return alias;
                }
                return "prefab " + prefabId.ToString(CultureInfo.InvariantCulture);
            }
            return "";
        }

        private static string ExtractDescriptorValue(string descriptor, string key)
        {
            if (String.IsNullOrEmpty(descriptor) || String.IsNullOrEmpty(key))
            {
                return "";
            }
            int start = descriptor.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "";
            }
            start += key.Length;
            int end = descriptor.IndexOf(',', start);
            if (end < 0)
            {
                end = descriptor.Length;
            }
            return descriptor.Substring(start, end - start).Trim();
        }

        private void DrawSelectedMovementSignal()
        {
            PlayerEntry selected = GetSelectedPlayer();
            string steamId = selected == null ? "" : CleanSteamId(selected.SteamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }

            MovementSignalState state;
            if (!_movementSignals.TryGetValue(steamId, out state) || state == null || !state.HasSample)
            {
                GUILayout.Label("Movement audit: no live movement sample for selected player yet.", _hintStyle);
                return;
            }

            string age = state.LastSampleUtc == DateTime.MinValue
                ? ""
                : Math.Max(0.0, DateTime.UtcNow.Subtract(state.LastSampleUtc).TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + "s ago";
            GUILayout.Label("Movement audit: h " + state.LastHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                + "m/s | v " + state.LastVerticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                + "m/s | jump " + state.LastDistance.ToString("0.#", CultureInfo.InvariantCulture)
                + "m | climb +" + state.FootClimbNetGain.ToString("0.#", CultureInfo.InvariantCulture)
                + "m | mode " + (String.IsNullOrEmpty(state.LastContext) ? "foot" : state.LastContext)
                + " | net " + state.LastNetworkedSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                + "m/s | strikes " + state.ConsecutiveViolations.ToString(CultureInfo.InvariantCulture)
                + " | total " + state.TotalViolations.ToString(CultureInfo.InvariantCulture)
                + " | " + Shorten(state.LastReason, 64)
                + (String.IsNullOrEmpty(age) ? "" : " | " + age), _hintStyle);
        }

        private void DrawJailedPlayersMiniList()
        {
            if (_jailedPlayers.Count == 0)
            {
                GUILayout.Label("No jailed players this session.", _hintStyle);
                return;
            }

            List<string> keys = new List<string>(_jailedPlayers.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            int count = Math.Min(keys.Count, 4);
            for (int i = 0; i < count; i++)
            {
                JailEntry jail = _jailedPlayers[keys[i]];
                GUILayout.BeginHorizontal(_rowStyle, GUILayout.Height(30f));
                GUILayout.Label(Shorten((jail.Name ?? "Unknown") + " | " + jail.SteamId, 48), _labelStyle, GUILayout.Width(320f));
                GUILayout.Label(Shorten(jail.Reason ?? "", 42), _hintStyle, GUILayout.Width(260f));
                GUILayout.Label(new GUIContent(jail.MovementStatus, null, jail.MovementDetail + "\n" + GetJailCareDetail(jail)), _hintStyle, GUILayout.Width(104f));
                GUILayout.Label("x" + jail.TriggerCount.ToString(CultureInfo.InvariantCulture), _hintStyle, GUILayout.Width(42f));
                if (GUILayout.Button("Select", _quietButtonStyle, GUILayout.Width(72f), GUILayout.Height(26f)))
                {
                    PlayerEntry entry = FindPlayerBySteamId(jail.SteamId);
                    if (entry != null)
                    {
                        SelectPlayer(entry, false);
                    }
                    else
                    {
                        _manualSteamIdInput = jail.SteamId;
                        SelectManualTarget();
                    }
                }
                if (DrawModerationButton("Release", _buttonStyle, _hostActionsAvailable, GUILayout.Width(82f), GUILayout.Height(26f)))
                {
                    ReleaseJailedSteamId(jail.SteamId, true, out _, out _);
                }
                GUILayout.EndHorizontal();
            }
            if (keys.Count > count)
            {
                GUILayout.Label("+" + (keys.Count - count).ToString(CultureInfo.InvariantCulture) + " more jailed player(s)", _hintStyle);
            }
        }

        private void DrawNamesTab()
        {
            BeginPanel("Local Name");
            GUILayout.BeginHorizontal();
            _localAliasInput = GUILayout.TextField(_localAliasInput ?? "", _inputStyle, GUILayout.Height(32f));
            if (GUILayout.Button("Save Local", _primaryButtonStyle, GUILayout.Width(110f), GUILayout.Height(32f)))
            {
                _localAlias = CleanName(_localAliasInput);
                SaveSettingsFromConsole("Local alias updated");
            }
            if (GUILayout.Button("Clear", _quietButtonStyle, GUILayout.Width(80f), GUILayout.Height(32f)))
            {
                _localAlias = "";
                _localAliasInput = "";
                SaveSettingsFromConsole("Local alias cleared");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Current local alias: " + (String.IsNullOrEmpty(_localAlias) ? "(none)" : _localAlias), _hintStyle);
            EndPanel();

            BeginPanel("Player Name Override");
            GUILayout.Label("Selected: " + FormatSelectedPlayer(GetSelectedPlayer()), _hintStyle);
            GUILayout.BeginHorizontal();
            _aliasKeyInput = GUILayout.TextField(_aliasKeyInput ?? "", _inputStyle, GUILayout.Width(290f), GUILayout.Height(32f));
            _aliasNameInput = GUILayout.TextField(_aliasNameInput ?? "", _inputStyle, GUILayout.Width(230f), GUILayout.Height(32f));
            if (GUILayout.Button("Apply", _primaryButtonStyle, GUILayout.Width(82f), GUILayout.Height(32f)))
            {
                ApplyAliasRule();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Push Names", _primaryButtonStyle, GUILayout.Width(116f), GUILayout.Height(34f))) PushAliases();
            if (GUILayout.Button("Clear Fields", _quietButtonStyle, GUILayout.Width(112f), GUILayout.Height(34f)))
            {
                _aliasKeyInput = "";
                _aliasNameInput = "";
            }
            if (GUILayout.Button("Remove Rule", _dangerButtonStyle, GUILayout.Width(112f), GUILayout.Height(34f))) RemoveAliasRule();
            if (GUILayout.Button("Reload", _buttonStyle, GUILayout.Width(96f), GUILayout.Height(34f))) ReloadAliases();
            if (GUILayout.Button("Save All", _buttonStyle, GUILayout.Width(96f), GUILayout.Height(34f))) SaveSettingsFromConsole();
            GUILayout.EndHorizontal();
            GUILayout.Label("Rules: " + _aliases.Count.ToString(CultureInfo.InvariantCulture) + " | Auto push: " + (_serverPushAliases ? "on" : "off") + " | Last push touched " + _lastAliasSweepTouches.ToString(CultureInfo.InvariantCulture), _hintStyle);

            List<string> keys = new List<string>(_aliases.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            if (keys.Count == 0)
            {
                DrawEmptyListState("No saved name overrides.", "Identity rules are ready.");
            }
            else
            {
                _rosterScroll.x = 0f;
                _rosterScroll = GUILayout.BeginScrollView(_rosterScroll, false, false, GUILayout.Height(170f));
                for (int i = 0; i < keys.Count; i++)
                {
                    string key = keys[i];
                    GUILayout.BeginHorizontal(_rowStyle);
                    GUILayout.Label(Shorten(key + " -> " + _aliases[key], 82), _labelStyle);
                    bool remove = GUILayout.Button("Remove", _dangerButtonStyle, GUILayout.Width(88f), GUILayout.Height(28f));
                    GUILayout.EndHorizontal();
                    if (remove)
                    {
                        _aliases.Remove(key);
                        SaveSettingsFromConsole("Alias rule removed for " + key);
                        break;
                    }
                }
                GUILayout.EndScrollView();
            }
            EndPanel();
        }

        private void DrawUtilityTab()
        {
            BeginPanel("Server Operations");
            GUILayout.Label("Command Console", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Command", _contextLabelStyle, GUILayout.Width(76f), GUILayout.Height(32f));
            _commandInput = GUILayout.TextField(_commandInput ?? "", _inputStyle, GUILayout.Height(32f));
            if (GUILayout.Button("Execute", _primaryButtonStyle, GUILayout.Width(96f), GUILayout.Height(32f)))
            {
                RunCommand();
            }
            if (GUILayout.Button("Clear", _quietButtonStyle, GUILayout.Width(74f), GUILayout.Height(32f)))
            {
                _commandInput = "";
                ActionOk("Command cleared");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("World State", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton("World Save", _primaryButtonStyle, _worldActionsAvailable, GUILayout.Width(142f), GUILayout.Height(34f))) ForceSave();
            if (GUILayout.Button("Refresh Roster", _buttonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) RefreshPlayers();
            if (GUILayout.Button("Sync Ban Registry", _buttonStyle, GUILayout.Width(148f), GUILayout.Height(34f))) RefreshBansFromGame();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawSaveActivity();
            DrawServerEnvironmentControls();
            DrawServerSleepControls();
            DrawServerAdmissionControls();

            GUILayout.Label("Session Tools", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            if (GUILayout.Button("Restore Cursor", _quietButtonStyle, GUILayout.Width(126f), GUILayout.Height(34f))) SetMenuMode(true);
            if (GUILayout.Button("Reload Settings", _quietButtonStyle, GUILayout.Width(132f), GUILayout.Height(34f))) ReloadAliases();
            if (GUILayout.Button("Save Settings", _quietButtonStyle, GUILayout.Width(124f), GUILayout.Height(34f))) SaveSettingsFromConsole();
            if (GUILayout.Button("Copy Target ID", _quietButtonStyle, GUILayout.Width(128f), GUILayout.Height(34f))) CopySelectedSteamId();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(_settingsStatus, _hintStyle);
            if (!String.IsNullOrEmpty(_settingsProblem)) GUILayout.Label(_settingsProblem, _hintStyle);

            GUILayout.Label("Diagnostics", _sectionTitleStyle);
            GUILayout.BeginVertical(_rowStyle);
            GUILayout.BeginHorizontal();
            DrawPill("Chat " + _chatPatchCount.ToString(CultureInfo.InvariantCulture), _pillStyle, 78f);
            DrawPill("Network " + _networkPatchCount.ToString(CultureInfo.InvariantCulture), _pillStyle, 98f);
            DrawPill("Guard " + _antiCheatPatchCount.ToString(CultureInfo.InvariantCulture), _pillStyle, 86f);
            DrawPill("Aliases " + _aliases.Count.ToString(CultureInfo.InvariantCulture), _pillStyle, 92f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.Label("Roster source: " + Shorten(_lastRosterDiagnostics, 96), _hintStyle);
            GUILayout.Label("Last operation: " + Shorten(_lastAction, 110), _hintStyle);
            GUILayout.EndVertical();
            EndPanel();
        }

        private void DrawDebugCheatingTab()
        {
            BeginPanel("Debug(Cheating)");
            DrawDebugSubTabs();
            GUILayout.Label("On/Off: observed in game. ?: state unavailable. *: requested, not confirmed.", _hintStyle);
            GUILayout.Space(8f);

            if (String.Equals(_debugActiveTab, "Self", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugSelfSubTab();
            }
            else if (String.Equals(_debugActiveTab, "Players", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugPlayersSubTab();
            }
            else if (String.Equals(_debugActiveTab, "Items", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugItemsSubTab();
            }
            else if (String.Equals(_debugActiveTab, "World", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugWorldSubTab();
            }
            else if (String.Equals(_debugActiveTab, "Build", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugBuildSubTab();
            }
            else if (String.Equals(_debugActiveTab, "AI/NPC", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugAiNpcSubTab();
            }
            else if (String.Equals(_debugActiveTab, "Visual", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugVisualSubTab();
            }
            else if (String.Equals(_debugActiveTab, "Server", StringComparison.OrdinalIgnoreCase))
            {
                DrawDebugServerSubTab();
            }
            else
            {
                DrawDebugRawSubTab();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Last operation: " + _lastAction, _hintStyle);
            EndPanel();
        }

        private void DrawDebugSubTabs()
        {
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSubTabButton("Self", 74f, _iconPlayers);
            DrawDebugSubTabButton("Players", 96f, _iconRoster);
            DrawDebugSubTabButton("Items", 82f, _iconItems);
            DrawDebugSubTabButton("World", 88f, _iconWorld);
            DrawDebugSubTabButton("Build", 80f, _iconBuild);
            DrawDebugSubTabButton("AI/NPC", 94f, _iconNpc);
            DrawDebugSubTabButton("Visual", 88f, _iconVisual);
            DrawDebugSubTabButton("Server", 92f, _iconServer);
            DrawDebugSubTabButton("Raw", 70f, _iconRaw);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawDebugSubTabButton(string tabName, float width)
        {
            DrawDebugSubTabButton(tabName, width, null);
        }

        private void DrawDebugSubTabButton(string tabName, float width, Texture2D icon)
        {
            GUIStyle style = String.Equals(_debugActiveTab, tabName, StringComparison.OrdinalIgnoreCase) ? _primaryButtonStyle : _quietButtonStyle;
            GUIContent content = icon == null ? new GUIContent(tabName) : new GUIContent(tabName, icon, tabName);
            if (GUILayout.Button(content, style, GUILayout.Width(width), GUILayout.Height(30f)))
            {
                _debugActiveTab = tabName;
            }
        }

        private void DrawDebugSelfSubTab()
        {
            GUILayout.Label("Local Player Powers", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("God", "_godmode", "on", "off", "godmode on", "godmode off", 96f, _primaryButtonStyle, _buttonStyle, "Toggle local god mode.");
            DrawDebugToggleButton("No Drain", "_survival", "off", "on", "survival off", "survival on", 124f, _primaryButtonStyle, _buttonStyle, "Toggle survival stat drain. On means survival drain is disabled.");
            DrawDebugToggleButton("Energy", "_energyhack", "on", "off", "energyhack on", "energyhack off", 112f, _primaryButtonStyle, _buttonStyle, "Toggle local stamina/energy cheat.");
            DrawDebugToggleButton("Speed", "_speedyrun", "on", "off", "speedyrun on", "speedyrun off", 108f, _primaryButtonStyle, _buttonStyle, "Toggle the game's speedy-run mode.");
            DrawDebugToggleButton("Super Jump", "_superJump", "on", "off", "superjump on", "superjump off", 132f, _primaryButtonStyle, _buttonStyle, "Toggle the game's super jump mode.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Heal", "_heallocalplayer", "100", "heallocalplayer 100", 72f, _primaryButtonStyle);
            DrawDebugMethodButton("Regen", "_regenhealth", "", "regenhealth", 76f, _buttonStyle);
            DrawDebugMethodButton("Revive", "_revivelocalplayer", "", "revivelocalplayer", 78f, _buttonStyle);
            DrawDebugMethodButton("Kill", "_killlocalplayer", "", "killlocalplayer", 64f, _dangerButtonStyle);
            DrawDebugMethodButton("Fake Drown", "_fakeDrown", "", "fakedrown", 94f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Movement Tuning", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Walk", ref _debugWalkInput, 1f, 50f, 5f, "Local walk speed.", 42f, 105f);
            DrawDebugSliderField("Run", ref _debugRunInput, 1f, 80f, 8f, "Local run speed.", 34f, 105f);
            DrawDebugActionButton(_iconSpeed, "Apply Move", _primaryButtonStyle, 116f, 32f, ApplyLocalMovementDebug);
            DrawDebugActionButton(_iconRaw, "Defaults", _quietButtonStyle, 92f, 32f, ResetLocalMovementDebugInputs);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Swim", ref _debugSwimInput, 1f, 50f, 4f, "Local swim speed.", 42f, 105f);
            DrawDebugSliderField("Jump", ref _debugJumpInput, 0f, 20f, 1f, "Local jump multiplier.", 42f, 105f);
            DrawDebugSliderField("Scale", ref _debugScaleInput, 0.2f, 5f, 1f, "Local player body scale.", 44f, 96f);
            DrawDebugActionButton(_iconSpeed, "Set Scale", _buttonStyle, 100f, 32f, ApplyLocalScaleDebug);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Gravity", ref _debugGravityInput, -30f, 5f, -9.81f, "World gravity scalar. Normal is -9.81.", 56f, 180f);
            if (DrawDebugCommandButton(_iconWorld, "Set Gravity", _buttonStyle, 108f, 32f, "_gravity")) RunDebugConsoleAction("Gravity", "_gravity", _debugGravityInput, "gravity " + (_debugGravityInput ?? ""));
            DrawDebugToggleButton("Free Cam", "_freecamera", "on", "off", "freecamera on", "freecamera off", 118f, _primaryButtonStyle, _buttonStyle, "Toggle free camera. On/Off reflects the local camera controller's enabled state.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Armour / Loadout", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugEquipButton("Bone Armour", "BoneArmour", 106f);
            DrawDebugEquipButton("Creepy Armour", "CreepyArmour", 124f);
            DrawDebugEquipButton("Tech Armour", "TechArmour", 112f);
            DrawDebugEquipButton("Golden Armour", "GoldenArmor", 124f);
            DrawDebugMethodButton("Add All", "_addAllItems", "", "addallitems", 84f, _dangerButtonStyle);
            DrawDebugMethodButton("Remove All", "_removeAllItems", "", "removeallitems", 104f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawDebugPlayersSubTab()
        {
            PlayerEntry selected = GetSelectedPlayer();
            DrawModerationAvailability();
            GUILayout.Label("Target Player", _sectionTitleStyle);
            GUILayout.Label("Selected: " + Shorten(FormatSelectedPlayer(selected), 110), _hintStyle);

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugActionButton(_iconSummon, "Summon", _primaryButtonStyle, 94f, 34f, SummonSelectedPlayer, _hostActionsAvailable);
            DrawDebugActionButton(_iconGoTo, "Go To", _buttonStyle, 82f, 34f, TeleportToSelectedPlayer);
            DrawDebugActionButton(_iconWarning, "Jail", _dangerButtonStyle, 72f, 34f, JailSelectedPlayer, _hostActionsAvailable);
            DrawDebugActionButton(_iconShield, "Release", _buttonStyle, 92f, 34f, ReleaseSelectedPlayer, _hostActionsAvailable);
            DrawDebugActionButton(_iconItems, "Audit", _buttonStyle, 86f, 34f, RequestSelectedInventoryAudit);
            DrawDebugActionButton(_iconShield, IsSelectedAntiCheatExempt() ? "Unexempt" : "Exempt", _buttonStyle, 108f, 34f, ToggleSelectedAntiCheatExempt);
            DrawDebugActionButton(_iconCopy, "Copy ID", _quietButtonStyle, 88f, 34f, CopySelectedSteamId);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugActionButton(_iconKick, "Kick", _dangerButtonStyle, 74f, 34f, KickSelectedPlayer, _serverCommandsAvailable);
            DrawDebugActionButton(_iconBan, "Ban", _dangerButtonStyle, 72f, 34f, BanSelectedPlayer, _serverCommandsAvailable);
            DrawDebugActionButton(_iconShield, "Unban", _buttonStyle, 86f, 34f, UnbanSelectedPlayer, _hostActionsAvailable);
            DrawDebugActionButton(_iconMute, IsPlayerMuted(selected) ? "Unmute" : "Mute", _buttonStyle, 90f, 34f, ToggleSelectedMute);
            DrawDebugActionButton(_iconServer, "Admin", _buttonStyle, 82f, 34f, MakeSelectedPlayerAdmin, _serverCommandsAvailable);
            DrawDebugActionButton(_iconShield, "Trusted", _buttonStyle, 94f, 34f, MakeSelectedPlayerTrusted, _serverCommandsAvailable);
            DrawDebugActionButton(_iconPlayers, "Guest", _buttonStyle, 82f, 34f, MakeSelectedPlayerGuest, _serverCommandsAvailable);
            DrawDebugActionButton(_iconBan, "Check Ban", _quietButtonStyle, 108f, 34f, CheckSelectedPlayerBan);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Experimental Client Event", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Command", _contextLabelStyle, GUILayout.Width(74f), GUILayout.Height(32f));
            _debugPlayerCommandInput = GUILayout.TextField(_debugPlayerCommandInput ?? "", _inputStyle, GUILayout.Width(150f), GUILayout.Height(32f));
            GUILayout.Label("Data", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugPlayerCommandDataInput = GUILayout.TextField(_debugPlayerCommandDataInput ?? "", _inputStyle, GUILayout.Width(220f), GUILayout.Height(32f));
            if (DrawModerationIconButton(_iconGoTo, "Send Target", _primaryButtonStyle, _hostActionsAvailable, 122f, 32f)) SendSelectedAdminDebugCommand();
            if (DrawDebugIconButton(_iconRaw, "Clear", _quietButtonStyle, 76f, 32f))
            {
                _debugPlayerCommandInput = "";
                _debugPlayerCommandDataInput = "";
                ActionOk("Target command fields cleared");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Host only. Sends one AdminCommand to the selected player's current connection. Client handling depends on the command; delivery and execution are unconfirmed.", _hintStyle);
        }

        private void DrawDebugAiNpcSubTab()
        {
            DrawWorldActionAvailability();
            GUILayout.Label("AI / NPC Control", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugToggleButton("AI Disabled", "_aiDisable", "on", "off", "aidisable on", "aidisable off", 132f, _dangerButtonStyle, _buttonStyle, "Toggle global AI processing.");
            DrawDebugToggleButton("AI God", "_aiGodMode", "on", "off", "aigodmode on", "aigodmode off", 104f, _primaryButtonStyle, _buttonStyle, "Toggle AI invulnerability.");
            DrawDebugToggleButton("Dummy", "_aiDummy", "on", "off", "aidummy on", "aidummy off", 104f, _primaryButtonStyle, _buttonStyle, "Toggle AI dummy behavior.");
            DrawDebugToggleButton("Animals", "_animalsEnabled", "on", "off", "animalsenabled on", "animalsenabled off", 118f, _primaryButtonStyle, _dangerButtonStyle, "Toggle animal spawns/behavior.", true);
            DrawDebugToggleButton("No Thought CD", "_aiThoughtNoCooldown", "on", "off", "aithoughtnocooldown on", "aithoughtnocooldown off", 148f, _primaryButtonStyle, _buttonStyle, "Toggle AI thought cooldown bypass.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugSliderField("Radius", ref _debugRadiusInput, 1f, 80f, 15f, "Radius around the game's active world position for these AI actions.", 56f, 170f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(new GUIContent("Lifetime", null, "Optional duration for Ignite and Shock. Leave blank to use the game's default."), _contextLabelStyle, GUILayout.Width(68f), GUILayout.Height(32f));
            _debugEffectLifetimeInput = GUILayout.TextField(_debugEffectLifetimeInput ?? "", _inputStyle, GUILayout.Width(76f), GUILayout.Height(32f));
            GUILayout.Label(new GUIContent("Damage", null, "Required positive amount for the Damage action."), _contextLabelStyle, GUILayout.Width(62f), GUILayout.Height(32f));
            _debugRadiusDamageInput = GUILayout.TextField(_debugRadiusDamageInput ?? "", _inputStyle, GUILayout.Width(76f), GUILayout.Height(32f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugRadiusButton("Kill", "_killRadius", "killradius");
            DrawDebugRadiusButton("Ignite", "_igniteRadius", "igniteradius", _debugEffectLifetimeInput);
            DrawDebugRadiusButton("Shock", "_shockRadius", "shockradius", _debugEffectLifetimeInput);
            DrawDebugRadiusButton("Burn", "_burnBodyRadius", "burnbodyradius");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugRadiusButton("Dismember", "_dismemberRadius", "dismemberradius");
            DrawDebugRadiusButton("Gib", "_gibRadius", "gibradius");
            DrawDebugRadiusButton("Damage", "_damageRadius", "damageradius", _debugRadiusDamageInput);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Village Commands", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Creepy Party", "_creepyAttackParty", "", "creepyattackparty", 110f, _dangerButtonStyle);
            DrawDebugMethodButton("Creepy Village", "_creepyVillage", "", "creepyvillage", 122f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Creepy Village requests a takeover of the nearest village. No arguments are needed.", _hintStyle);

            GUILayout.Label("Companions", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Robby Carry", "_robbyCarry", "", "robbycarry", 104f, _buttonStyle);
            DrawDebugMethodButton("Robby Info", "_robbyStateInfo", "", "robbystateinfo", 96f, _buttonStyle);
            DrawDebugActionButton(_iconNpc, _possession.Active ? "Stop Kelvin" : _possession.NeedsRelease ? "Retry Release" : "Possess Kelvin", _buttonStyle, 132f, 32f, TogglePossession, _possession.Active || _possession.NeedsRelease || (_experimentalPossession && _worldActionsAvailable));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(_possession.LastStatus, _hintStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Remove Dead", "_removeDead", "", "removedead", 106f, _dangerButtonStyle);
            DrawDebugMethodButton("Remove Living", "_removeLiving", "", "removeliving", 116f, _dangerButtonStyle);
            DrawDebugMethodButton("Animal Limit x50", "_animalLimitMult", "50", "animallimitmult 50", 138f, _dangerButtonStyle);
            DrawDebugMethodButton("Animal Limit x1", "_animalLimitMult", "1", "animallimitmult 1", 132f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawDebugServerSubTab()
        {
            DrawModerationAvailability();
            GUILayout.Label("Server Policy / Guards", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationIconButton(_iconServer, "Cheats Off", _dangerButtonStyle, _hostActionsAvailable, 106f, 34f)) PushServerCheatsOff(true);
            if (DrawModerationIconButton(_iconServer, "Cheats On", _buttonStyle, _serverCommandsAvailable, 104f, 34f)) RequestServerToggle("Command_setcheatsenabled", "setcheatsenabled", true, "Cheats on");
            if (DrawModerationIconButton(_iconShield, "PVP On", _buttonStyle, _serverCommandsAvailable, 90f, 34f)) RequestServerToggle("Command_setpvpdamage", "setpvpdamage", true, "PvP on");
            if (DrawModerationIconButton(_iconShield, "PVP Off", _buttonStyle, _serverCommandsAvailable, 92f, 34f)) RequestServerToggle("Command_setpvpdamage", "setpvpdamage", false, "PvP off");
            DrawDebugActionButton(_iconSave, "World Save", _primaryButtonStyle, 114f, 34f, ForceSave, _worldActionsAvailable);
            DrawDebugActionButton(_iconSave, "Player Save", _buttonStyle, 112f, 34f, RequestPlayerSave, _worldReady && _networkRunning == true && _networkServer == false);
            DrawDebugActionButton(_iconBan, "Sync Bans", _buttonStyle, 104f, 34f, RefreshBansFromGame);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            DrawSaveActivitySummary();

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawDebugIconButton(_iconServer, "Cheats: " + (_disallowCheats ? "Off" : "Allowed"), _disallowCheats ? _primaryButtonStyle : _buttonStyle, 136f, 34f))
            {
                _disallowCheats = !_disallowCheats;
                SaveSettingsFromConsole("Disallow cheats " + (_disallowCheats ? "enabled" : "disabled"));
            }
            if (DrawDebugIconButton(_iconSpeed, "Move Guard: " + (_movementGuard ? "On" : "Off"), _movementGuard ? _primaryButtonStyle : _buttonStyle, 152f, 34f))
            {
                _movementGuard = !_movementGuard;
                SaveSettingsFromConsole("Movement guard " + (_movementGuard ? "enabled" : "disabled"));
            }
            if (DrawDebugIconButton(_iconItems, "Item Guard: " + (_strictItemEventGuard ? "On" : "Off"), _strictItemEventGuard ? _primaryButtonStyle : _buttonStyle, 142f, 34f))
            {
                _strictItemEventGuard = !_strictItemEventGuard;
                SaveSettingsFromConsole("Item guard " + (_strictItemEventGuard ? "enabled" : "disabled"));
            }
            if (DrawDebugIconButton(_iconWorld, "Vehicle: " + (_vehicleSpawnGuard ? "On" : "Off"), _vehicleSpawnGuard ? _primaryButtonStyle : _buttonStyle, 124f, 34f))
            {
                _vehicleSpawnGuard = !_vehicleSpawnGuard;
                SaveSettingsFromConsole("Vehicle guard " + (_vehicleSpawnGuard ? "enabled" : "disabled"));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Disconnect / Network Debug", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Steam64", _contextLabelStyle, GUILayout.Width(62f), GUILayout.Height(32f));
            _manualSteamIdInput = GUILayout.TextField(_manualSteamIdInput ?? "", _inputStyle, GUILayout.Width(170f), GUILayout.Height(32f));
            if (DrawModerationIconButton(_iconKick, "Kick ID", _dangerButtonStyle, _serverCommandsAvailable, 90f, 32f)) KickManualSteamId();
            if (DrawModerationIconButton(_iconBan, "Ban ID", _dangerButtonStyle, _serverCommandsAvailable, 86f, 32f)) BanManualSteamId();
            if (DrawModerationIconButton(_iconShield, "Unban ID", _buttonStyle, _hostActionsAvailable, 100f, 32f)) UnbanManualSteamId();
            DrawDebugMethodButton("Kick All", "_kickPlayers", "", "kickplayers", 86f, _dangerButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Guard state: jailed " + _jailedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " | exempt " + _antiCheatExemptPlayers.Count.ToString(CultureInfo.InvariantCulture) + " | sources " + Shorten(_lastRosterDiagnostics, 86), _hintStyle);
        }

        private void DrawDebugRawSubTab()
        {
            GUILayout.Label("Raw Debug Command", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            _debugCommandInput = GUILayout.TextField(_debugCommandInput ?? "", _inputStyle, GUILayout.Height(32f));
            if (DrawDebugIconButton(_iconRaw, "Run", _primaryButtonStyle, 72f, 32f)) RunDebugRawCommand();
            if (DrawDebugIconButton(_iconRaw, "Clear", _quietButtonStyle, 78f, 32f))
            {
                _debugCommandInput = "";
                ActionOk("Debug command cleared");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Quick Raw Routes", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Help", "", "", "help", 64f, _buttonStyle);
            DrawDebugMethodButton("Clear Console", "", "", "clear", 104f, _buttonStyle);
            DrawDebugMethodButton("Dump Lobby", "", "", "dumplobbyinfo", 104f, _buttonStyle);
            DrawDebugMethodButton("GC Collect", "", "", "gccollect", 92f, _buttonStyle);
            DrawDebugActionButton(_iconSave, "Player Save", _buttonStyle, 108f, 32f, RequestPlayerSave, _worldReady && _networkRunning == true && _networkServer == false);
            DrawDebugActionButton(_iconSave, "World Save", _primaryButtonStyle, 108f, 32f, ForceSave, _worldActionsAvailable);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            DrawSaveActivitySummary();
        }

        private void DrawDebugEquipButton(string label, string itemName, float width)
        {
            if (GUILayout.Button(label, _buttonStyle, GUILayout.Width(width), GUILayout.Height(32f)))
            {
                _debugItemInput = itemName;
                RunDebugItemAction("_equipitem");
            }
        }

        private void DrawDebugCheatingLegacyTab()
        {
            BeginPanel("Debug(Cheating)");
            GUILayout.Label("Local Powers", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("God On", "_godmode", "on", "godmode on", 82f, _primaryButtonStyle);
            DrawDebugMethodButton("God Off", "_godmode", "off", "godmode off", 82f, _buttonStyle);
            DrawDebugMethodButton("Speed On", "_speedyrun", "on", "speedyrun on", 92f, _primaryButtonStyle);
            DrawDebugMethodButton("Speed Off", "_speedyrun", "off", "speedyrun off", 92f, _buttonStyle);
            DrawDebugMethodButton("Heal", "_heallocalplayer", "100", "heallocalplayer 100", 76f, _primaryButtonStyle);
            DrawDebugMethodButton("Revive", "_revivelocalplayer", "", "revivelocalplayer", 82f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawDebugMethodButton("Logs On", "_loghack", "on", "loghack on", 84f, _primaryButtonStyle);
            DrawDebugMethodButton("Stone On", "_stonehack", "on", "stonehack on", 88f, _primaryButtonStyle);
            DrawDebugMethodButton("Energy On", "_energyhack", "on", "energyhack on", 94f, _primaryButtonStyle);
            DrawDebugMethodButton("Survival Off", "_survival", "off", "survival off", 108f, _buttonStyle);
            DrawDebugMethodButton("Add All", "_addAllItems", "", "addallitems", 88f, _dangerButtonStyle);
            DrawDebugMethodButton("Free Cam", "_freecamera", "on", "freecamera on", 88f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Item Fabricator", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Item", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugItemInput = GUILayout.TextField(_debugItemInput ?? "", _inputStyle, GUILayout.Width(190f), GUILayout.Height(32f));
            GUILayout.Label("Count", _contextLabelStyle, GUILayout.Width(48f), GUILayout.Height(32f));
            _debugItemCountInput = GUILayout.TextField(_debugItemCountInput ?? "", _inputStyle, GUILayout.Width(52f), GUILayout.Height(32f));
            if (GUILayout.Button("Add", _primaryButtonStyle, GUILayout.Width(74f), GUILayout.Height(32f))) RunDebugItemAction("_additem");
            if (GUILayout.Button("Spawn Drop", _buttonStyle, GUILayout.Width(100f), GUILayout.Height(32f))) RunDebugItemAction("_spawnitem");
            if (GUILayout.Button("Equip", _buttonStyle, GUILayout.Width(74f), GUILayout.Height(32f))) RunDebugItemAction("_equipitem");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Tag", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugTagInput = GUILayout.TextField(_debugTagInput ?? "", _inputStyle, GUILayout.Width(190f), GUILayout.Height(32f));
            if (GUILayout.Button("Add Tag", _buttonStyle, GUILayout.Width(88f), GUILayout.Height(32f))) RunDebugConsoleAction("Item tag", "_additemswithtag", _debugTagInput, "additemswithtag " + (_debugTagInput ?? ""));
            DrawDebugQuickItemButton("Pistol", 74f);
            DrawDebugQuickItemButton("Shotgun", 82f);
            DrawDebugQuickItemButton("Rifle", 68f);
            DrawDebugQuickItemButton("Rebreather", 98f);
            DrawDebugQuickItemButton("Shovel", 74f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Target Operations", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton("Summon Target", _primaryButtonStyle, _hostActionsAvailable, GUILayout.Width(126f), GUILayout.Height(34f))) SummonSelectedPlayer();
            if (GUILayout.Button("Go To Target", _buttonStyle, GUILayout.Width(116f), GUILayout.Height(34f))) TeleportToSelectedPlayer();
            if (GUILayout.Button("Audit Target", _buttonStyle, GUILayout.Width(112f), GUILayout.Height(34f))) RequestSelectedInventoryAudit();
            if (GUILayout.Button("Exempt Target", _buttonStyle, GUILayout.Width(122f), GUILayout.Height(34f))) ToggleSelectedAntiCheatExempt();
            if (DrawModerationButton("Jail Target", _dangerButtonStyle, _hostActionsAvailable, GUILayout.Width(104f), GUILayout.Height(34f))) JailSelectedPlayer();
            if (DrawModerationButton("Release", _buttonStyle, _hostActionsAvailable, GUILayout.Width(82f), GUILayout.Height(34f))) ReleaseSelectedPlayer();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Target: " + Shorten(FormatSelectedPlayer(GetSelectedPlayer()), 96), _hintStyle);

            GUILayout.Space(8f);
            GUILayout.Label("World Debug", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Coords", _contextLabelStyle, GUILayout.Width(54f), GUILayout.Height(32f));
            _debugCoordInput = GUILayout.TextField(_debugCoordInput ?? "", _inputStyle, GUILayout.Width(210f), GUILayout.Height(32f));
            if (GUILayout.Button("Use Here", _quietButtonStyle, GUILayout.Width(86f), GUILayout.Height(32f))) SetDebugCoordsFromLocalPosition();
            if (GUILayout.Button("Go", _primaryButtonStyle, GUILayout.Width(58f), GUILayout.Height(32f))) RunDebugConsoleAction("Coordinate teleport", "_gotoCoords", _debugCoordInput, "gotocoords " + (_debugCoordInput ?? ""));
            GUILayout.Label("Time", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugTimeInput = GUILayout.TextField(_debugTimeInput ?? "", _inputStyle, GUILayout.Width(52f), GUILayout.Height(32f));
            if (GUILayout.Button("Set", _buttonStyle, GUILayout.Width(58f), GUILayout.Height(32f))) RunDebugConsoleAction("Set time", "_setTimeOfDay", _debugTimeInput, "settimeofday " + (_debugTimeInput ?? ""));
            if (GUILayout.Button("Lock", _buttonStyle, GUILayout.Width(62f), GUILayout.Height(32f))) RunDebugConsoleAction("Time lock", "_lockTimeOfDay", _debugTimeInput, "locktimeofday " + (_debugTimeInput ?? ""));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Radius", _contextLabelStyle, GUILayout.Width(54f), GUILayout.Height(32f));
            _debugRadiusInput = GUILayout.TextField(_debugRadiusInput ?? "", _inputStyle, GUILayout.Width(58f), GUILayout.Height(32f));
            DrawDebugRadiusButton("Kill", "_killRadius", "killradius");
            DrawDebugRadiusButton("Ignite", "_igniteRadius", "igniteradius");
            DrawDebugRadiusButton("Shock", "_shockRadius", "shockradius");
            DrawDebugMethodButton("Rain On", "_forcerain", "on", "forcerain on", 82f, _buttonStyle);
            DrawDebugMethodButton("Rain Off", "_forcerain", "off", "forcerain off", 84f, _buttonStyle);
            DrawDebugMethodButton("GolfCart Net", "_golfCartNetworkDebug", "on", "golfcartnetworkdebug on", 112f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Raw Debug Command", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            _debugCommandInput = GUILayout.TextField(_debugCommandInput ?? "", _inputStyle, GUILayout.Height(32f));
            if (GUILayout.Button("Run", _primaryButtonStyle, GUILayout.Width(68f), GUILayout.Height(32f))) RunDebugRawCommand();
            if (GUILayout.Button("Clear", _quietButtonStyle, GUILayout.Width(72f), GUILayout.Height(32f)))
            {
                _debugCommandInput = "";
                ActionOk("Debug command cleared");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Last operation: " + Shorten(_lastAction, 110), _hintStyle);
            EndPanel();
        }

        private void BeginPanel(string title, params GUILayoutOption[] options)
        {
            GUILayout.BeginVertical(_boxStyle, options);
            GUILayout.Label(title, _smallHeaderStyle, GUILayout.Height(24f));
            Rect titleRect = GUILayoutUtility.GetLastRect();
            if (Event.current != null && Event.current.type == EventType.Repaint && titleRect.width > 1f)
            {
                FillRect(new Rect(titleRect.x, titleRect.yMax + 1f, titleRect.width, 1f), new Color(0.085f, 0.100f, 0.090f, 0.92f));
                FillRect(new Rect(titleRect.x, titleRect.yMax + 1f, Math.Min(150f, titleRect.width * 0.35f), 1f), new Color(0.42f, 0.55f, 0.29f, 0.88f));
            }
            GUILayout.Space(8f);
        }

        private static void EndPanel()
        {
            GUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        private void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            SetMenuMode(_menuOpen);
            if (_menuOpen)
            {
                RefreshPlayers();
            }
            LogInfo("Menu hotkey fired; menu open=" + _menuOpen);
        }

        private void SetMenuMode(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            catch
            {
            }
        }

        private bool GetKeyDown(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return false;
            }

            KeyCode code;
            return Enum.TryParse(key, true, out code) && Input.GetKeyDown(code);
        }

        private string[] GetPlayerOptions()
        {
            if (_players.Count == 0)
            {
                return new string[] { "No players detected" };
            }

            string[] options = new string[_players.Count];
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerEntry entry = _players[i];
                string name = String.IsNullOrEmpty(entry.Name) ? "(unknown)" : entry.Name;
                string id = entry.SteamId ?? "";
                if (id.Length > 8)
                {
                    id = id.Substring(id.Length - 8);
                }
                options[i] = (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + Shorten(name, 34) + (String.IsNullOrEmpty(id) ? "" : " [" + id + "]");
            }

            return options;
        }

        private PlayerEntry GetSelectedPlayer()
        {
            if (_players.Count == 0)
            {
                if (_manualTarget != null
                    && (String.IsNullOrEmpty(_selectedPlayerKey)
                        || String.Equals(_manualTarget.Key, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)
                        || String.Equals(_manualTarget.SteamId, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)))
                {
                    return _manualTarget;
                }
                return null;
            }

            if (!String.IsNullOrEmpty(_selectedPlayerKey))
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    if (String.Equals(_players[i].Key, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)
                        || String.Equals(_players[i].SteamId, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedPlayerIndex = i;
                        return _players[i];
                    }
                }
                if (_manualTarget != null
                    && (String.Equals(_manualTarget.Key, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)
                        || String.Equals(_manualTarget.SteamId, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)))
                {
                    return _manualTarget;
                }
                return null;
            }

            if (_selectedPlayerIndex < 0 || _selectedPlayerIndex >= _players.Count)
            {
                _selectedPlayerIndex = 0;
            }

            return _players[_selectedPlayerIndex];
        }

        private void SelectPlayer(PlayerEntry entry, bool fillAlias)
        {
            if (entry == null)
            {
                return;
            }

            _selectedPlayerKey = entry.Key;
            if (fillAlias)
            {
                _aliasKeyInput = !String.IsNullOrEmpty(entry.SteamId) ? entry.SteamId : entry.Key;
                _aliasNameInput = entry.Alias ?? "";
            }
        }

        private string FormatSelectedPlayer(PlayerEntry entry)
        {
            if (entry == null)
            {
                return "none";
            }

            string text = BestDisplayBaseName(entry);
            if (!String.IsNullOrEmpty(entry.SteamId))
            {
                text += " | " + entry.SteamId;
            }
            if (!String.IsNullOrEmpty(entry.Role))
            {
                text += " | " + entry.Role;
            }
            if (!String.IsNullOrEmpty(entry.Alias))
            {
                text += " -> " + entry.Alias;
            }
            return Shorten(text, 110);
        }

        private bool IsSelectedPlayer(PlayerEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(_selectedPlayerKey))
            {
                return false;
            }
            return String.Equals(entry.Key, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase)
                || String.Equals(entry.SteamId, _selectedPlayerKey, StringComparison.OrdinalIgnoreCase);
        }

        private string DisplayPlayerName(PlayerEntry entry)
        {
            if (entry == null)
            {
                return "";
            }
            string baseName = BestDisplayBaseName(entry);
            if (!String.IsNullOrEmpty(entry.Alias))
            {
                if (String.Equals(entry.Alias, baseName, StringComparison.OrdinalIgnoreCase) || String.Equals(baseName, "Unknown player", StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Alias;
                }
                return entry.Alias + " (" + baseName + ")";
            }
            return baseName;
        }

        private static string BuildObservedNamesText(PlayerEntry entry)
        {
            if (entry == null || entry.ObservedNames == null || entry.ObservedNames.Count < 2)
            {
                return "";
            }

            string text = "";
            int shown = 0;
            for (int i = 0; i < entry.ObservedNames.Count; i++)
            {
                string name = entry.ObservedNames[i];
                if (String.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (shown > 0)
                {
                    text += ", ";
                }
                text += name;
                shown++;
                if (shown >= 5)
                {
                    break;
                }
            }
            if (shown == 0)
            {
                return "";
            }
            if (entry.ObservedNames.Count > shown)
            {
                text += " +" + (entry.ObservedNames.Count - shown).ToString(CultureInfo.InvariantCulture);
            }
            return "Names: " + text;
        }

        private string BuildPlayerStatus(PlayerEntry entry, bool banned, bool muted)
        {
            string status = GetPlayerPresenceText(entry);
            if (!String.IsNullOrEmpty(entry.Role))
            {
                status += " | " + entry.Role;
            }
            if (banned)
            {
                status += (status.Length == 0 ? "" : " | ") + (HasSavedBanRecord(entry) ? "Ban record" : "Ban observed");
            }
            if (muted)
            {
                status += (status.Length == 0 ? "" : " | ") + "Muted";
            }
            return status.Length == 0 ? "Active" : status;
        }

        private string BuildNetworkStatus(PlayerEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.NetworkId))
            {
                return "";
            }
            return " | Network: " + entry.NetworkId;
        }

        private string BuildLastSeenStatus(PlayerEntry entry)
        {
            if (entry == null || entry.LastSeenUtc == DateTime.MinValue)
            {
                return "";
            }
            TimeSpan age = DateTime.UtcNow.Subtract(entry.LastSeenUtc);
            if (entry.PresenceUnresolved)
            {
                return " | Last confirmed " + (age.TotalSeconds < 60.0
                    ? Math.Max(1, (int)age.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s ago"
                    : Math.Max(1, (int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m ago");
            }
            if (entry.Online)
            {
                return " | Seen now";
            }
            if (age.TotalSeconds < 60.0)
            {
                return " | Left " + Math.Max(1, (int)age.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s ago";
            }
            return " | Left " + Math.Max(1, (int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m ago";
        }

        private string BuildLastActivityStatus(PlayerEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.LastActivity))
            {
                return "";
            }
            return " | Activity: " + entry.LastActivity;
        }

        private int CountPlayers(bool online)
        {
            int count = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null && !_players[i].PresenceUnresolved && _players[i].Online == online)
                {
                    count++;
                }
            }
            return count;
        }

        private void SelectManualTarget()
        {
            string id = CleanSteamId(_manualSteamIdInput);
            if (!LooksLikeSteamId(id))
            {
                ActionFail("Manual target needs a full Steam64 ID");
                return;
            }

            PlayerEntry entry = FindPlayerBySteamId(id);
            if (entry == null)
            {
                entry = new PlayerEntry();
                entry.Key = id;
                entry.SteamId = id;
                entry.Name = String.IsNullOrEmpty(_manualNameInput) ? "Manual Steam64" : CleanName(_manualNameInput);
                entry.Source = "Manual target";
            }
            _manualTarget = entry;
            SelectPlayer(entry, true);
            ActionOk("Target set to " + id);
        }

        private PlayerEntry FindPlayerBySteamId(string steamId)
        {
            steamId = CleanSteamId(steamId);
            if (String.IsNullOrEmpty(steamId))
            {
                return null;
            }
            for (int i = 0; i < _players.Count; i++)
            {
                if (String.Equals(_players[i].SteamId, steamId, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(_players[i].Key, steamId, StringComparison.OrdinalIgnoreCase))
                {
                    return _players[i];
                }
            }
            return null;
        }

        private bool IsPlayerBanned(PlayerEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            return HasSavedBanRecord(entry) || entry.NativeBan == true;
        }

        private bool IsPlayerMuted(PlayerEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            return IsSteamIdMuted(entry.SteamId);
        }

        private bool IsSteamIdMuted(string steamId)
        {
            steamId = CleanSteamId(steamId);
            return LooksLikeSteamId(steamId) && _mutedPlayers.ContainsKey(steamId);
        }

        private bool IsSelectedAntiCheatExempt()
        {
            return TryGetSelectedModerationTarget(out _, out ulong steamId)
                && IsSteamIdAntiCheatExempt(steamId.ToString(CultureInfo.InvariantCulture));
        }

        private bool IsSteamIdAntiCheatExempt(string steamId)
        {
            steamId = CleanSteamId(steamId);
            return LooksLikeSteamId(steamId) && _antiCheatExemptPlayers.ContainsKey(steamId);
        }

        private void CopySteamId(PlayerEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.SteamId))
            {
                ActionFail("Copy failed: no SteamID on that row");
                return;
            }
            GUIUtility.systemCopyBuffer = entry.SteamId;
            ActionOk("Copied SteamID " + entry.SteamId);
        }

        private void KickSelectedPlayer()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Kick requires a selected player with a resolved Steam64 ID");
                return;
            }
            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Removed by host" : _adminReasonInput;
            bool sent = SendKick(steamId, entry, reason, out string outcome);
            ActionResult(sent, outcome, outcome);
        }

        private void BanSelectedPlayer()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Ban requires a selected player with a resolved Steam64 ID");
                return;
            }

            if (entry != null)
            {
                TryRefreshPlayerNetworkHandles(entry, steamId);
            }
            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Banned by host" : _adminReasonInput;
            bool ok = SendBan(steamId, entry, reason, "Ban action", out bool saved, out string outcome);
            if (ok) RefreshPlayers();
            ActionResult(ok && saved, outcome, outcome);
        }

        private void UnbanSelectedPlayer()
        {
            if (!TryGetSelectedModerationTarget(out _, out ulong steamId))
            {
                ActionFail("Unban requires a selected player with a resolved Steam64 ID");
                return;
            }

            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Unban requested by host" : _adminReasonInput;
            bool ok = UnbanSteamId(steamId, reason, out string outcome);
            if (ok) RefreshPlayers();
            ActionResult(ok, outcome, outcome);
        }

        private bool TryResolveExactPlayerTransform(PlayerEntry entry, out object transform, out string route)
        {
            transform = null;
            route = "";
            if (entry == null)
            {
                return false;
            }

            ulong steamId;
            if (!UInt64.TryParse(CleanSteamId(entry.SteamId), NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
            {
                return false;
            }

            TryRefreshPlayerNetworkHandles(entry, steamId);

            if (TryUsePlayerTransformCandidate(entry, entry.Transform, "entry.Transform", steamId, out transform, out route)
                || TryUsePlayerTransformCandidate(entry, entry.BoltEntity, "entry.BoltEntity", steamId, out transform, out route)
                || TryUsePlayerTransformCandidate(entry, entry.SourceObject, "entry.SourceObject", steamId, out transform, out route))
            {
                return true;
            }

            object connection = entry.BoltConnection ?? GetConnectionFromSteamId(steamId);
            if (connection != null)
            {
                entry.BoltConnection = connection;
                if (TryUsePlayerTransformCandidate(entry, connection, "connection", steamId, out transform, out route))
                {
                    return true;
                }
                object entityFromConnection = GetEntityFromConnection(connection);
                if (entityFromConnection != null)
                {
                    entry.BoltEntity = entityFromConnection;
                    if (TryUsePlayerTransformCandidate(entry, entityFromConnection, "connection.entity", steamId, out transform, out route))
                    {
                        return true;
                    }
                }
            }

            object entity = entry.BoltEntity ?? GetEntityFromSteamId(steamId);
            if (entity != null)
            {
                entry.BoltEntity = entity;
                if (TryUsePlayerTransformCandidate(entry, entity, "steam.entity", steamId, out transform, out route))
                {
                    return true;
                }
            }

            if (TryResolvePlayerEntityFromServerInfo(steamId, out entity))
            {
                entry.BoltEntity = entity;
                if (TryUsePlayerTransformCandidate(entry, entity, "CoopServerInfo.entity", steamId, out transform, out route))
                {
                    return true;
                }
            }

            if (TryResolvePlayerEntityFromConnectionAndEntity(steamId, out connection, out entity))
            {
                if (connection != null) entry.BoltConnection = connection;
                if (entity != null) entry.BoltEntity = entity;
                if (TryUsePlayerTransformCandidate(entry, entity, "MultiplayerUtilities.GetConnectionAndEntity", steamId, out transform, out route))
                {
                    return true;
                }
            }

            if (TryFindExactRuntimePlayerTransform(entry, steamId, out transform, out route))
            {
                entry.Transform = transform;
                return true;
            }

            return false;
        }

        private bool TryUsePlayerTransformCandidate(PlayerEntry entry, object candidate, string source, ulong steamId, out object transform, out string route)
        {
            transform = null;
            route = "";
            if (candidate == null || !PlayerObjectMatchesSteamId(candidate, steamId, entry))
            {
                return false;
            }

            transform = ExtractTransform(candidate);
            if (transform == null && candidate is Transform)
            {
                transform = candidate;
            }
            if (transform == null)
            {
                return false;
            }

            entry.Transform = transform;
            route = source;
            return true;
        }

        private bool PlayerObjectMatchesSteamId(object candidate, ulong steamId, PlayerEntry entry)
        {
            if (candidate == null || steamId == 0UL)
            {
                return false;
            }

            string wanted = steamId.ToString(CultureInfo.InvariantCulture);
            string id = CleanSteamId(GetSteamIdFromEntity(candidate));
            if (LooksLikeSteamId(id) && String.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            id = CleanSteamId(GetSteamIdFromConnection(candidate));
            if (LooksLikeSteamId(id) && String.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            id = CleanSteamId(ExtractSteamId(candidate));
            if (LooksLikeSteamId(id) && String.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            object entity = FindBoltEntityForObject(candidate);
            if (entity != null && !ObjectsSame(entity, candidate))
            {
                id = CleanSteamId(GetSteamIdFromEntity(entity));
                if (LooksLikeSteamId(id) && String.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            object connection = ExtractFirstMember(candidate, new string[] { "Connection", "BoltConnection", "PlayerConnection", "_connection", "_boltConnection", "connection" });
            if (connection != null && !ObjectsSame(connection, candidate))
            {
                id = CleanSteamId(GetSteamIdFromConnection(connection));
                if (LooksLikeSteamId(id) && String.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (entry != null
                && String.Equals(CleanSteamId(entry.SteamId), wanted, StringComparison.OrdinalIgnoreCase)
                && (ObjectsSame(candidate, entry.Transform)
                    || ObjectsSame(candidate, entry.BoltEntity)
                    || ObjectsSame(candidate, entry.BoltConnection)
                    || ObjectsSame(candidate, entry.SourceObject)))
            {
                return true;
            }

            return false;
        }

        private bool TryResolvePlayerEntityFromServerInfo(ulong steamId, out object entity)
        {
            entity = null;
            Type serverInfo = ReflectionTools.FindType("CoopServerInfo");
            if (serverInfo == null)
            {
                return false;
            }

            object[] args = new object[] { steamId, null, false };
            object result;
            if (InvokeMethod(null, serverInfo, "TryGetPlayerEntityFromSteamId", args, true, out result)
                && result is bool
                && (bool)result
                && args.Length > 1
                && args[1] != null)
            {
                entity = args[1];
                return true;
            }
            return false;
        }

        private bool TryResolvePlayerEntityFromConnectionAndEntity(ulong steamId, out object connection, out object entity)
        {
            connection = null;
            entity = null;
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            if (utilities == null)
            {
                return false;
            }

            object[] args = new object[] { steamId, null, null };
            object result;
            if (!InvokeMethod(null, utilities, "GetConnectionAndEntity", args, true, out result))
            {
                return false;
            }
            if (args.Length > 1) connection = args[1];
            if (args.Length > 2) entity = args[2];
            return connection != null || entity != null;
        }

        private bool TryFindExactRuntimePlayerTransform(PlayerEntry entry, ulong steamId, out object transform, out string route)
        {
            transform = null;
            route = "";

            foreach (PlayerEntry registered in _registeredPlayers.Values)
            {
                if (registered == null || !String.Equals(CleanSteamId(registered.SteamId), steamId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (TryUsePlayerTransformCandidate(entry, registered.Transform, "registered.Transform", steamId, out transform, out route)
                    || TryUsePlayerTransformCandidate(entry, registered.BoltEntity, "registered.BoltEntity", steamId, out transform, out route)
                    || TryUsePlayerTransformCandidate(entry, registered.SourceObject, "registered.SourceObject", steamId, out transform, out route))
                {
                    return true;
                }
            }

            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            object entities = ReflectionTools.GetStaticMemberValue(utilities, "_playerEntities")
                ?? ReflectionTools.InvokeStaticNoArgMethod(utilities, "get__playerEntities");
            if (TryFindExactTransformInCollection(entry, steamId, entities, "MultiplayerUtilities._playerEntities", out transform, out route))
            {
                return true;
            }

            Type trackerType = ReflectionTools.FindType("PlayerTracker");
            object tracker = ReflectionTools.InvokeStaticNoArgMethod(trackerType, "get_Instance")
                ?? ReflectionTools.GetStaticMemberValue(trackerType, "Instance")
                ?? ReflectionTools.GetStaticMemberValue(trackerType, "_instance");
            if (tracker == null)
            {
                Array trackers = ReflectionTools.FindObjectsOfType(trackerType);
                if (trackers != null && trackers.Length > 0)
                {
                    tracker = trackers.GetValue(0);
                }
            }
            if (tracker != null)
            {
                object allEntities = ReflectionTools.GetMemberValue(tracker, "AllPlayerEntities") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_AllPlayerEntities");
                if (TryFindExactTransformInCollection(entry, steamId, allEntities, "PlayerTracker.AllPlayerEntities", out transform, out route))
                {
                    return true;
                }
                object allPlayers = ReflectionTools.GetMemberValue(tracker, "AllPlayers") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_AllPlayers");
                if (TryFindExactTransformInCollection(entry, steamId, allPlayers, "PlayerTracker.AllPlayers", out transform, out route))
                {
                    return true;
                }
                object netPlayer = ReflectionTools.GetMemberValue(tracker, "NetPlayer") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_NetPlayer");
                if (TryUsePlayerTransformCandidate(entry, netPlayer, "PlayerTracker.NetPlayer", steamId, out transform, out route))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindExactTransformInCollection(PlayerEntry entry, ulong steamId, object collection, string source, out object transform, out string route)
        {
            transform = null;
            route = "";
            bool found = false;
            object foundTransform = null;
            string foundRoute = "";
            VisitCollectionItems(collection, delegate(object item)
            {
                object candidate = FindBoltEntityForObject(item) ?? item;
                object candidateTransform;
                string candidateRoute;
                if (TryUsePlayerTransformCandidate(entry, candidate, source, steamId, out candidateTransform, out candidateRoute)
                    || TryUsePlayerTransformCandidate(entry, item, source, steamId, out candidateTransform, out candidateRoute))
                {
                    foundTransform = candidateTransform;
                    foundRoute = candidateRoute;
                    found = true;
                    return false;
                }
                return true;
            });
            transform = foundTransform;
            route = foundRoute;
            return found;
        }

        private void JailSelectedPlayer()
        {
            if (!RequireHostAction("Jail")) return;
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Jail requires a selected player with a resolved Steam64 ID");
                return;
            }

            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Jailed by host" : _adminReasonInput;
            string steamIdText = steamId.ToString(CultureInfo.InvariantCulture);
            bool moved = JailSteamId(steamIdText, entry, reason, "Manual jail", true);
            string outcome = "Jail " + steamIdText + ": " + GetJailMovementDetail(steamIdText);
            ActionResult(moved, outcome, outcome);
        }

        private void ReleaseSelectedPlayer()
        {
            if (!TryGetSelectedModerationTarget(out _, out ulong steamId))
            {
                ActionFail("Release requires a selected player with a resolved Steam64 ID");
                return;
            }

            ReleaseJailedSteamId(steamId.ToString(CultureInfo.InvariantCulture), true, out _, out _);
        }

        private void QuarantineSelectedPlayer()
        {
            if (!RequireHostAction("Quarantine")) return;
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Quarantine requires a selected player with a resolved Steam64 ID");
                return;
            }

            string steamIdText = steamId.ToString(CultureInfo.InvariantCulture);
            string local = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (LooksLikeSteamId(local) && String.Equals(local, steamIdText, StringComparison.OrdinalIgnoreCase))
            {
                ActionFail("Quarantine refused: target is the local host");
                return;
            }

            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Quarantined by host" : _adminReasonInput;
            bool guest = DemotePlayerToGuestForAdminAction(steamId, out string roleOutcome);
            bool muteSaved = SetSteamIdMuted(steamIdText, entry, true, out string muteOutcome);
            bool muted = _mutedPlayers.ContainsKey(steamIdText);
            bool jailed = JailSteamId(steamIdText, entry, reason, "Manual quarantine", false);
            if (jailed)
            {
                SendAntiCheatNotice(entry, steamIdText, "Host quarantine: " + reason + ". A jail move has been requested; chat is muted locally.", null, null);
            }
            string outcome = "Quarantine for " + steamIdText
                + " | " + muteOutcome
                + " | " + roleOutcome
                + " | jail=" + GetJailMovementDetail(steamIdText);
            RecordPlayerActivity(entry, steamIdText, outcome + ": " + CleanConfigText(reason, 96));
            ActionResult(guest && muted && jailed && muteSaved, outcome, outcome);
        }

        private void SetJailPointHere()
        {
            Vector3 target;
            if (!TryGetLocalJailFallback(out target))
            {
                ActionFail("Set jail point failed: local player position is not available");
                return;
            }

            _jailPosition = target;
            _warnedLegacyJailPosition = false;
            _nextJailTickUtc = DateTime.MinValue;
            SaveSettingsFromConsole("Jail point set to " + FormatVector(_jailPosition));
        }

        private void ToggleSelectedAntiCheatExempt()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Exemption changes require a selected player with a resolved Steam64 ID");
                return;
            }

            string steamIdText = steamId.ToString(CultureInfo.InvariantCulture);
            if (_antiCheatExemptPlayers.Remove(steamIdText))
            {
                bool saved = SaveConfig();
                string outcome = "Anti-cheat exemption removed for " + steamIdText + "; " + DescribeModerationSettingsSave(saved);
                RecordPlayerActivity(entry, steamIdText, outcome);
                ActionResult(saved, outcome, outcome);
                return;
            }

            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Allowed by host" : _adminReasonInput;
            RecordModerationEntry(_antiCheatExemptPlayers, steamIdText, entry, reason, "Anti-cheat exemption");
            bool addedSaved = SaveConfig();
            string addedOutcome = "Anti-cheat exemption added for " + steamIdText + "; " + DescribeModerationSettingsSave(addedSaved);
            RecordPlayerActivity(entry, steamIdText, addedOutcome + "; reason: " + CleanConfigText(reason, 96));
            ActionResult(addedSaved, addedOutcome, addedOutcome);
        }

        private void AnnounceCheatEnforcementRequest(PlayerEntry entry, string steamId, string reason, string source, bool banned)
        {
            if (!_serverGuardAnnouncements || !ShouldGlobalChatForJail(source))
            {
                return;
            }

            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            DateTime last;
            if (_globalCheatNoticeTimes.TryGetValue(steamId, out last) && now.Subtract(last).TotalSeconds < 60.0)
            {
                return;
            }
            _globalCheatNoticeTimes[steamId] = now;

            string label = BuildAutomationPlayerLabel(entry, steamId);
            string text = banned
                ? "Ban requested for " + label + " after repeated guard violations."
                : "Jail move requested for " + label + ": " + Shorten(CleanConfigText(reason, 120), 96);
            SendCodexBridgeBotChat("AntiCheat", text, out _);
        }

        private static bool ShouldGlobalChatForJail(string source)
        {
            source = source ?? "";
            if (source.IndexOf("manual", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            return source.IndexOf("anti-cheat", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("inventory audit", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("playerstate", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("codex bridge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TickMovementGuard()
        {
            _nextMovementSampleUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(0.25f, _movementSampleSeconds));
            if (!_disallowCheats || !_movementGuard || !HasHostSession())
            {
                return;
            }

            if (_players.Count == 0)
            {
                RefreshPlayers(false);
            }

            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerEntry entry = _players[i];
                if (entry == null || !entry.Online)
                {
                    continue;
                }
                string steamId = CleanSteamId(entry.SteamId);
                if (!LooksLikeSteamId(steamId)
                    || IsLocalPlayerEntry(entry)
                    || IsSteamIdAdminOrOwner(steamId, entry)
                    || IsSteamIdAntiCheatExempt(steamId)
                    || IsJailedSteamId(steamId))
                {
                    ResetMovementState(steamId);
                    continue;
                }

                MovementSignalState state = GetOrCreateMovementState(steamId);
                bool sampled = TryReadMovementSample(entry, ref state, out Vector3 position, out DateTime observedAt, out object playerState);
                _movementSignals[steamId] = state;
                if (!sampled) continue;
                if (!TryGetSampleMovementContext(entry, state, playerState, out MovementContext context)) continue;
                ObserveMovementTransition(state, context, observedAt, now);
                if (now < state.IgnoreUntilUtc)
                {
                    SetMovementBaseline(state, position, observedAt, "Movement grace: " + state.IgnoreReason);
                    state.LastReason = "allowed spike: " + state.IgnoreReason;
                    continue;
                }

                if (!state.HasSample || state.LastSampleUtc == DateTime.MinValue)
                {
                    SetMovementBaseline(state, position, observedAt, "Baseline; waiting for a second position");
                    continue;
                }

                double seconds = observedAt.Subtract(state.LastSampleUtc).TotalSeconds;
                if (seconds < 0.2 || seconds > 10.0)
                {
                    InvalidateMovementSample(state, "Sample interval outside 0.2-10s; baseline reset");
                    SetMovementBaseline(state, position, observedAt, state.LastSamplePhase);
                    continue;
                }

                Vector3 delta = position - state.LastPosition;
                float horizontal = (float)Math.Sqrt(delta.x * delta.x + delta.z * delta.z);
                float vertical = Math.Abs(delta.y);
                float distance = delta.magnitude;
                float horizontalSpeed = horizontal / (float)seconds;
                float verticalSpeed = vertical / (float)seconds;

                UpdateGliderMovementEvidence(state, delta, horizontalSpeed, verticalSpeed, distance, seconds);
                context = ApplyDescentMovementContext(context, state);
                state.LastHorizontalSpeed = horizontalSpeed;
                state.LastVerticalSpeed = verticalSpeed;
                state.LastDistance = distance;
                state.LastPosition = position;
                state.LastSampleUtc = observedAt;
                UpdateFootClimbEvidence(state, position, delta, context, seconds);
                state.LastContext = context.Label;
                state.LastContextEvidence = context.Evidence;
                state.LastEvaluationUtc = now;
                state.LastEvaluationSeconds = seconds;
                state.LastSamplePhase = "Evaluated";
                state.LastContextHorizontalLimit = Math.Max(10f, context.HorizontalLimit);
                state.LastContextVerticalLimit = Math.Max(20f, context.VerticalLimit);
                state.LastContextTeleportLimit = Math.Max(Math.Max(_teleportDistanceThreshold, context.TeleportBase),
                    state.LastContextHorizontalLimit * (float)seconds * Math.Max(2.5f, context.TeleportMultiplier));
                state.LastNetworkedSpeed = context.NetworkedSpeed;

                string reason;
                bool severe;
                if (!MovementLooksSuspicious(position, horizontalSpeed, verticalSpeed, distance, seconds, context, state, out reason, out severe))
                {
                    if (state.ConsecutiveViolations > 0)
                    {
                        state.ConsecutiveViolations--;
                    }
                    state.LastReason = "normal (" + context.Label + ")";
                    continue;
                }

                string learnedNote;
                string movementDetails = context.Label + " pos " + FormatVector(position)
                    + " h " + horizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                    + " v " + verticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                    + " d " + distance.ToString("0.#", CultureInfo.InvariantCulture);
                if (LearnedPolicySuppressesSelfAdmin(steamId, "MovementGuard " + context.Label, reason, movementDetails, out learnedNote))
                {
                    state.ConsecutiveViolations = 0;
                    state.LastReason = "learned allow: " + Shorten(learnedNote, 64);
                    RecordGuardEvidence(steamId, "Movement", "Host position samples", movementDetails,
                        CaptureMovementPolicy(state, severe ? 1 : Math.Max(1, _movementViolationThreshold)), "Held by learned policy: " + learnedNote);
                    AllowMovementSpike(steamId, Math.Max(4.0, (double)_movementSampleSeconds * 4.0), "learned " + learnedNote);
                    AppendEventLog("LEARN", Shorten("Movement held for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                    continue;
                }
                if (LearnedPolicyConfirmsCheat(steamId, "MovementGuard " + context.Label, reason, movementDetails, out learnedNote))
                {
                    severe = true;
                    reason = reason + " | learned confirmed pattern: " + Shorten(learnedNote, 80);
                }

                state.ConsecutiveViolations++;
                state.TotalViolations++;
                state.LastReason = reason;
                int threshold = severe ? 1 : Math.Max(1, _movementViolationThreshold);
                if (state.ConsecutiveViolations < threshold)
                {
                    RecordGuardEvidence(steamId, "Movement", "Host position samples", movementDetails,
                        CaptureMovementPolicy(state, threshold), "Observation " + state.ConsecutiveViolations.ToString(CultureInfo.InvariantCulture)
                        + "/" + threshold.ToString(CultureInfo.InvariantCulture) + ": " + reason);
                    LogInfo("Movement guard warning " + steamId + " " + reason
                        + " (" + state.ConsecutiveViolations.ToString(CultureInfo.InvariantCulture)
                        + "/" + threshold.ToString(CultureInfo.InvariantCulture) + ")");
                    continue;
                }

                RecordGuardEvidence(steamId, "Movement", "Host position samples", movementDetails,
                    CaptureMovementPolicy(state, threshold), "Review threshold reached: " + reason);
                state.ConsecutiveViolations = 0;
                string fullReason = reason + " | mode " + context.Label
                    + " | pos " + FormatVector(position)
                    + " | h " + horizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                    + "m/s v " + verticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                    + "m/s d " + distance.ToString("0.#", CultureInfo.InvariantCulture) + "m";
                AnnounceGuardBlock(entry, steamId, "MovementGuard", fullReason);
                if (ShouldGuardAutoJail())
                {
                    JailSteamId(steamId, entry, fullReason, "Anti-cheat MovementGuard", true);
                }
                else
                {
                    SendAntiCheatNotice(entry, steamId, "Anti-cheat: suspicious movement detected. " + reason, entry.BoltConnection, entry.BoltEntity);
                }
                LogWarning("Movement guard triggered for " + steamId + ": " + fullReason);
            }
        }

        private void TickMovementTelemetry()
        {
            DateTime now = DateTime.UtcNow;
            _nextMovementTelemetryUtc = GetSettingsDeadline(now, Math.Max(0.25f, _movementTelemetrySampleSeconds));
            if (_players.Count == 0)
            {
                RefreshPlayers(false);
            }

            StringBuilder rows = new StringBuilder();
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerEntry entry = _players[i];
                if (entry == null || !entry.Online)
                {
                    continue;
                }

                string steamId = CleanSteamId(entry.SteamId);
                if (!LooksLikeSteamId(steamId))
                {
                    continue;
                }

                MovementSignalState state = GetOrCreateMovementTelemetryState(steamId);
                bool sampled = TryReadMovementSample(entry, ref state, out Vector3 position, out DateTime observedAt, out object playerState);
                _movementTelemetrySignals[steamId] = state;
                if (!sampled) continue;
                if (!TryGetSampleMovementContext(entry, state, playerState, out MovementContext context)) continue;
                ObserveMovementTransition(state, context, observedAt, now);
                if (now < state.IgnoreUntilUtc)
                {
                    SetMovementBaseline(state, position, observedAt, "Movement grace: " + state.IgnoreReason);
                    state.LastReason = "allowed spike: " + state.IgnoreReason;
                    AppendMovementTelemetryRow(rows, now, entry, steamId, "grace", position, Vector3.zero, 0.0, 0f, 0f, 0f, context, state, false, false, state.IgnoreReason);
                    continue;
                }
                if (!state.HasSample || state.LastSampleUtc == DateTime.MinValue)
                {
                    SetMovementBaseline(state, position, observedAt, "Baseline; waiting for a second position");
                    AppendMovementTelemetryRow(rows, now, entry, steamId, "baseline", position, Vector3.zero, 0.0, 0f, 0f, 0f, context, state, false, false, "");
                    continue;
                }

                double seconds = observedAt.Subtract(state.LastSampleUtc).TotalSeconds;
                if (seconds < 0.2 || seconds > 10.0)
                {
                    InvalidateMovementSample(state, "Sample interval outside 0.2-10s; baseline reset");
                    SetMovementBaseline(state, position, observedAt, state.LastSamplePhase);
                    AppendMovementTelemetryRow(rows, now, entry, steamId, "resync", position, Vector3.zero, Math.Max(0.0, seconds), 0f, 0f, 0f, context, state, false, false, "sample gap");
                    continue;
                }

                Vector3 delta = position - state.LastPosition;
                float horizontal = (float)Math.Sqrt(delta.x * delta.x + delta.z * delta.z);
                float vertical = Math.Abs(delta.y);
                float distance = delta.magnitude;
                float horizontalSpeed = horizontal / (float)seconds;
                float verticalSpeed = vertical / (float)seconds;

                UpdateGliderMovementEvidence(state, delta, horizontalSpeed, verticalSpeed, distance, seconds);
                context = ApplyDescentMovementContext(context, state);
                state.LastHorizontalSpeed = horizontalSpeed;
                state.LastVerticalSpeed = verticalSpeed;
                state.LastDistance = distance;
                state.LastPosition = position;
                state.LastSampleUtc = observedAt;
                UpdateFootClimbEvidence(state, position, delta, context, seconds);
                state.LastContext = context.Label;
                state.LastContextEvidence = context.Evidence;
                state.LastEvaluationUtc = now;
                state.LastEvaluationSeconds = seconds;
                state.LastSamplePhase = "Evaluated";
                state.LastContextHorizontalLimit = context.HorizontalLimit;
                state.LastContextVerticalLimit = context.VerticalLimit;
                state.LastContextTeleportLimit = context.TeleportBase;
                state.LastNetworkedSpeed = context.NetworkedSpeed;

                string reason;
                bool severe;
                bool suspicious = MovementLooksSuspicious(position, horizontalSpeed, verticalSpeed, distance, seconds, context, state, out reason, out severe);
                state.LastReason = suspicious ? reason : "normal (" + context.Label + ")";
                AppendMovementTelemetryRow(rows, now, entry, steamId, "sample", position, delta, seconds, horizontalSpeed, verticalSpeed, distance, context, state, suspicious, severe, reason);
            }

            if (rows.Length > 0)
            {
                WriteMovementTelemetryRows(rows.ToString());
            }
        }

        private MovementSignalState GetOrCreateMovementTelemetryState(string steamId)
        {
            steamId = CleanSteamId(steamId);
            MovementSignalState state;
            if (!_movementTelemetrySignals.TryGetValue(steamId, out state) || state == null)
            {
                state = new MovementSignalState();
                state.SteamId = steamId;
                _movementTelemetrySignals[steamId] = state;
            }
            return state;
        }

        private void AppendMovementTelemetryRow(StringBuilder rows, DateTime now, PlayerEntry entry, string steamId, string phase, Vector3 position, Vector3 delta, double seconds, float horizontalSpeed, float verticalSpeed, float distance, MovementContext context, MovementSignalState state, bool suspicious, bool severe, string reason)
        {
            if (rows == null)
            {
                return;
            }
            context = context ?? MovementContext.Foot(_maxHorizontalSpeed, _maxVerticalSpeed, _teleportDistanceThreshold);
            float teleportLimit = Math.Max(_teleportDistanceThreshold, context.TeleportBase);
            float dynamicTeleport = Math.Max(teleportLimit, context.HorizontalLimit * (float)Math.Max(0.0, seconds) * Math.Max(2.5f, context.TeleportMultiplier));
            bool overHorizontal = seconds > 0.0 && horizontalSpeed > Math.Max(10f, context.HorizontalLimit);
            bool overVertical = seconds > 0.0 && verticalSpeed > Math.Max(20f, context.VerticalLimit);
            bool overDistance = distance > dynamicTeleport;

            rows.Append(now.ToString("o", CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(steamId)).Append(',')
                .Append(Csv(DisplayPlayerName(entry))).Append(',')
                .Append(Csv(phase)).Append(',')
                .Append(Csv(context.Label)).Append(',')
                .Append(FormatTelemetryFloat(position.x)).Append(',')
                .Append(FormatTelemetryFloat(position.y)).Append(',')
                .Append(FormatTelemetryFloat(position.z)).Append(',')
                .Append(FormatTelemetryFloat(delta.x)).Append(',')
                .Append(FormatTelemetryFloat(delta.y)).Append(',')
                .Append(FormatTelemetryFloat(delta.z)).Append(',')
                .Append(seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(FormatTelemetryFloat(horizontalSpeed)).Append(',')
                .Append(FormatTelemetryFloat(verticalSpeed)).Append(',')
                .Append(FormatTelemetryFloat(distance)).Append(',')
                .Append(FormatTelemetryFloat(context.NetworkedSpeed)).Append(',')
                .Append(FormatTelemetryFloat(context.HorizontalLimit)).Append(',')
                .Append(FormatTelemetryFloat(context.VerticalLimit)).Append(',')
                .Append(FormatTelemetryFloat(context.TeleportBase)).Append(',')
                .Append(FormatTelemetryFloat(dynamicTeleport)).Append(',')
                .Append(overHorizontal ? "true" : "false").Append(',')
                .Append(overVertical ? "true" : "false").Append(',')
                .Append(overDistance ? "true" : "false").Append(',')
                .Append(state == null ? "0" : FormatTelemetryFloat(state.FootClimbUpMeters)).Append(',')
                .Append(state == null ? "0" : FormatTelemetryFloat(state.FootClimbNetGain)).Append(',')
                .Append(state == null ? "0" : FormatTelemetryFloat(state.FootClimbWindowAgeSeconds)).Append(',')
                .Append(state == null ? "0" : state.GliderLikeSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(state == null || state.IgnoreUntilUtc == DateTime.MinValue ? "" : state.IgnoreUntilUtc.ToString("o", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(state == null ? "" : state.IgnoreReason)).Append(',')
                .Append(suspicious ? "true" : "false").Append(',')
                .Append(severe ? "true" : "false").Append(',')
                .Append(Csv(reason))
                .Append(Environment.NewLine);
        }

        private void WriteMovementTelemetryRows(string rows)
        {
            if (String.IsNullOrEmpty(rows))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(_modDir);
                string path = Path.Combine(_modDir, "movement-telemetry.csv");
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    File.AppendAllText(path, MovementTelemetryHeader() + Environment.NewLine);
                }
                File.AppendAllText(path, rows);
            }
            catch (Exception ex)
            {
                LogWarning("Movement telemetry write failed: " + ex.Message);
            }
        }

        private static string MovementTelemetryHeader()
        {
            return "utc,steam_id,name,phase,context,pos_x,pos_y,pos_z,delta_x,delta_y,delta_z,seconds,horizontal_mps,vertical_mps,distance_m,networked_mps,limit_horizontal_mps,limit_vertical_mps,teleport_base_m,dynamic_teleport_m,over_horizontal,over_vertical,over_distance,foot_climb_up_m,foot_climb_net_m,foot_climb_window_s,glider_samples,ignore_until_utc,ignore_reason,suspicious,severe,reason";
        }

        private static string FormatTelemetryFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void TickAdminControlMode()
        {
            string mode = GetAdminControlMode();
            if (String.Equals(mode, _lastAdminControlMode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _lastAdminControlMode = mode;
            AppendEventLog("SYSTEM", "Admin control mode: " + mode);
            LogInfo("Admin control mode: " + mode);
            _nextCodexAutomationStateWriteUtc = DateTime.MinValue;
        }

        private string GetAdminControlMode()
        {
            if (_codexBridgeEnabled && IsCodexAdminActive())
            {
                return "Codex";
            }
            if (_selfAdminFallback)
            {
                return "Self";
            }
            return "Manual";
        }

        private bool IsCodexAdminActive()
        {
            if (!_codexBridgeEnabled || !_externalAiReviewEnabled || _lastCodexBridgeCommandUtc == DateTime.MinValue)
            {
                return false;
            }
            return DateTime.UtcNow.Subtract(_lastCodexBridgeCommandUtc).TotalSeconds <= Math.Max(30f, _codexAdminQuietSeconds);
        }

        private bool ShouldGuardAutoJail()
        {
            if (!_autoJailCheaters)
            {
                return false;
            }
            if (!_codexBridgeEnabled || !_externalAiReviewEnabled)
            {
                return true;
            }
            return _selfAdminFallback && !IsCodexAdminActive();
        }

        private bool IsLearnedSelfAdminActive()
        {
            if (!_learnedSelfAdminPolicyEnabled || !_selfAdminFallback)
            {
                return false;
            }
            if (!_codexBridgeEnabled || !_externalAiReviewEnabled)
            {
                return true;
            }
            return !IsCodexAdminActive();
        }

        private void TickLearnedSelfAdminPolicy(bool force)
        {
            DateTime now = DateTime.UtcNow;
            _nextLearnedPolicyRefreshUtc = GetSettingsDeadline(now, Math.Max(5f, _learnedPolicyRefreshSeconds));
            if (!_learnedSelfAdminPolicyEnabled)
            {
                return;
            }

            try
            {
                bool hasMemoryFile = File.Exists(_automationMemoryPath);
                long memoryLength = 0L;
                DateTime memoryWriteUtc = DateTime.MinValue;
                FileInfo info = null;
                if (hasMemoryFile)
                {
                    info = new FileInfo(_automationMemoryPath);
                    memoryLength = info.Length;
                    memoryWriteUtc = info.LastWriteTimeUtc;
                }

                if (!force && memoryLength == _learnedPolicyMemoryLength && memoryWriteUtc == _learnedPolicyMemoryWriteUtc && _learnedSelfAdminPolicy.RulesLoaded > 0)
                {
                    return;
                }

                LearnedSelfAdminPolicy next = new LearnedSelfAdminPolicy();
                AddBuiltInLearnedPolicyRules(next);
                string[] lines = hasMemoryFile ? File.ReadAllLines(_automationMemoryPath) : new string[0];
                next.MemoryLineCount = lines == null ? 0 : lines.Length;
                int start = lines == null ? 0 : Math.Max(0, lines.Length - 700);
                for (int i = start; lines != null && i < lines.Length; i++)
                {
                    ApplyLearnedMemoryLine(next, lines[i]);
                }
                next.LastLoadedUtc = now;
                next.FinalizeSummary();

                _learnedSelfAdminPolicy.CopyFrom(next);
                _learnedPolicyMemoryLength = memoryLength;
                _learnedPolicyMemoryWriteUtc = memoryWriteUtc;
                _nextCodexAutomationStateWriteUtc = DateTime.MinValue;
                if (_learnedSelfAdminPolicy.RulesLoaded > 0)
                {
                    AppendEventLog("LEARN", Shorten("Loaded self-admin policy: " + _learnedSelfAdminPolicy.Summary, 140));
                }
            }
            catch (Exception ex)
            {
                LogWarning("Learned self-admin policy load failed: " + ex.Message);
            }
        }

        private void AddBuiltInLearnedPolicyRules(LearnedSelfAdminPolicy policy)
        {
            if (policy == null)
            {
                return;
            }

            string builtIn = "Modstoke Admin built-in lesson";
            string[] falsePositiveTokens = new string[]
            {
                "pickupglider", "airborne descent", "descent pattern",
                "golf cart", "golfcart", "snow golf cart", "snowgolfcart", "knightv", "knight v",
                "zipline", "raft", "setplayerspawnpoint", "playeractorspawnposition", "trapgrabcaptured", "vailrevive", "deathmarkerrequest", "camp capture", "cannibal camp", "tied",
                "chair", "stick chair", "dismantle", "deconstruct", "salvage",
                "stickpickup", "stonepickup", "logpickup", "firewood"
            };
            for (int i = 0; i < falsePositiveTokens.Length; i++)
            {
                AddLearnedPolicyRule(policy, "false_positive", "", falsePositiveTokens[i], "built_in_false_positive", builtIn, false);
            }

            string[] allowEventTokens = new string[]
            {
                "chair", "stick chair", "dismantle", "deconstruct", "salvage", "stickpickup", "stonepickup", "logpickup", "firewood", "pickupglider"
            };
            for (int i = 0; i < allowEventTokens.Length; i++)
            {
                AddLearnedPolicyRule(policy, "allow_event", "", allowEventTokens[i], "built_in_allow_event", builtIn, false);
            }

            string[] confirmedTokens = new string[]
            {
                "wemod", "speed hack", "possible speed hack", "quadruple speed", "infinite jump", "vertical movement",
                "foot climb", "noclip", "below allowed world floor", "unauthorized teleport", "teleport jump",
                "item burst", "item amount over limit", "mass item", "spawn loot", "artifact", "high-value gear",
                "printed sled", "printedsled", "golf cart spawn", "vehicle spawn"
            };
            for (int i = 0; i < confirmedTokens.Length; i++)
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", "", confirmedTokens[i], "built_in_confirmed_cheat", builtIn, false);
            }
        }

        private void ApplyLearnedMemoryLine(LearnedSelfAdminPolicy policy, string line)
        {
            if (policy == null || String.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (!TryReadLearnedMemoryLine(line, out string category, out string note, out string steamId))
            {
                return;
            }
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                steamId = ExtractFirstSteamId(category + " " + note);
            }

            bool validRule = TryReadLearnedRuleFields(note, out bool hasFields, out string action, out string taggedToken);
            if (hasFields)
            {
                if (!validRule)
                {
                    return;
                }
                string[] tokens = taggedToken.Split(new char[] { ',', '+' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    AddLearnedPolicyRule(policy, action, steamId, tokens[i], category, note, true);
                    if (action == "allow_event")
                    {
                        AddLearnedPolicyRule(policy, "false_positive", steamId, tokens[i], category, note, true);
                    }
                }
                return;
            }

            string combined = (category + " " + note).ToLowerInvariant();
            if (String.IsNullOrWhiteSpace(combined))
            {
                return;
            }
            bool falsePositive = ContainsLearnedPhrase(combined, new string[]
            {
                "false positive", "false_positive", "not cheating", "legitimate", "normal play", "normal gameplay",
                "allowed", "allowlist", "safe behavior", "known good", "allow_event", "allow event", "allowevent",
                "do not block", "dont block", "don't block", "unblock event"
            });
            bool confirmed = ContainsLearnedPhrase(combined, new string[] { "confirmed cheat", "confirmed_cheat", "actual cheat", "cheating confirmed", "wemod", "speed hack", "speedhack", "infinite jump", "noclip", "mass item", "mass spawn", "unauthorized teleport" });

            if (falsePositive)
            {
                AddLearnedTokenRules(policy, "false_positive", steamId, category, note, combined, new string[]
                {
                    "glider", "hang glider", "hangglider", "pickupglider",
                    "golf cart", "golfcart", "snow golf cart", "snowgolfcart",
                    "knightv", "knight v", "zipline", "raft", "vehicle",
                    "spawn", "join", "death", "died", "revive", "camp capture", "cannibal camp", "tied",
                    "chair", "stick chair", "dismantle", "deconstruct", "salvage", "construction",
                    "stickpickup", "stonepickup", "logpickup", "firewood"
                }, false);
            }

            if (confirmed)
            {
                AddConfirmedCheatTokenRules(policy, steamId, category, note, combined);
            }
        }

        private void AddConfirmedCheatTokenRules(LearnedSelfAdminPolicy policy, string steamId, string category, string note, string combined)
        {
            if (policy == null || String.IsNullOrEmpty(combined))
            {
                return;
            }

            if (ContainsLearnedPhrase(combined, new string[] { "wemod", "speed hack", "speedhack", "quadruple speed" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "speed hack", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "possible speed hack", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "infinite jump", "jump hack", "fly hack" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "vertical movement", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "foot climb", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "above allowed world", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "noclip", "no clip" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "noclip", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "below allowed world floor", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "unauthorized teleport", "teleport hack" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "unauthorized teleport", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "teleport jump", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "mass item", "mass spawn", "item spam", "spawn drop", "spawned item" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "item burst", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "item amount over limit", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "spawn loot", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "artifact", "late game gear", "gun", "guns" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "artifact", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "high-value gear", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "printed sled", "3d printed sled", "printed_sled" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "printed sled", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "printedsled", category, note, false);
            }
            if (ContainsLearnedPhrase(combined, new string[] { "golf cart spawn", "spawn golf cart", "spawned golf cart" }))
            {
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "golfcart", category, note, false);
                AddLearnedPolicyRule(policy, "confirmed_cheat", steamId, "vehicle spawn", category, note, false);
            }
        }

        private void AddLearnedTokenRules(LearnedSelfAdminPolicy policy, string action, string steamId, string category, string note, string combined, string[] tokens, bool structured)
        {
            if (policy == null || tokens == null || String.IsNullOrEmpty(combined))
            {
                return;
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (!String.IsNullOrEmpty(token) && combined.IndexOf(token.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    AddLearnedPolicyRule(policy, action, steamId, token, category, note, structured);
                }
            }
        }

        private void AddLearnedPolicyRule(LearnedSelfAdminPolicy policy, string action, string steamId, string token, string category, string note, bool structured)
        {
            if (policy == null)
            {
                return;
            }
            token = CleanConfigText(token, 80);
            string normalized = NormalizeGuardText(token);
            if (normalized.Length < 3)
            {
                return;
            }

            LearnedRule rule = new LearnedRule();
            rule.Action = CleanConfigText(action, 32);
            rule.SteamId = CleanSteamId(steamId);
            rule.Token = token;
            rule.NormalizedToken = normalized;
            rule.Category = CleanConfigText(category, 64);
            rule.Note = CleanConfigText(note, 180);
            rule.Structured = structured;
            rule.AddedUtc = DateTime.UtcNow;
            policy.AddRule(rule);
        }

        private bool LearnedPolicySuppressesSelfAdmin(string steamId, string area, string reason, string details, out string note)
        {
            note = "";
            if (!IsLearnedSelfAdminActive())
            {
                return false;
            }
            return TryMatchLearnedRules(_learnedSelfAdminPolicy.FalsePositiveRules, steamId, area, reason, details, out note);
        }

        private bool LearnedPolicyAllowsBlockedEvent(string steamId, string eventName, string reason, string details, out string note)
        {
            note = "";
            if (!IsLearnedSelfAdminActive())
            {
                return false;
            }
            return TryMatchLearnedRules(_learnedSelfAdminPolicy.AllowEventRules, steamId, eventName, reason, details, out note);
        }

        private bool LearnedPolicyConfirmsCheat(string steamId, string area, string reason, string details, out string note)
        {
            note = "";
            if (!IsLearnedSelfAdminActive())
            {
                return false;
            }
            return TryMatchLearnedRules(_learnedSelfAdminPolicy.ConfirmedCheatRules, steamId, area, reason, details, out note);
        }

        private bool TryMatchLearnedRules(List<LearnedRule> rules, string steamId, string area, string reason, string details, out string note)
        {
            note = "";
            if (rules == null || rules.Count == 0)
            {
                return false;
            }

            steamId = CleanSteamId(steamId);
            string haystack = NormalizeGuardText((area ?? "") + " " + (reason ?? "") + " " + (details ?? ""));
            if (String.IsNullOrEmpty(haystack))
            {
                return false;
            }

            for (int i = rules.Count - 1; i >= 0; i--)
            {
                LearnedRule rule = rules[i];
                if (rule == null || String.IsNullOrEmpty(rule.NormalizedToken))
                {
                    continue;
                }
                if (!String.IsNullOrEmpty(rule.SteamId) && !String.Equals(rule.SteamId, steamId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (haystack.IndexOf(rule.NormalizedToken, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                note = String.IsNullOrEmpty(rule.Note)
                    ? rule.Token
                    : Shorten(rule.Note, 120);
                return true;
            }
            return false;
        }

        private void AppendLearnedPolicyJson(StringBuilder sb)
        {
            if (sb == null)
            {
                return;
            }
            sb.Append("\"learned_policy\":{");
            sb.Append("\"enabled\":").Append(_learnedSelfAdminPolicyEnabled ? "true" : "false").Append(',');
            sb.Append("\"active\":").Append(IsLearnedSelfAdminActive() ? "true" : "false").Append(',');
            sb.Append("\"refresh_seconds\":").Append(_learnedPolicyRefreshSeconds.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"memory_lines\":").Append(_learnedSelfAdminPolicy.MemoryLineCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"rules_loaded\":").Append(_learnedSelfAdminPolicy.RulesLoaded.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"false_positive_rules\":").Append(_learnedSelfAdminPolicy.FalsePositiveRules.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"allow_event_rules\":").Append(_learnedSelfAdminPolicy.AllowEventRules.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"confirmed_cheat_rules\":").Append(_learnedSelfAdminPolicy.ConfirmedCheatRules.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"last_loaded_utc\":\"").Append(_learnedSelfAdminPolicy.LastLoadedUtc == DateTime.MinValue ? "" : JsonEscape(_learnedSelfAdminPolicy.LastLoadedUtc.ToString("o", CultureInfo.InvariantCulture))).Append("\",");
            sb.Append("\"summary\":\"").Append(JsonEscape(_learnedSelfAdminPolicy.Summary)).Append("\"},");
        }

        private static bool ContainsLearnedPhrase(string text, string[] phrases)
        {
            if (String.IsNullOrEmpty(text) || phrases == null)
            {
                return false;
            }
            for (int i = 0; i < phrases.Length; i++)
            {
                string phrase = phrases[i];
                if (!String.IsNullOrEmpty(phrase) && text.IndexOf(phrase.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string ExtractFirstSteamId(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }

            int start = -1;
            int length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsDigit(text[i]))
                {
                    if (start < 0)
                    {
                        start = i;
                        length = 0;
                    }
                    length++;
                    if (length >= 17)
                    {
                        string candidate = CleanSteamId(text.Substring(start, length));
                        if (LooksLikeSteamId(candidate))
                        {
                            return candidate;
                        }
                    }
                    continue;
                }
                start = -1;
                length = 0;
            }
            return "";
        }

        private static string Csv(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return "";
            }
            value = value.Replace("\r", " ").Replace("\n", " ");
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private void TickCodexBridge()
        {
            _nextCodexBridgePollUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(1f, _codexBridgePollSeconds));
            string inboxPath = Path.Combine(_modDir, "codex-command-inbox.txt");
            try
            {
                Directory.CreateDirectory(_modDir);
                if (!File.Exists(inboxPath))
                {
                    return;
                }
                string[] lines = File.ReadAllLines(inboxPath);
                if (lines == null || lines.Length == 0)
                {
                    return;
                }
                File.WriteAllText(inboxPath, "");
                for (int i = 0; i < lines.Length; i++)
                {
                    ProcessCodexBridgeCommand(lines[i]);
                }
            }
            catch (Exception ex)
            {
                LogWarning("Codex bridge poll failed: " + ex.Message);
            }
        }

        private void ProcessCodexBridgeCommand(string line)
        {
            string raw = CleanBridgeText(line, 512);
            if (String.IsNullOrEmpty(raw) || raw.StartsWith("#", StringComparison.Ordinal))
            {
                return;
            }

            _lastCodexBridgeCommandUtc = DateTime.UtcNow;
            string[] parts = SplitBridgeCommand(raw);
            string action = parts.Length > 0 ? CleanConfigText(parts[0], 32).ToLowerInvariant() : "";
            bool textCommand = IsBridgeTextCommand(action);
            string firstArg = parts.Length > 1 ? CleanBridgeText(parts[1], 220) : "";
            string steamId = !textCommand && parts.Length > 1 ? CleanSteamId(parts[1]) : "";
            string reason = textCommand ? firstArg
                : parts.Length > 2 ? CleanBridgeText(parts[2], 220) : "";
            if (String.IsNullOrEmpty(reason) && !textCommand && action != "tell" && action != "dm" && action != "message")
            {
                reason = "Bridge review";
            }

            bool ok = false;
            string message = "";
            PlayerEntry entry = LooksLikeSteamId(steamId) ? FindPlayerBySteamId(steamId) : null;
            try
            {
                if ((action == "release" || action == "unjail" || action == "releaseall" || action == "clearjails"
                    || action == "jail" || action == "quarantine" || action == "unban") && !HasHostSession())
                {
                    message = "Action requires an active multiplayer host session";
                }
                else if (action == "say" || action == "announce" || action == "broadcast" || action == "chat" || action == "localchat" || action == "hostchat" || action == "acchat" || action == "anticheat")
                {
                    ok = SendCodexBridgeLocalChat(reason, action == "acchat" || action == "anticheat", out message);
                }
                else if (action == "botchat" || action == "aichat" || action == "systemchat")
                {
                    string botName = String.IsNullOrEmpty(firstArg) ? "AntiCheat" : firstArg;
                    string botMessage = parts.Length > 2 ? CleanBridgeText(parts[2], 220) : "";
                    if (String.IsNullOrEmpty(botMessage))
                    {
                        botMessage = "Server moderation is active.";
                    }
                    ok = SendCodexBridgeBotChat(botName, botMessage, out message);
                }
                else if (action == "notice" || action == "adminnotice")
                {
                    ok = SendCodexBridgeNotice(reason, out message);
                }
                else if (action == "warn" || action == "review" || action == "suspect")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        string warning = BuildAutomationWarningMessage(entry, steamId, reason);
                        ok = SendCodexBridgeLocalChat(warning, true, out string warningOutcome);
                        if (ok)
                        {
                            RecordPlayerActivity(entry, steamId, "Automation warning dispatch: " + reason);
                        }
                        message = "Automation warning for " + steamId + ": " + warningOutcome;
                    }
                    else
                    {
                        message = "Warning ignored: invalid SteamID";
                    }
                }
                else if (action == "tell" || action == "dm" || action == "message")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = SendCodexBridgeTell(entry, steamId, reason, out string noticeOutcome);
                        message = "Direct message for " + steamId + ": " + noticeOutcome;
                    }
                    else
                    {
                        message = "Direct message ignored: invalid SteamID";
                    }
                }
                else if (action == "flag" || action == "watch" || action == "note")
                {
                    ok = LooksLikeSteamId(steamId);
                    message = ok ? "Flagged " + steamId + ": " + reason : "Flag ignored: invalid SteamID";
                    if (ok)
                    {
                        AppendEventLog("CODEX", Shorten(message, 140));
                        RecordPlayerActivity(entry, steamId, "Automation flag: " + reason);
                    }
                }
                else if (action == "learn" || action == "memory" || action == "skill")
                {
                    string category = String.IsNullOrEmpty(firstArg) || LooksLikeSteamId(steamId) ? "general" : firstArg;
                    ok = AppendCodexAutomationMemory(category, reason, steamId, raw);
                    message = ok ? "Automation memory saved" : "Automation memory failed";
                }
                else if (action == "heartbeat" || action == "ping" || action == "noop")
                {
                    ok = WriteCodexAutomationState(true);
                    message = "Codex bridge heartbeat received";
                }
                else if (action == "exempt" || action == "allow" || action == "notcheating")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        RecordModerationEntry(_antiCheatExemptPlayers, steamId, entry, reason, "Codex bridge");
                        ok = SaveConfig();
                        message = "Anti-cheat exemption added for " + steamId + "; " + DescribeModerationSettingsSave(ok);
                        RecordPlayerActivity(entry, steamId, message + "; reason: " + CleanConfigText(reason, 96));
                    }
                    else
                    {
                        message = "Exempt ignored: invalid SteamID";
                    }
                }
                else if (action == "unexempt" || action == "removeexempt")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = _antiCheatExemptPlayers.Remove(steamId);
                        bool saved = SaveConfig();
                        message = ok ? "Anti-cheat exemption removed for " + steamId : "No anti-cheat exemption for " + steamId;
                        message += "; " + DescribeModerationSettingsSave(saved);
                        RecordPlayerActivity(entry, steamId, message + "; reason: " + CleanConfigText(reason, 96));
                        ok = ok && saved;
                    }
                    else
                    {
                        message = "Unexempt ignored: invalid SteamID";
                    }
                }
                else if (action == "release" || action == "unjail")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = ReleaseJailedSteamId(steamId, false, out _, out message);
                    }
                    else
                    {
                        message = "Release ignored: invalid SteamID";
                    }
                }
                else if (action == "releaseall" || action == "clearjails")
                {
                    ok = ReleaseAllJailedPlayers(true, out message);
                }
                else if (action == "jail")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = JailSteamId(steamId, entry, reason, "Codex bridge", true);
                        message = "Jail " + steamId + ": " + GetJailMovementDetail(steamId);
                    }
                    else
                    {
                        message = "Jail ignored: invalid SteamID";
                    }
                }
                else if (action == "quarantine")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ulong parsed;
                        bool parsedOk = UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
                        string roleOutcome = "Guest request not sent: invalid Steam64 ID";
                        bool guest = parsedOk && DemotePlayerToGuestForAdminAction(parsed, out roleOutcome);
                        SetSteamIdMuted(steamId, entry, true, out string muteOutcome);
                        bool jailed = JailSteamId(steamId, entry, reason, "Codex bridge quarantine", true);
                        ok = jailed && guest && IsSteamIdMuted(steamId);
                        message = "Quarantine " + (ok ? "sent" : "incomplete") + " for " + steamId
                            + " | " + muteOutcome
                            + " | " + roleOutcome
                            + " jail=" + GetJailMovementDetail(steamId);
                    }
                    else
                    {
                        message = "Quarantine ignored: invalid SteamID";
                    }
                }
                else if (action == "kick")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = KickSteamIdFromBridge(steamId, entry, reason, out message);
                    }
                    else
                    {
                        message = "Kick ignored: invalid SteamID";
                    }
                }
                else if (action == "ban")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = BanSteamIdFromBridge(steamId, entry, reason, out message);
                    }
                    else
                    {
                        message = "Ban ignored: invalid SteamID";
                    }
                }
                else if (action == "unban")
                {
                    ulong parsed;
                    if (LooksLikeSteamId(steamId) && UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        ok = UnbanSteamId(parsed, reason, out message);
                    }
                    else
                    {
                        message = "Unban ignored: invalid SteamID";
                    }
                }
                else if (action == "mute")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        SetSteamIdMuted(steamId, entry, true, out message);
                        ok = IsSteamIdMuted(steamId);
                    }
                    else
                    {
                        message = "Mute ignored: invalid SteamID";
                    }
                }
                else if (action == "unmute")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        SetSteamIdMuted(steamId, entry, false, out message);
                        ok = !IsSteamIdMuted(steamId);
                    }
                    else
                    {
                        message = "Unmute ignored: invalid SteamID";
                    }
                }
                else if (action == "audit" || action == "inventoryaudit")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = RequestInventoryAuditFromBridge(steamId, entry);
                        message = ok ? "Inventory audit requested for " + steamId : "Inventory audit failed for " + steamId;
                    }
                    else
                    {
                        message = "Inventory audit ignored: invalid SteamID";
                    }
                }
                else if (action == "guest" || action == "trusted" || action == "admin")
                {
                    if (LooksLikeSteamId(steamId))
                    {
                        ok = SetSteamIdRoleFromBridge(steamId, action, reason, out message);
                    }
                    else
                    {
                        message = "Role change ignored: invalid SteamID";
                    }
                }
                else if (action == "role")
                {
                    string role = CleanConfigText(reason, 32).ToLowerInvariant();
                    if (LooksLikeSteamId(steamId) && (role == "guest" || role == "trusted" || role == "admin"))
                    {
                        ok = SetSteamIdRoleFromBridge(steamId, role, "Codex role command", out message);
                    }
                    else
                    {
                        message = "Role change ignored: use role|steam64|guest, role|steam64|trusted or role|steam64|admin";
                    }
                }
                else if (action == "grace" || action == "movementgrace")
                {
                    float seconds = 12f;
                    if (!String.IsNullOrWhiteSpace(reason)
                        && (!Single.TryParse(reason, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                            || Single.IsNaN(seconds) || Single.IsInfinity(seconds) || seconds <= 0f))
                    {
                        message = "Movement grace ignored: seconds must be a finite positive number";
                    }
                    else if (LooksLikeSteamId(steamId))
                    {
                        AllowMovementSpike(steamId, Math.Max(1f, seconds), "Codex bridge grace");
                        ok = true;
                        message = "Movement grace " + Math.Max(1f, seconds).ToString("0.#", CultureInfo.InvariantCulture) + "s for " + steamId;
                    }
                    else
                    {
                        message = "Movement grace ignored: invalid SteamID";
                    }
                }
                else if (action == "snapshot" || action == "state")
                {
                    ok = WriteCodexAutomationState(true);
                    message = ok ? "Automation state snapshot written" : "Automation state snapshot failed";
                }
                else if (action == "reload")
                {
                    ok = LoadConfig();
                    if (ok)
                    {
                        bool exported = WriteCodexAutomationState(true);
                        message = exported ? "Config reloaded" : "Config reloaded; automation snapshot write failed";
                    }
                    else
                    {
                        message = _settingsProblem;
                    }
                }
                else
                {
                    message = "Unknown Codex bridge command: " + action;
                }
            }
            catch (Exception ex)
            {
                ok = false;
                message = "Command failed: " + ex.Message;
            }

            if ((action == "ban" || action == "unban" || action == "mute" || action == "unmute")
                && !String.IsNullOrEmpty(_settingsProblem))
            {
                message += "; " + _settingsProblem;
            }
            AppendCodexBridgeResult(action, steamId, ok, message, raw);
            if (!String.IsNullOrEmpty(message))
            {
                LogInfo("Codex bridge result: " + message);
            }
        }

        private bool SendCodexBridgeLocalChat(string message, bool antiCheatPrefix, out string outcome)
        {
            outcome = "No chat was sent: an active multiplayer host session is required.";
            if (!HasHostSession()) return false;
            bool shortened = (message ?? "").Length > 180;
            message = CleanBridgeText(message, 180);
            if (String.IsNullOrEmpty(message))
            {
                outcome = "No chat was sent: the message is empty.";
                return false;
            }

            string text = message;
            if (antiCheatPrefix
                && !text.StartsWith("AntiCheat:", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("[AntiCheat]", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("[OpenACAI Guard]", StringComparison.OrdinalIgnoreCase))
            {
                text = "AntiCheat: " + text;
            }

            bool sent = SendLocalChatEvent(text, out bool sendAttempted, out outcome);
            if (!sent && !sendAttempted)
            {
                string chatOutcome = outcome;
                sent = SendCodexBridgeNotice(text, out string noticeOutcome);
                outcome = chatOutcome + " Native notice fallback: " + noticeOutcome;
            }
            if (shortened) outcome += " Text was shortened to fit the chat limit.";
            AppendEventLog("CODEX", Shorten("Chat request: " + text, 140));
            return sent;
        }

        private bool SendCodexBridgeBotChat(string botName, string message, out string outcome)
        {
            bool shortenedName = (botName ?? "").Length > 32;
            botName = CleanBridgeText(botName, 32);
            if (String.IsNullOrEmpty(botName))
            {
                botName = "AntiCheat";
            }
            if (String.IsNullOrEmpty(message))
            {
                outcome = "No named announcement was sent: the message is empty.";
                return false;
            }
            // The game's sender is still the local player. The label belongs in the message body.
            bool sent = SendCodexBridgeLocalChat(botName + ": " + message, false, out outcome);
            if (shortenedName) outcome += " The announcement label was shortened.";
            return sent;
        }

        private bool SendCodexBridgeNotice(string message, out string outcome)
        {
            outcome = "No notice was sent: an active multiplayer host session is required.";
            if (!HasHostSession()) return false;
            bool shortened = (message ?? "").Length > 180;
            message = CleanBridgeText(message, 180);
            if (String.IsNullOrEmpty(message))
            {
                outcome = "No notice was sent: the message is empty.";
                return false;
            }
            string text = "[Host] " + message;
            AppendEventLog("CODEX", Shorten("Notice request: " + message, 140));
            bool shown = InvokeStaticMethod("CoopAdminCommand", "SendLocalMessage", new object[] { text });
            bool sent = InvokeStaticMethod("CoopAdminCommand", "SendNetworkMessageAll", new object[] { text });
            outcome = "Host display call " + (shown ? "returned" : "is unconfirmed")
                + "; broadcast call " + (sent ? "returned" : "is unconfirmed")
                + ". Delivery and display are unconfirmed; neither call was retried.";
            if (shortened) outcome += " Text was shortened to fit the notice limit.";
            return shown && sent;
        }

        private string BuildAutomationWarningMessage(PlayerEntry entry, string steamId, string reason)
        {
            string label = BuildAutomationPlayerLabel(entry, steamId);
            return "AntiCheat: " + label + " is under review: " + CleanBridgeText(reason, 120);
        }

        private string BuildAutomationPlayerLabel(PlayerEntry entry, string steamId)
        {
            string name = entry == null ? "" : DisplayPlayerName(entry);
            steamId = CleanSteamId(steamId);
            if (String.IsNullOrEmpty(name) || String.Equals(name, steamId, StringComparison.OrdinalIgnoreCase))
            {
                name = "Player " + LastSteamDigits(steamId, 4);
            }
            if (LooksLikeSteamId(steamId))
            {
                return Shorten(name, 32) + " [" + LastSteamDigits(steamId, 4) + "]";
            }
            return Shorten(name, 32);
        }

        private static string LastSteamDigits(string steamId, int count)
        {
            steamId = CleanSteamId(steamId);
            if (String.IsNullOrEmpty(steamId))
            {
                return "????";
            }
            count = Math.Max(1, Math.Min(count, steamId.Length));
            return steamId.Substring(steamId.Length - count);
        }

        private bool SendCodexBridgeTell(PlayerEntry entry, string steamId, string message, out string outcome)
        {
            outcome = "No notice was sent: invalid recipient or empty message.";
            steamId = CleanSteamId(steamId);
            bool shortened = (message ?? "").Length > 180;
            message = CleanBridgeText(message, 180);
            if (!LooksLikeSteamId(steamId) || String.IsNullOrEmpty(message))
            {
                return false;
            }
            if (!HasHostSession())
            {
                outcome = "No notice was sent: an active multiplayer host session is required.";
                return false;
            }

            string text = FormatLoaderNotice("[Host] " + message, out bool byteLimitReached);
            shortened |= byteLimitReached;
            bool accepted;
            if (String.Equals(steamId, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                accepted = InvokeStaticMethod("CoopAdminCommand", "SendLocalMessage", new object[] { text });
                outcome = accepted ? "Local host display call returned; rendering and reading are unconfirmed."
                    : "Local host display could not be confirmed; no retry or network send was attempted.";
                RecordModerationNotice(steamId, "Bridge direct message", "Local notice: " + text, outcome);
            }
            else if (!TrySendLoaderNotice(steamId, text, "Bridge direct message", out accepted, out outcome))
            {
                // Only an unavailable SDK route permits native dispatch. Revalidate cached handles there.
                if (!HasHostSession())
                {
                    outcome = "No native notice was sent: host authority is unavailable.";
                    return false;
                }
                accepted = SendNativeModerationNotice(steamId, text, "Bridge direct message", entry?.BoltConnection, entry?.BoltEntity);
                outcome = accepted ? "Native dispatch returned; delivery and display are unconfirmed."
                    : "Native dispatch could not be confirmed; no second send was attempted.";
            }
            if (shortened) outcome += " Text was shortened to fit the notice limit.";
            if (accepted) AppendEventLog("CODEX", Shorten("Tell request " + steamId + " " + text, 140));
            return accepted;
        }

        private bool KickSteamIdFromBridge(string steamId, PlayerEntry entry, string reason, out string outcome)
        {
            outcome = "Kick not sent: invalid Steam64 ID";
            steamId = CleanSteamId(steamId);
            ulong parsed;
            if (!LooksLikeSteamId(steamId) || !UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false;
            return SendKick(parsed, entry ?? FindPlayerBySteamId(steamId),
                String.IsNullOrEmpty(reason) ? "Removed by automation" : reason, out outcome);
        }

        private bool BanSteamIdFromBridge(string steamId, PlayerEntry entry, string reason, out string outcome)
        {
            outcome = "Ban not attempted: invalid Steam64 ID";
            steamId = CleanSteamId(steamId);
            ulong parsed;
            if (!LooksLikeSteamId(steamId) || !UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }
            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            reason = String.IsNullOrEmpty(reason) ? "Banned by automation" : CleanConfigText(reason, 140);
            return SendBan(parsed, entry, reason, "Codex bridge", out _, out outcome);
        }

        private bool RequestInventoryAuditFromBridge(string steamId, PlayerEntry entry)
        {
            steamId = CleanSteamId(steamId);
            if (!_clientInventoryAudit || !LooksLikeSteamId(steamId))
            {
                return false;
            }
            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }

            string nonce = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
            InventorySignalState state = GetOrCreateInventorySignal(steamId);
            state.LastAuditNonce = nonce;
            state.LastAuditRequestUtc = DateTime.UtcNow;
            state.LastAuditStatus = "requested through bridge";
            state.PendingAuditSessionId = Guid.Empty;

            string localSteamId = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (String.Equals(localSteamId, steamId, StringComparison.OrdinalIgnoreCase) || IsLocalPlayerEntry(entry))
            {
                if (!TryCollectLocalInventoryAuditItems(out var localItems, out string failure))
                {
                    state.LastAuditStatus = failure + "; previous observations retained";
                    return false;
                }
                RecordInventoryAuditResponse(steamId, nonce, Plugin.Version, "local", localItems, true);
                return true;
            }

            if (TryRequestLoaderInventoryAudit(steamId, state, out bool queued, out string detail))
            {
                state.LastAuditStatus = detail;
                return queued;
            }
            if (!BindInventoryAuditSession(steamId, state, out string sessionFailure))
            {
                state.LastAuditStatus = "not sent: " + sessionFailure;
                return false;
            }
            ulong parsedSteam;
            if (UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSteam))
            {
                TryRefreshPlayerNetworkHandles(entry, parsedSteam);
            }

            object connection = entry == null ? null : entry.BoltConnection;
            object entity = entry == null ? null : entry.BoltEntity;
            bool ok = SendNetworkMessageTo(AuditRequestPrefix + nonce, connection, entity);
            if (!ok)
            {
                state.LastAuditStatus = "send failed";
                state.PendingAuditSessionId = Guid.Empty;
            }
            return ok;
        }

        private bool SetSteamIdRoleFromBridge(string steamId, string role, string reason, out string outcome)
        {
            outcome = "Role request not sent: invalid Steam64 ID";
            steamId = CleanSteamId(steamId);
            ulong parsed;
            if (!LooksLikeSteamId(steamId) || !UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            role = CleanConfigText(role, 32).ToLowerInvariant();
            bool ok = SetPlayerRole(role, parsed, out outcome);
            RecordPlayerActivity(FindPlayerBySteamId(steamId), steamId, outcome + "; reason: " + CleanConfigText(reason, 96));
            return ok;
        }

        private bool AppendCodexAutomationMemory(string category, string note, string steamId, string raw)
        {
            try
            {
                Directory.CreateDirectory(_modDir);
                string line = "{"
                    + "\"utc\":\"" + JsonEscape(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) + "\","
                    + "\"category\":\"" + JsonEscape(CleanConfigText(category, 64)) + "\","
                    + "\"steam_id\":\"" + JsonEscape(CleanSteamId(steamId)) + "\","
                    + "\"note\":\"" + JsonEscape(CleanConfigText(note, 260)) + "\","
                    + "\"raw\":\"" + JsonEscape(CleanConfigText(raw, 512)) + "\""
                    + "}";
                File.AppendAllText(_automationMemoryPath, line + Environment.NewLine);
                _nextLearnedPolicyRefreshUtc = DateTime.MinValue;
                _nextCodexAutomationStateWriteUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception ex)
            {
                LogWarning("Automation memory write failed: " + ex.Message);
                return false;
            }
        }

        private bool WriteCodexAutomationState(bool force)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && now < _nextCodexAutomationStateWriteUtc)
            {
                return true;
            }
            _nextCodexAutomationStateWriteUtc = now.AddSeconds(force ? 1.0 : 10.0);

            try
            {
                Directory.CreateDirectory(_modDir);
                int online = CountPlayers(true);
                int offline = CountPlayers(false);

                StringBuilder sb = new StringBuilder(8192);
                sb.Append("{");
                sb.Append("\"utc\":\"").Append(JsonEscape(now.ToString("o", CultureInfo.InvariantCulture))).Append("\",");
                sb.Append("\"version\":\"").Append(JsonEscape(Plugin.Version)).Append("\",");
                sb.Append("\"admin_control\":\"").Append(JsonEscape(GetAdminControlMode())).Append("\",");
                sb.Append("\"external_ai_review\":").Append(_externalAiReviewEnabled ? "true" : "false").Append(',');
                sb.Append("\"codex_active\":").Append(IsCodexAdminActive() ? "true" : "false").Append(',');
                sb.Append("\"self_admin_fallback\":").Append(_selfAdminFallback ? "true" : "false").Append(',');
                sb.Append("\"last_codex_command_utc\":\"").Append(_lastCodexBridgeCommandUtc == DateTime.MinValue ? "" : JsonEscape(_lastCodexBridgeCommandUtc.ToString("o", CultureInfo.InvariantCulture))).Append("\",");
                sb.Append("\"codex_quiet_seconds\":").Append(_lastCodexBridgeCommandUtc == DateTime.MinValue ? "-1" : Math.Max(0.0, now.Subtract(_lastCodexBridgeCommandUtc).TotalSeconds).ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"codex_takeover_after_quiet_seconds\":").Append(_codexAdminQuietSeconds.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"roster_context\":\"").Append(JsonEscape(GetRosterContext())).Append("\",");
                sb.Append("\"roster_diagnostics\":\"").Append(JsonEscape(_lastRosterDiagnostics)).Append("\",");
                sb.Append("\"counts\":{");
                sb.Append("\"online\":").Append(online.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"offline\":").Append(offline.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"presence_unknown\":").Append(CountUnresolvedPlayers().ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"roster\":").Append(_players.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"muted\":").Append(_mutedPlayers.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"banned\":").Append(_bannedPlayers.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"jailed\":").Append(_jailedPlayers.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"exempt\":").Append(_antiCheatExemptPlayers.Count.ToString(CultureInfo.InvariantCulture)).Append("},");

                sb.Append("\"paths\":{");
                sb.Append("\"inbox\":\"").Append(JsonEscape(Path.Combine(_modDir, "codex-command-inbox.txt"))).Append("\",");
                sb.Append("\"results\":\"").Append(JsonEscape(Path.Combine(_modDir, "codex-command-results.jsonl"))).Append("\",");
                sb.Append("\"memory\":\"").Append(JsonEscape(_automationMemoryPath)).Append("\",");
                sb.Append("\"movement_telemetry\":\"").Append(JsonEscape(Path.Combine(_modDir, "movement-telemetry.csv"))).Append("\",");
                sb.Append("\"player_history\":\"").Append(JsonEscape(_playerHistoryPath)).Append("\"},");

                sb.Append("\"commands\":[");
                string[] commands = new string[]
                {
                    "heartbeat", "say|message", "chat|message", "acchat|message", "notice|message",
                    "botchat|AntiCheat|message",
                    "warn|steam64|reason", "tell|steam64|message", "flag|steam64|reason",
                    "jail|steam64|reason", "release|steam64|reason", "releaseall",
                    "mute|steam64|reason", "unmute|steam64|reason", "kick|steam64|reason",
                    "ban|steam64|reason", "unban|steam64|reason", "quarantine|steam64|reason",
                    "audit|steam64", "role|steam64|guest", "role|steam64|trusted", "role|steam64|admin", "grace|steam64|seconds",
                    "exempt|steam64|reason", "unexempt|steam64|reason", "learn|category|note",
                    "learn|movement_false_positive|token=glider; action=false_positive; reason=legit glide",
                    "learn|allow_event|token=chair; action=allow_event; reason=legit dismantle",
                    "learn|cheat_pattern|token=speed hack; action=confirmed; reason=confirmed WeMod",
                    "snapshot"
                };
                for (int i = 0; i < commands.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(commands[i])).Append('"');
                }
                sb.Append("],");

                AppendLearnedPolicyJson(sb);

                sb.Append("\"settings_file\":{");
                sb.Append("\"loaded\":").Append(_settingsLoaded ? "true" : "false").Append(',');
                sb.Append("\"last_operation\":\"").Append(JsonEscape(_settingsStatus)).Append("\",");
                sb.Append("\"problem\":\"").Append(JsonEscape(_settingsProblem)).Append("\"},");

                sb.Append("\"guard_settings\":{");
                sb.Append("\"disallow_cheats\":").Append(_disallowCheats ? "true" : "false").Append(',');
                sb.Append("\"auto_jail_enabled\":").Append(_autoJailCheaters ? "true" : "false").Append(',');
                sb.Append("\"self_auto_jail_active\":").Append(ShouldGuardAutoJail() ? "true" : "false").Append(',');
                sb.Append("\"movement_guard\":").Append(_movementGuard ? "true" : "false").Append(',');
                sb.Append("\"speed_guard\":").Append(_speedHackGuard ? "true" : "false").Append(',');
                sb.Append("\"teleport_guard\":").Append(_teleportGuard ? "true" : "false").Append(',');
                sb.Append("\"noclip_guard\":").Append(_noclipGuard ? "true" : "false").Append(',');
                sb.Append("\"item_guard\":").Append(_strictItemEventGuard ? "true" : "false").Append(',');
                sb.Append("\"vehicle_spawn_guard\":").Append(_vehicleSpawnGuard ? "true" : "false").Append(',');
                sb.Append("\"server_alerts\":").Append(_serverGuardAnnouncements ? "true" : "false").Append("},");

                sb.Append("\"movement_reference\":{");
                sb.Append("\"normal_local_defaults_mps\":{\"walk\":").Append(CleanConfigText(_debugWalkInput, 12)).Append(",\"run\":").Append(CleanConfigText(_debugRunInput, 12)).Append(",\"swim\":").Append(CleanConfigText(_debugSwimInput, 12)).Append("},");
                sb.Append("\"guard_limits_mps\":{");
                sb.Append("\"foot_horizontal\":").Append(_maxHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"foot_vertical\":").Append(_maxVerticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"foot_teleport_m\":").Append(_teleportDistanceThreshold.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"golf_cart_horizontal\":").Append(_golfCartHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"knight_v_horizontal\":").Append(_knightVHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"glider_horizontal\":").Append(_gliderHorizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"vehicle_vertical\":").Append(_vehicleVerticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"glider_vertical\":").Append(_gliderVerticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"glider_fallback_vertical\":").Append(_gliderFallbackVerticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)).Append("},");
                sb.Append("\"policy\":\"Foot speed over the guard limits is suspicious. Give grace for join, death/camp capture, spawn events, glider, golf cart, KnightV, raft, zipline, and explicit admin teleports. Prefer warn/audit before jail unless evidence is repeated or severe.\"},");

                sb.Append("\"players\":[");
                bool firstPlayer = true;
                for (int i = 0; i < _players.Count; i++)
                {
                    PlayerEntry entry = _players[i];
                    if (entry == null) continue;
                    if (!firstPlayer) sb.Append(',');
                    firstPlayer = false;
                    string steamId = CleanSteamId(entry.SteamId);
                    MovementSignalState movement;
                    _movementTelemetrySignals.TryGetValue(steamId, out movement);
                    if (movement == null)
                    {
                        _movementSignals.TryGetValue(steamId, out movement);
                    }
                    InventorySignalState inventory;
                    _inventorySignals.TryGetValue(steamId, out inventory);
                    sb.Append('{');
                    sb.Append("\"name\":\"").Append(JsonEscape(DisplayPlayerName(entry))).Append("\",");
                    sb.Append("\"steam_id\":\"").Append(JsonEscape(steamId)).Append("\",");
                    sb.Append("\"online\":").Append(!entry.PresenceUnresolved && entry.Online ? "true" : "false").Append(',');
                    sb.Append("\"presence_unknown\":").Append(entry.PresenceUnresolved ? "true" : "false").Append(',');
                    sb.Append("\"presence\":\"").Append(JsonEscape(GetPlayerPresenceText(entry))).Append("\",");
                    sb.Append("\"presence_detail\":\"").Append(JsonEscape(entry.PresenceDetail)).Append("\",");
                    sb.Append("\"last_confirmed_utc\":\"").Append(entry.LastSeenUtc == DateTime.MinValue ? "" : entry.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture)).Append("\",");
                    sb.Append("\"role\":\"").Append(JsonEscape(entry.Role)).Append("\",");
                    sb.Append("\"source\":\"").Append(JsonEscape(entry.Source)).Append("\",");
                    sb.Append("\"targetable\":").Append(!entry.PresenceUnresolved && entry.Online && (entry.BoltEntity != null || entry.Transform != null || entry.SourceObject != null) ? "true" : "false").Append(',');
                    sb.Append("\"muted\":").Append(IsSteamIdMuted(steamId) ? "true" : "false").Append(',');
                    sb.Append("\"jailed\":").Append(IsJailedSteamId(steamId) ? "true" : "false").Append(',');
                    sb.Append("\"last_activity\":\"").Append(JsonEscape(entry.LastActivity)).Append("\",");
                    sb.Append("\"movement\":{");
                    sb.Append("\"sample_available\":").Append(movement != null && movement.HasSample ? "true" : "false").Append(',');
                    sb.Append("\"sample_phase\":\"").Append(JsonEscape(movement == null ? "" : movement.LastSamplePhase)).Append("\",");
                    sb.Append("\"sample_source\":\"").Append(JsonEscape(movement == null ? "" : movement.PositionSource)).Append("\",");
                    sb.Append("\"last_sample_utc\":\"").Append(movement == null || movement.LastSampleUtc == DateTime.MinValue ? "" : movement.LastSampleUtc.ToString("o", CultureInfo.InvariantCulture)).Append("\",");
                    sb.Append("\"last_evaluation_utc\":\"").Append(movement == null || movement.LastEvaluationUtc == DateTime.MinValue ? "" : movement.LastEvaluationUtc.ToString("o", CultureInfo.InvariantCulture)).Append("\",");
                    sb.Append("\"context\":\"").Append(JsonEscape(movement == null ? "" : movement.LastContext)).Append("\",");
                    sb.Append("\"horizontal_mps\":").Append(movement == null ? "0" : movement.LastHorizontalSpeed.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"vertical_mps\":").Append(movement == null ? "0" : movement.LastVerticalSpeed.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"distance_m\":").Append(movement == null ? "0" : movement.LastDistance.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"last_reason\":\"").Append(JsonEscape(movement == null ? "" : movement.LastReason)).Append("\"},");
                    sb.Append("\"inventory\":{");
                    sb.Append("\"last_status\":\"").Append(JsonEscape(inventory == null ? "" : inventory.LastAuditStatus)).Append("\",");
                    sb.Append("\"last_items\":\"").Append(JsonEscape(inventory == null ? "" : inventory.LastAuditItems)).Append("\",");
                    sb.Append("\"last_reason\":\"").Append(JsonEscape(inventory == null ? "" : inventory.LastReason)).Append("\"}");
                    sb.Append('}');
                }
                sb.Append("],");

                sb.Append("\"jailed_players\":[");
                bool firstJail = true;
                foreach (JailEntry jail in _jailedPlayers.Values)
                {
                    if (jail == null) continue;
                    if (!firstJail) sb.Append(',');
                    firstJail = false;
                    sb.Append('{');
                    sb.Append("\"steam_id\":\"").Append(JsonEscape(jail.SteamId)).Append("\",");
                    sb.Append("\"name\":\"").Append(JsonEscape(jail.Name)).Append("\",");
                    sb.Append("\"reason\":\"").Append(JsonEscape(jail.Reason)).Append("\",");
                    sb.Append("\"source\":\"").Append(JsonEscape(jail.Source)).Append("\",");
                    sb.Append("\"triggers\":").Append(jail.TriggerCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append("\"has_return_position\":").Append(jail.HasReturnPosition ? "true" : "false").Append(',');
                    sb.Append("\"return_position\":").Append(JsonVector(jail.ReturnPosition));
                    sb.Append('}');
                }
                sb.Append("],");

                sb.Append("\"event_log_tail\":[");
                int start = Math.Max(0, _eventLog.Count - 14);
                for (int i = start; i < _eventLog.Count; i++)
                {
                    if (i > start) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(_eventLog[i])).Append('"');
                }
                sb.Append("]");
                sb.Append("}");

                File.WriteAllText(_automationStatePath, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                LogWarning("Automation state write failed: " + ex.Message);
                return false;
            }
        }

        private static string JsonVector(Vector3 value)
        {
            return "{\"x\":" + value.x.ToString("0.###", CultureInfo.InvariantCulture)
                + ",\"y\":" + value.y.ToString("0.###", CultureInfo.InvariantCulture)
                + ",\"z\":" + value.z.ToString("0.###", CultureInfo.InvariantCulture) + "}";
        }

        private void AppendCodexBridgeResult(string action, string steamId, bool ok, string message, string raw)
        {
            try
            {
                Directory.CreateDirectory(_modDir);
                string path = Path.Combine(_modDir, "codex-command-results.jsonl");
                string line = "{"
                    + "\"utc\":\"" + JsonEscape(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) + "\","
                    + "\"action\":\"" + JsonEscape(action) + "\","
                    + "\"steam_id\":\"" + JsonEscape(steamId) + "\","
                    + "\"ok\":" + (ok ? "true" : "false") + ","
                    + "\"message\":\"" + JsonEscape(message) + "\","
                    + "\"raw\":\"" + JsonEscape(raw) + "\""
                    + "}";
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogWarning("Codex bridge result write failed: " + ex.Message);
            }
        }

        private static string JsonEscape(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private bool MovementLooksSuspicious(Vector3 position, float horizontalSpeed, float verticalSpeed, float distance, double seconds, MovementContext context, MovementSignalState state, out string reason, out bool severe)
        {
            reason = "";
            severe = false;
            context = context ?? MovementContext.Foot(_maxHorizontalSpeed, _maxVerticalSpeed, _teleportDistanceThreshold);
            float horizontalLimit = Math.Max(10f, context.HorizontalLimit);
            float verticalLimit = Math.Max(20f, context.VerticalLimit);
            float teleportLimit = Math.Max(_teleportDistanceThreshold, context.TeleportBase);
            float dynamicTeleport = Math.Max(teleportLimit, horizontalLimit * (float)seconds * Math.Max(2.5f, context.TeleportMultiplier));

            if (_noclipGuard)
            {
                if (position.y < _noclipFloorY)
                {
                    reason = "below allowed world floor";
                    severe = true;
                    return true;
                }
                if (position.y > 1500f)
                {
                    reason = "above allowed world ceiling";
                    severe = true;
                    return true;
                }
                if (!context.AllowsAirborne && verticalSpeed > verticalLimit && distance > 35f)
                {
                    reason = "impossible vertical movement while " + context.Label
                        + " (" + verticalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                        + " > " + verticalLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m/s)";
                    severe = false;
                    return true;
                }
            }

            if (_teleportGuard)
            {
                if (distance > dynamicTeleport)
                {
                    reason = "unauthorized teleport jump while " + context.Label
                        + " (" + distance.ToString("0.#", CultureInfo.InvariantCulture)
                        + " > " + dynamicTeleport.ToString("0.#", CultureInfo.InvariantCulture) + "m)";
                    severe = true;
                    return true;
                }
            }

            if (_speedHackGuard && horizontalSpeed > horizontalLimit && distance > 15f)
            {
                reason = "possible speed hack while " + context.Label
                    + " (" + horizontalSpeed.ToString("0.#", CultureInfo.InvariantCulture)
                    + " > " + horizontalLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m/s)";
                severe = false;
                return true;
            }

            return false;
        }

        private static bool NetworkArrayContainsInt(object value, int wanted)
        {
            if (value == null)
            {
                return false;
            }
            int count;
            if (TryConvertToInt(ReflectionTools.GetMemberValue(value, "Length") ?? ReflectionTools.GetMemberValue(value, "Count")
                ?? ReflectionTools.InvokeNoArgMethod(value, "get_Length") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Count"), out count))
            {
                count = Math.Min(Math.Max(count, 0), 32);
                for (int i = 0; i < count; i++)
                {
                    object item = ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { i })
                        ?? ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { (uint)i });
                    int parsed;
                    if (TryConvertToInt(item, out parsed) && parsed == wanted)
                    {
                        return true;
                    }
                }
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(value, "GetEnumerator");
            int safety = 0;
            while (enumerator != null && safety++ < 32)
            {
                object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                if (!(moved is bool) || !(bool)moved)
                {
                    break;
                }
                object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                int parsed;
                if (TryConvertToInt(current, out parsed) && parsed == wanted)
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryGetPlayerWorldPosition(PlayerEntry entry, out Vector3 position)
        {
            position = Vector3.zero;
            if (entry == null)
            {
                return false;
            }

            object transform = entry.Transform ?? ExtractTransform(entry.BoltEntity) ?? ExtractTransform(entry.SourceObject);
            if (transform == null && LooksLikeSteamId(entry.SteamId))
            {
                ulong steamId;
                if (UInt64.TryParse(entry.SteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
                {
                    TryRefreshPlayerNetworkHandles(entry, steamId);
                    transform = entry.Transform ?? ExtractTransform(entry.BoltEntity) ?? ExtractTransform(entry.SourceObject);
                    if (transform == null)
                    {
                        string route;
                        TryResolveExactPlayerTransform(entry, out transform, out route);
                    }
                }
            }
            if (transform == null)
            {
                return false;
            }

            Transform playerTransform = transform as Transform;
            if (playerTransform == null) return false;
            try
            {
                position = playerTransform.position;
                return IsFiniteDebugValue(position.x) && IsFiniteDebugValue(position.y) && IsFiniteDebugValue(position.z);
            }
            catch { return false; }
        }

        private MovementSignalState GetOrCreateMovementState(string steamId)
        {
            steamId = CleanSteamId(steamId);
            MovementSignalState state;
            if (!_movementSignals.TryGetValue(steamId, out state) || state == null)
            {
                state = new MovementSignalState();
                state.SteamId = steamId;
                _movementSignals[steamId] = state;
            }
            return state;
        }

        private void AllowMovementSpike(string steamId, double seconds, string reason)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId) || Double.IsNaN(seconds) || Double.IsInfinity(seconds))
            {
                return;
            }
            MovementSignalState state = GetOrCreateMovementState(steamId);
            state.IgnoreUntilUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(1.0, seconds));
            state.IgnoreReason = CleanConfigText(reason, 64);
            InvalidateMovementSample(state, "Movement grace granted: " + state.IgnoreReason);
        }

        private void AllowFreshJoinMovementGrace(string steamId, string reason)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }
            ResetMovementState(steamId);
            _movementTelemetrySignals.Remove(steamId);
            AllowMovementSpike(steamId, Math.Max(5f, _joinMovementGraceSeconds), reason);
        }

        private void UpdateGliderMovementEvidence(MovementSignalState state, Vector3 delta, float horizontalSpeed, float verticalSpeed, float distance, double seconds)
        {
            if (state == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool descending = delta.y < -3.0f && distance > 12f;
            bool meaningfulAirMove = horizontalSpeed >= Math.Max(10f, _maxHorizontalSpeed * 0.5f)
                && verticalSpeed >= 5f;
            bool plausibleHorizontal = horizontalSpeed <= Math.Max(_gliderHorizontalSpeed * 1.15f, _maxHorizontalSpeed * 2.25f);
            bool plausibleVertical = verticalSpeed <= Math.Max(120f, _gliderVerticalSpeed * 1.35f);
            bool plausibleSample = seconds >= 0.35 && seconds <= 3.5;
            float glideRatio = verticalSpeed <= 0.1f ? 99f : horizontalSpeed / verticalSpeed;
            bool plausibleGlideRatio = glideRatio >= 0.35f && glideRatio <= 10f;

            if (descending && meaningfulAirMove && plausibleHorizontal && plausibleVertical && plausibleSample && plausibleGlideRatio)
            {
                state.GliderLikeSamples = Math.Min(6, state.GliderLikeSamples + 1);
                float holdSeconds = state.GliderLikeSamples >= 4 ? 8f : 4f;
                state.GliderLikeUntilUtc = now.AddSeconds(holdSeconds);
                state.GliderLikeReason = "descent pattern";
                return;
            }

            if (state.GliderLikeSamples > 0 && now > state.GliderLikeUntilUtc)
            {
                state.GliderLikeSamples--;
                if (state.GliderLikeSamples == 0)
                {
                    state.GliderLikeReason = "";
                }
            }
        }

        private void UpdateFootClimbEvidence(MovementSignalState state, Vector3 position, Vector3 delta, MovementContext context, double seconds)
        {
            if (state == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (context == null || context.AllowsAirborne || context.HasVehicle || seconds < 0.2 || seconds > 4.0)
            {
                ResetFootClimbEvidence(state);
                return;
            }

            float previousY = position.y - delta.y;
            if (state.FootClimbWindowStartUtc == DateTime.MinValue
                || now.Subtract(state.FootClimbWindowStartUtc).TotalSeconds > Math.Max(3f, _footClimbWindowSeconds)
                || Math.Abs(previousY - state.FootClimbLastY) > 18f)
            {
                state.FootClimbWindowStartUtc = now;
                state.FootClimbStartY = previousY;
                state.FootClimbLastY = previousY;
                state.FootClimbUpMeters = 0f;
                state.FootClimbDownMeters = 0f;
                state.FootClimbNetGain = 0f;
                state.FootClimbWindowAgeSeconds = 0f;
            }

            if (delta.y > 0.35f)
            {
                state.FootClimbUpMeters += delta.y;
            }
            else if (delta.y < -0.75f)
            {
                state.FootClimbDownMeters += -delta.y;
            }

            state.FootClimbLastY = position.y;
            state.FootClimbNetGain = position.y - state.FootClimbStartY;
            state.FootClimbWindowAgeSeconds = (float)Math.Max(0.0, now.Subtract(state.FootClimbWindowStartUtc).TotalSeconds);

            if (state.FootClimbNetGain < -2f || state.FootClimbDownMeters > Math.Max(6f, state.FootClimbUpMeters * 0.7f))
            {
                state.FootClimbWindowStartUtc = now;
                state.FootClimbStartY = position.y;
                state.FootClimbLastY = position.y;
                state.FootClimbUpMeters = 0f;
                state.FootClimbDownMeters = 0f;
                state.FootClimbNetGain = 0f;
                state.FootClimbWindowAgeSeconds = 0f;
            }
        }

        private static void ResetFootClimbEvidence(MovementSignalState state)
        {
            if (state == null)
            {
                return;
            }
            state.FootClimbWindowStartUtc = DateTime.MinValue;
            state.FootClimbStartY = 0f;
            state.FootClimbLastY = 0f;
            state.FootClimbUpMeters = 0f;
            state.FootClimbDownMeters = 0f;
            state.FootClimbNetGain = 0f;
            state.FootClimbWindowAgeSeconds = 0f;
        }

        public void AllowMovementSpikeForLocalPlayer(float seconds, string reason)
        {
            string steamId = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (!LooksLikeSteamId(steamId))
            {
                steamId = CleanSteamId(GetLocalSteamIdFromRoster());
            }
            AllowMovementSpike(steamId, seconds, reason);
        }

        private void ResetMovementState(string steamId)
        {
            steamId = CleanSteamId(steamId);
            if (LooksLikeSteamId(steamId))
            {
                _movementSignals.Remove(steamId);
            }
        }

        private bool TryRefreshPlayerNetworkHandles(PlayerEntry entry, ulong steamId)
        {
            if (entry == null)
            {
                return false;
            }

            bool changed = TryFindKnownPlayerPair(steamId, entry);
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            if (utilities != null)
            {
                if (entry.BoltConnection == null)
                {
                    object connection = GetConnectionFromSteamId(steamId);
                    if (connection != null)
                    {
                        entry.BoltConnection = connection;
                        changed = true;
                    }
                }
                if (entry.BoltEntity == null)
                {
                    object entity = GetEntityFromSteamId(steamId);
                    if (entity != null)
                    {
                        entry.BoltEntity = entity;
                        changed = true;
                    }
                }
                if (entry.BoltEntity == null && entry.BoltConnection != null)
                {
                    object entity = ReflectionTools.InvokeStaticMethodResult(utilities, "GetEntityFromBoltConnectionForServer", new object[] { entry.BoltConnection });
                    if (entity != null)
                    {
                        entry.BoltEntity = entity;
                        changed = true;
                    }
                }
                if (entry.BoltConnection == null || entry.BoltEntity == null)
                {
                    object[] args = new object[] { steamId, null, null };
                    object result;
                    if (InvokeMethod(null, utilities, "GetConnectionAndEntity", args, true, out result))
                    {
                        if (entry.BoltConnection == null && args.Length > 1 && args[1] != null)
                        {
                            entry.BoltConnection = args[1];
                            changed = true;
                        }
                        if (entry.BoltEntity == null && args.Length > 2 && args[2] != null)
                        {
                            entry.BoltEntity = args[2];
                            changed = true;
                        }
                    }
                }
            }
            Type serverInfo = ReflectionTools.FindType("CoopServerInfo");
            if (serverInfo != null && entry.BoltEntity == null)
            {
                object[] args = new object[] { steamId, null, false };
                object result;
                if (InvokeMethod(null, serverInfo, "TryGetPlayerEntityFromSteamId", args, true, out result)
                    && result is bool
                    && (bool)result
                    && args.Length > 1
                    && args[1] != null)
                {
                    entry.BoltEntity = args[1];
                    changed = true;
                }
            }
            if (serverInfo != null && entry.ConnectionId < 0)
            {
                object[] args = new object[] { steamId, 0 };
                object result;
                if (InvokeMethod(null, serverInfo, "TryGetConnectionIdFromSteamId", args, true, out result)
                    && result is bool
                    && (bool)result)
                {
                    try
                    {
                        entry.ConnectionId = Convert.ToInt32(args[1], CultureInfo.InvariantCulture);
                        changed = true;
                    }
                    catch { }
                }
            }
            if (entry.Transform == null)
            {
                entry.Transform = ExtractTransform(entry.BoltEntity) ?? ExtractTransform(entry.SourceObject);
                changed = entry.Transform != null || changed;
            }
            if (String.IsNullOrEmpty(entry.Name))
            {
                string name = ExtractNameFromEntity(entry.BoltEntity);
                if (!String.IsNullOrEmpty(name))
                {
                    ApplyBetterPlayerName(entry, name);
                    changed = true;
                }
            }
            return changed;
        }

        private bool TryFindKnownPlayerPair(ulong steamId, PlayerEntry entry)
        {
            Type listType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerListBase");
            object known = ReflectionTools.GetStaticMemberValue(listType, "_knownPlayers") ?? ReflectionTools.InvokeStaticNoArgMethod(listType, "get__knownPlayers");
            if (known == null)
            {
                return false;
            }

            bool found = false;
            IDictionary dictionary = known as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry pair in dictionary)
                {
                    found = ApplyKnownPlayerPair(steamId, entry, pair.Key, pair.Value) || found;
                }
                return found;
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(known, "GetEnumerator");
            int guard = 0;
            while (enumerator != null && guard++ < 128)
            {
                object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                if (!(moved is bool) || !(bool)moved)
                {
                    break;
                }
                object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                object key = ReflectionTools.GetMemberValue(current, "Key") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Key");
                object value = ReflectionTools.GetMemberValue(current, "Value") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Value");
                found = ApplyKnownPlayerPair(steamId, entry, key, value) || found;
            }
            return found;
        }

        private bool ApplyKnownPlayerPair(ulong steamId, PlayerEntry entry, object key, object value)
        {
            string foundSteamId = CleanSteamId(ExtractSteamId(value));
            if (String.IsNullOrEmpty(foundSteamId) || !String.Equals(foundSteamId, steamId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (entry.BoltConnection == null && key != null && (key.GetType().FullName ?? key.GetType().Name).IndexOf("BoltConnection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                entry.BoltConnection = key;
            }
            if (entry.BoltEntity == null)
            {
                entry.BoltEntity = ExtractFirstMember(value, new string[] { "BoltEntity", "Entity", "PlayerEntity", "NetworkEntity", "_boltEntity", "_entity", "get_BoltEntity" });
            }
            if (entry.Transform == null)
            {
                entry.Transform = ExtractTransform(entry.BoltEntity) ?? ExtractTransform(value);
            }
            if (String.IsNullOrEmpty(entry.Name))
            {
                entry.Name = ExtractPlayerName(value);
            }
            return true;
        }

        private bool DemotePlayerToGuestForAdminAction(ulong steamId, out string outcome)
        {
            return SetPlayerRole("Guest", steamId, out outcome);
        }

        private void MakeSelectedPlayerAdmin() => ChangeSelectedPlayerRole("Admin");
        private void MakeSelectedPlayerTrusted() => ChangeSelectedPlayerRole("Trusted");
        private void MakeSelectedPlayerGuest() => ChangeSelectedPlayerRole("Guest");

        private void TickHostPolicyEnforcement()
        {
            _nextPolicyPushUtc = DateTime.UtcNow.AddSeconds(30.0);
            if (!HasHostSession())
            {
                return;
            }
            PushServerCheatsOff(false);
        }

        private bool PushServerCheatsOff(bool showAction)
        {
            _nextPolicyPushUtc = DateTime.UtcNow.AddSeconds(30.0);
            if (!HasHostSession())
            {
                if (showAction)
                {
                    ActionFail("Cheats off not attempted: host session is not active");
                }
                return false;
            }

            bool ok = SendAdminBoolCommand("Command_setcheatsenabled", "setcheatsenabled", false, out string outcome);
            outcome = "Cheats off: " + outcome;
            if (showAction)
            {
                ActionResult(ok, outcome, outcome);
            }
            else if (ok)
            {
                LogInfo("Host policy " + outcome);
            }
            else
            {
                LogWarning("Host policy " + outcome);
            }
            return ok;
        }

        private void CheckSelectedPlayerBan()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Ban check requires a selected player with a resolved Steam64 ID");
                return;
            }

            bool banned;
            bool available = TryCheckPlayerBanned(steamId, out banned);
            RecordNativeBanQuery(entry, available, banned);
            if (available)
            {
                ActionOk("Ban check: " + steamId.ToString(CultureInfo.InvariantCulture) + " = " + (banned ? "banned" : "not banned"));
            }
            else
            {
                ActionFail("Ban check failed for " + steamId.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void CopySelectedSteamId()
        {
            if (!TryGetSelectedModerationTarget(out _, out ulong steamId))
            {
                ActionFail("Copy ID requires a selected player with a resolved Steam64 ID");
                return;
            }

            GUIUtility.systemCopyBuffer = steamId.ToString(CultureInfo.InvariantCulture);
            ActionOk("Copied SteamID " + steamId.ToString(CultureInfo.InvariantCulture));
        }

        private void ToggleSelectedMute()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out _))
            {
                ActionFail("Mute changes require a selected player with a resolved Steam64 ID");
                return;
            }
            SetPlayerMuted(entry, !IsPlayerMuted(entry));
        }

        private void SetPlayerMuted(PlayerEntry entry, bool muted)
        {
            if (entry == null || String.IsNullOrEmpty(entry.SteamId))
            {
                ActionFail("Mute failed: no SteamID on that player row");
                return;
            }
            SetSteamIdMuted(entry.SteamId, entry, muted, out _);
        }

        private bool SetSteamIdMuted(string steamId, PlayerEntry entry, bool muted, out string outcome)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                outcome = (muted ? "Mute" : "Unmute") + " failed: invalid Steam64 ID";
                ActionFail(outcome);
                return false;
            }

            bool voiceOk = false;
            bool voiceListOk = false;
            ulong parsed;
            if (UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                voiceOk = InvokeStaticMethod("CoopSteamVoicePlayer", "SetPlayerSteamIdMuted", new object[] { parsed, muted });
                voiceListOk = ApplyGameVoiceMute(parsed, muted);
            }

            if (muted)
            {
                string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Muted by host" : _adminReasonInput;
                RecordModerationEntry(_mutedPlayers, steamId, entry, reason, "Mute action");
                if (entry == null)
                {
                    entry = FindPlayerBySteamId(steamId);
                }
                SendMuteNotice(entry, steamId, true);
            }
            else
            {
                _mutedPlayers.Remove(steamId);
                _muteNoticeTimes.Remove(steamId);
            }

            bool saved = SaveConfig();
            RefreshPlayers();
            outcome = "Admin mute record " + (muted ? "enabled" : "removed") + " for " + steamId
                + "; " + DescribeModerationSettingsSave(saved)
                + "; voice mute " + (voiceOk || voiceListOk ? "call returned; effect unconfirmed" : "calls unconfirmed");
            RecordPlayerActivity(entry, steamId, outcome
                + (muted ? "; reason: " + CleanConfigText(_adminReasonInput, 96) : ""));
            ActionResult(saved, outcome, outcome);
            return saved;
        }

        private bool ApplyGameVoiceMute(ulong steamId, bool muted)
        {
            Type listType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerListBase") ?? ReflectionTools.FindType("PlayerListBase");
            Array lists = ReflectionTools.FindObjectsOfType(listType);
            if (lists == null)
            {
                return false;
            }

            bool applied = false;
            foreach (object list in lists)
            {
                if (list == null)
                {
                    continue;
                }
                object result;
                applied = InvokeMethod(list, list.GetType(), "MuteToggleChanged", new object[] { steamId, muted }, false, out result) || applied;
            }
            return applied;
        }

        private void KickManualSteamId()
        {
            string id = CleanSteamId(_manualSteamIdInput);
            if (!LooksLikeSteamId(id))
            {
                ActionFail("Kick failed: manual Steam64 ID is invalid");
                return;
            }
            ulong steamId = UInt64.Parse(id, CultureInfo.InvariantCulture);
            PlayerEntry entry = FindPlayerBySteamId(id);
            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Removed by host" : _adminReasonInput;
            bool sent = SendKick(steamId, entry, reason, out string outcome);
            ActionResult(sent, outcome, outcome);
        }

        private void BanManualSteamId()
        {
            string id = CleanSteamId(_manualSteamIdInput);
            if (!LooksLikeSteamId(id))
            {
                ActionFail("Ban failed: manual Steam64 ID is invalid");
                return;
            }
            ulong steamId = UInt64.Parse(id, CultureInfo.InvariantCulture);
            PlayerEntry entry = FindPlayerBySteamId(id);
            if (entry == null && !String.IsNullOrEmpty(_manualNameInput))
            {
                entry = new PlayerEntry { Key = id, SteamId = id, Name = CleanName(_manualNameInput), Source = "Manual ban" };
            }
            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Banned by host" : _adminReasonInput;
            bool ok = SendBan(steamId, entry, reason, "Manual ban", out bool saved, out string outcome);
            if (ok) RefreshPlayers();
            ActionResult(ok && saved, outcome, outcome);
        }

        private void UnbanManualSteamId()
        {
            string id = CleanSteamId(_manualSteamIdInput);
            if (!LooksLikeSteamId(id))
            {
                ActionFail("Unban failed: manual Steam64 ID is invalid");
                return;
            }
            UnbanSteamIdFromList(id);
        }

        private void MuteManualSteamId()
        {
            string id = CleanSteamId(_manualSteamIdInput);
            PlayerEntry entry = FindPlayerBySteamId(id);
            if (entry == null && LooksLikeSteamId(id) && !String.IsNullOrEmpty(_manualNameInput))
            {
                entry = new PlayerEntry { Key = id, SteamId = id, Name = CleanName(_manualNameInput), Source = "Manual mute" };
            }
            SetSteamIdMuted(id, entry, true, out _);
        }

        private void UnmuteManualSteamId()
        {
            SetSteamIdMuted(CleanSteamId(_manualSteamIdInput), null, false, out _);
        }

        private void UnbanSteamIdFromList(string steamIdText)
        {
            steamIdText = CleanSteamId(steamIdText);
            ulong steamId;
            if (!LooksLikeSteamId(steamIdText) || !UInt64.TryParse(steamIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
            {
                ActionFail("Unban failed: invalid Steam64 ID");
                return;
            }
            string reason = String.IsNullOrEmpty(_adminReasonInput) ? "Unban requested by host" : _adminReasonInput;
            bool ok = UnbanSteamId(steamId, reason, out string outcome);
            if (ok) RefreshPlayers();
            ActionResult(ok, outcome, outcome);
        }

        private void ForgetModerationEntry(Dictionary<string, ModerationEntry> target, string steamIdText, string label)
        {
            steamIdText = CleanSteamId(steamIdText);
            if (!LooksLikeSteamId(steamIdText))
            {
                ActionFail("Forget failed: invalid Steam64 ID");
                return;
            }
            if (target.Remove(steamIdText))
            {
                bool saved = SaveConfig();
                RefreshPlayers();
                ReportSettingsSave(saved, "Removed " + label + " record for " + steamIdText);
            }
            else
            {
                ActionFail("No saved " + label + " record for " + steamIdText);
            }
        }

        private void RunCommand()
        {
            string command = (_commandInput ?? "").Trim();
            if (command.Length == 0)
            {
                ActionFail("Command is empty");
                return;
            }
            bool ok = InvokeConsoleCommand(command);
            ActionResult(ok, "Command sent: " + Shorten(command, 48), DebugCommandFailure("Command"));
        }

        private void DrawDebugMethodButton(string label, string methodName, string arg, string fallbackCommand, float width, GUIStyle style)
        {
            Texture2D icon = ResolveDebugButtonIcon(label, methodName, fallbackCommand);
            if (DrawDebugCommandButton(icon, label, style, width, 32f, String.IsNullOrEmpty(methodName) ? fallbackCommand : methodName))
            {
                RunDebugConsoleAction(label, methodName, arg, fallbackCommand);
            }
        }

        private bool DrawDebugIconButton(Texture2D icon, string label, GUIStyle style, float width, float height)
        {
            return DrawDebugIconButton(icon, label, style, width, height, label);
        }

        private bool DrawDebugIconButton(Texture2D icon, string label, GUIStyle style, float width, float height, string tooltip)
        {
            GUIContent content = icon == null ? new GUIContent(label) : new GUIContent(label, icon, label);
            content.tooltip = tooltip ?? label;
            return GUILayout.Button(content, style, GUILayout.Width(width), GUILayout.Height(height));
        }

        private void DrawDebugActionButton(Texture2D icon, string label, GUIStyle style, float width, float height, Action action, bool available = true)
        {
            if (DrawModerationIconButton(icon, label, style, available, width, height) && action != null)
            {
                action();
            }
        }

        private void DrawDebugSliderField(string label, ref string valueText, float min, float max, float fallback, string tooltip, float labelWidth, float sliderWidth)
        {
            float value = ParseDebugFloat(valueText, fallback, min, max);
            GUILayout.Label(new GUIContent(label, null, tooltip), _contextLabelStyle, GUILayout.Width(labelWidth), GUILayout.Height(30f));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(sliderWidth), GUILayout.Height(30f));
            if (Math.Abs(next - value) > 0.001f)
            {
                valueText = next.ToString("0.##", CultureInfo.InvariantCulture);
            }
            valueText = GUILayout.TextField(valueText ?? value.ToString("0.##", CultureInfo.InvariantCulture), _inputStyle, GUILayout.Width(58f), GUILayout.Height(30f));
        }

        private Texture2D ResolveDebugButtonIcon(string label, string methodName, string fallbackCommand)
        {
            string key = ((label ?? "") + " " + (methodName ?? "") + " " + (fallbackCommand ?? "")).ToLowerInvariant();
            if (key.IndexOf("summon", StringComparison.Ordinal) >= 0) return _iconSummon;
            if (key.IndexOf("go to", StringComparison.Ordinal) >= 0 || key.IndexOf("goto", StringComparison.Ordinal) >= 0 || key.IndexOf("coords", StringComparison.Ordinal) >= 0) return _iconGoTo;
            if (key.IndexOf("kick", StringComparison.Ordinal) >= 0) return _iconKick;
            if (key.IndexOf("mute", StringComparison.Ordinal) >= 0) return _iconMute;
            if (key.IndexOf("ban", StringComparison.Ordinal) >= 0 || key.IndexOf("kill", StringComparison.Ordinal) >= 0 || key.IndexOf("damage", StringComparison.Ordinal) >= 0 || key.IndexOf("ignite", StringComparison.Ordinal) >= 0 || key.IndexOf("shock", StringComparison.Ordinal) >= 0 || key.IndexOf("burn", StringComparison.Ordinal) >= 0 || key.IndexOf("dismember", StringComparison.Ordinal) >= 0 || key.IndexOf("gib", StringComparison.Ordinal) >= 0 || key.IndexOf("drown", StringComparison.Ordinal) >= 0) return _iconWarning;
            if (key.IndexOf("copy", StringComparison.Ordinal) >= 0) return _iconCopy;
            if (key.IndexOf("heal", StringComparison.Ordinal) >= 0 || key.IndexOf("regen", StringComparison.Ordinal) >= 0 || key.IndexOf("revive", StringComparison.Ordinal) >= 0) return _iconHeal;
            if (key.IndexOf("god", StringComparison.Ordinal) >= 0 || key.IndexOf("survival", StringComparison.Ordinal) >= 0 || key.IndexOf("energy", StringComparison.Ordinal) >= 0 || key.IndexOf("armor", StringComparison.Ordinal) >= 0 || key.IndexOf("armour", StringComparison.Ordinal) >= 0) return _iconShield;
            if (key.IndexOf("speed", StringComparison.Ordinal) >= 0 || key.IndexOf("jump", StringComparison.Ordinal) >= 0 || key.IndexOf("gravity", StringComparison.Ordinal) >= 0 || key.IndexOf("scale", StringComparison.Ordinal) >= 0) return _iconSpeed;
            if (key.IndexOf("add", StringComparison.Ordinal) >= 0 || key.IndexOf("item", StringComparison.Ordinal) >= 0 || key.IndexOf("equip", StringComparison.Ordinal) >= 0 || key.IndexOf("refill", StringComparison.Ordinal) >= 0 || key.IndexOf("tag", StringComparison.Ordinal) >= 0) return _iconItems;
            if (key.IndexOf("build", StringComparison.Ordinal) >= 0 || key.IndexOf("builder", StringComparison.Ordinal) >= 0 || key.IndexOf("log", StringComparison.Ordinal) >= 0 || key.IndexOf("stone", StringComparison.Ordinal) >= 0 || key.IndexOf("ghost", StringComparison.Ordinal) >= 0 || key.IndexOf("trap", StringComparison.Ordinal) >= 0 || key.IndexOf("slap", StringComparison.Ordinal) >= 0) return _iconBuild;
            if (key.IndexOf("rain", StringComparison.Ordinal) >= 0 || key.IndexOf("cloud", StringComparison.Ordinal) >= 0 || key.IndexOf("time", StringComparison.Ordinal) >= 0 || key.IndexOf("season", StringComparison.Ordinal) >= 0 || key.IndexOf("tree", StringComparison.Ordinal) >= 0 || key.IndexOf("bush", StringComparison.Ordinal) >= 0 || key.IndexOf("dig", StringComparison.Ordinal) >= 0 || key.IndexOf("locator", StringComparison.Ordinal) >= 0 || key.IndexOf("world", StringComparison.Ordinal) >= 0) return _iconWorld;
            if (key.IndexOf("ai", StringComparison.Ordinal) >= 0 || key.IndexOf("animal", StringComparison.Ordinal) >= 0 || key.IndexOf("robby", StringComparison.Ordinal) >= 0 || key.IndexOf("creepy", StringComparison.Ordinal) >= 0 || key.IndexOf("thought", StringComparison.Ordinal) >= 0 || key.IndexOf("living", StringComparison.Ordinal) >= 0 || key.IndexOf("dead", StringComparison.Ordinal) >= 0) return _iconNpc;
            if (key.IndexOf("hud", StringComparison.Ordinal) >= 0 || key.IndexOf("ui", StringComparison.Ordinal) >= 0 || key.IndexOf("cam", StringComparison.Ordinal) >= 0 || key.IndexOf("debug", StringComparison.Ordinal) >= 0 || key.IndexOf("stats", StringComparison.Ordinal) >= 0 || key.IndexOf("audio", StringComparison.Ordinal) >= 0 || key.IndexOf("object", StringComparison.Ordinal) >= 0) return _iconVisual;
            if (key.IndexOf("save", StringComparison.Ordinal) >= 0) return _iconSave;
            if (key.IndexOf("cheat", StringComparison.Ordinal) >= 0 || key.IndexOf("pvp", StringComparison.Ordinal) >= 0 || key.IndexOf("guard", StringComparison.Ordinal) >= 0 || key.IndexOf("server", StringComparison.Ordinal) >= 0) return _iconServer;
            if (key.IndexOf("help", StringComparison.Ordinal) >= 0 || key.IndexOf("clear", StringComparison.Ordinal) >= 0 || key.IndexOf("dump", StringComparison.Ordinal) >= 0 || key.IndexOf("gc", StringComparison.Ordinal) >= 0) return _iconRaw;
            return _iconDebug;
        }

        private void DrawDebugQuickItemButton(string itemName, float width)
        {
            if (GUILayout.Button(itemName, _quietButtonStyle, GUILayout.Width(width), GUILayout.Height(32f)))
            {
                _debugItemInput = itemName;
                _debugItemCountInput = "1";
                RunDebugItemAction("_additem");
            }
        }

        private void DrawDebugRadiusButton(string label, string methodName, string fallbackCommand, string additionalArgument = null)
        {
            if (DrawDebugCommandButton(_iconWarning, label, _dangerButtonStyle, 92f, 32f, methodName, "Run " + label + " with the selected radius."))
            {
                float radius;
                if (!TryParseLocalDebugFloat(_debugRadiusInput, "Radius", 1f, 80f, out radius)) return;
                string argument = radius.ToString("G9", CultureInfo.InvariantCulture);
                if (!String.IsNullOrWhiteSpace(additionalArgument)) argument += " " + additionalArgument;
                RunDebugConsoleAction(label + " radius", methodName, argument, fallbackCommand + " " + argument);
            }
        }

        private void RunDebugRawCommand()
        {
            string command = (_debugCommandInput ?? "").Trim();
            if (String.IsNullOrEmpty(command))
            {
                ActionFail("Debug command is empty");
                return;
            }
            bool ok = InvokeConsoleCommand(command);
            ActionResult(ok, "Debug command sent: " + Shorten(command, 58), DebugCommandFailure("Debug command"));
        }

        private int ResolveDebugItemId(string item)
        {
            item = CleanConfigText(item, 80);
            int parsed;
            if (TryConvertToInt(item, out parsed) && parsed > 0)
            {
                return parsed;
            }
            return ResolveItemIdByName(item);
        }

        private static object GetLocalPlayerInventoryForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return null;
            }
            return ReflectionTools.GetStaticMemberValue(localPlayer, "Inventory")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_Inventory")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_Inventory_k__BackingField")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_inventory");
        }

        private void RunDebugConsoleAction(string label, string methodName, string arg, string fallbackCommand)
        {
            arg = CleanConfigText(arg, 160);
            fallbackCommand = CleanConfigText(fallbackCommand, 180);
            if (String.IsNullOrEmpty(methodName) && String.IsNullOrEmpty(fallbackCommand))
            {
                ActionFail(label + " failed: no command route");
                return;
            }
            if (!ValidateDebugActionArgument(methodName, ref arg)) return;
            if (!String.IsNullOrEmpty(fallbackCommand)) fallbackCommand = BuildDebugCommand(GetDebugCommandName(fallbackCommand), arg);
            bool ok = InvokeDebugConsoleMethod(methodName, arg, fallbackCommand);
            ActionResult(ok, label + " requested" + GetWorldCommandObservation(methodName), DebugCommandFailure(label));
        }

        private void SetDebugCoordsFromLocalPosition()
        {
            Vector3 position;
            object transform = GetLocalPlayerTransformForAdmin();
            if (transform == null || !TryGetObjectWorldPosition(transform, out position))
            {
                ActionFail("Coordinate capture failed");
                return;
            }
            _debugCoordInput = FormatDebugCoords(position);
            ActionOk("Coordinates captured: " + _debugCoordInput);
        }

        private static string FormatDebugCoords(Vector3 value)
        {
            return value.x.ToString("0.0", CultureInfo.InvariantCulture)
                + " " + value.y.ToString("0.0", CultureInfo.InvariantCulture)
                + " " + value.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string BuildDebugCommand(string command, string arg)
        {
            command = CleanConfigText(command, 80);
            arg = CleanConfigText(arg, 120);
            return String.IsNullOrEmpty(arg) ? command : command + " " + arg;
        }

        private static float ParseDebugFloat(string text, float fallback, float min, float max)
        {
            float parsed;
            if (!Single.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || !IsFiniteDebugValue(parsed))
            {
                parsed = fallback;
            }
            if (parsed < min)
            {
                parsed = min;
            }
            if (parsed > max)
            {
                parsed = max;
            }
            return parsed;
        }

        private void ResetLocalMovementDebugInputs()
        {
            _debugWalkInput = "5";
            _debugRunInput = "8";
            _debugSwimInput = "4";
            _debugJumpInput = "1";
            ActionOk("Movement debug inputs reset");
        }

        private static object GetLocalFpCharacterForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return null;
            }
            return ReflectionTools.GetStaticMemberValue(localPlayer, "FpCharacter")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_FpCharacter")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_fpCharacter")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "FirstPersonCharacter");
        }

        private static object GetLocalPlayerBaseTransformForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return null;
            }
            object playerBase = ReflectionTools.GetStaticMemberValue(localPlayer, "PlayerBase")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_PlayerBase")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_playerBase");
            return ExtractTransform(playerBase);
        }

        private static object GetLocalPlayerTransformForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return null;
            }
            return ReflectionTools.GetStaticMemberValue(localPlayer, "Transform")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_Transform")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "transform");
        }

        private static object GetLocalCameraTransformForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer != null)
            {
                object transform = ReflectionTools.GetStaticMemberValue(localPlayer, "MainCamTr")
                    ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_MainCamTr");
                if (transform != null)
                {
                    return transform;
                }
            }
            Camera main = Camera.main;
            return main == null ? null : main.transform;
        }

        private static string FormatVector(Vector3 value)
        {
            return value.x.ToString("0.0", CultureInfo.InvariantCulture)
                + ", " + value.y.ToString("0.0", CultureInfo.InvariantCulture)
                + ", " + value.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void ApplyAliasRule()
        {
            string key = CleanName(_aliasKeyInput);
            string alias = CleanName(_aliasNameInput);
            if (key.Length == 0 || alias.Length == 0)
            {
                ActionFail("Alias rule needs both fields");
                return;
            }

            _aliases[key] = alias;
            ClearAuthorityNamePushForAliasKey(key);
            bool saved = SaveConfig();
            RefreshPlayers(false);
            int authorityTouches = PushAliasAuthorityRule(key, alias, true);
            int uiTouches = SweepAliasSurfaces();
            _lastAliasSweepTouches = authorityTouches + uiTouches;
            RefreshPlayers();
            ReportSettingsSave(saved, "Alias rule active"
                + " | authority " + authorityTouches.ToString(CultureInfo.InvariantCulture) + " | UI " + uiTouches.ToString(CultureInfo.InvariantCulture));
        }

        private void RemoveAliasRule()
        {
            string key = CleanName(_aliasKeyInput);
            PlayerEntry selected = GetSelectedPlayer();
            if (String.IsNullOrEmpty(key) && selected != null)
            {
                key = !String.IsNullOrEmpty(selected.SteamId) ? selected.SteamId : selected.Name;
            }
            if (String.IsNullOrEmpty(key))
            {
                ActionFail("Remove rule failed: no alias key selected");
                return;
            }
            if (_aliases.Remove(key))
            {
                ClearAuthorityNamePushForAliasKey(key);
                bool saved = SaveConfig();
                _aliasKeyInput = "";
                _aliasNameInput = "";
                RefreshPlayers();
                ReportSettingsSave(saved, "Alias rule removed for " + key);
            }
            else
            {
                ActionFail("No alias rule found for " + key);
            }
        }

        private void PushAliases()
        {
            bool saved = SaveConfig();
            RefreshPlayers(false);
            int authorityTouches = PushAliasAuthorityRules(true);
            int uiTouches = SweepAliasSurfaces();
            _lastAliasSweepTouches = authorityTouches + uiTouches;
            RefreshPlayers();
            ReportSettingsSave(saved, "Name push touched " + _lastAliasSweepTouches.ToString(CultureInfo.InvariantCulture) + " object(s) | authority " + authorityTouches.ToString(CultureInfo.InvariantCulture));
        }

        private int PushAliasAuthorityRules(bool force)
        {
            int changes = 0;
            foreach (KeyValuePair<string, string> pair in _aliases)
            {
                changes += PushAliasAuthorityRule(pair.Key, pair.Value, force);
            }
            return changes;
        }

        private int PushAliasAuthorityRule(string key, string alias, bool force)
        {
            key = CleanName(key);
            alias = CleanName(alias);
            if (String.IsNullOrEmpty(key) || String.IsNullOrEmpty(alias))
            {
                return 0;
            }

            ulong steamId;
            if (!TryResolveAliasRuleSteamId(key, out steamId))
            {
                return 0;
            }

            string id = steamId.ToString(CultureInfo.InvariantCulture);
            string last;
            if (!force && _lastAuthorityNamePush.TryGetValue(id, out last) && String.Equals(last, alias, StringComparison.Ordinal))
            {
                return 0;
            }

            int changes = 0;
            if (InvokeRolesMethod("UpdatePlayerName", new object[] { steamId, alias }))
            {
                changes++;
            }
            changes += PushAliasToPlayerRoleData(steamId, alias);
            if (changes > 0)
            {
                _lastAuthorityNamePush[id] = alias;
            }
            return changes;
        }

        private void ClearAuthorityNamePushForAliasKey(string key)
        {
            string id = CleanSteamId(key);
            if (LooksLikeSteamId(id))
            {
                _lastAuthorityNamePush.Remove(id);
                return;
            }
            _lastAuthorityNamePush.Clear();
        }

        private bool TryResolveAliasRuleSteamId(string key, out ulong steamId)
        {
            steamId = 0UL;
            key = CleanName(key);
            if (String.IsNullOrEmpty(key))
            {
                return false;
            }

            string id = CleanSteamId(key);
            if (LooksLikeSteamId(id) && IsCurrentAuthorityAliasTarget(id))
            {
                return UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
            }

            PlayerEntry entry = FindCurrentPlayerByAliasKey(key);
            if (entry == null || !LooksLikeSteamId(entry.SteamId))
            {
                return false;
            }
            return UInt64.TryParse(entry.SteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
        }

        private PlayerEntry FindCurrentPlayerByAliasKey(string key)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (PlayerEntryMatchesAliasKey(_players[i], key) && IsAuthorityAliasTarget(_players[i]))
                {
                    return _players[i];
                }
            }
            foreach (PlayerEntry entry in _registeredPlayers.Values)
            {
                if (PlayerEntryMatchesAliasKey(entry, key) && IsAuthorityAliasTarget(entry))
                {
                    return entry;
                }
            }
            foreach (PlayerEntry entry in _sessionPlayers.Values)
            {
                if (PlayerEntryMatchesAliasKey(entry, key) && IsAuthorityAliasTarget(entry))
                {
                    return entry;
                }
            }
            return null;
        }

        private bool IsCurrentAuthorityAliasTarget(string steamId)
        {
            PlayerEntry entry = FindPlayerBySteamId(steamId);
            if (IsAuthorityAliasTarget(entry))
            {
                return true;
            }
            foreach (PlayerEntry registered in _registeredPlayers.Values)
            {
                if (String.Equals(registered.SteamId, steamId, StringComparison.OrdinalIgnoreCase) && IsAuthorityAliasTarget(registered))
                {
                    return true;
                }
            }
            foreach (PlayerEntry session in _sessionPlayers.Values)
            {
                if (String.Equals(session.SteamId, steamId, StringComparison.OrdinalIgnoreCase) && IsAuthorityAliasTarget(session))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAuthorityAliasTarget(PlayerEntry entry)
        {
            return entry != null
                && !entry.PresenceUnresolved
                && LooksLikeSteamId(entry.SteamId)
                && (entry.Online || entry.SourceObject != null || entry.BoltEntity != null || entry.BoltConnection != null || entry.Transform != null);
        }

        private static bool PlayerEntryMatchesAliasKey(PlayerEntry entry, string key)
        {
            if (entry == null || String.IsNullOrEmpty(key))
            {
                return false;
            }
            string cleanKey = CleanName(key);
            string steamKey = CleanSteamId(key);
            if (LooksLikeSteamId(steamKey)
                && (String.Equals(entry.SteamId, steamKey, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(entry.Key, steamKey, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            if (String.Equals(entry.Key, cleanKey, StringComparison.OrdinalIgnoreCase)
                || String.Equals(entry.Name, cleanKey, StringComparison.OrdinalIgnoreCase)
                || String.Equals(entry.Alias, cleanKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            for (int i = 0; i < entry.ObservedNames.Count; i++)
            {
                if (String.Equals(entry.ObservedNames[i], cleanKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private int PushAliasToPlayerRoleData(ulong steamId, string alias)
        {
            object data = GetPlayerRoleDataObject();
            if (data == null)
            {
                return 0;
            }

            int changes = 0;
            object[] args = new object[] { steamId, null };
            object result;
            if (InvokeMethod(data, data.GetType(), "TryGetData", args, false, out result) && result is bool && (bool)result && args.Length > 1 && args[1] != null)
            {
                changes += PushAliasToPlayerDataObject(args[1], steamId, alias);
            }

            object playerList = ReflectionTools.GetMemberValue(data, "PlayerList") ?? ReflectionTools.InvokeNoArgMethod(data, "get_PlayerList");
            if (playerList != null)
            {
                VisitCollectionItems(playerList, delegate(object item)
                {
                    changes += PushAliasToPlayerDataObject(item, steamId, alias);
                    return true;
                });
            }
            return changes;
        }

        private object GetPlayerRoleDataObject()
        {
            Type rolesType = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoles") ?? ReflectionTools.FindType("MultiplayerPlayerRoles");
            object roles = FindRolesRuntimeObject(rolesType);
            object data = ReflectionTools.GetMemberValue(roles, "_playerRoleData")
                ?? ReflectionTools.GetMemberValue(roles, "PlayerRoleData")
                ?? ReflectionTools.GetMemberValue(roles, "playerRoleData");
            if (data != null)
            {
                return data;
            }

            Type dataType = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoleData") ?? ReflectionTools.FindType("MultiplayerPlayerRoleData");
            return FindRuntimeObjectNoStaticProperties(dataType);
        }

        private int PushAliasToPlayerDataObject(object playerData, ulong steamId, string alias)
        {
            if (playerData == null)
            {
                return 0;
            }

            string id = CleanSteamId(ExtractSteamId(playerData));
            bool idMatches = LooksLikeSteamId(id) && String.Equals(id, steamId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            object matches = ReflectionTools.InvokeInstanceMethodResult(playerData, "Matches", new object[] { steamId });
            if (!idMatches && !(matches is bool && (bool)matches))
            {
                return 0;
            }

            int changes = 0;
            object currentObj = ReflectionTools.InvokeNoArgMethod(playerData, "GetPlayerName")
                ?? ReflectionTools.GetMemberValue(playerData, "_lastKnownPlayerName");
            string current = currentObj as string;
            if (!String.Equals(current, alias, StringComparison.Ordinal))
            {
                if (ReflectionTools.InvokeInstanceMethod(playerData, "SetPlayerName", new object[] { alias }))
                {
                    changes++;
                }
                if (ReflectionTools.SetMemberValue(playerData, "_lastKnownPlayerName", alias))
                {
                    changes++;
                }
            }
            return changes;
        }

        private void ReloadAliases()
        {
            if (!LoadConfig()) return;
            RefreshPlayers();
            ActionOk("Settings and aliases reloaded");
        }

        private void ActionResult(bool ok, string success, string failure)
        {
            if (ok)
            {
                ActionOk(success);
            }
            else
            {
                ActionFail(failure);
            }
        }

        private void ActionOk(string message)
        {
            _lastAction = message;
            AppendEventLog("SYSTEM", message);
            LogInfo(message);
        }

        private void ActionFail(string message)
        {
            _lastAction = message;
            AppendEventLog("WARN", message);
            LogWarning(message);
        }

        private void AppendEventLog(string channel, string message)
        {
            string text = "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] "
                + channel + "  " + (message ?? "");
            _eventLog.Add(text);
            while (_eventLog.Count > 40)
            {
                _eventLog.RemoveAt(0);
            }
            _eventLogScroll.y = 99999f;
        }

        private void RefreshPlayers()
        {
            RefreshPlayers(true);
        }

        private void RefreshPlayers(bool verbose)
        {
            _nextRosterRefreshUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(1f, _rosterRefreshSeconds));
            bool inWorld = IsLocalPlayerInWorld();
            string context = GetRosterContext();
            if (String.IsNullOrEmpty(context) && inWorld && _lastRosterInWorld && ReadNetworkFlag("isRunning") == true)
            {
                context = _lastRosterContext;
            }
            bool contextChanged = !String.Equals(context ?? "", _lastRosterContext ?? "", StringComparison.OrdinalIgnoreCase)
                && (!String.IsNullOrEmpty(context) || !String.IsNullOrEmpty(_lastRosterContext));
            bool worldChanged = _hasRosterWorldState && inWorld != _lastRosterInWorld;
            if (contextChanged || worldChanged)
            {
                ResetSessionRoster(contextChanged ? "network context changed" : "world/lobby state changed");
            }
            ExpireRecentDisconnects(DateTime.UtcNow);
            _lastRosterContext = context ?? "";
            _lastRosterInWorld = inWorld;
            _hasRosterWorldState = true;
            EnsurePlayerHistoryForContext(context);

            Dictionary<string, PlayerEntry> liveEntries = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
            List<string> diagnostics = new List<string>();
            int localAdded = AddLocalPlayerEntry(liveEntries);
            if (localAdded > 0)
            {
                diagnostics.Add("LocalPlayer+" + localAdded.ToString(CultureInfo.InvariantCulture));
            }
            int hookAdded = AddRegisteredClientCacheEntries(liveEntries);
            if (hookAdded > 0)
            {
                diagnostics.Add("registered hook cache+" + hookAdded.ToString(CultureInfo.InvariantCulture));
            }
            int registeredAdded = AddGameServerRegisteredClients(liveEntries);
            if (registeredAdded > 0)
            {
                diagnostics.Add("GameServerManager.clients+" + registeredAdded.ToString(CultureInfo.InvariantCulture));
            }
            int utilityAdded = AddMultiplayerUtilityPlayerEntities(liveEntries);
            if (utilityAdded > 0)
            {
                diagnostics.Add("MultiplayerUtilities.entities+" + utilityAdded.ToString(CultureInfo.InvariantCulture));
            }
            int trackerAdded = AddPlayerTrackerEntries(liveEntries);
            if (trackerAdded > 0)
            {
                diagnostics.Add("PlayerTracker.entities+" + trackerAdded.ToString(CultureInfo.InvariantCulture));
            }
            int serverInfoAdded = AddCoopServerInfoSteamIds(liveEntries);
            if (serverInfoAdded > 0)
            {
                diagnostics.Add("CoopServerInfo.steamIds+" + serverInfoAdded.ToString(CultureInfo.InvariantCulture));
            }
            int blobAdded = AddBlobSyncConnectionEntries(liveEntries);
            if (blobAdded > 0)
            {
                diagnostics.Add("BlobSync.connections+" + blobAdded.ToString(CultureInfo.InvariantCulture));
            }
            int knownAdded = AddKnownPlayerListEntries(liveEntries);
            if (knownAdded > 0)
            {
                diagnostics.Add("PlayerListBase.known+" + knownAdded.ToString(CultureInfo.InvariantCulture));
            }
            int visiblePlayerListAdded = AddVisiblePlayerListElementEntries(liveEntries);
            if (visiblePlayerListAdded > 0)
            {
                diagnostics.Add("PlayerListElementView.visible+" + visiblePlayerListAdded.ToString(CultureInfo.InvariantCulture));
            }
            int nameAdded = AddPlayerNameEntries(liveEntries);
            if (nameAdded > 0)
            {
                diagnostics.Add("PlayerName+" + nameAdded.ToString(CultureInfo.InvariantCulture));
            }
            int visibleLobbyAdded = AddVisibleCoopLobbyDialogMembers(liveEntries);
            if (visibleLobbyAdded > 0)
            {
                diagnostics.Add("CoopLobbyDialogGui.visible+" + visibleLobbyAdded.ToString(CultureInfo.InvariantCulture));
            }
            var lobbyMembers = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
            int activeLobbyAdded = AddActiveCoopLobbyMembers(lobbyMembers);
            foreach (var member in lobbyMembers)
            {
                MergePlayerEntry(GetOrCreateEntry(liveEntries, member.Key), member.Value);
            }
            if (activeLobbyAdded > 0)
            {
                diagnostics.Add("CoopLobbyManager.active+" + activeLobbyAdded.ToString(CultureInfo.InvariantCulture));
            }
            string[] typeNames = new string[]
            {
                "Sons.Multiplayer.GameServerManager",
                "Sons.Multiplayer.PlayerData",
                "PlayerNetwork",
                "Sons.Multiplayer.PlayerNetwork",
                "CoopPlayerRemoteSetup",
                "Sons.Multiplayer.MultiplayerPlayerRoleData",
                "Sons.Multiplayer.MultiplayerPlayerRoles",
                "Sons.Multiplayer.Gui.PlayerListBase",
                "Sons.Multiplayer.Gui.PlayerName",
                "Sons.Multiplayer.Gui.PlayerNameUiLink"
            };

            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = ReflectionTools.FindType(typeNames[i]);
                if (type == null)
                {
                    continue;
                }
                int before = liveEntries.Count;
                AddPlayerObjectsOfType(type, typeNames[i], liveEntries);
                AddStaticPlayerRoots(type, typeNames[i], liveEntries);
                int roots = AddStaticPlayerMethodRoots(type, typeNames[i], liveEntries);
                int added = liveEntries.Count - before;
                if (added > 0)
                {
                    diagnostics.Add(typeNames[i] + "+" + added.ToString(CultureInfo.InvariantCulture));
                }
                else if (roots > 0)
                {
                    diagnostics.Add(typeNames[i] + " roots=" + roots.ToString(CultureInfo.InvariantCulture));
                }
            }

            var unresolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool loaderRoster = TryCollectLoaderRoster(liveEntries, unresolved, diagnostics);
            int unprovenPlayers = loaderRoster ? 0 : RemoveUnprovenPlayers(liveEntries, inWorld, lobbyMembers);
            if (unprovenPlayers > 0)
            {
                diagnostics.Add("unproven players suppressed " + unprovenPlayers.ToString(CultureInfo.InvariantCulture));
            }

            DateTime now = DateTime.UtcNow;
            int suppressedDisconnects = loaderRoster ? 0 : SuppressRecentlyDisconnectedEntries(liveEntries, now);
            if (suppressedDisconnects > 0)
            {
                diagnostics.Add("recent disconnects suppressed " + suppressedDisconnects.ToString(CultureInfo.InvariantCulture));
            }
            HashSet<string> previouslyOnline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerEntry entry in _sessionPlayers.Values)
            {
                if (entry != null)
                {
                    string previousSteamId = CleanSteamId(entry.SteamId);
                    if (entry.Online && LooksLikeSteamId(previousSteamId))
                    {
                        previouslyOnline.Add(previousSteamId);
                    }
                    entry.PresenceUnresolved = unresolved.TryGetValue(previousSteamId, out string presenceDetail);
                    entry.PresenceDetail = presenceDetail;
                    if (entry.PresenceUnresolved)
                        ClearPlayerHandles(entry);
                    else
                        entry.Online = false;
                }
            }

            foreach (PlayerEntry live in liveEntries.Values)
            {
                if (live == null)
                {
                    continue;
                }
                string key = !String.IsNullOrEmpty(live.SteamId) ? live.SteamId : live.Key;
                if (String.IsNullOrEmpty(key))
                {
                    continue;
                }
                PlayerEntry session;
                if (!_sessionPlayers.TryGetValue(key, out session))
                {
                    session = new PlayerEntry();
                    session.Key = key;
                    session.FirstSeenUtc = now;
                    _sessionPlayers[key] = session;
                }
                bool joined = !previouslyOnline.Contains(CleanSteamId(live.SteamId)) || HasRosterSessionChanged(session, live);
                MergePlayerEntry(session, live);
                session.BoltConnection = live.BoltConnection;
                session.BoltEntity = live.BoltEntity;
                session.SourceObject = live.SourceObject;
                session.Transform = live.Transform;
                session.NetworkId = live.NetworkId;
                session.ConnectionId = live.ConnectionId;
                session.Online = true;
                CopyRosterObservation(session, live, now);
                session.LastRosterContext = NormalizeHistoryContext(context);
                string sessionSteamId = CleanSteamId(session.SteamId);
                _recentDisconnects.Remove(sessionSteamId);
                TouchPlayerHistory(session, joined ? "Joined server" : null, joined, false);
                if (inWorld
                    && LooksLikeSteamId(sessionSteamId)
                    && joined
                    && !IsLocalPlayerEntry(session))
                {
                    AllowFreshJoinMovementGrace(sessionSteamId, "roster join/spawn sync");
                }
            }

            foreach (string steamId in previouslyOnline)
            {
                if (_sessionPlayers.TryGetValue(steamId, out PlayerEntry session) && !session.Online)
                {
                    MarkPlayerDisconnected(steamId, "roster refresh");
                }
            }
            _nextRosterRefreshUtc = GetSettingsDeadline(now, Math.Max(1f, _rosterRefreshSeconds));
            PruneOfflinePlayers(now);

            RebuildPlayersFromSession();
            SavePlayerHistoryThrottled(false);
            _lastRosterDiagnostics = diagnostics.Count == 0 ? "no live sources found" : String.Join(", ", diagnostics.ToArray());
            string summary = CountPlayers(true).ToString(CultureInfo.InvariantCulture) + "/" + CountPlayers(false).ToString(CultureInfo.InvariantCulture) + "/" + CountUnresolvedPlayers().ToString(CultureInfo.InvariantCulture) + "/" + _lastRosterDiagnostics;
            if (verbose || !String.Equals(summary, _lastRosterSummary, StringComparison.Ordinal))
            {
                LogInfo("Roster refresh: " + CountPlayers(true).ToString(CultureInfo.InvariantCulture) + " online, " + CountPlayers(false).ToString(CultureInfo.InvariantCulture) + " offline, " + CountUnresolvedPlayers().ToString(CultureInfo.InvariantCulture) + " presence unknown (" + _lastRosterDiagnostics + ").");
            }
            _lastRosterSummary = summary;
        }

        private void ResetSessionRoster(string reason)
        {
            if (_sessionPlayers.Count > 0 || _manualTarget != null || !String.IsNullOrEmpty(_selectedPlayerKey))
            {
                LogInfo("Roster session reset: " + reason);
            }
            SavePlayerHistoryThrottled(true);
            ResetDebugToggleStates();
            ResetItemCatalog();
            if (_possession.Active || _possession.NeedsRelease)
            {
                if (!_possession.Stop("Session changed")) LogWarning(_possession.LastStatus);
            }
            _sessionPlayers.Clear();
            _registeredPlayers.Clear();
            _players.Clear();
            _jailedPlayers.Clear();
            ClearJailReturns();
            _antiCheatNoticeTimes.Clear();
            ClearModerationNoticeTracking();
            _itemEventWindows.Clear();
            ClearLoaderAuditState();
            _inventorySignals.Clear();
            _blockedVehicleSpawnProbes.Clear();
            _guardEvidence.Clear();
            ResetLoaderRequestEvidence();
            ResetLoaderSessionEvidence();
            ResetLoaderChatEvidence();
            ResetLoaderPlayerHitEvidence();
            ResetLoaderStructureHitEvidence();
            ResetLoaderBlueprintEvidence();
            ResetLoaderStructureRelocationEvidence();
            ResetLoaderConstructionResourceEvidence();
            ResetLoaderStorageEvidence();
            ResetLoaderVailHitEvidence();
            ResetLoaderDismemberHitEvidence();
            ResetLoaderWorldObjectEvidence();
            ResetServerSleepPolicy();
            ClearServerItemApprovals();
            ClearServerRoleObservations();
            ClearServerConnectionObservations();
            ResetServerAdmissionObservations();
            _unattributedGuardEvents = 0;
            _lastUnattributedGuardUtc = DateTime.MinValue;
            _lastUnattributedGuardEvent = "";
            _recentDisconnects.Clear();
            _movementSignals.Clear();
            _movementTelemetrySignals.Clear();
            _currentGolfCarts.Clear();
            _golfCartScanFrame = -1;
            _manualTarget = null;
            _selectedPlayerIndex = 0;
            _selectedPlayerKey = "";
            _loadedPlayerHistoryContext = "";
        }

        private int SuppressRecentlyDisconnectedEntries(Dictionary<string, PlayerEntry> entries, DateTime now)
        {
            if (entries == null || entries.Count == 0 || _recentDisconnects.Count == 0)
            {
                return 0;
            }
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, PlayerEntry> pair in entries)
            {
                PlayerEntry entry = pair.Value;
                string steamId = entry == null ? CleanSteamId(pair.Key) : CleanSteamId(entry.SteamId);
                if (!LooksLikeSteamId(steamId))
                {
                    steamId = CleanSteamId(pair.Key);
                }
                DateTime until;
                if (LooksLikeSteamId(steamId) && _recentDisconnects.TryGetValue(steamId, out until) && now < until)
                {
                    if (!HasFreshNetworkProof(entry, steamId))
                    {
                        remove.Add(pair.Key);
                    }
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                entries.Remove(remove[i]);
            }
            return remove.Count;
        }

        private void ExpireRecentDisconnects(DateTime now)
        {
            if (_recentDisconnects.Count == 0)
            {
                return;
            }
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in _recentDisconnects)
            {
                if (now >= pair.Value)
                {
                    remove.Add(pair.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                _recentDisconnects.Remove(remove[i]);
            }
        }

        private static bool IsLocalPlayerInWorld()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return false;
            }
            object instance = ReflectionTools.GetStaticMemberValue(localPlayer, "_instance") ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get__instance");
            object transform = ReflectionTools.GetStaticMemberValue(localPlayer, "Transform") ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_Transform");
            return instance != null || transform != null;
        }

        private int AddLocalPlayerEntry(Dictionary<string, PlayerEntry> entries)
        {
            if (!IsLocalPlayerInWorld())
            {
                return 0;
            }

            object localPlayer = GetLocalPlayerInstanceForAdmin();
            object localTransform = GetLocalPlayerTransformForAdmin();
            object entity = GetLocalPlayerEntityForAdmin(localPlayer, localTransform);
            string id = CleanSteamId(GetSteamIdFromEntity(entity));
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(ExtractSteamId(localPlayer));
            }
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(ExtractSteamId(entity));
            }
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(GetLocalSteamIdFromRuntime());
            }
            if (!LooksLikeSteamId(id))
            {
                return 0;
            }

            string name = GetLocalPlayerNameForAdmin(localPlayer, entity);
            object connection = null;
            ulong steamId;
            if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
            {
                connection = GetConnectionFromSteamId(steamId);
            }

            object sourceObject = localPlayer ?? entity ?? localTransform;
            bool added = AddLiveNetworkPlayer(entries, id, name, "LocalPlayer", sourceObject, connection, entity);
            if (!added)
            {
                return 0;
            }

            PlayerEntry entry = entries[id];
            if (entry.Transform == null)
            {
                entry.Transform = localTransform;
            }
            if (String.IsNullOrEmpty(entry.Role))
            {
                entry.Role = "Local";
            }
            AddPlayerSource(entry, "LocalPlayer");
            return 1;
        }

        private static object GetLocalPlayerInstanceForAdmin()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayer == null)
            {
                return null;
            }

            string[] names = new string[] { "_instance", "Instance", "LocalInstance", "Player", "FpCharacter", "get__instance", "get_Instance", "get_LocalInstance" };
            for (int i = 0; i < names.Length; i++)
            {
                object found = ReflectionTools.GetStaticMemberValue(localPlayer, names[i]) ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, names[i]);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static object GetLocalPlayerEntityForAdmin(object localPlayer, object localTransform)
        {
            string[] names = new string[] { "Entity", "BoltEntity", "PlayerEntity", "NetworkEntity", "_entity", "_boltEntity", "entity", "get_Entity", "get_BoltEntity" };
            object entity = ExtractFirstMember(localPlayer, names);
            if (entity != null)
            {
                return entity;
            }

            Type localPlayerType = ReflectionTools.FindType("TheForest.Utils.LocalPlayer");
            if (localPlayerType != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    entity = ReflectionTools.GetStaticMemberValue(localPlayerType, names[i]) ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayerType, names[i]);
                    if (entity != null)
                    {
                        return entity;
                    }
                }
            }

            Type boltEntityType = ReflectionTools.FindType("BoltEntity");
            entity = ReflectionTools.GetComponentInParent(localTransform, boltEntityType);
            if (entity != null)
            {
                return entity;
            }
            return ReflectionTools.GetComponentInParent(localPlayer, boltEntityType);
        }

        private string GetLocalPlayerNameForAdmin(object localPlayer, object entity)
        {
            string name = ExtractPlayerName(localPlayer);
            if (String.IsNullOrEmpty(name))
            {
                name = ExtractNameFromEntity(entity);
            }
            if (String.IsNullOrEmpty(name))
            {
                name = GetLocalSteamNameFromRuntime();
            }
            if (String.IsNullOrEmpty(name))
            {
                name = _localAlias;
            }
            if (String.IsNullOrEmpty(name))
            {
                name = "Local Player";
            }
            return name;
        }

        private static string GetLocalSteamIdFromRuntime()
        {
            string[] typeNames = new string[]
            {
                "Steamworks.SteamUser",
                "SteamUser",
                "Sons.Multiplayer.CoopSteamManager",
                "CoopSteamManager",
                "Sons.Multiplayer.CoopLobbyManager",
                "CoopLobbyManager",
                "Sons.Multiplayer.GameServerManager",
                "GameServerManager"
            };
            string[] memberNames = new string[]
            {
                "GetSteamID",
                "GetSteamId",
                "GetLocalSteamID",
                "GetLocalSteamId",
                "get_SteamID",
                "get_SteamId",
                "get_LocalSteamID",
                "get_LocalSteamId",
                "SteamID",
                "SteamId",
                "LocalSteamID",
                "LocalSteamId",
                "UserSteamID",
                "UserSteamId",
                "OwnerSteamID",
                "OwnerSteamId",
                "Id"
            };
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = ReflectionTools.FindType(typeNames[i]);
                if (type == null)
                {
                    continue;
                }
                for (int j = 0; j < memberNames.Length; j++)
                {
                    object value = ReflectionTools.GetStaticMemberValue(type, memberNames[j]) ?? ReflectionTools.InvokeStaticNoArgMethod(type, memberNames[j]);
                    string id = CleanSteamId(TryGetSteamIdFromValue(value));
                    if (LooksLikeSteamId(id))
                    {
                        return id;
                    }
                }
            }
            return "";
        }

        private static string GetLocalSteamNameFromRuntime()
        {
            string[] typeNames = new string[] { "Steamworks.SteamFriends", "SteamFriends", "Steamworks.SteamClient", "SteamClient" };
            string[] memberNames = new string[] { "GetPersonaName", "get_PersonaName", "PersonaName", "get_Name", "Name", "GetName", "GetSteamName" };
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = ReflectionTools.FindType(typeNames[i]);
                if (type == null)
                {
                    continue;
                }
                for (int j = 0; j < memberNames.Length; j++)
                {
                    string text = CleanPlayerProbe(ReflectionTools.GetStaticMemberValue(type, memberNames[j]) ?? ReflectionTools.InvokeStaticNoArgMethod(type, memberNames[j]));
                    if (!String.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
            return "";
        }

        private int AddVisibleCoopLobbyDialogMembers(Dictionary<string, PlayerEntry> entries)
        {
            Type dialogType = ReflectionTools.FindType("Sons.Gui.Multiplayer.CoopLobbyDialogGui");
            Array dialogs = ReflectionTools.FindObjectsOfType(dialogType);
            if (dialogs == null || dialogs.Length == 0)
            {
                LogVisibleLobbyProbe("dialogs=0 type=" + (dialogType == null ? "null" : dialogType.FullName));
                return 0;
            }

            int added = 0;
            foreach (object dialog in dialogs)
            {
                if (dialog == null)
                {
                    continue;
                }
                object members = ReflectionTools.GetMemberValue(dialog, "_membersGui")
                    ?? ReflectionTools.GetMemberValue(dialog, "MembersGui")
                    ?? ReflectionTools.InvokeNoArgMethod(dialog, "get__membersGui");
                int dictionaryAdded = AddCoopLobbyMemberDictionary(members, "CoopLobbyDialogGui._membersGui", entries);
                added += dictionaryAdded;
                LogVisibleLobbyProbe("dialog=" + DescribeObject(dialog) + " members=" + DescribeObject(members) + " added=" + dictionaryAdded.ToString(CultureInfo.InvariantCulture));

                if (added == 0)
                {
                    object lobby = ReflectionTools.GetMemberValue(dialog, "_lobby")
                        ?? ReflectionTools.GetMemberValue(dialog, "Lobby")
                        ?? ReflectionTools.InvokeNoArgMethod(dialog, "get__lobby");
                    object allMembers = ReflectionTools.GetMemberValue(lobby, "AllMembers")
                        ?? ReflectionTools.InvokeNoArgMethod(lobby, "get_AllMembers");
                    int lobbyAdded = AddCoopLobbySteamIdCollection(allMembers, "CoopLobbyDialogGui._lobby.AllMembers", entries);
                    added += lobbyAdded;
                    LogVisibleLobbyProbe("dialogLobby=" + DescribeObject(lobby) + " allMembers=" + DescribeObject(allMembers) + " added=" + lobbyAdded.ToString(CultureInfo.InvariantCulture));
                }
            }
            return added;
        }

        private void LogVisibleLobbyProbe(string message)
        {
            if (_visibleLobbyProbeLogs++ >= 8)
            {
                return;
            }
            LogInfo("Visible lobby probe " + message);
        }

        private int AddCoopLobbyMemberDictionary(object members, string source, Dictionary<string, PlayerEntry> entries)
        {
            if (members == null)
            {
                return 0;
            }
            int added = 0;
            VisitKeyValuePairs(members, delegate(object key, object value)
            {
                string id = CleanSteamId(ExtractSteamId(key));
                if (!LooksLikeSteamId(id))
                {
                    id = CleanSteamId(ExtractSteamId(value));
                }
                if (!LooksLikeSteamId(id))
                {
                    id = CleanSteamId(TryGetSteamIdFromValue(key));
                }
                if (!LooksLikeSteamId(id))
                {
                    return true;
                }
                int before = entries.Count;
                object member = key ?? value;
                if (AddActiveCoopLobbyMember(member, id, source, entries) && entries.Count > before)
                {
                    added++;
                }
                return true;
            });
            return added;
        }

        private int AddCoopLobbySteamIdCollection(object members, string source, Dictionary<string, PlayerEntry> entries)
        {
            if (members == null)
            {
                return 0;
            }
            int added = 0;
            VisitCollectionItems(members, delegate(object steamIdValue)
            {
                string id = CleanSteamId(ExtractSteamId(steamIdValue));
                if (!LooksLikeSteamId(id))
                {
                    id = CleanSteamId(TryGetSteamIdFromValue(steamIdValue));
                }
                if (!LooksLikeSteamId(id))
                {
                    return true;
                }
                int before = entries.Count;
                if (AddActiveCoopLobbyId(id, steamIdValue, source, entries) && entries.Count > before)
                {
                    added++;
                }
                return true;
            });
            return added;
        }

        private int AddActiveCoopLobbyMembers(Dictionary<string, PlayerEntry> entries)
        {
            Type manager = ReflectionTools.FindType("Sons.Multiplayer.CoopLobbyManager") ?? ReflectionTools.FindType("CoopLobbyManager");
            if (manager == null)
            {
                if (_activeLobbyProbeLogs++ < 8)
                {
                    LogInfo("Active lobby probe manager=null");
                }
                return 0;
            }

            object inLobby = ReflectionTools.InvokeStaticNoArgMethod(manager, "IsInLobby");
            if (inLobby is not true) return 0;
            int expected = TryConvertInt(ReflectionTools.InvokeStaticNoArgMethod(manager, "ActiveMemberCount"), 0);
            int max = expected > 0 ? Math.Min(expected, 64) : 64;
            int added = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            object activeMembers = ReflectionTools.InvokeStaticNoArgMethod(manager, "GetActiveMembers");
            added += AddActiveCoopLobbyIdCollection(manager, activeMembers, "CoopLobbyManager.GetActiveMembers", max, seen, entries);

            if (added > 0)
            {
                LogActiveLobbyProbe(manager, inLobby, expected, activeMembers, null, null, added);
                return added;
            }

            object activeInstance = ReflectionTools.InvokeStaticNoArgMethod(manager, "GetActiveInstance")
                ?? ReflectionTools.GetStaticMemberValue(manager, "_activeInstance")
                ?? ReflectionTools.GetStaticMemberValue(manager, "ActiveInstance");
            object instanceMembers = ReflectionTools.GetMemberValue(activeInstance, "AllMembers")
                ?? ReflectionTools.InvokeNoArgMethod(activeInstance, "get_AllMembers");
            added += AddActiveCoopLobbyIdCollection(manager, instanceMembers, "CoopLobby.GetActiveInstance.AllMembers", max, seen, entries);

            if (added > 0)
            {
                LogActiveLobbyProbe(manager, inLobby, expected, activeMembers, activeInstance, null, added);
                return added;
            }

            object activeDetails = ReflectionTools.InvokeStaticNoArgMethod(manager, "GetActiveMembersDetails");
            VisitCollectionItems(activeDetails, delegate(object member)
            {
                if (added >= max)
                {
                    return false;
                }
                string id = CleanSteamId(ExtractSteamId(member));
                if (!LooksLikeSteamId(id) || !seen.Add(id))
                {
                    return true;
                }
                added += AddActiveCoopLobbyMember(member, id, "CoopLobbyManager.GetActiveMembersDetails", entries) ? 1 : 0;
                return true;
            });
            LogActiveLobbyProbe(manager, inLobby, expected, activeMembers, activeInstance, activeDetails, added);
            return added;
        }

        private int AddActiveCoopLobbyIdCollection(Type manager, object collection, string source, int max, HashSet<string> seen, Dictionary<string, PlayerEntry> entries)
        {
            if (collection == null)
            {
                return 0;
            }
            int added = 0;
            VisitCollectionItems(collection, delegate(object steamIdValue)
            {
                if (seen.Count >= max)
                {
                    return false;
                }
                string id = CleanSteamId(ExtractSteamId(steamIdValue));
                if (!LooksLikeSteamId(id))
                {
                    id = CleanSteamId(TryGetSteamIdFromValue(steamIdValue));
                }
                if (!LooksLikeSteamId(id) || !seen.Add(id))
                {
                    return true;
                }

                object details = ReflectionTools.InvokeStaticMethodResult(manager, "GetMemberDetails", new object[] { steamIdValue, false });
                if (details != null)
                {
                    added += AddActiveCoopLobbyMember(details, id, source, entries) ? 1 : 0;
                }
                else
                {
                    added += AddActiveCoopLobbyId(id, steamIdValue, source, entries) ? 1 : 0;
                }
                return true;
            });
            return added;
        }

        private void LogActiveLobbyProbe(Type manager, object inLobby, int expected, object activeMembers, object activeInstance, object activeDetails, int added)
        {
            if (_activeLobbyProbeLogs++ >= 8)
            {
                return;
            }
            LogInfo("Active lobby probe manager=" + (manager == null ? "null" : manager.FullName)
                + " inLobby=" + DescribeObject(inLobby)
                + " count=" + expected.ToString(CultureInfo.InvariantCulture)
                + " members=" + DescribeObject(activeMembers)
                + " instance=" + DescribeObject(activeInstance)
                + " details=" + DescribeObject(activeDetails)
                + " added=" + added.ToString(CultureInfo.InvariantCulture));
        }

        private bool AddActiveCoopLobbyMember(object member, string id, string source, Dictionary<string, PlayerEntry> entries)
        {
            id = CleanSteamId(id);
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(ExtractSteamId(member));
            }
            if (!LooksLikeSteamId(id))
            {
                return false;
            }

            string name = ExtractPlayerName(member);
            if (String.IsNullOrEmpty(name))
            {
                name = CleanPlayerProbe(ReflectionTools.GetMemberValue(member, "Name") ?? ReflectionTools.InvokeNoArgMethod(member, "get_Name"));
            }

            PlayerEntry entry = GetOrCreateEntry(entries, id);
            if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id;
            ApplyBetterPlayerName(entry, name);
            if (entry.SourceObject == null) entry.SourceObject = member;
            if (String.IsNullOrEmpty(entry.Role))
            {
                bool isHost = ExtractBool(member, "IsHost");
                entry.Role = isHost ? "Host" : entry.Role;
            }
            AddPlayerSource(entry, source);
            return true;
        }

        private bool AddActiveCoopLobbyId(string id, object sourceObject, string source, Dictionary<string, PlayerEntry> entries)
        {
            id = CleanSteamId(id);
            if (!LooksLikeSteamId(id))
            {
                return false;
            }

            PlayerEntry entry = GetOrCreateEntry(entries, id);
            if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id;
            if (entry.SourceObject == null) entry.SourceObject = sourceObject;
            AddPlayerSource(entry, source);
            return true;
        }

        private static int TryConvertInt(object value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryConvertToInt(object value, out int parsed)
        {
            parsed = 0;
            if (value == null)
            {
                return false;
            }
            try
            {
                parsed = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToFloat(object value, out float parsed)
        {
            parsed = 0f;
            if (value == null)
            {
                return false;
            }
            try
            {
                parsed = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadFloatMember(object value, string name, out float parsed)
        {
            parsed = 0f;
            object found = ReflectionTools.GetMemberValue(value, name) ?? ReflectionTools.InvokeNoArgMethod(value, "get_" + name);
            return TryConvertToFloat(found, out parsed);
        }

        private static bool TryReadIntMember(object value, string name, out int parsed)
        {
            parsed = 0;
            object found = ReflectionTools.GetMemberValue(value, name) ?? ReflectionTools.InvokeNoArgMethod(value, "get_" + name);
            return TryConvertToInt(found, out parsed);
        }

        private static bool TryConvertToVector3(object value, out Vector3 parsed)
        {
            parsed = Vector3.zero;
            if (value == null)
            {
                return false;
            }
            if (value is Vector3)
            {
                parsed = (Vector3)value;
                return true;
            }
            object xValue = ReflectionTools.GetMemberValue(value, "x") ?? ReflectionTools.GetMemberValue(value, "X");
            object yValue = ReflectionTools.GetMemberValue(value, "y") ?? ReflectionTools.GetMemberValue(value, "Y");
            object zValue = ReflectionTools.GetMemberValue(value, "z") ?? ReflectionTools.GetMemberValue(value, "Z");
            float x;
            float y;
            float z;
            if (!TryConvertToFloat(xValue, out x) || !TryConvertToFloat(yValue, out y) || !TryConvertToFloat(zValue, out z))
            {
                return false;
            }
            parsed = new Vector3(x, y, z);
            return true;
        }

        private static bool ReadBoolMember(object value, string name)
        {
            object found = ReflectionTools.GetMemberValue(value, name) ?? ReflectionTools.InvokeNoArgMethod(value, "get_" + name);
            return found is bool && (bool)found;
        }

        private static bool ExtractBool(object value, string name)
        {
            object found = ReflectionTools.GetMemberValue(value, name) ?? ReflectionTools.InvokeNoArgMethod(value, "get_" + name);
            return found is bool && (bool)found;
        }

        private int AddKnownPlayerListEntries(Dictionary<string, PlayerEntry> entries)
        {
            Type listType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerListBase");
            object known = ReflectionTools.GetStaticMemberValue(listType, "_knownPlayers") ?? ReflectionTools.InvokeStaticNoArgMethod(listType, "get__knownPlayers");
            return AddKnownPlayerDictionary(known, "PlayerListBase._knownPlayers", entries, null);
        }

        private int AddVisiblePlayerListElementEntries(Dictionary<string, PlayerEntry> entries)
        {
            Type viewType = ReflectionTools.FindType("Sons.UI.PlayerListElementView");
            Array views = ReflectionTools.FindObjectsOfType(viewType);
            if (views == null || views.Length == 0)
            {
                return 0;
            }

            Type activeListType = ReflectionTools.FindType("Sons.Multiplayer.Gui.ActivePlayerList") ?? ReflectionTools.FindType("ActivePlayerList");
            int added = 0;
            foreach (object view in views)
            {
                if (view == null)
                {
                    continue;
                }

                string id = CleanSteamId(ExtractSteamId(view));
                if (!LooksLikeSteamId(id))
                {
                    continue;
                }

                object nameLabel = ReflectionTools.GetMemberValue(view, "_playerName")
                    ?? ReflectionTools.GetMemberValue(view, "PlayerName")
                    ?? ReflectionTools.GetMemberValue(view, "playerName");
                string name = CleanPlayerProbe(ReflectionTools.GetMemberValue(nameLabel, "text")
                    ?? ReflectionTools.InvokeNoArgMethod(nameLabel, "get_text"));
                if (String.IsNullOrEmpty(name))
                {
                    name = ExtractPlayerName(view);
                }

                ulong steamId;
                UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
                object connection = null;
                object entity = null;
                if (steamId != 0)
                {
                    connection = ReflectionTools.InvokeStaticMethodResult(activeListType, "GetConnectionFromSteamId", new object[] { steamId }) ?? GetConnectionFromSteamId(steamId);
                    entity = ReflectionTools.InvokeStaticMethodResult(activeListType, "GetEntityFromSteamId", new object[] { steamId }) ?? GetEntityFromSteamId(steamId);
                }

                int before = entries.Count;
                AddLiveNetworkPlayer(entries, id, name, "PlayerListElementView.visible", view, connection, entity);
                if (entries.Count > before)
                {
                    added += entries.Count - before;
                }
            }
            return added;
        }

        private int AddKnownPlayerDictionary(object known, string source, Dictionary<string, PlayerEntry> entries, object onlyEntity)
        {
            if (known == null)
            {
                return 0;
            }

            int added = 0;
            IDictionary dictionary = known as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry pair in dictionary)
                {
                    added += AddKnownPlayerPair(pair.Key, pair.Value, source, entries, onlyEntity) ? 1 : 0;
                }
                return added;
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(known, "GetEnumerator");
            int guard = 0;
            while (enumerator != null && guard++ < 256)
            {
                object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                if (!(moved is bool) || !(bool)moved)
                {
                    break;
                }
                object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                object key = ReflectionTools.GetMemberValue(current, "Key") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Key");
                object value = ReflectionTools.GetMemberValue(current, "Value") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Value");
                added += AddKnownPlayerPair(key, value, source, entries, onlyEntity) ? 1 : 0;
            }
            return added;
        }

        private bool AddKnownPlayerPair(object key, object value, string source, Dictionary<string, PlayerEntry> entries, object onlyEntity)
        {
            object entity = ExtractFirstMember(value, new string[] { "BoltEntity", "Entity", "PlayerEntity", "NetworkEntity", "_boltEntity", "_entity", "get_BoltEntity" });
            if (onlyEntity != null && !ObjectsSame(entity, onlyEntity))
            {
                return false;
            }

            string id = CleanSteamId(ExtractSteamId(value));
            if (String.IsNullOrEmpty(id))
            {
                id = CleanSteamId(ExtractSteamId(key));
            }
            if (!LooksLikeSteamId(id))
            {
                return false;
            }
            string name = ExtractPlayerName(value);
            string keyText = id;
            if (String.IsNullOrEmpty(keyText))
            {
                return false;
            }

            PlayerEntry entry = GetOrCreateEntry(entries, keyText);
            if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id ?? "";
            ApplyBetterPlayerName(entry, name);
            if (entry.SourceObject == null) entry.SourceObject = value;
            if (entry.BoltConnection == null && key != null && (key.GetType().FullName ?? key.GetType().Name).IndexOf("BoltConnection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                entry.BoltConnection = key;
            }
            if (entry.BoltEntity == null) entry.BoltEntity = entity;
            if (entry.Transform == null) entry.Transform = ExtractTransform(entity) ?? ExtractTransform(value);
            if (String.IsNullOrEmpty(entry.NetworkId)) entry.NetworkId = ExtractNetworkId(entity) ?? ExtractNetworkId(value);
            AddPlayerSource(entry, source);
            return true;
        }

        private int AddPlayerNameEntries(Dictionary<string, PlayerEntry> entries)
        {
            Type playerNameType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerName");
            Type boltEntityType = ReflectionTools.FindType("BoltEntity");
            Array playerNames = ReflectionTools.FindObjectsOfType(playerNameType);
            if (playerNames == null)
            {
                return 0;
            }

            Type listType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerListBase");
            object known = ReflectionTools.GetStaticMemberValue(listType, "_knownPlayers") ?? ReflectionTools.InvokeStaticNoArgMethod(listType, "get__knownPlayers");
            int added = 0;
            foreach (object playerName in playerNames)
            {
                if (playerName == null)
                {
                    continue;
                }
                object entity = ReflectionTools.GetComponentInParent(playerName, boltEntityType);
                int before = entries.Count;
                if (entity != null)
                {
                    AddKnownPlayerDictionary(known, "PlayerName.known", entries, entity);
                }
                string id = CleanSteamId(ExtractSteamId(playerName));
                if (!LooksLikeSteamId(id))
                {
                    continue;
                }
                string name = ExtractPlayerName(playerName);
                string keyText = id;
                if (!String.IsNullOrEmpty(keyText))
                {
                    PlayerEntry entry = GetOrCreateEntry(entries, keyText);
                    if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id ?? "";
                    ApplyBetterPlayerName(entry, name);
                    if (entry.SourceObject == null) entry.SourceObject = playerName;
                    if (entry.BoltEntity == null) entry.BoltEntity = entity;
                    if (entry.Transform == null) entry.Transform = ExtractTransform(playerName) ?? ExtractTransform(entity);
                    if (String.IsNullOrEmpty(entry.NetworkId)) entry.NetworkId = ExtractNetworkId(entity) ?? ExtractNetworkId(playerName);
                    AddPlayerSource(entry, "PlayerName");
                }
                if (entries.Count > before)
                {
                    added += entries.Count - before;
                }
            }
            return added;
        }

        private int AddRegisteredClientCacheEntries(Dictionary<string, PlayerEntry> entries)
        {
            int added = 0;
            foreach (PlayerEntry cached in _registeredPlayers.Values)
            {
                if (cached == null || !LooksLikeSteamId(cached.SteamId))
                {
                    continue;
                }
                if (!HasFreshNetworkProof(cached, cached.SteamId)) continue;
                added += AddLiveNetworkPlayer(entries, cached.SteamId, cached.Name, cached.Source, cached.SourceObject, cached.BoltConnection, cached.BoltEntity) ? 1 : 0;
            }
            return added;
        }

        internal void RememberRegisteredClient(object connection, object steamHandle, string name, string source)
        {
            string id = CleanSteamId(ExtractSteamId(steamHandle));
            if (!LooksLikeSteamId(id))
            {
                id = GetSteamIdFromConnection(connection);
            }
            if (!LooksLikeSteamId(id))
            {
                return;
            }

            bool recentlyDisconnected = _recentDisconnects.ContainsKey(id);
            _recentDisconnects.Remove(id);
            PlayerEntry entry;
            bool added = !_registeredPlayers.TryGetValue(id, out entry);
            if (entry == null)
            {
                entry = new PlayerEntry();
                entry.Key = id;
                entry.FirstSeenUtc = DateTime.UtcNow;
                _registeredPlayers[id] = entry;
            }

            entry.SteamId = id;
            ApplyBetterPlayerName(entry, name);
            if (entry.SourceObject == null)
            {
                entry.SourceObject = steamHandle ?? connection;
            }
            if (connection != null)
            {
                entry.BoltConnection = connection;
            }
            if (entry.BoltEntity == null && connection != null)
            {
                entry.BoltEntity = GetEntityFromConnection(connection);
            }
            if (entry.Transform == null)
            {
                entry.Transform = ExtractTransform(entry.BoltEntity);
            }
            AddPlayerSource(entry, source);
            entry.LastSeenUtc = DateTime.UtcNow;
            _nextRosterRefreshUtc = DateTime.MinValue;
            if (added || recentlyDisconnected)
            {
                AllowFreshJoinMovementGrace(id, "client registration/spawn sync");
            }
            if (added)
            {
                LogInfo("Registered client captured " + id + (String.IsNullOrEmpty(entry.Name) ? "" : " / " + entry.Name) + " via " + source);
            }
        }

        internal void RememberAttachedPlayerEntity(object entity, string source)
        {
            string id = GetSteamIdFromEntity(entity);
            if (!LooksLikeSteamId(id))
            {
                return;
            }
            string name = ExtractNameFromEntity(entity);
            object connection = null;
            ulong steamId;
            if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
            {
                connection = GetConnectionFromSteamId(steamId);
            }
            RememberRegisteredClient(connection, id, name, source);
            PlayerEntry entry;
            if (_registeredPlayers.TryGetValue(id, out entry) && entry != null)
            {
                entry.BoltEntity = entity;
                entry.Transform = ExtractTransform(entity);
                entry.SourceObject = entity;
                AddPlayerSource(entry, source);
            }
        }

        private int AddGameServerRegisteredClients(Dictionary<string, PlayerEntry> entries)
        {
            Type manager = ReflectionTools.FindType("Sons.Multiplayer.GameServerManager") ?? ReflectionTools.FindType("GameServerManager");
            object clients = ReflectionTools.GetStaticMemberValue(manager, "_clientConnectionInfo")
                ?? ReflectionTools.InvokeStaticNoArgMethod(manager, "get__clientConnectionInfo");
            if (manager == null || clients == null)
            {
                return 0;
            }

            int added = 0;
            VisitKeyValuePairs(clients, delegate(object key, object value)
            {
                object connection = TypeNameEquals(key, "BoltConnection") ? key : ExtractFirstMember(value, new string[] { "Connection", "BoltConnection", "PlayerConnection", "_connection", "_boltConnection", "connection" });
                object steamHandle = ExtractTypedMember(value, "CSteamID") ?? ExtractTypedMember(key, "CSteamID");
                if (steamHandle == null && connection != null)
                {
                    steamHandle = GetRegisteredClientSteamHandle(manager, connection);
                }

                string id = CleanSteamId(ExtractSteamId(value));
                if (String.IsNullOrEmpty(id))
                {
                    id = CleanSteamId(ExtractSteamId(key));
                }
                if (String.IsNullOrEmpty(id))
                {
                    id = CleanSteamId(TryGetSteamIdFromValue(steamHandle));
                }
                if (String.IsNullOrEmpty(id))
                {
                    id = GetSteamIdFromConnection(connection);
                }

                string name = ExtractPlayerName(value);
                if (String.IsNullOrEmpty(name))
                {
                    name = ExtractPlayerName(key);
                }
                if (String.IsNullOrEmpty(name))
                {
                    name = GetRegisteredClientName(manager, steamHandle);
                }

                object entity = ExtractFirstMember(value, new string[] { "BoltEntity", "Entity", "PlayerEntity", "NetworkEntity", "_boltEntity", "_entity", "get_BoltEntity" });
                if (entity == null && connection != null)
                {
                    entity = GetEntityFromConnection(connection);
                }
                if (entity == null)
                {
                    ulong steamId;
                    if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
                    {
                        entity = GetEntityFromSteamId(steamId);
                    }
                }

                added += AddLiveNetworkPlayer(entries, id, name, "GameServerManager._clientConnectionInfo", value ?? key, connection, entity) ? 1 : 0;
                return true;
            });
            return added;
        }

        private int AddMultiplayerUtilityPlayerEntities(Dictionary<string, PlayerEntry> entries)
        {
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            object entities = ReflectionTools.GetStaticMemberValue(utilities, "_playerEntities")
                ?? ReflectionTools.InvokeStaticNoArgMethod(utilities, "get__playerEntities");
            return AddBoltEntityCollectionEntries(entities, "MultiplayerUtilities._playerEntities", entries);
        }

        private int AddPlayerTrackerEntries(Dictionary<string, PlayerEntry> entries)
        {
            Type trackerType = ReflectionTools.FindType("PlayerTracker");
            object tracker = ReflectionTools.InvokeStaticNoArgMethod(trackerType, "get_Instance")
                ?? ReflectionTools.GetStaticMemberValue(trackerType, "Instance")
                ?? ReflectionTools.GetStaticMemberValue(trackerType, "_instance");
            if (tracker == null)
            {
                Array trackers = ReflectionTools.FindObjectsOfType(trackerType);
                if (trackers != null && trackers.Length > 0)
                {
                    tracker = trackers.GetValue(0);
                }
            }
            if (tracker == null)
            {
                return 0;
            }

            int added = 0;
            object entities = ReflectionTools.GetMemberValue(tracker, "AllPlayerEntities") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_AllPlayerEntities");
            added += AddBoltEntityCollectionEntries(entities, "PlayerTracker.AllPlayerEntities", entries);
            object players = ReflectionTools.GetMemberValue(tracker, "AllPlayers") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_AllPlayers");
            added += AddBoltEntityCollectionEntries(players, "PlayerTracker.AllPlayers", entries);
            object netPlayer = ReflectionTools.GetMemberValue(tracker, "NetPlayer") ?? ReflectionTools.InvokeNoArgMethod(tracker, "get_NetPlayer");
            if (netPlayer != null)
            {
                object entity = FindBoltEntityForObject(netPlayer);
                added += AddBoltEntityPlayerEntry(entity, "PlayerTracker.NetPlayer", entries) ? 1 : 0;
            }
            return added;
        }

        private int AddCoopServerInfoSteamIds(Dictionary<string, PlayerEntry> entries)
        {
            Type serverInfo = ReflectionTools.FindType("CoopServerInfo");
            object steamIds = ReflectionTools.InvokeStaticNoArgMethod(serverInfo, "GetSteamIds")
                ?? ReflectionTools.InvokeStaticNoArgMethod(serverInfo, "get_SteamIds");
            if (steamIds == null)
            {
                return 0;
            }

            int count;
            int max = TryGetCollectionCount(steamIds, out count) && count > 0 ? Math.Min(count, 64) : 32;
            int added = 0;
            for (int i = 0; i < max; i++)
            {
                object token = ReflectionTools.InvokeInstanceMethodResult(steamIds, "get_Item", new object[] { i })
                    ?? ReflectionTools.InvokeInstanceMethodResult(steamIds, "get_Item", new object[] { (uint)i });
                string id = CleanSteamId(ExtractSteamId(token));
                if (!LooksLikeSteamId(id))
                {
                    continue;
                }

                ulong steamId;
                UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
                object connection = steamId == 0 ? null : GetConnectionFromSteamId(steamId);
                object entity = steamId == 0 ? null : GetEntityFromSteamId(steamId);
                added += AddLiveNetworkPlayer(entries, id, "", "CoopServerInfo.GetSteamIds", token, connection, entity) ? 1 : 0;
            }
            return added;
        }

        private int AddBlobSyncConnectionEntries(Dictionary<string, PlayerEntry> entries)
        {
            Type blobSync = ReflectionTools.FindType("Sons.Multiplayer.BlobSync") ?? ReflectionTools.FindType("BlobSync");
            object map = ReflectionTools.GetStaticMemberValue(blobSync, "_boltToblobSyncConnections")
                ?? ReflectionTools.InvokeStaticNoArgMethod(blobSync, "get__boltToblobSyncConnections");
            if (blobSync == null || map == null)
            {
                return 0;
            }

            int added = 0;
            VisitKeyValuePairs(map, delegate(object key, object value)
            {
                object connection = TypeNameEquals(key, "BoltConnection") ? key : ExtractFirstMember(value, new string[] { "Connection", "BoltConnection", "PlayerConnection", "_connection", "_boltConnection", "connection" });
                if (connection == null)
                {
                    return true;
                }
                string id = GetSteamIdFromConnection(connection);
                if (!LooksLikeSteamId(id))
                {
                    return true;
                }
                object entity = GetEntityFromConnection(connection);
                added += AddLiveNetworkPlayer(entries, id, "", "BlobSync._boltToblobSyncConnections", value ?? key, connection, entity) ? 1 : 0;
                return true;
            });
            return added;
        }

        private int AddBoltEntityCollectionEntries(object collection, string source, Dictionary<string, PlayerEntry> entries)
        {
            if (collection == null)
            {
                return 0;
            }

            int added = 0;
            VisitCollectionItems(collection, delegate(object item)
            {
                object entity = FindBoltEntityForObject(item);
                added += AddBoltEntityPlayerEntry(entity, source, entries) ? 1 : 0;
                return true;
            });
            return added;
        }

        private bool AddBoltEntityPlayerEntry(object entity, string source, Dictionary<string, PlayerEntry> entries)
        {
            if (entity == null)
            {
                return false;
            }

            string id = GetSteamIdFromEntity(entity);
            if (!LooksLikeSteamId(id))
            {
                return false;
            }

            ulong steamId;
            UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
            object connection = steamId == 0 ? null : GetConnectionFromSteamId(steamId);
            string name = ExtractNameFromEntity(entity);
            return AddLiveNetworkPlayer(entries, id, name, source, entity, connection, entity);
        }

        private bool AddLiveNetworkPlayer(Dictionary<string, PlayerEntry> entries, string id, string name, string source, object sourceObject, object connection, object entity)
        {
            id = CleanSteamId(id);
            if (!LooksLikeSteamId(id))
            {
                return false;
            }

            PlayerEntry entry = GetOrCreateEntry(entries, id);
            if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id;
            ApplyBetterPlayerName(entry, name);
            if (entry.SourceObject == null && sourceObject != null) entry.SourceObject = sourceObject;
            if (entry.BoltConnection == null && connection != null) entry.BoltConnection = connection;
            if (entry.BoltEntity == null && entity != null) entry.BoltEntity = entity;
            if (entry.BoltConnection == null)
            {
                ulong steamId;
                if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
                {
                    entry.BoltConnection = GetConnectionFromSteamId(steamId);
                }
            }
            if (entry.BoltEntity == null && entry.BoltConnection != null)
            {
                entry.BoltEntity = GetEntityFromConnection(entry.BoltConnection);
            }
            if (entry.BoltEntity == null)
            {
                ulong steamId;
                if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
                {
                    entry.BoltEntity = GetEntityFromSteamId(steamId);
                }
            }
            if (entry.Transform == null) entry.Transform = ExtractTransform(entry.BoltEntity) ?? ExtractTransform(sourceObject);
            if (String.IsNullOrEmpty(entry.NetworkId))
            {
                string networkId = ExtractNetworkId(entry.BoltEntity);
                if (String.IsNullOrEmpty(networkId))
                {
                    networkId = ExtractNetworkId(sourceObject);
                }
                entry.NetworkId = networkId;
            }
            if (entry.ConnectionId < 0)
            {
                int connectionId = ExtractConnectionId(entry.BoltConnection);
                entry.ConnectionId = connectionId >= 0 ? connectionId : ExtractConnectionId(sourceObject);
            }
            if (String.IsNullOrEmpty(entry.Name))
            {
                ApplyBetterPlayerName(entry, ExtractNameFromEntity(entry.BoltEntity));
            }
            AddPlayerSource(entry, source);
            return true;
        }

        private object FindBoltEntityForObject(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (TypeNameEquals(value, "BoltEntity"))
            {
                return value;
            }
            object entity = ExtractFirstMember(value, new string[] { "BoltEntity", "Entity", "PlayerEntity", "NetworkEntity", "_boltEntity", "_entity", "get_BoltEntity" });
            if (entity != null)
            {
                return entity;
            }
            Type boltEntityType = ReflectionTools.FindType("BoltEntity");
            return ReflectionTools.GetComponentInParent(value, boltEntityType);
        }

        private string GetSteamIdFromEntity(object entity)
        {
            if (entity == null)
            {
                return "";
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            object result = ReflectionTools.InvokeStaticMethodResult(utilities, "GetSteamId", new object[] { entity });
            string id = CleanSteamId(TryGetSteamIdFromValue(result));
            if (LooksLikeSteamId(id))
            {
                return id;
            }
            return CleanSteamId(ExtractSteamId(entity));
        }

        private string GetSteamIdFromConnection(object connection)
        {
            if (connection == null)
            {
                return "";
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            object result = ReflectionTools.InvokeStaticMethodResult(utilities, "GetSteamId", new object[] { connection });
            string id = CleanSteamId(TryGetSteamIdFromValue(result));
            if (LooksLikeSteamId(id))
            {
                return id;
            }
            return CleanSteamId(ExtractSteamId(connection));
        }

        private object GetConnectionFromSteamId(ulong steamId)
        {
            Type activeListType = ReflectionTools.FindType("Sons.Multiplayer.Gui.ActivePlayerList") ?? ReflectionTools.FindType("ActivePlayerList");
            object result = ReflectionTools.InvokeStaticMethodResult(activeListType, "GetConnectionFromSteamId", new object[] { steamId });
            if (result != null)
            {
                return result;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            return ReflectionTools.InvokeStaticMethodResult(utilities, "GetConnectionFromSteamId", new object[] { steamId });
        }

        private object GetConnectionFromConnectionId(int connectionId)
        {
            if (connectionId < 0)
            {
                return null;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            return ReflectionTools.InvokeStaticMethodResult(utilities, "GetConnectionFromConnectionId", new object[] { connectionId });
        }

        private object GetEntityFromSteamId(ulong steamId)
        {
            Type activeListType = ReflectionTools.FindType("Sons.Multiplayer.Gui.ActivePlayerList") ?? ReflectionTools.FindType("ActivePlayerList");
            object result = ReflectionTools.InvokeStaticMethodResult(activeListType, "GetEntityFromSteamId", new object[] { steamId });
            if (result != null)
            {
                return result;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            return ReflectionTools.InvokeStaticMethodResult(utilities, "GetEntityFromSteamId", new object[] { steamId });
        }

        private object GetEntityFromConnectionId(int connectionId)
        {
            if (connectionId < 0)
            {
                return null;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            return ReflectionTools.InvokeStaticMethodResult(utilities, "GetPlayerEntityFromBoltConnectionId", new object[] { connectionId });
        }

        private object GetEntityFromConnection(object connection)
        {
            if (connection == null)
            {
                return null;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            return ReflectionTools.InvokeStaticMethodResult(utilities, "GetEntityFromBoltConnectionForServer", new object[] { connection });
        }

        private object GetRegisteredClientSteamHandle(Type manager, object connection)
        {
            if (manager == null || connection == null)
            {
                return null;
            }
            object[] args = new object[] { connection, null };
            object result;
            if (InvokeMethod(null, manager, "GetRegisteredClient", args, true, out result)
                && result is bool
                && (bool)result
                && args.Length > 1)
            {
                return args[1];
            }
            return null;
        }

        private string GetRegisteredClientName(Type manager, object steamHandle)
        {
            if (manager == null || steamHandle == null)
            {
                return "";
            }
            object[] args = new object[] { steamHandle, null };
            object result;
            if (InvokeMethod(null, manager, "GetRegisteredClientName", args, true, out result)
                && result is bool
                && (bool)result
                && args.Length > 1)
            {
                return CleanPlayerProbe(args[1]);
            }
            return "";
        }

        private string ExtractNameFromEntity(object entity)
        {
            if (entity == null)
            {
                return "";
            }
            string name = ExtractPlayerName(entity);
            if (!String.IsNullOrEmpty(name))
            {
                return name;
            }
            object state = GetPlayerStateFromEntity(entity);
            name = ExtractPlayerName(state);
            if (!String.IsNullOrEmpty(name))
            {
                return name;
            }
            return TryGetBoltPlayerSetupName(entity, state);
        }

        private object GetPlayerStateFromEntity(object entity)
        {
            if (entity == null)
            {
                return null;
            }
            object state = ReflectionTools.GetMemberValue(entity, "State")
                ?? ReflectionTools.GetMemberValue(entity, "state")
                ?? ReflectionTools.InvokeNoArgMethod(entity, "get_State")
                ?? ReflectionTools.InvokeNoArgMethod(entity, "GetState");
            return state;
        }

        private string TryGetBoltPlayerSetupName(object entity, object state)
        {
            if (entity == null || state == null)
            {
                return "";
            }
            object result = InvokeStaticMethodResult("BoltPlayerSetup", "GetPlayerName", new object[] { entity, state });
            if (result is InvokeFailed || result == null)
            {
                return "";
            }
            return CleanPlayerProbe(result);
        }

        private static object ExtractTypedMember(object value, string typeNamePart)
        {
            if (value == null)
            {
                return null;
            }
            if (TypeNameContains(value, typeNamePart))
            {
                return value;
            }
            string[] names = new string[] { "Item1", "Item2", "CSteamId", "CSteamID", "SteamId", "SteamID", "_CSteamId", "_cSteamId", "_steamId", "OwnerSteamId" };
            for (int i = 0; i < names.Length; i++)
            {
                object member = ReflectionTools.GetMemberValue(value, names[i]) ?? ReflectionTools.InvokeNoArgMethod(value, names[i]);
                if (member != null && TypeNameContains(member, typeNamePart))
                {
                    return member;
                }
            }
            return null;
        }

        private static bool TypeNameContains(object value, string text)
        {
            if (value == null || String.IsNullOrEmpty(text))
            {
                return false;
            }
            Type type = value as Type;
            if (type == null)
            {
                type = value.GetType();
            }
            string typeName = type.FullName ?? type.Name;
            return typeName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TypeNameEquals(object value, string text)
        {
            if (value == null || String.IsNullOrEmpty(text))
            {
                return false;
            }
            Type type = value as Type;
            if (type == null)
            {
                type = value.GetType();
            }
            return String.Equals(type.Name, text, StringComparison.OrdinalIgnoreCase)
                || String.Equals(type.FullName, text, StringComparison.OrdinalIgnoreCase);
        }

        private int VisitCollectionItems(object value, Func<object, bool> visitor)
        {
            if (value == null || visitor == null || value is string)
            {
                return 0;
            }

            Array array = value as Array;
            if (array != null)
            {
                int visitedArray = 0;
                try
                {
                    foreach (object item in array)
                    {
                        visitedArray++;
                        if (!visitor(item) || visitedArray >= 512)
                        {
                            break;
                        }
                    }
                    return visitedArray;
                }
                catch { }
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                int visitedEnumerable = 0;
                try
                {
                    foreach (object item in enumerable)
                    {
                        visitedEnumerable++;
                        if (!visitor(item) || visitedEnumerable >= 512)
                        {
                            break;
                        }
                    }
                    return visitedEnumerable;
                }
                catch { }
            }

            int count;
            if (TryGetCollectionCount(value, out count))
            {
                int visitedByIndex = 0;
                int max = Math.Min(count, 512);
                for (int i = 0; i < max; i++)
                {
                    object item = ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { i })
                        ?? ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { (uint)i });
                    visitedByIndex++;
                    if (!visitor(item))
                    {
                        break;
                    }
                }
                return visitedByIndex;
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(value, "GetEnumerator");
            int visited = 0;
            while (enumerator != null && visited < 512)
            {
                object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                if (!(moved is bool) || !(bool)moved)
                {
                    break;
                }
                object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                visited++;
                if (!visitor(current))
                {
                    break;
                }
            }
            return visited;
        }

        private int VisitKeyValuePairs(object value, Func<object, object, bool> visitor)
        {
            if (value == null || visitor == null)
            {
                return 0;
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                int visitedDictionary = 0;
                try
                {
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        visitedDictionary++;
                        if (!visitor(pair.Key, pair.Value) || visitedDictionary >= 512)
                        {
                            break;
                        }
                    }
                    return visitedDictionary;
                }
                catch { }
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(value, "GetEnumerator");
            int visited = 0;
            while (enumerator != null && visited < 512)
            {
                object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                if (!(moved is bool) || !(bool)moved)
                {
                    break;
                }
                object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                object key = ReflectionTools.GetMemberValue(current, "Key") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Key") ?? ReflectionTools.GetMemberValue(current, "Item1");
                object itemValue = ReflectionTools.GetMemberValue(current, "Value") ?? ReflectionTools.InvokeNoArgMethod(current, "get_Value") ?? ReflectionTools.GetMemberValue(current, "Item2");
                visited++;
                if (!visitor(key, itemValue))
                {
                    break;
                }
            }
            if (visited == 0)
            {
                object keys = ReflectionTools.GetMemberValue(value, "Keys") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Keys");
                object values = ReflectionTools.GetMemberValue(value, "Values") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Values");
                if (keys != null && values != null)
                {
                    List<object> keyList = new List<object>();
                    List<object> valueList = new List<object>();
                    VisitCollectionItems(keys, delegate(object item)
                    {
                        keyList.Add(item);
                        return keyList.Count < 512;
                    });
                    VisitCollectionItems(values, delegate(object item)
                    {
                        valueList.Add(item);
                        return valueList.Count < 512;
                    });
                    int max = Math.Min(keyList.Count, valueList.Count);
                    for (int i = 0; i < max; i++)
                    {
                        visited++;
                        if (!visitor(keyList[i], valueList[i]))
                        {
                            break;
                        }
                    }
                }
            }
            return visited;
        }

        private static PlayerEntry GetOrCreateEntry(Dictionary<string, PlayerEntry> entries, string key)
        {
            PlayerEntry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new PlayerEntry();
                entry.Key = key;
                entries[key] = entry;
            }
            return entry;
        }

        private static bool ObjectsSame(object a, object b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }
            try
            {
                if (a.Equals(b) || b.Equals(a))
                {
                    return true;
                }
            }
            catch { }
            try
            {
                UnityEngine.Object ua = a as UnityEngine.Object;
                UnityEngine.Object ub = b as UnityEngine.Object;
                return ua != null && ub != null && ua == ub;
            }
            catch { return false; }
        }

        private string GetRosterContext()
        {
            Type lobbyType = ReflectionTools.FindType("Sons.Multiplayer.CoopLobbyManager");
            string lobby = NormalizeNetworkKey(ReflectionTools.InvokeStaticNoArgMethod(lobbyType, "ActiveLobbyId"));
            string server = NormalizeNetworkKey(ReflectionTools.InvokeStaticNoArgMethod(lobbyType, "ActiveServerId"));
            if (IsAmbiguousNetworkKey(lobby)) lobby = "";
            if (IsAmbiguousNetworkKey(server)) server = "";
            if (!String.IsNullOrEmpty(lobby) || !String.IsNullOrEmpty(server))
            {
                return lobby + "|" + server;
            }

            Type serverType = ReflectionTools.FindType("Sons.Multiplayer.GameServerManager");
            server = NormalizeNetworkKey(ReflectionTools.GetStaticMemberValue(serverType, "ServerId"));
            return IsAmbiguousNetworkKey(server) ? "" : server;
        }

        private void MergePlayerEntry(PlayerEntry target, PlayerEntry source)
        {
            if (target == null || source == null)
            {
                return;
            }
            if (String.IsNullOrEmpty(target.Key)) target.Key = source.Key;
            if (String.IsNullOrEmpty(target.SteamId)) target.SteamId = source.SteamId;
            ApplyBetterPlayerName(target, source.Name);
            MergeObservedNames(target, source);
            if (!String.IsNullOrEmpty(source.Alias)) target.Alias = source.Alias;
            AddPlayerSource(target, source.Source);
            if (source.Sources != null)
            {
                for (int i = 0; i < source.Sources.Count; i++)
                {
                    AddPlayerSource(target, source.Sources[i]);
                }
            }
            if (!String.IsNullOrEmpty(source.Role)) target.Role = source.Role;
            if (!String.IsNullOrEmpty(source.NetworkId)) target.NetworkId = source.NetworkId;
            if (!String.IsNullOrEmpty(source.LastActivity)) target.LastActivity = source.LastActivity;
            if (!String.IsNullOrEmpty(source.LastActivityUtc)) target.LastActivityUtc = source.LastActivityUtc;
            if (!String.IsNullOrEmpty(source.LastRosterContext)) target.LastRosterContext = source.LastRosterContext;
            if (source.ActivityCount > target.ActivityCount) target.ActivityCount = source.ActivityCount;
            MergeNativeBanObservation(target, source);
            if (source.ConnectionId >= 0) target.ConnectionId = source.ConnectionId;
            if (source.SourceObject != null) target.SourceObject = source.SourceObject;
            if (source.BoltEntity != null) target.BoltEntity = source.BoltEntity;
            if (source.BoltConnection != null) target.BoltConnection = source.BoltConnection;
            if (source.Transform != null) target.Transform = source.Transform;
        }

        private static void ApplyBetterPlayerName(PlayerEntry entry, string name)
        {
            if (entry == null)
            {
                return;
            }
            string candidate = CleanPlayerProbe(name);
            AddObservedPlayerName(entry, candidate);
            if (!ShouldReplacePlayerName(entry.Name, candidate))
            {
                string best = BestObservedPlayerName(entry);
                if (ShouldReplacePlayerName(entry.Name, best))
                {
                    entry.Name = best;
                }
                return;
            }
            entry.Name = candidate;
            string observedBest = BestObservedPlayerName(entry);
            if (ShouldReplacePlayerName(entry.Name, observedBest))
            {
                entry.Name = observedBest;
            }
        }

        private static void MergeObservedNames(PlayerEntry target, PlayerEntry source)
        {
            if (target == null || source == null || source.ObservedNames == null)
            {
                return;
            }
            for (int i = 0; i < source.ObservedNames.Count; i++)
            {
                AddObservedPlayerName(target, source.ObservedNames[i]);
            }
            string best = BestObservedPlayerName(target);
            if (ShouldReplacePlayerName(target.Name, best))
            {
                target.Name = best;
            }
        }

        private static bool AddObservedPlayerName(PlayerEntry entry, string name)
        {
            if (entry == null)
            {
                return false;
            }
            string candidate = CleanPlayerProbe(name);
            if (String.IsNullOrEmpty(candidate) || LooksLikeSteamId(CleanSteamId(candidate)))
            {
                return false;
            }
            for (int i = 0; i < entry.ObservedNames.Count; i++)
            {
                if (String.Equals(entry.ObservedNames[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            if (entry.ObservedNames.Count < 12)
            {
                entry.ObservedNames.Add(candidate);
                return true;
            }
            return false;
        }

        private static string BestObservedPlayerName(PlayerEntry entry)
        {
            if (entry == null || entry.ObservedNames == null || entry.ObservedNames.Count == 0)
            {
                return "";
            }
            string best = entry.Name;
            for (int i = 0; i < entry.ObservedNames.Count; i++)
            {
                string candidate = entry.ObservedNames[i];
                if (ShouldReplacePlayerName(best, candidate))
                {
                    best = candidate;
                }
            }
            return best;
        }

        private static string BestDisplayBaseName(PlayerEntry entry)
        {
            if (entry == null)
            {
                return "";
            }

            string name = entry.Name;
            string observed = BestObservedPlayerName(entry);
            if (ShouldReplacePlayerName(name, observed))
            {
                name = observed;
            }

            name = CleanPlayerProbe(name);
            if (!String.IsNullOrEmpty(name))
            {
                return name;
            }
            if (!String.IsNullOrEmpty(entry.Role) && entry.Role.IndexOf("Local", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Local Player";
            }
            if (LooksLikeSteamId(entry.SteamId))
            {
                return "Player " + entry.SteamId.Substring(entry.SteamId.Length - 4);
            }
            if (!String.IsNullOrEmpty(entry.Key) && !LooksLikeSteamId(CleanSteamId(entry.Key)))
            {
                string keyName = CleanPlayerProbe(entry.Key);
                if (!String.IsNullOrEmpty(keyName))
                {
                    return keyName;
                }
            }
            return "Unknown player";
        }

        private static void AddPlayerSource(PlayerEntry entry, string source)
        {
            if (entry == null)
            {
                return;
            }
            if (entry.Sources.Count == 0 && !String.IsNullOrEmpty(entry.Source))
            {
                AddPlayerSourceToken(entry, entry.Source);
            }
            if (!String.IsNullOrEmpty(source))
            {
                string[] parts = source.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    AddPlayerSourceToken(entry, parts[i]);
                }
            }
            string summary = BuildPlayerSourceSummary(entry);
            if (!String.IsNullOrEmpty(summary))
            {
                entry.Source = summary;
            }
        }

        private static void AddPlayerSourceToken(PlayerEntry entry, string source)
        {
            string text = CleanConfigText(source, 80);
            if (String.IsNullOrEmpty(text))
            {
                return;
            }
            for (int i = 0; i < entry.Sources.Count; i++)
            {
                if (String.Equals(entry.Sources[i], text, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            if (entry.Sources.Count < 12)
            {
                entry.Sources.Add(text);
            }
        }

        private static string BuildPlayerSourceSummary(PlayerEntry entry)
        {
            if (entry == null || entry.Sources == null || entry.Sources.Count == 0)
            {
                return "";
            }
            string text = "";
            int shown = Math.Min(entry.Sources.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    text += ", ";
                }
                text += entry.Sources[i];
            }
            if (entry.Sources.Count > shown)
            {
                text += " +" + (entry.Sources.Count - shown).ToString(CultureInfo.InvariantCulture);
            }
            return text;
        }

        private static bool ShouldReplacePlayerName(string current, string candidate)
        {
            if (String.IsNullOrEmpty(candidate))
            {
                return false;
            }
            if (String.Equals(current, candidate, StringComparison.Ordinal))
            {
                return false;
            }
            if (String.IsNullOrEmpty(current))
            {
                return true;
            }

            bool currentWeak = IsWeakPlayerName(current);
            bool candidateWeak = IsWeakPlayerName(candidate);
            if (currentWeak && !candidateWeak)
            {
                return true;
            }
            if (!currentWeak && candidateWeak)
            {
                return false;
            }
            return !candidateWeak;
        }

        private static bool IsWeakPlayerName(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return true;
            }
            string text = value.Trim();
            if (LooksLikeSteamId(CleanSteamId(text)))
            {
                return true;
            }
            if (IsNonPlayerObjectName(text))
            {
                return true;
            }
            string lower = text.ToLowerInvariant();
            return lower.IndexOf("(clone)", StringComparison.Ordinal) >= 0
                || lower.IndexOf("playernetwork", StringComparison.Ordinal) >= 0
                || lower.IndexOf("boltentity", StringComparison.Ordinal) >= 0
                || lower.IndexOf("networkid", StringComparison.Ordinal) >= 0
                || lower.StartsWith("sons.", StringComparison.Ordinal)
                || lower.StartsWith("theforest.", StringComparison.Ordinal)
                || lower.StartsWith("bolt.", StringComparison.Ordinal);
        }

        private void PruneOfflinePlayers(DateTime now)
        {
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, PlayerEntry> pair in _sessionPlayers)
            {
                PlayerEntry entry = pair.Value;
                if (entry == null)
                {
                    remove.Add(pair.Key);
                    continue;
                }
                if (!entry.Online
                    && entry.LastSeenUtc != DateTime.MinValue
                    && now.Subtract(entry.LastSeenUtc).TotalMinutes > Math.Max(1f, _offlineRetentionMinutes))
                {
                    string steamId = CleanSteamId(entry.SteamId);
                    if (LooksLikeSteamId(steamId) && _playerHistory.ContainsKey(BuildPlayerHistoryKey(entry.LastRosterContext, steamId)))
                    {
                        continue;
                    }
                    remove.Add(pair.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                _sessionPlayers.Remove(remove[i]);
            }
        }

        private void AddPlayerObjectsOfType(Type type, string source, Dictionary<string, PlayerEntry> entries)
        {
            Array objects = ReflectionTools.FindObjectsOfType(type);
            if (objects == null)
            {
                return;
            }
            foreach (object obj in objects)
            {
                AddPlayerEntryFromObject(obj, source, entries);
            }
        }

        private void AddStaticPlayerRoots(Type type, string source, Dictionary<string, PlayerEntry> entries)
        {
            string[] rootNames = new string[]
            {
                "Instance",
                "_instance",
                "Current",
                "Lobby",
                "CurrentLobby",
                "ActiveLobby",
                "ActiveInstance",
                "Members",
                "AllMembers",
                "ActiveMembers",
                "Players",
                "AllPlayers",
                "PlayerData",
                "MemberDetails",
                "Roster",
                "_membersCached",
                "_clientConnectionInfo"
            };
            HashSet<int> visited = new HashSet<int>();
            for (int i = 0; i < rootNames.Length; i++)
            {
                object root = ReflectionTools.GetStaticMemberValue(type, rootNames[i]);
                AddPlayerEntriesFromRoot(root, source, entries, visited, 2);
            }
        }

        private int AddStaticPlayerMethodRoots(Type type, string source, Dictionary<string, PlayerEntry> entries)
        {
            string[] methodNames = new string[]
            {
                "GetActiveMembersDetails",
                "GetActiveMembers",
                "GetActiveInstance",
                "get__membersCached",
                "get__clientConnectionInfo"
            };
            HashSet<int> visited = new HashSet<int>();
            int roots = 0;
            for (int i = 0; i < methodNames.Length; i++)
            {
                object root = ReflectionTools.InvokeStaticNoArgMethod(type, methodNames[i]);
                if (root != null)
                {
                    roots++;
                }
                AddPlayerEntriesFromRoot(root, source + "." + methodNames[i], entries, visited, 3);
            }
            return roots;
        }

        private void AddPlayerEntriesFromRoot(object value, string source, Dictionary<string, PlayerEntry> entries, HashSet<int> visited, int depth)
        {
            if (value == null || depth < 0)
            {
                return;
            }
            AddPlayerEntryFromObject(value, source, entries);
            if (IsLeafType(value.GetType()))
            {
                return;
            }
            int identity = RuntimeHelpers.GetHashCode(value);
            if (visited.Contains(identity))
            {
                return;
            }
            visited.Add(identity);

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    AddPlayerEntriesFromRoot(entry.Key, source, entries, visited, depth - 1);
                    AddPlayerEntriesFromRoot(entry.Value, source, entries, visited, depth - 1);
                }
                return;
            }

            if (TraverseIl2CppDictionary(value, source, entries, visited, depth))
            {
                return;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                int count = 0;
                foreach (object child in enumerable)
                {
                    AddPlayerEntriesFromRoot(child, source, entries, visited, depth - 1);
                    if (++count > 128)
                    {
                        break;
                    }
                }
                return;
            }

            if (TraverseIl2CppCollection(value, source, entries, visited, depth))
            {
                return;
            }

            if (depth == 0)
            {
                return;
            }

            string[] memberNames = new string[] { "Members", "AllMembers", "ActiveMembers", "Players", "AllPlayers", "PlayerData", "MemberDetails", "KnownPlayers", "Roster", "_members", "_membersCached", "_players", "_playerData", "_clientConnectionInfo", "Item1", "Item2" };
            for (int i = 0; i < memberNames.Length; i++)
            {
                AddPlayerEntriesFromRoot(ReflectionTools.GetMemberValue(value, memberNames[i]), source, entries, visited, depth - 1);
            }
        }

        private bool TraverseIl2CppDictionary(object value, string source, Dictionary<string, PlayerEntry> entries, HashSet<int> visited, int depth)
        {
            if (value == null || depth <= 0)
            {
                return false;
            }
            string typeName = value.GetType().FullName ?? value.GetType().Name;
            if (typeName.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            bool touched = false;
            object keys = ReflectionTools.GetMemberValue(value, "Keys") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Keys");
            object values = ReflectionTools.GetMemberValue(value, "Values") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Values");
            if (keys != null)
            {
                touched = true;
                AddPlayerEntriesFromRoot(keys, source + ".Keys", entries, visited, depth - 1);
            }
            if (values != null)
            {
                touched = true;
                AddPlayerEntriesFromRoot(values, source + ".Values", entries, visited, depth - 1);
            }
            return touched;
        }

        private bool TraverseIl2CppCollection(object value, string source, Dictionary<string, PlayerEntry> entries, HashSet<int> visited, int depth)
        {
            if (value == null || depth <= 0 || value is string)
            {
                return false;
            }

            bool touched = false;
            int count;
            if (TryGetCollectionCount(value, out count))
            {
                touched = true;
                int max = Math.Min(count, 256);
                for (int i = 0; i < max; i++)
                {
                    object item = ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { i })
                        ?? ReflectionTools.InvokeInstanceMethodResult(value, "get_Item", new object[] { (uint)i });
                    AddPlayerEntriesFromRoot(item, source + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", entries, visited, depth - 1);
                }
            }

            object enumerator = ReflectionTools.InvokeNoArgMethod(value, "GetEnumerator");
            if (enumerator != null)
            {
                touched = true;
                int guard = 0;
                while (guard++ < 256)
                {
                    object moved = ReflectionTools.InvokeInstanceMethodResult(enumerator, "MoveNext", new object[0]);
                    if (!(moved is bool) || !(bool)moved)
                    {
                        break;
                    }
                    object current = ReflectionTools.GetMemberValue(enumerator, "Current") ?? ReflectionTools.InvokeNoArgMethod(enumerator, "get_Current");
                    AddPlayerEntriesFromRoot(current, source + ".Current", entries, visited, depth - 1);
                }
            }

            return touched;
        }

        private static bool TryGetCollectionCount(object value, out int count)
        {
            count = 0;
            object found = ReflectionTools.GetMemberValue(value, "Count")
                ?? ReflectionTools.GetMemberValue(value, "Length")
                ?? ReflectionTools.InvokeNoArgMethod(value, "get_Count")
                ?? ReflectionTools.InvokeNoArgMethod(value, "get_Length");
            if (found == null)
            {
                return false;
            }
            try
            {
                count = Convert.ToInt32(found, CultureInfo.InvariantCulture);
                return count >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void AddPlayerEntryFromObject(object value, string source, Dictionary<string, PlayerEntry> entries)
        {
            if (value == null)
            {
                return;
            }
            string id = CleanSteamId(ExtractSteamId(value));
            if (!LooksLikeSteamId(id))
            {
                return;
            }
            string name = ExtractPlayerName(value);
            string key = id;
            if (String.IsNullOrEmpty(key))
            {
                return;
            }

            PlayerEntry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new PlayerEntry();
                entry.Key = key;
                entries[key] = entry;
            }
            if (String.IsNullOrEmpty(entry.SteamId)) entry.SteamId = id ?? "";
            ApplyBetterPlayerName(entry, name);
            if (entry.SourceObject == null) entry.SourceObject = value;
            if (entry.BoltEntity == null) entry.BoltEntity = ExtractFirstMember(value, new string[] { "Entity", "BoltEntity", "PlayerEntity", "NetworkEntity", "_entity", "_boltEntity", "entity" });
            if (entry.BoltConnection == null) entry.BoltConnection = ExtractFirstMember(value, new string[] { "Connection", "BoltConnection", "PlayerConnection", "_connection", "_boltConnection", "connection" });
            if (entry.Transform == null) entry.Transform = ExtractTransform(value) ?? ExtractTransform(entry.BoltEntity);
            if (String.IsNullOrEmpty(entry.NetworkId)) entry.NetworkId = ExtractNetworkId(value) ?? ExtractNetworkId(entry.BoltEntity);
            if (entry.ConnectionId < 0) entry.ConnectionId = ExtractConnectionId(value);
            if (String.IsNullOrEmpty(entry.Role)) entry.Role = ExtractRoleText(value);
            if (ExtractBannedFlag(value)) RecordRosterBanFlag(entry);
            string alias;
            if (!String.IsNullOrEmpty(id) && _aliases.TryGetValue(id, out alias)) entry.Alias = alias;
            else if (!String.IsNullOrEmpty(name) && _aliases.TryGetValue(name, out alias)) entry.Alias = alias;
            AddPlayerSource(entry, source);
        }

        private void EnrichPlayerEntry(PlayerEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.SteamId))
            {
                return;
            }

            ulong steamId;
            if (!UInt64.TryParse(entry.SteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId))
            {
                return;
            }

            if (String.IsNullOrEmpty(entry.Role))
            {
                object role = InvokeRolesMethodResult("GetRoleValue", new object[] { steamId });
                if (!(role is InvokeFailed) && role != null)
                {
                    entry.Role = role.ToString();
                }
            }
            EnrichPlayerIdentityFromRoleData(entry, steamId);
            EnrichPlayerIdentityFromSteamFriends(entry, steamId);
            bool banned;
            bool available = TryCheckPlayerBanned(steamId, out banned);
            RecordNativeBanQuery(entry, available, banned);
        }

        private void EnrichPlayerIdentityFromRoleData(PlayerEntry entry, ulong steamId)
        {
            if (entry == null || steamId == 0UL)
            {
                return;
            }
            try
            {
                Type rolesType = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoles") ?? ReflectionTools.FindType("MultiplayerPlayerRoles");
                object roles = FindRolesRuntimeObject(rolesType);
                object data = ReflectionTools.GetMemberValue(roles, "_playerRoleData") ?? ReflectionTools.GetMemberValue(roles, "PlayerRoleData");
                if (data == null)
                {
                    Type dataType = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoleData") ?? ReflectionTools.FindType("MultiplayerPlayerRoleData");
                    data = FindRuntimeObjectNoStaticProperties(dataType);
                }
                if (data == null)
                {
                    return;
                }

                object[] args = new object[] { steamId, null };
                object result;
                if (!InvokeMethod(data, data.GetType(), "TryGetData", args, false, out result) || !(result is bool) || !(bool)result || args.Length < 2 || args[1] == null)
                {
                    return;
                }

                object playerData = args[1];
                string name = CleanPlayerProbe(ReflectionTools.InvokeNoArgMethod(playerData, "GetPlayerName")
                    ?? ReflectionTools.GetMemberValue(playerData, "PlayerName")
                    ?? ReflectionTools.GetMemberValue(playerData, "_playerName"));
                ApplyBetterPlayerName(entry, name);

                if (String.IsNullOrEmpty(entry.Role))
                {
                    object role = ReflectionTools.InvokeNoArgMethod(playerData, "GetRoleValue")
                        ?? ReflectionTools.GetMemberValue(playerData, "Role")
                        ?? ReflectionTools.GetMemberValue(playerData, "_role");
                    if (role != null)
                    {
                        entry.Role = CleanConfigText(role.ToString(), 32);
                    }
                }
                if (entry.SourceObject == null)
                {
                    entry.SourceObject = playerData;
                }
                AddPlayerSource(entry, "PlayerRoleData.TryGetData");
            }
            catch (Exception ex)
            {
                LogWarning("Player role data name enrichment skipped: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void EnrichPlayerIdentityFromSteamFriends(PlayerEntry entry, ulong steamId)
        {
            if (entry == null || steamId == 0UL)
            {
                return;
            }
            try
            {
                Steamworks.CSteamID cSteamId = new Steamworks.CSteamID(steamId);
                string name = CleanPlayerProbe(Steamworks.SteamFriends.GetFriendPersonaName(cSteamId));
                if (String.IsNullOrEmpty(name))
                {
                    try { Steamworks.SteamFriends.RequestUserInformation(cSteamId, true); } catch { }
                    return;
                }
                ApplyBetterPlayerName(entry, name);
                AddPlayerSource(entry, "SteamFriends.GetFriendPersonaName");
            }
            catch
            {
            }
        }

        private void RefreshBansFromGame()
        {
            int added = 0;
            Type kickType = ReflectionTools.FindType("CoopKick");
            object kick = FindRuntimeObject(kickType);
            added += ImportBansFromObject(kick, "CoopKick");
            added += ImportBansFromStatic(kickType, "CoopKick");

            Type rolesType = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoles") ?? ReflectionTools.FindType("MultiplayerPlayerRoles");
            object roles = FindRolesRuntimeObject(rolesType);
            added += ImportBansFromObject(roles, "MultiplayerPlayerRoles");
            if (roles != null)
            {
                added += ImportBansFromStatic(rolesType, "MultiplayerPlayerRoles");
            }

            bool saved = added > 0 && SaveConfig();
            RefreshPlayers();
            string outcome = "Ban sync added " + added.ToString(CultureInfo.InvariantCulture)
                + " SteamID(s); known bans " + _bannedPlayers.Count.ToString(CultureInfo.InvariantCulture);
            if (added > 0) ReportSettingsSave(saved, outcome);
            else ActionOk(outcome + "; settings were not written");
        }

        private int ImportBansFromStatic(Type type, string source)
        {
            if (type == null)
            {
                return 0;
            }
            int added = 0;
            string[] names = new string[] { "KickedPlayers", "kickedSteamIds", "BannedPlayers", "BannedPlayerIds", "BannedSteamIds", "_bannedPlayers", "_kickedSteamIds", "_playerRoleData" };
            for (int i = 0; i < names.Length; i++)
            {
                added += ImportBansFromRoot(ReflectionTools.GetStaticMemberValue(type, names[i]), source + "." + names[i], 3, new HashSet<int>());
            }
            return added;
        }

        private int ImportBansFromObject(object instance, string source)
        {
            if (instance == null)
            {
                return 0;
            }
            int added = 0;
            string[] names = new string[] { "KickedPlayers", "kickedSteamIds", "BannedPlayers", "BannedPlayerIds", "BannedSteamIds", "_bannedPlayers", "_kickedSteamIds", "_playerRoleData" };
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReflectionTools.GetMemberValue(instance, names[i]) ?? ReflectionTools.InvokeNoArgMethod(instance, names[i]);
                added += ImportBansFromRoot(value, source + "." + names[i], 3, new HashSet<int>());
            }
            return added;
        }

        private int ImportBansFromRoot(object value, string source, int depth, HashSet<int> visited)
        {
            if (value == null || depth < 0)
            {
                return 0;
            }
            Type type = value.GetType();
            if (!IsLeafType(type))
            {
                int identity = RuntimeHelpers.GetHashCode(value);
                if (visited.Contains(identity))
                {
                    return 0;
                }
                visited.Add(identity);
            }

            int added = 0;
            string id = TryGetSteamIdFromValue(value);
            if (String.IsNullOrEmpty(id))
            {
                id = ExtractSteamId(value);
            }
            string loweredSource = (source ?? "").ToLowerInvariant();
            bool sourceSuggestsBan = loweredSource.IndexOf("ban", StringComparison.Ordinal) >= 0 || loweredSource.IndexOf("kick", StringComparison.Ordinal) >= 0;
            string roleText = ExtractRoleText(value);
            bool valueSuggestsBan = ExtractBannedFlag(value) || roleText.IndexOf("banned", StringComparison.OrdinalIgnoreCase) >= 0;
            if (LooksLikeSteamId(id) && (sourceSuggestsBan || valueSuggestsBan))
            {
                ModerationEntry moderation = new ModerationEntry();
                moderation.SteamId = id;
                moderation.Name = CleanPlayerProbe(ReflectionTools.GetMemberValue(value, "Name") ?? ReflectionTools.GetMemberValue(value, "PlayerName"));
                moderation.Reason = "Imported from game ban list";
                moderation.Source = source;
                moderation.AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (!_bannedPlayers.ContainsKey(id))
                {
                    _bannedPlayers[id] = moderation;
                    added++;
                }
            }

            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry pair in dictionary)
                {
                    added += ImportBansFromRoot(pair.Key, source, depth - 1, visited);
                    added += ImportBansFromRoot(pair.Value, source, depth - 1, visited);
                }
                return added;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                int count = 0;
                foreach (object child in enumerable)
                {
                    added += ImportBansFromRoot(child, source, depth - 1, visited);
                    if (++count > 256)
                    {
                        break;
                    }
                }
                return added;
            }

            if (depth == 0 || IsLeafType(type))
            {
                return added;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = SafeFields(type, flags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (LooksLikeModerationMember(fields[i].Name, fields[i].FieldType))
                {
                    try { added += ImportBansFromRoot(fields[i].GetValue(value), source + "." + fields[i].Name, depth - 1, visited); } catch { }
                }
            }
            PropertyInfo[] props = SafeProps(type, flags);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].CanRead && props[i].GetIndexParameters().Length == 0 && LooksLikeModerationMember(props[i].Name, props[i].PropertyType))
                {
                    try { added += ImportBansFromRoot(props[i].GetValue(value, null), source + "." + props[i].Name, depth - 1, visited); } catch { }
                }
            }
            return added;
        }

        private static bool LooksLikeModerationMember(string memberName, Type memberType)
        {
            string lowered = (memberName ?? "").ToLowerInvariant();
            string typeName = memberType == null ? "" : (memberType.FullName ?? memberType.Name).ToLowerInvariant();
            return lowered.IndexOf("ban", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("kick", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("role", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("steam", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("player", StringComparison.Ordinal) >= 0 && typeName.IndexOf("data", StringComparison.Ordinal) >= 0;
        }

        private bool RecordModerationEntry(Dictionary<string, ModerationEntry> target, string steamId, PlayerEntry player, string reason, string source)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return false;
            }

            ModerationEntry entry;
            bool added = !target.TryGetValue(steamId, out entry);
            if (entry == null)
            {
                entry = new ModerationEntry();
                target[steamId] = entry;
            }

            entry.SteamId = steamId;
            if (player != null && !String.IsNullOrEmpty(player.Name))
            {
                entry.Name = CleanConfigText(player.Name, 48);
            }
            else if (String.IsNullOrEmpty(entry.Name) && !String.IsNullOrEmpty(_manualNameInput))
            {
                entry.Name = CleanConfigText(_manualNameInput, 48);
            }
            if (!String.IsNullOrEmpty(reason))
            {
                entry.Reason = CleanConfigText(reason, 96);
            }
            if (String.IsNullOrEmpty(entry.AddedUtc))
            {
                entry.AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
            entry.Source = CleanConfigText(source, 48);
            return added;
        }

        private List<ModerationEntry> SortedModerationEntries(Dictionary<string, ModerationEntry> source)
        {
            List<ModerationEntry> list = new List<ModerationEntry>(source.Values);
            list.Sort(delegate(ModerationEntry a, ModerationEntry b)
            {
                string aName = String.IsNullOrEmpty(a.Name) ? a.SteamId : a.Name;
                string bName = String.IsNullOrEmpty(b.Name) ? b.SteamId : b.Name;
                return String.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private void SendMuteNotice(PlayerEntry entry, string steamId, bool force)
        {
            SendMuteNotice(entry, steamId, force, null, null);
        }

        private void SendMuteNotice(PlayerEntry entry, string steamId, bool force, object fallbackConnection, object fallbackEntity)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }
            DateTime last;
            if (!force && _muteNoticeTimes.TryGetValue(steamId, out last) && DateTime.UtcNow.Subtract(last).TotalSeconds < 2.0)
            {
                return;
            }
            _muteNoticeTimes[steamId] = DateTime.UtcNow;

            string message = String.IsNullOrEmpty(_muteMessageInput) ? "You are muted on this server." : CleanConfigText(_muteMessageInput, 120);
            if (TrySendLoaderModerationNotice(steamId, message, "Mute notice")) return;
            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            object connection = entry != null && entry.BoltConnection != null ? entry.BoltConnection : fallbackConnection;
            object entity = entry != null && entry.BoltEntity != null ? entry.BoltEntity : fallbackEntity;
            if (!SendNativeModerationNotice(steamId, message, "Mute notice", connection, entity))
            {
                bool statusShown = ShowLocalStatusBarNotice("Muted " + (entry == null ? steamId : DisplayPlayerName(entry)) + ": " + message);
                if (!statusShown)
                {
                    LogWarning("Mute notice delivery is unconfirmed for " + steamId + "; no local status fallback was available");
                }
                else
                {
                    LogWarning("Mute notice delivery is unconfirmed for " + steamId + "; showed local status fallback");
                }
            }
        }

        private void SendAntiCheatNotice(PlayerEntry entry, string steamId, string message, object fallbackConnection, object fallbackEntity)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId) || String.IsNullOrEmpty(message))
            {
                return;
            }
            DateTime last;
            if (_antiCheatNoticeTimes.TryGetValue(steamId, out last) && DateTime.UtcNow.Subtract(last).TotalSeconds < 10.0)
            {
                return;
            }
            _antiCheatNoticeTimes[steamId] = DateTime.UtcNow;

            message = CleanConfigText(message, 160);
            if (TrySendLoaderModerationNotice(steamId, message, "Anti-cheat notice")) return;

            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            object connection = entry != null && entry.BoltConnection != null ? entry.BoltConnection : fallbackConnection;
            object entity = entry != null && entry.BoltEntity != null ? entry.BoltEntity : fallbackEntity;
            if (!SendNativeModerationNotice(steamId, message, "Anti-cheat notice", connection, entity))
            {
                ShowLocalStatusBarNotice((entry == null ? steamId : DisplayPlayerName(entry)) + ": " + CleanConfigText(message, 120));
            }
        }

        private bool ShowLocalStatusBarNotice(string message)
        {
            if (String.IsNullOrEmpty(message))
            {
                return false;
            }

            Type type = ReflectionTools.FindType("Sons.Gui.Multiplayer.Notifications.GameServerBrowserStatusBar") ?? ReflectionTools.FindType("GameServerBrowserStatusBar");
            object statusBar = FindRuntimeObject(type);
            if (statusBar == null)
            {
                return false;
            }

            object result;
            bool invoked = false;
            object state = TryGetStatusBarState(type, new string[] { "Unknown", "Empty", "InLobby", "NotConnected" });
            if (state != null)
            {
                invoked = InvokeMethod(statusBar, type, "ShowTemporary", new object[] { state, 5f }, false, out result)
                    || InvokeMethod(statusBar, type, "Show", new object[] { state }, false, out result);
            }
            bool wrote = SetTextOnObjectGraph(statusBar, message);
            return wrote || invoked;
        }

        private object TryGetStatusBarState(Type ownerType, string[] names)
        {
            if (ownerType == null || names == null)
            {
                return null;
            }

            List<Type> candidates = new List<Type>();
            try
            {
                Type[] nested = ownerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < nested.Length; i++)
                {
                    if (nested[i] != null && nested[i].IsEnum)
                    {
                        candidates.Add(nested[i]);
                    }
                }
            }
            catch { }

            Type direct = ReflectionTools.FindType((ownerType.FullName ?? ownerType.Name) + "+State");
            if (direct != null && direct.IsEnum)
            {
                candidates.Add(direct);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = 0; j < names.Length; j++)
                {
                    try { return Enum.Parse(candidates[i], names[j]); } catch { }
                }
            }
            return null;
        }

        private bool SetTextOnObjectGraph(object root, string message)
        {
            bool changed = false;
            string[] directNames = new string[] { "_text", "Text", "text", "_label", "Label", "_message", "Message" };
            for (int i = 0; i < directNames.Length; i++)
            {
                object target = ReflectionTools.GetMemberValue(root, directNames[i]);
                changed = TrySetTextValue(target, message) || changed;
            }
            changed = TrySetTextValue(root, message) || changed;

            object gameObject = ReflectionTools.GetMemberValue(root, "gameObject");
            if (gameObject == null)
            {
                return changed;
            }

            Type[] textTypes = new Type[] { ReflectionTools.FindType("TMPro.TMP_Text"), ReflectionTools.FindType("UnityEngine.UI.Text") };
            for (int i = 0; i < textTypes.Length; i++)
            {
                Type textType = textTypes[i];
                if (textType == null)
                {
                    continue;
                }
                Array components = ReflectionTools.GetComponentsInChildren(gameObject, textType, true);
                if (components == null)
                {
                    continue;
                }
                foreach (object component in components)
                {
                    changed = TrySetTextValue(component, message) || changed;
                }
            }
            return changed;
        }

        private static bool TrySetTextValue(object target, string message)
        {
            if (target == null)
            {
                return false;
            }
            bool changed = ReflectionTools.SetMemberValue(target, "text", message)
                || ReflectionTools.SetMemberValue(target, "Text", message)
                || ReflectionTools.InvokeInstanceMethod(target, "set_text", new object[] { message })
                || ReflectionTools.InvokeInstanceMethod(target, "SetText", new object[] { message });
            return changed;
        }

        private bool SendNetworkMessageTo(string message, object connection, object entity, bool allowFallbackAfterSend = true)
        {
            if (String.IsNullOrEmpty(message))
            {
                return false;
            }

            Type eventType = ReflectionTools.FindType("SendMessageEvent");
            object evnt = null;
            if (connection != null)
            {
                evnt = ReflectionTools.InvokeStaticMethodResult(eventType, "Create", new object[] { connection });
            }
            if (evnt == null && entity != null)
            {
                evnt = ReflectionTools.InvokeStaticMethodResult(eventType, "Create", new object[] { entity });
            }
            if (evnt != null)
            {
                ReflectionTools.SetMemberValue(evnt, "Message", message);
                if (entity != null)
                {
                    ReflectionTools.SetMemberValue(evnt, "Target", entity);
                }
                if (ReflectionTools.InvokeInstanceMethod(evnt, "Send", new object[0]))
                {
                    if (message.StartsWith(AuditRequestPrefix, StringComparison.Ordinal) || message.StartsWith(AuditResponsePrefix, StringComparison.Ordinal))
                    {
                        LogInfo("Inventory audit network message sent via SendMessageEvent connection="
                            + (connection == null ? "no" : "yes")
                            + " entity=" + (entity == null ? "no" : "yes"));
                    }
                    return true;
                }
                if (!allowFallbackAfterSend) return false;
            }

            bool sent = connection != null && InvokeStaticMethod("CoopAdminCommand", "SendNetworkMessage", new object[] { message, connection });
            if (sent && (message.StartsWith(AuditRequestPrefix, StringComparison.Ordinal) || message.StartsWith(AuditResponsePrefix, StringComparison.Ordinal)))
            {
                LogInfo("Inventory audit network message sent via CoopAdminCommand.SendNetworkMessage.");
            }
            return sent;
        }

        private void RequestSelectedInventoryAudit()
        {
            if (!_clientInventoryAudit)
            {
                ActionFail("Client inventory audit is disabled.");
                return;
            }

            PlayerEntry entry = GetSelectedPlayer();
            string steamId = entry == null ? "" : CleanSteamId(entry.SteamId);
            if (!LooksLikeSteamId(steamId))
            {
                ActionFail("Select a live player with a Steam64 ID first.");
                return;
            }

            string nonce = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
            InventorySignalState state = GetOrCreateInventorySignal(steamId);
            state.LastAuditNonce = nonce;
            state.LastAuditRequestUtc = DateTime.UtcNow;
            state.LastAuditStatus = "requested";
            state.PendingAuditSessionId = Guid.Empty;

            string localSteamId = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (String.Equals(localSteamId, steamId, StringComparison.OrdinalIgnoreCase) || IsLocalPlayerEntry(entry))
            {
                if (!TryCollectLocalInventoryAuditItems(out var localItems, out string failure))
                {
                    state.LastAuditStatus = failure + "; previous observations retained";
                    ActionFail(state.LastAuditStatus);
                    return;
                }
                RecordInventoryAuditResponse(steamId, nonce, Plugin.Version, "local", localItems, true);
                ActionOk("Local inventory audit captured " + localItems.Count.ToString(CultureInfo.InvariantCulture) + " item type(s).");
                return;
            }

            if (TryRequestLoaderInventoryAudit(steamId, state, out bool queued, out string detail))
            {
                state.LastAuditStatus = detail;
                ActionResult(queued, detail, detail);
                return;
            }
            if (!BindInventoryAuditSession(steamId, state, out string sessionFailure))
            {
                state.LastAuditStatus = "not sent: " + sessionFailure;
                ActionFail("Inventory audit unavailable: " + sessionFailure);
                return;
            }
            ulong parsedSteam;
            if (UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSteam))
            {
                TryRefreshPlayerNetworkHandles(entry, parsedSteam);
            }

            object connection = entry == null ? null : entry.BoltConnection;
            object entity = entry == null ? null : entry.BoltEntity;
            if (!SendNetworkMessageTo(AuditRequestPrefix + nonce, connection, entity))
            {
                state.LastAuditStatus = "send failed";
                state.PendingAuditSessionId = Guid.Empty;
                ActionFail("Could not send audit request. The player may not have a live connection/entity handle.");
                return;
            }

            ActionOk("Inventory audit requested from " + DisplayPlayerName(entry));
            LogInfo("Inventory audit requested from " + steamId + " nonce=" + nonce);
        }

        internal bool FilterSendMessageEvent(object listener, object evnt)
        {
            if (evnt == null)
            {
                return true;
            }

            string message = ReflectionTools.GetMemberValue(evnt, "Message") as string;
            if (String.IsNullOrEmpty(message))
            {
                return true;
            }

            bool auditReply = message.StartsWith(AuditResponsePrefix, StringComparison.Ordinal);
            // Current clients send replies through server chat; alternate replies must not bypass its checks.
            if (auditReply && LoaderChatSubscribed) return false;
            if (!_clientInventoryAudit) return true;

            if (auditReply)
            {
                return !HandleInventoryAuditResponse(evnt, message);
            }

            if (message.StartsWith(AuditRequestPrefix, StringComparison.Ordinal))
            {
                string nonce = CleanConfigText(message.Substring(AuditRequestPrefix.Length), 48);
                LogInfo("Inventory audit request received nonce=" + nonce);
                TrySendLocalInventoryAuditResponse(nonce);
                return false;
            }

            return true;
        }

        private bool HandleInventoryAuditResponse(object chatEvent, string message)
        {
            if (String.IsNullOrEmpty(message) || !message.StartsWith(AuditResponsePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string body = message.Substring(AuditResponsePrefix.Length);
            string[] parts = body.Split(new char[] { '|' }, 4);
            string nonce = parts.Length > 0 ? CleanConfigText(parts[0], 48) : "";
            string version = parts.Length > 1 ? CleanConfigText(parts[1], 24) : "";
            string reportedSteamId = parts.Length > 2 ? CleanSteamId(parts[2]) : "";
            string itemText = parts.Length > 3 ? parts[3] : "";

            string steamId;
            object entity;
            object connection;
            if (!TryResolveChatSender(chatEvent, out steamId, out entity, out connection)
                && !TryResolveEventSource(chatEvent, out steamId, out entity, out connection))
            {
                steamId = reportedSteamId;
            }
            if (!LooksLikeSteamId(steamId))
            {
                LogWarning("Suppressed inventory audit response but could not resolve sender.");
                return true;
            }

            Dictionary<int, int> items = ParseAuditItems(itemText);
            RecordInventoryAuditResponse(steamId, nonce, version, "client", items, false);
            LogInfo("Inventory audit response from " + steamId
                + " items=" + items.Count.ToString(CultureInfo.InvariantCulture)
                + " version=" + version);
            return true;
        }

        private bool TrySendLocalInventoryAuditResponse(string nonce)
        {
            if (!TryCollectLocalInventoryAuditItems(out var items, out string failure))
            {
                LogWarning("Inventory audit response not sent: " + failure);
                return false;
            }
            string steamId = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (!LooksLikeSteamId(steamId))
            {
                steamId = CleanSteamId(GetLocalSteamIdFromRoster());
            }
            if (!LooksLikeSteamId(steamId)) return false;
            string itemPayload = BuildAuditItemsPayload(items, 0);
            if (itemPayload.Length > 900)
            {
                LogWarning("Inventory audit response exceeds the legacy transport limit; no partial report was sent.");
                return false;
            }

            string payload = AuditResponsePrefix
                + CleanConfigText(nonce, 48)
                + "|" + Plugin.Version
                + "|" + steamId
                + "|" + itemPayload;
            if (SendLocalChatEvent(payload))
            {
                LogInfo("Inventory audit response sent with " + items.Count.ToString(CultureInfo.InvariantCulture) + " item type(s).");
                return true;
            }

            LogWarning("Inventory audit response could not be sent; ChatEvent.Create/Send failed. items="
                + items.Count.ToString(CultureInfo.InvariantCulture)
                + " nonce=" + CleanConfigText(nonce, 48));
            return false;
        }

        private bool SendLocalChatEvent(string message)
        {
            return SendLocalChatEvent(message, out _, out _);
        }

        private bool SendLocalChatEvent(string message, out bool sendAttempted, out string outcome)
        {
            sendAttempted = false;
            outcome = "No chat send was attempted: the event type or message is unavailable.";
            Type chatType = ReflectionTools.FindType("ChatEvent");
            if (chatType == null || String.IsNullOrEmpty(message))
            {
                return false;
            }

            object localEntity = GetLocalPlayerEntity();
            if (localEntity == null)
            {
                outcome = "No chat send was attempted: the local player entity is unavailable.";
                return false;
            }

            object networkId = ReflectionTools.GetMemberValue(localEntity, "networkId")
                ?? ReflectionTools.InvokeNoArgMethod(localEntity, "get_networkId")
                ?? ReflectionTools.GetMemberValue(localEntity, "NetworkId")
                ?? ReflectionTools.InvokeNoArgMethod(localEntity, "get_NetworkId");
            if (networkId == null)
            {
                outcome = "No chat send was attempted: the local player's network identity is unavailable.";
                return false;
            }

            object evnt = ReflectionTools.InvokeStaticMethodResult(chatType, "Create", new object[] { localEntity });
            if (evnt == null)
            {
                evnt = ReflectionTools.InvokeStaticMethodResult(chatType, "Create", new object[0]);
            }
            if (evnt == null)
            {
                outcome = "No chat send was attempted: event creation failed.";
                return false;
            }

            if (!ReflectionTools.SetMemberValue(evnt, "Message", message)
                || !ReflectionTools.SetMemberValue(evnt, "Sender", networkId))
            {
                outcome = "No chat send was attempted: the message or sender could not be assigned.";
                return false;
            }
            sendAttempted = true;
            bool sent = ReflectionTools.InvokeInstanceMethod(evnt, "Send", new object[0]);
            outcome = sent ? "Local-player chat dispatch returned; delivery and display are unconfirmed."
                : "Chat send outcome is uncertain; no retry or notice fallback was attempted.";
            return sent;
        }

        private object GetLocalPlayerEntity()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer") ?? ReflectionTools.FindType("LocalPlayer");
            return ReflectionTools.GetStaticMemberValue(localPlayer, "Entity")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_Entity_k__BackingField")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_Entity");
        }

        private object GetLocalPlayerInventory()
        {
            Type localPlayer = ReflectionTools.FindType("TheForest.Utils.LocalPlayer") ?? ReflectionTools.FindType("LocalPlayer");
            return ReflectionTools.GetStaticMemberValue(localPlayer, "Inventory")
                ?? ReflectionTools.GetStaticMemberValue(localPlayer, "_Inventory_k__BackingField")
                ?? ReflectionTools.InvokeStaticNoArgMethod(localPlayer, "get_Inventory");
        }

        private List<int> GetAuditItemIds()
        {
            SortedDictionary<int, bool> ids = new SortedDictionary<int, bool>();
            AddAuditItemIds(ids, _restrictedEarlyItemTokens);
            AddAuditItemIds(ids, _highValueItemTokens);
            AddAuditItemIds(ids, _artifactItemTokens);
            AddStoryLocationAuditItemIds(ids);
            return new List<int>(ids.Keys);
        }

        private void AddAuditItemIds(SortedDictionary<int, bool> ids, string tokensText)
        {
            if (ids == null || String.IsNullOrEmpty(tokensText))
            {
                return;
            }
            string[] tokens = tokensText.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (String.IsNullOrEmpty(token))
                {
                    continue;
                }
                int itemId;
                if (!Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
                {
                    itemId = ResolveItemIdByName(token);
                }
                if (itemId > 0 && itemId < 10000)
                {
                    ids[itemId] = true;
                }
            }
        }

        private void AddStoryLocationAuditItemIds(SortedDictionary<int, bool> ids)
        {
            if (ids == null)
            {
                return;
            }
            for (int i = 0; i < _storyItemLocationRules.Count; i++)
            {
                GuardedItemLocationRule rule = _storyItemLocationRules[i];
                if (rule != null && rule.ItemId > 0 && rule.ItemId < 10000)
                {
                    ids[rule.ItemId] = true;
                }
            }
        }

        private static string BuildAuditItemsPayload(Dictionary<int, int> items, int maxChars)
        {
            if (items == null || items.Count == 0)
            {
                return "";
            }
            List<int> keys = new List<int>(items.Keys);
            keys.Sort();
            List<string> parts = new List<string>();
            int length = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                string part = keys[i].ToString(CultureInfo.InvariantCulture)
                    + ":" + Math.Max(0, items[keys[i]]).ToString(CultureInfo.InvariantCulture);
                if (parts.Count > 0)
                {
                    length++;
                }
                length += part.Length;
                if (maxChars > 0 && length > maxChars)
                {
                    break;
                }
                parts.Add(part);
            }
            return String.Join(",", parts.ToArray());
        }

        private static Dictionary<int, int> ParseAuditItems(string text)
        {
            Dictionary<int, int> items = new Dictionary<int, int>();
            if (String.IsNullOrEmpty(text))
            {
                return items;
            }
            string[] pairs = text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pairs.Length; i++)
            {
                string[] parts = pairs[i].Split(new char[] { ':' }, 2);
                int itemId;
                int count = 1;
                if (parts.Length > 0 && Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId) && itemId > 0)
                {
                    if (parts.Length > 1)
                    {
                        Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                    }
                    items[itemId] = Math.Max(0, count);
                }
            }
            return items;
        }

        private void RecordInventoryAuditResponse(string steamId, string nonce, string version, string source, Dictionary<int, int> items, bool local)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }

            InventorySignalState state = GetOrCreateInventorySignal(steamId);
            state.PendingAuditSessionId = Guid.Empty;
            state.LastAuditNonce = CleanConfigText(nonce, 48);
            state.LastAuditVersion = CleanConfigText(version, 24);
            state.LastAuditStatus = String.IsNullOrEmpty(source) ? "received" : source;
            state.LastAuditResponseUtc = DateTime.UtcNow;
            state.LastAuditItemCount = items == null ? 0 : items.Count;
            state.LastAuditHighValueCount = 0;
            state.LastAuditArtifactCount = 0;
            state.LastAuditItems = BuildAuditItemsPayload(items, 240);
            bool unapprovedHighValue = false;
            bool unapprovedArtifact = false;
            int approvedItemTypes = 0;

            if (items != null)
            {
                foreach (KeyValuePair<int, int> pair in items)
                {
                    state.KnownItemDeltas[pair.Key] = pair.Value;
                    string descriptor = "InventoryAudit itemId=" + pair.Key.ToString(CultureInfo.InvariantCulture)
                        + ", count=" + pair.Value.ToString(CultureInfo.InvariantCulture);
                    bool approved = IsServerApprovedItem(steamId, pair.Key);
                    if (approved) approvedItemTypes++;
                    if (!IsAllowedCommonTransportItem(pair.Key, descriptor) && ItemMatchesTokens(pair.Key, descriptor, _highValueItemTokens))
                    {
                        state.LastAuditHighValueCount = (int)Math.Min(Int32.MaxValue, (long)state.LastAuditHighValueCount + Math.Max(1, pair.Value));
                        if (!approved) unapprovedHighValue = true;
                    }
                    if (ItemMatchesTokens(pair.Key, descriptor, _artifactItemTokens))
                    {
                        state.LastAuditArtifactCount = (int)Math.Min(Int32.MaxValue, (long)state.LastAuditArtifactCount + Math.Max(1, pair.Value));
                        if (!approved) unapprovedArtifact = true;
                    }
                }
            }

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            if (local || IsSteamIdAdminOrOwner(steamId, entry) || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordGuardEvidence(steamId, "Items", local ? "Local inventory audit" : "Client-provided audit", state.LastAuditItems,
                    CaptureItemGuardPolicy(), "Not evaluated: local player, admin/owner or configured exemption");
                return;
            }
            if (!_disallowCheats || !HasHostSession())
            {
                RecordGuardEvidence(steamId, "Items", "Client-provided audit", state.LastAuditItems,
                    CaptureItemGuardPolicy(), "Reply stored; host enforcement unavailable or disabled");
                return;
            }

            string reason = "";
            float sessionMinutes = GetPlayerSessionMinutes(steamId, entry);
            if (_artifactItemGuard && unapprovedArtifact)
            {
                reason = "Client inventory audit reported artifact gear: " + Shorten(state.LastAuditItems, 120);
            }
            else if (_highValueItemGuard && unapprovedHighValue && sessionMinutes <= _highValueGuardMinutes)
            {
                reason = "Client inventory audit reported high-value gear after "
                    + sessionMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m: "
                    + Shorten(state.LastAuditItems, 120);
            }

            if (String.IsNullOrEmpty(reason))
            {
                RecordGuardEvidence(steamId, "Items", "Client-provided audit", state.LastAuditItems,
                    CaptureItemGuardPolicy(), "No configured rule triggered; session-approved item types: " + approvedItemTypes.ToString(CultureInfo.InvariantCulture));
                return;
            }

            string learnedNote;
            if (LearnedPolicySuppressesSelfAdmin(steamId, "InventoryAudit", reason, state.LastAuditItems, out learnedNote))
            {
                state.LastAuditStatus = "learned review";
                state.LastReason = "learned allow: " + Shorten(learnedNote, 96);
                RecordGuardEvidence(steamId, "Items", "Client-provided audit", state.LastAuditItems,
                    CaptureItemGuardPolicy(), "Held by learned policy: " + learnedNote);
                AppendEventLog("LEARN", Shorten("Inventory audit held for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, "InventoryAudit", reason, state.LastAuditItems, out learnedNote))
            {
                reason = reason + " | learned confirmed pattern: " + Shorten(learnedNote, 80);
            }

            RecordGuardEvidence(steamId, "Items", "Client-provided audit", state.LastAuditItems,
                CaptureItemGuardPolicy(), "Flagged for review: " + reason);
            AnnounceGuardBlock(entry, steamId, "InventoryAudit", reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Inventory audit", false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), null, null);
            }
        }

        private InventorySignalState GetOrCreateInventorySignal(string steamId)
        {
            steamId = CleanSteamId(steamId);
            InventorySignalState state;
            if (!_inventorySignals.TryGetValue(steamId, out state) || state == null)
            {
                state = new InventorySignalState();
                state.SteamId = steamId;
                _inventorySignals[steamId] = state;
            }
            return state;
        }

        private void AnnounceGuardBlock(PlayerEntry entry, string steamId, string eventName, string reason)
        {
            string label = entry == null ? CleanSteamId(steamId) : DisplayPlayerName(entry);
            AppendEventLog("GUARD", Shorten(eventName + " " + label + " " + reason, 140));
            LogInfo("Guard observation " + eventName + " " + label + ": " + Shorten(reason, 180));
        }

        internal bool FilterCheatEvent(object listener, object evnt)
        {
            if (!_disallowCheats || !HasHostSession() || evnt == null)
            {
                return true;
            }

            if (!IsGuardServerReceiver(listener))
            {
                return true;
            }

            string eventName = evnt.GetType().Name ?? "";
            if (!IsCandidateCheatEvent(eventName))
            {
                return true;
            }

            if (LoaderAdminRequestsSubscribed && eventName == "AdminCommand") return true;
            // The shared callback owns both jail and native validation decisions for this event.
            if (LoaderStructureHitHookInstalled && eventName == "StructureHitEvent") return true;
            if (LoaderBlueprintHookInstalled && eventName == BlueprintPlacementEvent) return true;
            if (LoaderStructureRelocationHookInstalled && eventName == StructureRelocationEvent) return true;
            if (LoaderStorageAddHookInstalled && eventName == "ItemStorageAddItemEvent") return true;
            if (LoaderStorageRemoveHookInstalled && eventName == "ItemStorageRemoveItemEvent") return true;

            if ((ReflectionTools.GetMemberValue(evnt, "FromSelf")
                ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_FromSelf")) is true) return true;

            string steamId;
            object entity;
            object connection;
            if (!TryResolveGuardSender(evnt, out steamId, out entity, out connection))
            {
                if (IsOwnServerPolicyCommand(eventName, evnt))
                {
                    return true;
                }
                _unattributedGuardEvents++;
                _lastUnattributedGuardUtc = DateTime.UtcNow;
                _lastUnattributedGuardEvent = eventName;
                AppendEventLog("GUARD", Shorten("Unresolved " + eventName + " " + DescribeCheatEvent(evnt), 140));
                LogWarning("Anti-cheat saw " + eventName + " but could not resolve sender: " + DescribeCheatEvent(evnt));
                return true;
            }

            PlayerEntry entry = FindPlayerBySteamId(steamId);
            if (entry == null && entity != null)
            {
                entry = new PlayerEntry { Key = steamId, SteamId = steamId, Name = ExtractNameFromEntity(entity), BoltEntity = entity, BoltConnection = connection, Source = "Anti-cheat event" };
            }
            bool roleExempt = IsSteamIdAdminOrOwner(steamId, entry);
            if (roleExempt || IsSteamIdAntiCheatExempt(steamId))
            {
                RecordInventorySignal(steamId, eventName, evnt, false, roleExempt ? "admin/owner exemption" : "configured exemption");
                return true;
            }

            string reason;
            string validation;
            if (!ShouldBlockCheatEvent(eventName, evnt, steamId, entry, connection, out reason, out validation))
            {
                RecordInventorySignal(steamId, eventName, evnt, false, "allowed", validation);
                return true;
            }

            string eventDetails = DescribeCheatEvent(evnt);
            string learnedNote;
            if (LearnedPolicyAllowsBlockedEvent(steamId, eventName, reason, eventDetails, out learnedNote))
            {
                RecordInventorySignal(steamId, eventName, evnt, false, "learned allow: " + learnedNote, validation);
                AppendEventLog("LEARN", Shorten("Allowed " + eventName + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy allowed " + eventName + " from " + steamId + ": " + learnedNote);
                return true;
            }

            if (LearnedPolicySuppressesSelfAdmin(steamId, eventName, reason, eventDetails, out learnedNote))
            {
                RecordInventorySignal(steamId, eventName, evnt, true, "learned review: " + learnedNote + RejectedPreSpawnedObjectStatus(evnt), validation);
                AppendEventLog("LEARN", Shorten("Blocked without punishment " + eventName + " for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                LogInfo("Learned policy suppressed self-admin action for " + eventName + " from " + steamId + ": " + learnedNote);
                return false;
            }

            if (LearnedPolicyConfirmsCheat(steamId, eventName, reason, eventDetails, out learnedNote))
            {
                reason = reason + " | learned confirmed pattern: " + Shorten(learnedNote, 80);
            }

            reason += RejectedPreSpawnedObjectStatus(evnt);
            RecordInventorySignal(steamId, eventName, evnt, true, reason, validation);
            AnnounceGuardBlock(entry, steamId, eventName, reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "Anti-cheat " + eventName, false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), connection, entity);
            }
            else
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason + " was blocked.", connection, entity);
            }

            LogWarning("Anti-cheat blocked " + eventName + " from " + steamId + " (" + reason + ")");
            return false;
        }

        private static bool IsOwnServerPolicyCommand(string eventName, object evnt)
        {
            if (!String.Equals(eventName, "AdminCommand", StringComparison.OrdinalIgnoreCase) || evnt == null)
            {
                return false;
            }
            string command = Convert.ToString(ReflectionTools.GetMemberValue(evnt, "Command") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Command"), CultureInfo.InvariantCulture);
            string data = Convert.ToString(ReflectionTools.GetMemberValue(evnt, "Data") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Data"), CultureInfo.InvariantCulture);
            return String.Equals(command, "setcheatsenabled", StringComparison.OrdinalIgnoreCase)
                && String.Equals(data, "false", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCandidateCheatEvent(string eventName)
        {
            return String.Equals(eventName, "AdminCommand", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "debugCommand", StringComparison.OrdinalIgnoreCase)
                || IsItemBearingServerEvent(eventName)
                || IsWorldMutationServerEvent(eventName);
        }

        private bool ShouldBlockCheatEvent(string eventName, object evnt, string steamId, PlayerEntry entry, object connection, out string reason, out string validation)
        {
            reason = "";
            validation = _nativeServerValidationGuard ? "Not evaluated" : "Disabled";
            if (String.Equals(eventName, "AdminCommand", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "debugCommand", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Unauthorized cheat command: " + DescribeCheatEvent(evnt);
                return true;
            }

            if (_jailedWorldEventGuard && IsJailedSteamId(steamId)
                && (IsItemBearingServerEvent(eventName) || IsWorldMutationServerEvent(eventName)))
            {
                reason = "Jailed player attempted blocked server event: " + DescribeCheatEvent(evnt);
                return true;
            }

            if (_nativeServerValidationGuard && TryNativeServerValidationViolation(eventName, evnt, connection, steamId, out reason, out validation))
            {
                return true;
            }

            if (!_strictItemEventGuard)
            {
                return false;
            }

            if (!IsItemBearingServerEvent(eventName))
            {
                return false;
            }

            int count = ExtractEventItemCount(evnt);
            int itemId = ExtractEventItemId(evnt);
            string itemDescription = DescribeItemEvent(evnt, itemId, count);
            bool negativeStorageCount = IsStorageItemEvent(eventName)
                && ReflectionTools.GetMemberValue(evnt, "Count") is int requestedCount && requestedCount < 0;
            bool ReadPosition(out Vector3 position, out string source) => TryGetItemEventPosition(evnt, out position, out source);
            return ShouldBlockItemPolicy(eventName, steamId, entry, itemId, count, itemDescription,
                negativeStorageCount, ReadPosition, out reason);
        }

        private static int GetServerValidationDistance(string eventName)
        {
            if (String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupRequest", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DropItem", StringComparison.OrdinalIgnoreCase))
            {
                return 35;
            }
            if (String.Equals(eventName, "SpawnCutTree", StringComparison.OrdinalIgnoreCase))
            {
                return 65;
            }
            if (String.Equals(eventName, "DismantleScrewStructure", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DestroyPickUp", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DisablePickup", StringComparison.OrdinalIgnoreCase))
            {
                return 50;
            }
            if (String.Equals(eventName, "FreeFormPlaceElement", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceConstruction", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceFoundationEx", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeAddIngredient", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeCreate", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeComplete", StringComparison.OrdinalIgnoreCase))
            {
                return 45;
            }
            return 40;
        }

        private static bool IsPositiveItemAcquisitionEvent(string eventName)
        {
            return String.Equals(eventName, "PlayerAddItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "SpawnLootItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupRequest", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemStorageRemoveItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemHolderTakeItem", StringComparison.OrdinalIgnoreCase);
        }

        private List<GuardedItemLocationRule> GetStoryItemLocationRules(int itemId, string descriptor)
        {
            List<GuardedItemLocationRule> result = new List<GuardedItemLocationRule>();
            string normalizedDescriptor = NormalizeGuardText(descriptor);
            for (int i = 0; i < _storyItemLocationRules.Count; i++)
            {
                GuardedItemLocationRule rule = _storyItemLocationRules[i];
                if (rule == null)
                {
                    continue;
                }
                if (rule.ItemId > 0 && itemId == rule.ItemId)
                {
                    result.Add(rule);
                    continue;
                }
                if (!String.IsNullOrEmpty(rule.NormalizedToken) && normalizedDescriptor.IndexOf(rule.NormalizedToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(rule);
                }
            }
            return result;
        }

        private static bool IsItemBearingServerEvent(string eventName)
        {
            return String.Equals(eventName, "DropItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "SpawnLootItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlayerAddItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlayerRemoveItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemRemoveFromPlayer", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemStorageAddItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemStorageRemoveItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlayerGiveActorItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlayerRequestActorItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "AttachItemToZiplineEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemHolderAddItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemHolderTakeItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupRequest", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StealItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "AddItemToDoor", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWorldMutationServerEvent(string eventName)
        {
            return String.Equals(eventName, "FreeFormPlaceElement", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceConstruction", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceFoundationEx", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceDryingFood", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceRope", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlaceTrophy", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureHitEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DismantleScrewStructure", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DropVailActorEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "SpawnVailPrefabEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "SpawnCutTree", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeAddIngredient", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeCreate", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeComplete", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ArtifactStructureActivateEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemPlatingStructureActivateEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DestroyPickUp", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "DisablePickup", StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanPrefabMemberName(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return "";
            }
            if (name.StartsWith("NativeFieldInfoPtr_", StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring("NativeFieldInfoPtr_".Length);
            }
            return name.Trim();
        }

        private static string StripKnownPrefabSuffix(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return "";
            }

            string result = name.Trim();
            string[] suffixes = new string[]
            {
                "PickupItem",
                "ItemPickup",
                "Pickup",
                "AmmoProjectile",
                "Projectile",
                "ClientGhost",
                "StructureNode",
                "Structure",
                "Entity",
                "Prefab"
            };
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < suffixes.Length; i++)
                {
                    if (result.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase) && result.Length > suffixes[i].Length)
                    {
                        result = result.Substring(0, result.Length - suffixes[i].Length);
                        changed = true;
                    }
                }
            }
            return result.Trim('_', ' ', '-');
        }

        private static string KnownItemAliasFromPrefabName(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return "";
            }

            string normalized = name.Trim('_', ' ', '-');
            if (normalized.Equals("CompactPistol", StringComparison.OrdinalIgnoreCase) || normalized.Equals("CompactPistolPickup", StringComparison.OrdinalIgnoreCase))
            {
                return "Pistol";
            }
            if (normalized.Equals("ShotgunPumpAction", StringComparison.OrdinalIgnoreCase) || normalized.Equals("ShotgunPumpActionPickup", StringComparison.OrdinalIgnoreCase))
            {
                return "Shotgun";
            }
            if (normalized.Equals("HangGliderPickup", StringComparison.OrdinalIgnoreCase))
            {
                return "HangGlider";
            }
            if (normalized.Equals("KnightVPickup", StringComparison.OrdinalIgnoreCase))
            {
                return "KnightV";
            }
            if (normalized.Equals("3dPrintedSledPickup", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("3DPrintedSledPickup", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("PrintedSledPickup", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("3dPrintedSled", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("3DPrintedSled", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("PrintedSled", StringComparison.OrdinalIgnoreCase))
            {
                return "PrintedSled";
            }
            if (normalized.Equals("GolfCart", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("GolfCartClientGhost", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("SnowGolfCart", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0 ? "SnowGolfCart" : "GolfCart";
            }
            return "";
        }

        private static string NormalizeLeadingUnderscoreNumber(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return "";
            }
            string result = name.Trim();
            while (result.StartsWith("_", StringComparison.Ordinal))
            {
                result = result.Substring(1);
            }
            if (result.StartsWith("3d", StringComparison.OrdinalIgnoreCase))
            {
                return "3D" + result.Substring(2);
            }
            return result;
        }

        private static bool TryParseLastPositiveInt(string text, out int parsed)
        {
            parsed = 0;
            if (String.IsNullOrEmpty(text))
            {
                return false;
            }

            int lastStart = -1;
            int lastLength = 0;
            int currentStart = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsDigit(text[i]))
                {
                    if (currentStart < 0)
                    {
                        currentStart = i;
                    }
                    continue;
                }
                if (currentStart >= 0)
                {
                    lastStart = currentStart;
                    lastLength = i - currentStart;
                    currentStart = -1;
                }
            }
            if (currentStart >= 0)
            {
                lastStart = currentStart;
                lastLength = text.Length - currentStart;
            }
            if (lastStart < 0 || lastLength <= 0)
            {
                return false;
            }

            string digits = text.Substring(lastStart, lastLength);
            return Int32.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
        }

        private bool IsJailedSteamId(string steamId)
        {
            steamId = CleanSteamId(steamId);
            return LooksLikeSteamId(steamId) && _jailedPlayers.ContainsKey(steamId);
        }

        private bool ItemMatchesTokens(int itemId, string descriptor, string tokensText)
        {
            if (String.IsNullOrEmpty(tokensText))
            {
                return false;
            }

            string normalizedDescriptor = NormalizeGuardText(descriptor);
            string[] tokens = tokensText.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (String.IsNullOrEmpty(token))
                {
                    continue;
                }

                int parsed;
                if (Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    if (itemId > 0 && itemId == parsed)
                    {
                        return true;
                    }
                    continue;
                }

                if (itemId > 0)
                {
                    int resolved = ResolveItemIdByName(token);
                    if (resolved == itemId)
                    {
                        return true;
                    }
                }

                string normalizedToken = NormalizeGuardText(token);
                if (normalizedToken.Length >= 3 && normalizedDescriptor.IndexOf(normalizedToken, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAllowedCommonTransportItem(int itemId, string descriptor)
        {
            if (itemId == 626)
            {
                return true;
            }
            if (String.IsNullOrEmpty(descriptor))
            {
                return false;
            }
            return descriptor.IndexOf("PickupGlider", StringComparison.OrdinalIgnoreCase) >= 0
                || descriptor.IndexOf("HangGliderPickup", StringComparison.OrdinalIgnoreCase) >= 0
                || descriptor.IndexOf("item=HangGlider", StringComparison.OrdinalIgnoreCase) >= 0
                || descriptor.IndexOf("itemId=626", StringComparison.OrdinalIgnoreCase) >= 0
                || descriptor.IndexOf("itemId = 626", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeGuardText(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            char[] buffer = new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = Char.ToLowerInvariant(text[i]);
                if (Char.IsLetterOrDigit(c))
                {
                    buffer[count++] = c;
                }
            }
            return count == 0 ? "" : new string(buffer, 0, count);
        }

        private bool TrackItemBurst(string steamId, int itemId, int count, string eventName, string itemDescription, out string reason)
        {
            reason = "";
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return false;
            }
            if (IsAllowedCommonTransportItem(itemId, itemDescription)
                || String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            ItemEventWindow window;
            if (!_itemEventWindows.TryGetValue(steamId, out window) || window == null || now.Subtract(window.WindowStartUtc).TotalSeconds > Math.Max(1f, _itemBurstWindowSeconds))
            {
                window = new ItemEventWindow();
                window.SteamId = steamId;
                window.WindowStartUtc = now;
                _itemEventWindows[steamId] = window;
            }

            int amount = Math.Max(1, count);
            bool constructionSalvage = IsBasicConstructionSalvageDrop(eventName, itemId, itemDescription);
            window.TotalCount = (int)Math.Min(Int32.MaxValue, (long)window.TotalCount + amount);
            if (constructionSalvage)
            {
                window.ConstructionSalvageCount = (int)Math.Min(Int32.MaxValue, (long)window.ConstructionSalvageCount + amount);
            }
            else
            {
                window.SuspiciousCount = (int)Math.Min(Int32.MaxValue, (long)window.SuspiciousCount + amount);
            }
            window.EventCount++;
            window.LastEventUtc = now;
            window.LastEventName = eventName;
            if (itemId > 0)
            {
                string idText = itemId.ToString(CultureInfo.InvariantCulture);
                if (String.IsNullOrEmpty(window.ItemIds))
                {
                    window.ItemIds = idText;
                }
                else if (("," + window.ItemIds + ",").IndexOf("," + idText + ",", StringComparison.Ordinal) < 0)
                {
                    window.ItemIds += "," + idText;
                }
            }
            else
            {
                string label = ExtractReadableItemLabelFromDescriptor(itemDescription);
                if (!String.IsNullOrEmpty(label) && (String.IsNullOrEmpty(window.ItemIds)
                    || ("," + window.ItemIds + ",").IndexOf("," + label + ",", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    window.ItemIds = String.IsNullOrEmpty(window.ItemIds) ? label : window.ItemIds + "," + label;
                }
            }

            if (!constructionSalvage && window.SuspiciousCount >= Math.Max(2, _itemBurstCountThreshold))
            {
                reason = window.SuspiciousCount.ToString(CultureInfo.InvariantCulture)
                    + " item/event units in " + Math.Max(1f, _itemBurstWindowSeconds).ToString("0.#", CultureInfo.InvariantCulture)
                    + "s, ids=" + (String.IsNullOrEmpty(window.ItemIds) ? "unknown" : window.ItemIds);
                return true;
            }
            if (constructionSalvage && window.ConstructionSalvageCount >= Math.Max(_constructionSalvageBurstCountThreshold, (long)_itemBurstCountThreshold * 3))
            {
                reason = window.ConstructionSalvageCount.ToString(CultureInfo.InvariantCulture)
                    + " salvage item/event units in " + Math.Max(1f, _itemBurstWindowSeconds).ToString("0.#", CultureInfo.InvariantCulture)
                    + "s, ids=" + (String.IsNullOrEmpty(window.ItemIds) ? "basic material" : window.ItemIds);
                return true;
            }
            return false;
        }

        private bool IsBasicConstructionSalvageDrop(string eventName, int itemId, string itemDescription)
        {
            if (!String.Equals(eventName, "DropItem", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (DescriptorContainsAny(itemDescription, new string[] { "StickPickup", "StonePickup", "StickPickupBundle", "LogPlank", "Firewood", "LogPickup" }))
            {
                return true;
            }
            if (ItemMatchesTokens(itemId, itemDescription, _artifactItemTokens)
                || ItemMatchesTokens(itemId, itemDescription, _highValueItemTokens)
                || ItemMatchesTokens(itemId, itemDescription, _restrictedEarlyItemTokens))
            {
                return false;
            }
            return ItemMatchesTokens(itemId, itemDescription, BasicConstructionSalvageTokens);
        }

        private static bool DescriptorContainsAny(string text, string[] tokens)
        {
            if (String.IsNullOrEmpty(text) || tokens == null)
            {
                return false;
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!String.IsNullOrEmpty(tokens[i]) && text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void RecordInventorySignal(string steamId, string eventName, object evnt, bool blocked, string reason, string validation = "Not evaluated")
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId) || String.IsNullOrEmpty(eventName))
            {
                return;
            }

            int itemId = ExtractEventItemId(evnt);
            int count = ExtractEventItemCount(evnt);
            RecordInventorySignal(steamId, eventName, itemId, count, DescribeItemEvent(evnt, itemId, count),
                CaptureEventGuardPolicy(eventName, evnt), "Server event / Bolt sender", blocked, reason, validation);
        }

        private void RecordInventorySignal(string steamId, string eventName, int itemId, int count, string descriptor,
            string policy, string source, bool blocked, string reason, string validation)
        {
            if (IsServerApprovedItem(steamId, itemId)) reason += "; host-approved item for this session";
            InventorySignalState state;
            if (!_inventorySignals.TryGetValue(steamId, out state) || state == null)
            {
                state = new InventorySignalState();
                state.SteamId = steamId;
                _inventorySignals[steamId] = state;
            }

            state.SignalCount++;
            if (blocked)
            {
                state.BlockedCount++;
            }
            state.LastEventName = eventName;
            state.LastReason = CleanConfigText(reason, 120);
            state.LastItemId = itemId;
            state.LastCount = count;
            state.LastSeenUtc = DateTime.UtcNow;
            state.LastDescriptor = Shorten(descriptor, 180);
            state.LastEventReason = state.LastReason;
            state.LastEventDescriptor = state.LastDescriptor;
            state.LastEventValidation = validation;
            RecordGuardEvidence(steamId, GuardEventCategory(eventName),
                source, eventName + ": " + state.LastEventDescriptor,
                policy + "; validation: " + validation, (blocked ? "Blocked: " : "Allowed: ") + reason);

            bool allowedCommonTransportItem = IsAllowedCommonTransportItem(itemId, state.LastDescriptor)
                || String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase);
            if (!allowedCommonTransportItem && ItemMatchesTokens(itemId, state.LastDescriptor, _highValueItemTokens))
            {
                state.HighValueSignals++;
            }
            if (ItemMatchesTokens(itemId, state.LastDescriptor, _artifactItemTokens))
            {
                state.ArtifactSignals++;
            }

            if (itemId > 0 && count > 0 && !blocked)
            {
                int old;
                state.KnownItemDeltas.TryGetValue(itemId, out old);
                int delta = InventorySignalDelta(eventName, count);
                if (delta != 0)
                {
                    state.KnownItemDeltas[itemId] = (int)Math.Max(0L, Math.Min(Int32.MaxValue, (long)old + delta));
                }
            }
        }

        private static int InventorySignalDelta(string eventName, int count)
        {
            if (count <= 0) return 0;
            int amount = count;
            if (String.Equals(eventName, "PlayerAddItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "SpawnLootItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemStorageRemoveItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PickupRequest", StringComparison.OrdinalIgnoreCase))
            {
                return amount;
            }
            if (String.Equals(eventName, "DropItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "PlayerRemoveItem", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemRemoveFromPlayer", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "ItemStorageAddItemEvent", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "StructureCraftingNodeAddIngredient", StringComparison.OrdinalIgnoreCase)
                || String.Equals(eventName, "FreeFormPlaceElement", StringComparison.OrdinalIgnoreCase))
            {
                return -amount;
            }
            return 0;
        }

        private float GetPlayerSessionMinutes(string steamId, PlayerEntry entry)
        {
            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            if (entry == null && LooksLikeSteamId(steamId))
            {
                _registeredPlayers.TryGetValue(CleanSteamId(steamId), out entry);
            }
            if (entry == null || entry.FirstSeenUtc == DateTime.MinValue)
            {
                return 0f;
            }
            return (float)Math.Max(0.0, DateTime.UtcNow.Subtract(entry.FirstSeenUtc).TotalMinutes);
        }

        private bool IsEarlyRestrictedItem(int itemId)
        {
            if (itemId <= 0 || String.IsNullOrEmpty(_restrictedEarlyItemTokens))
            {
                return false;
            }

            string[] tokens = _restrictedEarlyItemTokens.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (String.IsNullOrEmpty(token))
                {
                    continue;
                }
                int parsed;
                if (Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    if (parsed == itemId)
                    {
                        return true;
                    }
                    continue;
                }

                int resolved = ResolveItemIdByName(token);
                if (resolved == itemId)
                {
                    return true;
                }
            }
            return false;
        }

        private int ResolveItemIdByName(string name)
        {
            name = CleanConfigText(name, 80);
            if (String.IsNullOrEmpty(name))
            {
                return -1;
            }
            int cached;
            if (_itemNameIdCache.TryGetValue(name, out cached))
            {
                return cached;
            }

            object result = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "ItemIdByName", new object[] { name });
            int parsed;
            if (!TryConvertToInt(result, out parsed) || parsed <= 0)
            {
                result = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "ItemByName", new object[] { name });
                object idValue = ReflectionTools.GetMemberValue(result, "Id")
                    ?? ReflectionTools.GetMemberValue(result, "ItemId")
                    ?? ReflectionTools.GetMemberValue(result, "_id")
                    ?? ReflectionTools.InvokeNoArgMethod(result, "get_Id")
                    ?? ReflectionTools.InvokeNoArgMethod(result, "get_ItemId");
                if (!TryConvertToInt(idValue, out parsed))
                {
                    parsed = -1;
                }
            }
            _itemNameIdCache[name] = parsed;
            if (parsed > 0 && !_itemIdNameCache.ContainsKey(parsed))
            {
                _itemIdNameCache[parsed] = name;
            }
            return parsed;
        }

        private string ResolveItemNameById(int itemId)
        {
            if (itemId <= 0 || itemId >= 10000)
            {
                return "";
            }
            if (_unresolvedItemIds.Contains(itemId))
            {
                return "";
            }
            string cached;
            if (_itemIdNameCache.TryGetValue(itemId, out cached) && !String.IsNullOrEmpty(cached))
            {
                return cached;
            }

            object item = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "ItemById", new object[] { itemId });
            if (item is InvokeFailed || item == null)
            {
                item = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "ItemById", new object[] { (uint)itemId });
            }
            if (item is InvokeFailed || item == null)
            {
                item = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "GetItem", new object[] { itemId });
            }
            if (item is InvokeFailed || item == null)
            {
                item = InvokeStaticMethodResult("Sons.Items.Core.ItemDatabaseManager", "GetItem", new object[] { (uint)itemId });
            }

            string name = ExtractItemDatabaseName(item);
            if (!String.IsNullOrEmpty(name))
            {
                _itemIdNameCache[itemId] = name;
                if (!_itemNameIdCache.ContainsKey(name))
                {
                    _itemNameIdCache[name] = itemId;
                }
            }
            else
            {
                _unresolvedItemIds.Add(itemId);
            }
            return name;
        }

        private static string ExtractItemDatabaseName(object item)
        {
            if (item == null || item is InvokeFailed)
            {
                return "";
            }
            string name = CleanConfigText(ReflectionTools.GetMemberValue(item, "Name") as string
                ?? ReflectionTools.InvokeNoArgMethod(item, "get_Name") as string
                ?? ReflectionTools.GetMemberValue(item, "_name") as string
                ?? ReflectionTools.GetMemberValue(item, "DisplayName") as string
                ?? ReflectionTools.InvokeNoArgMethod(item, "get_DisplayName") as string, 80);
            if (String.IsNullOrEmpty(name)
                || name.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PlayerNetwork", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("UnityEngine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("System.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "";
            }
            return name;
        }

        private string DescribeItemEvent(object evnt, int itemId, int count)
        {
            if (IsStorageItemEvent(evnt?.GetType().Name)) return DescribeStorageItemEvent(evnt, itemId, count);
            int prefabId;
            if (!TryExtractEventPrefabId(evnt, out prefabId)) prefabId = 0;
            return DescribeItemEventDetails(evnt?.GetType().Name, DescribeCheatEvent(evnt), itemId, count, prefabId);
        }

        private string DescribeItemEventDetails(string eventName, string descriptor, int itemId, int count, int prefabId)
        {
            string prefabDescription = "";
            string prefabName = "";
            if (prefabId > 0)
            {
                prefabName = GetBoltPrefabName(prefabId);
                prefabDescription = ", prefabId=" + prefabId.ToString(CultureInfo.InvariantCulture)
                    + (String.IsNullOrEmpty(prefabName) ? "" : "/" + prefabName);
            }

            string knownName = KnownItemNameForId(itemId);
            if (String.IsNullOrEmpty(knownName))
            {
                knownName = ResolveItemNameById(itemId);
            }
            if (String.IsNullOrEmpty(knownName))
            {
                knownName = ReadableItemNameFromPrefabName(prefabName);
            }

            return descriptor
                + prefabDescription
                + ", itemId=" + (itemId > 0 ? itemId.ToString(CultureInfo.InvariantCulture) : "unresolved")
                + (String.IsNullOrEmpty(knownName) ? "" : ", item=" + knownName)
                + (itemId > 0 && eventName == "PickupRequest" ? ", identity=live pickup and registered prefab" : "")
                + ", quantity=" + FormatEventItemCount(count);
        }

        private static string ReadableItemNameFromPrefabName(string prefabName)
        {
            if (String.IsNullOrEmpty(prefabName))
            {
                return "";
            }

            string[] parts = prefabName.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "";
            }

            string name = CleanPrefabMemberName(parts[0]);
            string alias = KnownItemAliasFromPrefabName(name);
            if (!String.IsNullOrEmpty(alias))
            {
                return alias;
            }

            string stripped = StripKnownPrefabSuffix(name);
            alias = KnownItemAliasFromPrefabName(stripped);
            if (!String.IsNullOrEmpty(alias))
            {
                return alias;
            }
            return NormalizeLeadingUnderscoreNumber(stripped);
        }

        private static string KnownItemNameForId(int itemId)
        {
            if (itemId == 626)
            {
                return "HangGlider";
            }
            if (itemId == 630)
            {
                return "KnightV";
            }
            return "";
        }

        private bool TryResolveEventSource(object evnt, out string steamId, out object entity, out object connection)
        {
            steamId = "";
            entity = null;
            connection = null;
            if (evnt == null)
            {
                return false;
            }

            object[] connectionCandidates = new object[]
            {
                ReflectionTools.GetMemberValue(evnt, "RaisedBy") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_RaisedBy"),
                ReflectionTools.GetMemberValue(evnt, "SourceConnection") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_SourceConnection"),
                ReflectionTools.GetMemberValue(evnt, "Connection"),
                ReflectionTools.GetMemberValue(evnt, "BoltConnection"),
                ReflectionTools.GetMemberValue(evnt, "Sender"),
                ReflectionTools.GetMemberValue(evnt, "Source")
            };
            for (int i = 0; i < connectionCandidates.Length; i++)
            {
                object candidate = UnwrapNullable(connectionCandidates[i]);
                string found = CleanSteamId(GetSteamIdFromConnection(candidate));
                if (LooksLikeSteamId(found))
                {
                    steamId = found;
                    connection = candidate;
                    entity = GetEntityFromConnection(connection);
                    return true;
                }
            }

            object[] entityCandidates = new object[]
            {
                ReflectionTools.GetMemberValue(evnt, "Player") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Player"),
                ReflectionTools.GetMemberValue(evnt, "PlayerEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PlayerEntity"),
                ReflectionTools.GetMemberValue(evnt, "PickUpPlayer") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PickUpPlayer"),
                ReflectionTools.GetMemberValue(evnt, "PickupPlayer") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PickupPlayer"),
                ReflectionTools.GetMemberValue(evnt, "SendingPlayerEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_SendingPlayerEntity"),
                ReflectionTools.GetMemberValue(evnt, "SenderEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_SenderEntity"),
                ReflectionTools.GetMemberValue(evnt, "SourceEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_SourceEntity"),
                ReflectionTools.GetMemberValue(evnt, "SourcePlayer") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_SourcePlayer"),
                ReflectionTools.GetMemberValue(evnt, "OwnerEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_OwnerEntity"),
                ReflectionTools.GetMemberValue(evnt, "Owner") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Owner"),
                ReflectionTools.GetMemberValue(evnt, "Actor") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Actor"),
                ReflectionTools.GetMemberValue(evnt, "thief") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_thief"),
                ReflectionTools.GetMemberValue(evnt, "target") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_target"),
                ReflectionTools.GetMemberValue(evnt, "Target") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Target"),
                ReflectionTools.GetMemberValue(evnt, "targetEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_targetEntity"),
                ReflectionTools.GetMemberValue(evnt, "TargetEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_TargetEntity"),
                ReflectionTools.GetMemberValue(evnt, "TargetStructure") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_TargetStructure"),
                ReflectionTools.GetMemberValue(evnt, "PickUpEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PickUpEntity"),
                ReflectionTools.GetMemberValue(evnt, "PickupEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PickupEntity"),
                ReflectionTools.GetMemberValue(evnt, "PreSpawned") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_PreSpawned"),
                ReflectionTools.GetMemberValue(evnt, "Sled") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_Sled"),
                ReflectionTools.GetMemberValue(evnt, "OccupiedEntity") ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_OccupiedEntity"),
                ReflectionTools.GetMemberValue(evnt, "BoltEntity")
            };
            for (int i = 0; i < entityCandidates.Length; i++)
            {
                object candidate = UnwrapNullable(entityCandidates[i]);
                string found = CleanSteamId(GetSteamIdFromEntity(candidate));
                if (LooksLikeSteamId(found))
                {
                    steamId = found;
                    entity = candidate;
                    connection = ExtractConnectionFromEntity(entity);
                    if (connection == null)
                    {
                        connection = GetConnectionFromSteamId(UInt64.Parse(steamId, CultureInfo.InvariantCulture));
                    }
                    return true;
                }
            }

            string[] networkMethods = new string[]
            {
                "GetPlayerNetworkIdValue",
                "GetPlayerEntityNetworkIdValue",
                "GetPickUpPlayerNetworkIdValue",
                "GetPickupPlayerNetworkIdValue",
                "GetSendingPlayerEntityNetworkIdValue",
                "GetSenderEntityNetworkIdValue",
                "GetSourceEntityNetworkIdValue",
                "GetSourcePlayerNetworkIdValue",
                "GetOwnerEntityNetworkIdValue",
                "GettargetNetworkIdValue",
                "GetTargetNetworkIdValue",
                "GettargetEntityNetworkIdValue",
                "GetTargetEntityNetworkIdValue",
                "GetTargetStructureNetworkIdValue",
                "GetPickUpEntityNetworkIdValue",
                "GetPickupEntityNetworkIdValue",
                "GetPreSpawnedNetworkIdValue",
                "GetActorNetworkIdValue",
                "GetthiefNetworkIdValue",
                "GetSledNetworkIdValue",
                "GetOccupiedEntityNetworkIdValue"
            };
            for (int i = 0; i < networkMethods.Length; i++)
            {
                object networkId = ReflectionTools.InvokeInstanceMethodResult(evnt, networkMethods[i], new object[0]);
                string found = TryGetSteamIdFromNetworkId(networkId, out entity, out connection);
                if (LooksLikeSteamId(found))
                {
                    steamId = found;
                    return true;
                }
            }

            Vector3 eventPosition;
            string positionSource;
            if (TryGetItemEventPosition(evnt, out eventPosition, out positionSource)
                && TryResolveNearestPlayerAtPosition(eventPosition, out steamId, out entity, out connection))
            {
                LogInfo("Resolved " + (evnt.GetType().Name ?? "event")
                    + " sender by nearest player at " + FormatVector(eventPosition)
                    + " via " + positionSource + " -> " + steamId);
                return true;
            }

            return false;
        }

        private bool TryResolveNearestPlayerAtPosition(Vector3 position, out string steamId, out object entity, out object connection)
        {
            steamId = "";
            entity = null;
            connection = null;

            float bestDistance = Single.MaxValue;
            float secondDistance = Single.MaxValue;
            PlayerEntry best = null;
            List<PlayerEntry> candidates = new List<PlayerEntry>();
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null)
                {
                    candidates.Add(_players[i]);
                }
            }
            foreach (PlayerEntry entry in _registeredPlayers.Values)
            {
                if (entry != null)
                {
                    bool duplicate = false;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (String.Equals(candidates[i].SteamId, entry.SteamId, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        candidates.Add(entry);
                    }
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                PlayerEntry candidate = candidates[i];
                if (candidate == null || candidate.PresenceUnresolved || !LooksLikeSteamId(candidate.SteamId))
                {
                    continue;
                }
                if (!candidate.Online && candidate.LastSeenUtc != DateTime.MinValue && DateTime.UtcNow.Subtract(candidate.LastSeenUtc).TotalSeconds > 8.0)
                {
                    continue;
                }

                Vector3 playerPosition;
                if (!TryGetPlayerWorldPosition(candidate, out playerPosition))
                {
                    continue;
                }
                float distance = Vector3.Distance(position, playerPosition);
                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    best = candidate;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }

            if (best == null || bestDistance > 45f)
            {
                return false;
            }
            if (secondDistance < Single.MaxValue && secondDistance - bestDistance < 4f)
            {
                return false;
            }

            steamId = CleanSteamId(best.SteamId);
            entity = best.BoltEntity;
            connection = best.BoltConnection;
            ulong parsed;
            if ((entity == null || connection == null) && UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                TryRefreshPlayerNetworkHandles(best, parsed);
                entity = best.BoltEntity;
                connection = best.BoltConnection;
            }
            return LooksLikeSteamId(steamId);
        }

        private bool IsSteamIdAdminOrOwner(string steamId, PlayerEntry entry)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return false;
            }

            string local = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (LooksLikeSteamId(local) && String.Equals(local, steamId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            if (entry != null && !String.IsNullOrEmpty(entry.Role) && RoleTextAllowsCheats(entry.Role))
            {
                return true;
            }

            ulong parsed;
            if (!UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            object admin = InvokeRolesMethodResult("IsAdmin", new object[] { parsed });
            if (admin is bool && (bool)admin)
            {
                return true;
            }
            object role = InvokeRolesMethodResult("GetRoleValue", new object[] { parsed });
            return role != null && RoleTextAllowsCheats(role.ToString());
        }

        private static bool RoleTextAllowsCheats(string role)
        {
            if (String.IsNullOrEmpty(role))
            {
                return false;
            }
            return role.IndexOf("Admin", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("Owner", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("Host", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string DescribeCheatEvent(object evnt)
        {
            if (evnt == null)
            {
                return "unknown event";
            }
            string name = evnt.GetType().Name ?? "event";
            if (IsStorageItemEvent(name)) return DescribeStorageItemEvent(evnt, ReadStorageInstanceItemId(evnt), ExtractEventItemCount(evnt));
            List<string> parts = new List<string>();
            string[] fields = new string[]
            {
                "Command",
                "Data",
                "input",
                "input2",
                "ItemId",
                "ItemID",
                "Item",
                "ItemInstance",
                "PrefabId",
                "PrefabID",
                "prefabId",
                "Prefab",
                "PickupPrefab",
                "ItemPrefabId",
                "SpawnPrefab",
                "GliderId",
                "gliderId",
                "Count",
                "Amount",
                "ItemCount",
                "ContentInfo",
                "ModuleID",
                "HeldOnlyItemCount",
                "PickUpPlayer",
                "PickupPlayer",
                "PickUpEntity",
                "PickupEntity",
                "PreSpawned",
                "SendingPlayerEntity",
                "Player",
                "Target",
                "TargetStructure",
                "targetEntity",
                "OccupiedEntity",
                "Velocity",
                "ShouldDespawn",
                "Dynamic",
                "FakeDrop",
                "SibblingId",
                "Radius",
                "Damage",
                "IsInvalid",
                "IsPlacing",
                "PlacementMode",
                "PlaceActionNum",
                "SlotIndex",
                "Disconnect",
                "Position",
                "PlacePosition",
                "dropPos",
                "DropPos"
            };
            for (int i = 0; i < fields.Length; i++)
            {
                object value = ReflectionTools.GetMemberValue(evnt, fields[i]) ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_" + fields[i]);
                AppendEventField(parts, fields[i], value);
            }
            return parts.Count == 0 ? name : name + " " + String.Join(", ", parts.ToArray());
        }

        private static void AppendEventField(List<string> parts, string field, object value)
        {
            string text = CleanConfigText(DescribeEventFieldValue(field, value), 64);
            if (!String.IsNullOrEmpty(text) && !String.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                parts.Add(HumanizeMemberName(field) + " " + text);
        }

        private static string DescribeEventFieldValue(string fieldName, object value)
        {
            if (value == null || value is InvokeFailed)
            {
                return "";
            }
            int parsed;
            if (TryConvertToInt(value, out parsed))
            {
                return parsed.ToString(CultureInfo.InvariantCulture);
            }
            bool boolValue;
            if (value is bool)
            {
                boolValue = (bool)value;
                return boolValue ? "yes" : "no";
            }
            if (value is float || value is double || value is decimal)
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (value is Vector3)
            {
                Vector3 vector = (Vector3)value;
                return FormatVector(vector);
            }
            string text = DescribeObject(value);
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            return SimplifyTypeNoise(text);
        }

        private static string HumanizeMemberName(string memberName)
        {
            if (String.IsNullOrEmpty(memberName))
            {
                return "value";
            }
            string normalized = memberName.Trim('_');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (i > 0 && Char.IsUpper(c) && !Char.IsUpper(normalized[i - 1]) && normalized[i - 1] != ' ')
                {
                    sb.Append(' ');
                }
                else if (i > 0 && Char.IsDigit(c) && !Char.IsDigit(normalized[i - 1]) && normalized[i - 1] != ' ')
                {
                    sb.Append(' ');
                }
                sb.Append(Char.ToLowerInvariant(c));
            }
            string[] words = sb.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (String.Equals(words[i], "id", StringComparison.OrdinalIgnoreCase))
                {
                    words[i] = "ID";
                }
            }
            return String.Join(" ", words);
        }

        internal bool FilterChatEvent(object listener, object chatEvent)
        {
            if (LoaderChatSubscribed) return true;
            try
            {
                if (chatEvent == null)
                {
                    return true;
                }

                string listenerName = listener == null ? "" : (listener.GetType().FullName ?? listener.GetType().Name);
                if (listenerName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                string message = ReflectionTools.GetMemberValue(chatEvent, "Message") as string;
                if (HandleInventoryAuditResponse(chatEvent, message))
                {
                    return false;
                }
                if (String.IsNullOrEmpty(message))
                {
                    LogWarning("Blocked malformed empty ChatEvent before server callback.");
                    return false;
                }

                if (_mutedPlayers.Count == 0)
                {
                    return true;
                }

                if (String.Equals(message, "[muted]", StringComparison.Ordinal))
                {
                    return true;
                }

                ModerationEntry muted;
                string steamId;
                object entity;
                object connection;
                if (!TryResolveMutedSender(chatEvent, out muted, out steamId, out entity, out connection))
                {
                    return true;
                }

                ReflectionTools.SetMemberValue(chatEvent, "Message", "[muted]");
                SendMuteNotice(FindPlayerBySteamId(muted.SteamId), muted.SteamId, false, connection, entity);
                LogInfo("Blocked muted chat from " + muted.SteamId + " on server callback; marker displayed");
                return false;
            }
            catch (Exception ex)
            {
                LogWarning("ChatEvent filter failed open: " + ex.GetType().Name + " " + ex.Message);
                return true;
            }
        }

        internal bool FilterLocalChatSend(string line)
        {
            if (_mutedPlayers.Count == 0 || String.IsNullOrEmpty(line) || line.Trim().Length == 0)
            {
                return true;
            }

            string localSteamId = ResolveMutedLocalSteamId();
            if (!LooksLikeSteamId(localSteamId))
            {
                return true;
            }

            ShowLocalMutedChatNotice(localSteamId);
            LogInfo("Blocked local muted chat send from " + localSteamId);
            return false;
        }

        internal bool FilterLocalChatSubmit(object chatBox)
        {
            if (_mutedPlayers.Count == 0)
            {
                return true;
            }

            string localSteamId = ResolveMutedLocalSteamId();
            if (!LooksLikeSteamId(localSteamId))
            {
                return true;
            }

            ClearChatBoxInput(chatBox);
            ShowLocalMutedChatNotice(localSteamId);
            LogInfo("Blocked local muted chat submit from " + localSteamId);
            return false;
        }

        private string ResolveMutedLocalSteamId()
        {
            string localSteamId = CleanSteamId(GetLocalSteamIdFromRuntime());
            if (LooksLikeSteamId(localSteamId) && _mutedPlayers.ContainsKey(localSteamId))
            {
                return localSteamId;
            }

            localSteamId = CleanSteamId(GetLocalSteamIdFromRoster());
            if (LooksLikeSteamId(localSteamId) && _mutedPlayers.ContainsKey(localSteamId))
            {
                return localSteamId;
            }

            PlayerEntry selected = GetSelectedPlayer();
            if (IsLocalPlayerEntry(selected) && LooksLikeSteamId(selected.SteamId) && _mutedPlayers.ContainsKey(selected.SteamId))
            {
                return selected.SteamId;
            }

            return "";
        }

        private string GetLocalSteamIdFromRoster()
        {
            string id = GetLocalSteamIdFromEntries(_players);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            id = GetLocalSteamIdFromEntries(_sessionPlayers.Values);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            return GetLocalSteamIdFromEntries(_registeredPlayers.Values);
        }

        private static string GetLocalSteamIdFromEntries(IEnumerable<PlayerEntry> entries)
        {
            if (entries == null)
            {
                return "";
            }
            foreach (PlayerEntry entry in entries)
            {
                if (IsLocalPlayerEntry(entry) && LooksLikeSteamId(entry.SteamId))
                {
                    return entry.SteamId;
                }
            }
            return "";
        }

        private static bool IsLocalPlayerEntry(PlayerEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            string role = entry.Role ?? "";
            string source = entry.Source ?? "";
            return role.IndexOf("Local", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("LocalPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ClearChatBoxInput(object chatBox)
        {
            object input = ReflectionTools.GetMemberValue(chatBox, "_input") ?? ReflectionTools.GetMemberValue(chatBox, "Input") ?? ReflectionTools.GetMemberValue(chatBox, "input");
            if (input != null)
            {
                TrySetTextValue(input, "");
                ReflectionTools.SetMemberValue(input, "m_Text", "");
                ReflectionTools.SetMemberValue(input, "_text", "");
                ReflectionTools.InvokeInstanceMethod(input, "SetTextWithoutNotify", new object[] { "" });
                ReflectionTools.InvokeInstanceMethod(input, "ForceLabelUpdate", new object[0]);
            }
            ReflectionTools.InvokeInstanceMethod(chatBox, "ForceRefreshInput", new object[0]);
        }

        private void ShowLocalMutedChatNotice(string steamId)
        {
            DateTime now = DateTime.UtcNow;
            if (now.Subtract(_lastLocalMuteNoticeUtc).TotalSeconds < 2.0)
            {
                return;
            }
            _lastLocalMuteNoticeUtc = now;

            string message = String.IsNullOrEmpty(_muteMessageInput) ? "You are muted on this server." : CleanConfigText(_muteMessageInput, 120);
            bool shown = InvokeStaticMethod("CoopAdminCommand", "SendLocalMessage", new object[] { message });
            if (!shown)
            {
                shown = ShowLocalStatusBarNotice(message);
            }
            if (!shown)
            {
                LogWarning("Local mute notice could not be shown for " + steamId);
            }
        }

        internal void RememberChatPlayer(object nameValue, object networkId, string source)
        {
            object senderValue = UnwrapNullable(networkId);
            object entity;
            object connection;
            string steamId = TryGetSteamIdFromNetworkId(senderValue, out entity, out connection);
            if (!LooksLikeSteamId(steamId))
            {
                LogChatPlayerProbe(source, nameValue, senderValue, "", entity, connection);
                return;
            }
            string name = CleanPlayerProbe(nameValue);
            RememberRegisteredClient(connection, steamId, name, source);
            PlayerEntry entry;
            if (_registeredPlayers.TryGetValue(steamId, out entry) && entry != null)
            {
                if (entity != null) entry.BoltEntity = entity;
                if (connection != null) entry.BoltConnection = connection;
                entry.NetworkId = NormalizeNetworkKey(senderValue);
                ApplyBetterPlayerName(entry, name);
            }
            LogChatPlayerProbe(source, nameValue, senderValue, steamId, entity, connection);
        }

        internal void ForgetChatPlayer(object networkId, string source)
        {
            object senderValue = UnwrapNullable(networkId);
            List<string> remove = new List<string>();
            string senderKey = NormalizeNetworkKey(senderValue);
            foreach (KeyValuePair<string, PlayerEntry> pair in _registeredPlayers)
            {
                PlayerEntry entry = pair.Value;
                if (entry != null && !String.IsNullOrEmpty(senderKey) && String.Equals(NormalizeNetworkKey(entry.NetworkId), senderKey, StringComparison.OrdinalIgnoreCase))
                {
                    remove.Add(pair.Key);
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                _registeredPlayers.Remove(remove[i]);
            }
            if (remove.Count > 0)
            {
                _nextRosterRefreshUtc = DateTime.MinValue;
                LogInfo("Chat player removed " + remove.Count.ToString(CultureInfo.InvariantCulture) + " via " + source);
            }
        }

        private bool TryResolveMutedSender(object chatEvent, out ModerationEntry muted, out string steamId, out object entity, out object connection)
        {
            muted = null;
            steamId = "";
            entity = null;
            connection = null;
            object sender = ReflectionTools.GetMemberValue(chatEvent, "Sender") ?? ReflectionTools.GetMemberValue(chatEvent, "Source") ?? ReflectionTools.GetMemberValue(chatEvent, "Connection");
            return TryResolveMutedSenderValue(sender, out muted, out steamId, out entity, out connection);
        }

        private bool TryResolveChatSender(object chatEvent, out string steamId, out object entity, out object connection)
        {
            steamId = "";
            entity = null;
            connection = null;
            if (chatEvent == null)
            {
                return false;
            }

            object sender = ReflectionTools.GetMemberValue(chatEvent, "Sender")
                ?? ReflectionTools.GetMemberValue(chatEvent, "Source")
                ?? ReflectionTools.GetMemberValue(chatEvent, "Connection");
            sender = UnwrapNullable(sender);
            steamId = CleanSteamId(TryGetSteamIdFromValue(sender));
            if (LooksLikeSteamId(steamId))
            {
                return true;
            }

            steamId = TryGetSteamIdFromNetworkId(sender, out entity, out connection);
            if (LooksLikeSteamId(steamId))
            {
                return true;
            }

            return false;
        }

        private bool TryResolveMutedSenderValue(object sender, out ModerationEntry muted, out string steamId, out object entity, out object connection)
        {
            muted = null;
            steamId = "";
            entity = null;
            connection = null;
            sender = UnwrapNullable(sender);
            string senderSteam = CleanSteamId(TryGetSteamIdFromValue(sender));
            if (LooksLikeSteamId(senderSteam) && _mutedPlayers.TryGetValue(senderSteam, out muted))
            {
                steamId = senderSteam;
                return true;
            }

            senderSteam = TryGetSteamIdFromNetworkId(sender, out entity, out connection);
            if (LooksLikeSteamId(senderSteam) && _mutedPlayers.TryGetValue(senderSteam, out muted))
            {
                steamId = senderSteam;
                return true;
            }

            muted = null;
            return false;
        }

        private string TryGetSteamIdFromNetworkId(object networkId, out object entity, out object connection)
        {
            entity = null;
            connection = null;
            if (networkId == null)
            {
                return "";
            }

            ulong packed;
            if (TryGetNetworkIdPacked(networkId, out packed))
            {
                string fromPacked = TryGetSteamIdFromPlayerEntityCandidate(packed, out entity, out connection);
                if (LooksLikeSteamId(fromPacked))
                {
                    LogNetworkIdProbe("packed", networkId, packed, fromPacked, entity, connection);
                    return fromPacked;
                }

                uint connectionId;
                if (TryGetConnectionIdFromNetworkId(networkId, out connectionId))
                {
                    connection = GetConnectionFromConnectionId(unchecked((int)connectionId));
                    entity = GetEntityFromConnectionId(unchecked((int)connectionId));
                    if (entity == null && connection != null)
                    {
                        entity = GetEntityFromConnection(connection);
                    }
                    string fromConnection = GetSteamIdFromConnection(connection);
                    if (!LooksLikeSteamId(fromConnection) && entity != null)
                    {
                        fromConnection = GetSteamIdFromEntity(entity);
                    }
                    if (LooksLikeSteamId(fromConnection))
                    {
                        LogNetworkIdProbe("connectionId " + connectionId.ToString(CultureInfo.InvariantCulture), networkId, packed, fromConnection, entity, connection);
                        return fromConnection;
                    }
                    LogNetworkIdProbe("connectionId unresolved " + connectionId.ToString(CultureInfo.InvariantCulture), networkId, packed, "", entity, connection);
                }

                uint low = unchecked((uint)(packed & 0xffffffffUL));
                uint high = unchecked((uint)(packed >> 32));
                ulong[] candidates = new ulong[] { low, high };
                for (int i = 0; i < candidates.Length; i++)
                {
                    string fromCandidate = TryGetSteamIdFromPlayerEntityCandidate(candidates[i], out entity, out connection);
                    if (LooksLikeSteamId(fromCandidate))
                    {
                        LogNetworkIdProbe("packed-part " + candidates[i].ToString(CultureInfo.InvariantCulture), networkId, packed, fromCandidate, entity, connection);
                        return fromCandidate;
                    }
                }
            }

            entity = FindEntityByNetworkId(networkId);
            string steamId = GetSteamIdFromEntity(entity);
            if (LooksLikeSteamId(steamId))
            {
                ulong steam;
                if (UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out steam))
                {
                    connection = GetConnectionFromSteamId(steam);
                }
                if (connection == null)
                {
                    connection = ExtractConnectionFromEntity(entity);
                }
                LogNetworkIdProbe("BoltNetwork.FindEntity", networkId, packed, steamId, entity, connection);
                return steamId;
            }

            string senderKey = NormalizeNetworkKey(networkId);
            if (!String.IsNullOrEmpty(senderKey) && !IsAmbiguousNetworkKey(senderKey))
            {
                foreach (PlayerEntry entry in _registeredPlayers.Values)
                {
                    if (entry != null && String.Equals(NormalizeNetworkKey(entry.NetworkId), senderKey, StringComparison.OrdinalIgnoreCase))
                    {
                        entity = entry.BoltEntity;
                        connection = entry.BoltConnection;
                        return entry.SteamId;
                    }
                }
            }
            return "";
        }

        private string TryGetSteamIdFromPlayerEntityCandidate(ulong candidate, out object entity, out object connection)
        {
            entity = null;
            connection = null;
            if (candidate == 0UL)
            {
                return "";
            }

            Type serverInfo = ReflectionTools.FindType("CoopServerInfo");
            object[] args = new object[] { candidate, 0UL };
            object result;
            if (InvokeMethod(null, serverInfo, "TryGetSteamIdFromPlayerEntity", args, true, out result)
                && result is bool
                && (bool)result)
            {
                string id = CleanSteamId(TryGetSteamIdFromValue(args[1]));
                if (LooksLikeSteamId(id))
                {
                    ulong steam;
                    if (UInt64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out steam))
                    {
                        connection = GetConnectionFromSteamId(steam);
                        entity = GetEntityFromSteamId(steam);
                    }
                    return id;
                }
            }
            return "";
        }

        private bool TryGetConnectionIdFromNetworkId(object networkId, out uint connectionId)
        {
            connectionId = 0U;
            networkId = UnwrapNullable(networkId);
            if (networkId == null)
            {
                return false;
            }
            Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities") ?? ReflectionTools.FindType("MultiplayerUtilities");
            object result;
            if (InvokeMethod(null, utilities, "GetBoltConnectionIdFromPlayerEntityNetworkId", new object[] { networkId }, true, out result) && result != null)
            {
                try
                {
                    connectionId = Convert.ToUInt32(result, CultureInfo.InvariantCulture);
                    return connectionId != 0U;
                }
                catch { }
            }
            return false;
        }

        private void LogChatPlayerProbe(string source, object nameValue, object networkId, string steamId, object entity, object connection)
        {
            if (_chatPlayerProbeLogs >= 24)
            {
                return;
            }
            _chatPlayerProbeLogs++;
            LogInfo("Chat player probe via " + (source ?? "unknown")
                + " name=" + Shorten(CleanPlayerProbe(nameValue), 32)
                + " network=" + Shorten(DescribeObject(networkId), 80)
                + " -> " + (LooksLikeSteamId(steamId) ? steamId : "no SteamID")
                + " entity=" + (entity == null ? "no" : "yes")
                + " connection=" + (connection == null ? "no" : "yes"));
        }

        private void LogNetworkIdProbe(string route, object networkId, ulong packed, string steamId, object entity, object connection)
        {
            if (_networkIdResolveLogs >= 24)
            {
                return;
            }
            _networkIdResolveLogs++;
            LogInfo("NetworkId probe " + route
                + " type=" + Shorten(DescribeObject(networkId), 80)
                + " packed=" + packed.ToString(CultureInfo.InvariantCulture)
                + " -> " + (LooksLikeSteamId(steamId) ? steamId : "no SteamID")
                + " entity=" + (entity == null ? "no" : "yes")
                + " connection=" + (connection == null ? "no" : "yes"));
        }

        private static object UnwrapNullable(object value)
        {
            if (value == null)
            {
                return null;
            }
            object hasValue = ReflectionTools.GetMemberValue(value, "HasValue") ?? ReflectionTools.InvokeNoArgMethod(value, "get_HasValue");
            if (hasValue is bool && !(bool)hasValue)
            {
                return null;
            }
            object inner = ReflectionTools.GetMemberValue(value, "Value") ?? ReflectionTools.InvokeNoArgMethod(value, "get_Value");
            return inner ?? value;
        }

        private static bool TryGetNetworkIdPacked(object networkId, out ulong packed)
        {
            packed = 0UL;
            networkId = UnwrapNullable(networkId);
            if (networkId == null)
            {
                return false;
            }
            object value = networkId;
            if (!(value is ulong) && !(value is long) && !(value is uint) && !(value is int))
            {
                value = ReflectionTools.GetMemberValue(networkId, "PackedValue")
                    ?? ReflectionTools.InvokeNoArgMethod(networkId, "get_PackedValue");
            }
            if (value == null)
            {
                return false;
            }
            try
            {
                packed = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                return packed != 0UL;
            }
            catch
            {
                return false;
            }
        }

        private object FindEntityByNetworkId(object networkId)
        {
            networkId = UnwrapNullable(networkId);
            if (networkId == null)
            {
                return null;
            }
            Type boltNetwork = ReflectionTools.FindType("BoltNetwork");
            object found = ReflectionTools.InvokeStaticMethodResult(boltNetwork, "FindEntity", new object[] { networkId });
            if (found == null)
            {
                return null;
            }
            if (TypeNameEquals(found, "BoltEntity"))
            {
                return found;
            }
            object unityObject = ExtractFirstMember(found, new string[] { "UnityObject", "BoltEntity", "Entity", "get_UnityObject" });
            return unityObject ?? found;
        }

        private static object ExtractConnectionFromEntity(object entity)
        {
            return ExtractFirstMember(entity, new string[] { "source", "Source", "controller", "Controller", "get_source", "get_controller", "Connection", "BoltConnection" });
        }

        private int SweepAliasSurfaces()
        {
            int changes = PushAliasAuthorityRules(false);
            HashSet<int> visited = new HashSet<int>();
            foreach (PlayerEntry entry in _players)
            {
                changes += ApplyAliasToPlayerEntrySurfaces(entry, visited);
            }
            foreach (PlayerEntry entry in _registeredPlayers.Values)
            {
                changes += ApplyAliasToPlayerEntrySurfaces(entry, visited);
            }
            changes += ApplyAliasToKnownPlayerListSurfaces(visited);

            string[] typeNames = new string[]
            {
                "CoopLobbyMember",
                "Sons.Multiplayer.PlayerData",
                "PlayerNetwork",
                "Sons.Multiplayer.PlayerNetwork",
                "Sons.Multiplayer.Client.CoopPlayerRemoteSetup",
                "Sons.Multiplayer.MultiplayerPlayerRoleData",
                "Sons.Multiplayer.MultiplayerPlayerRoles",
                "Sons.Multiplayer.CoopLobby",
                "Sons.Multiplayer.Gui.PlayerName",
                "Sons.UI.PlayerListElementView",
                "Sons.Multiplayer.Gui.ActivePlayerList",
                "Sons.Multiplayer.Gui.PlayerListBase"
            };

            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = ReflectionTools.FindType(typeNames[i]);
                if (type == null)
                {
                    continue;
                }
                Array objects = ReflectionTools.FindObjectsOfType(type);
                if (objects == null)
                {
                    continue;
                }
                foreach (object obj in objects)
                {
                    changes += ApplyAliasGraph(obj, null, 2, visited);
                }
            }
            return changes;
        }

        private int ApplyAliasToPlayerEntrySurfaces(PlayerEntry entry, HashSet<int> visited)
        {
            if (entry == null || String.IsNullOrEmpty(entry.SteamId))
            {
                return 0;
            }

            int changes = 0;
            changes += ApplyAliasGraph(entry.SourceObject, entry.SteamId, 3, visited);
            changes += ApplyAliasGraph(entry.BoltEntity, entry.SteamId, 3, visited);
            changes += ApplyAliasGraph(entry.BoltConnection, entry.SteamId, 2, visited);
            changes += ApplyAliasGraph(entry.Transform, entry.SteamId, 1, visited);
            return changes;
        }

        private int ApplyAliasToKnownPlayerListSurfaces(HashSet<int> visited)
        {
            Type listType = ReflectionTools.FindType("Sons.Multiplayer.Gui.PlayerListBase");
            object known = ReflectionTools.GetStaticMemberValue(listType, "_knownPlayers") ?? ReflectionTools.InvokeStaticNoArgMethod(listType, "get__knownPlayers");
            if (known == null)
            {
                return 0;
            }

            int changes = 0;
            VisitKeyValuePairs(known, delegate(object key, object value)
            {
                string id = ResolveAliasSteamIdForObject(value);
                if (!LooksLikeSteamId(id))
                {
                    id = ResolveAliasSteamIdForObject(key);
                }
                changes += ApplyAliasGraph(value, id, 2, visited);
                changes += ApplyAliasGraph(key, id, 1, visited);
                return true;
            });
            return changes;
        }

        private int ApplyAliasGraph(object value, string inheritedSteamId, int depth, HashSet<int> visited)
        {
            if (value == null || depth < 0 || IsLeafType(value.GetType()))
            {
                return 0;
            }
            int identity = RuntimeHelpers.GetHashCode(value);
            if (visited.Contains(identity))
            {
                return 0;
            }
            visited.Add(identity);
            string id = ResolveAliasSteamIdForObject(value);
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(inheritedSteamId);
            }
            int changes = ApplyAliasesToObject(value, id);

            if (depth == 0)
            {
                return changes;
            }
            changes += SweepObjectMembers(value, id, depth - 1, visited);
            return changes;
        }

        private int SweepObjectMembers(object instance, string inheritedSteamId, int depth, HashSet<int> visited)
        {
            int changes = 0;
            Type type = instance.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            while (type != null)
            {
                FieldInfo[] fields = SafeFields(type, flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (IsTraversalMember(fields[i].Name, fields[i].FieldType))
                    {
                        try { changes += ApplyAliasGraph(fields[i].GetValue(instance), inheritedSteamId, depth, visited); } catch { }
                    }
                }

                PropertyInfo[] props = SafeProps(type, flags);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].CanRead && props[i].GetIndexParameters().Length == 0 && IsTraversalMember(props[i].Name, props[i].PropertyType))
                    {
                        try { changes += ApplyAliasGraph(props[i].GetValue(instance, null), inheritedSteamId, depth, visited); } catch { }
                    }
                }
                type = type.BaseType;
            }
            return changes;
        }

        private int ApplyAliasesToObject(object instance)
        {
            return ApplyAliasesToObject(instance, null);
        }

        private int ApplyAliasesToObject(object instance, string inheritedSteamId)
        {
            string id = ResolveAliasSteamIdForObject(instance);
            if (!LooksLikeSteamId(id))
            {
                id = CleanSteamId(inheritedSteamId);
            }
            int changes = 0;
            changes += ApplyAliasToStringMembers(instance, id);
            changes += ApplyAliasToTextChildren(instance, id);
            changes += ApplyAliasToStateObject(instance, id);
            return changes;
        }

        private string ResolveAliasSteamIdForObject(object instance)
        {
            if (instance == null)
            {
                return "";
            }

            string id = CleanSteamId(ExtractSteamId(instance));
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            if (TypeNameEquals(instance, "BoltEntity"))
            {
                id = GetSteamIdFromEntity(instance);
                if (LooksLikeSteamId(id))
                {
                    return id;
                }
            }
            if (TypeNameContains(instance, "BoltConnection"))
            {
                id = GetSteamIdFromConnection(instance);
                if (LooksLikeSteamId(id))
                {
                    return id;
                }
            }

            object entity = FindBoltEntityForObject(instance);
            id = GetSteamIdFromEntity(entity);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            object connection = ExtractFirstMember(instance, new string[] { "Connection", "BoltConnection", "PlayerConnection", "_connection", "_boltConnection", "connection", "source", "Source" });
            id = GetSteamIdFromConnection(connection);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            object network = ExtractFirstMember(instance, new string[] { "NetworkId", "networkId", "NetworkID", "_networkId", "_networkID", "Sender", "EntityId", "Id", "get_NetworkId" });
            object resolvedEntity;
            object resolvedConnection;
            id = TryGetSteamIdFromNetworkId(network, out resolvedEntity, out resolvedConnection);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            string networkKey = NormalizeNetworkKey(instance);
            id = ResolveSteamIdFromCachedNetworkKey(networkKey);
            if (LooksLikeSteamId(id))
            {
                return id;
            }
            networkKey = ExtractNetworkId(instance);
            id = ResolveSteamIdFromCachedNetworkKey(networkKey);
            if (LooksLikeSteamId(id))
            {
                return id;
            }

            id = ResolveSteamIdFromKnownPlayerObject(instance);
            return LooksLikeSteamId(id) ? id : "";
        }

        private string ResolveSteamIdFromCachedNetworkKey(string networkKey)
        {
            networkKey = NormalizeNetworkKey(networkKey);
            if (String.IsNullOrEmpty(networkKey))
            {
                return "";
            }

            foreach (PlayerEntry entry in _registeredPlayers.Values)
            {
                if (entry != null
                    && LooksLikeSteamId(entry.SteamId)
                    && String.Equals(NormalizeNetworkKey(entry.NetworkId), networkKey, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.SteamId;
                }
            }
            foreach (PlayerEntry entry in _players)
            {
                if (entry != null
                    && LooksLikeSteamId(entry.SteamId)
                    && String.Equals(NormalizeNetworkKey(entry.NetworkId), networkKey, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.SteamId;
                }
            }
            return "";
        }

        private string ResolveSteamIdFromKnownPlayerObject(object instance)
        {
            foreach (PlayerEntry entry in _registeredPlayers.Values)
            {
                string id = ResolveSteamIdFromPlayerEntryObject(entry, instance);
                if (LooksLikeSteamId(id))
                {
                    return id;
                }
            }
            for (int i = 0; i < _players.Count; i++)
            {
                string id = ResolveSteamIdFromPlayerEntryObject(_players[i], instance);
                if (LooksLikeSteamId(id))
                {
                    return id;
                }
            }
            return "";
        }

        private static string ResolveSteamIdFromPlayerEntryObject(PlayerEntry entry, object instance)
        {
            if (entry == null || instance == null || !LooksLikeSteamId(entry.SteamId))
            {
                return "";
            }
            if (ObjectsSame(entry.SourceObject, instance)
                || ObjectsSame(entry.BoltEntity, instance)
                || ObjectsSame(entry.BoltConnection, instance)
                || ObjectsSame(entry.Transform, instance))
            {
                return entry.SteamId;
            }
            return "";
        }

        private int ApplyAliasToStateObject(object instance, string id)
        {
            if (instance == null)
            {
                return 0;
            }
            string alias = ResolveAlias(id, null);
            if (String.IsNullOrEmpty(alias))
            {
                return 0;
            }

            int changes = 0;
            object state = ReflectionTools.GetMemberValue(instance, "State")
                ?? ReflectionTools.GetMemberValue(instance, "state")
                ?? ReflectionTools.InvokeNoArgMethod(instance, "get_State")
                ?? ReflectionTools.InvokeNoArgMethod(instance, "GetState");
            if (state != null && !System.Object.ReferenceEquals(state, instance))
            {
                changes += ApplyAliasToStringMembers(state, id);
                changes += InvokeAliasNameSetters(state, alias);
            }
            changes += InvokeAliasNameSetters(instance, alias);
            return changes;
        }

        private int InvokeAliasNameSetters(object instance, string alias)
        {
            if (instance == null || String.IsNullOrEmpty(alias))
            {
                return 0;
            }

            int changes = 0;
            string[] methodNames = new string[] { "SetPlayerName", "SetDisplayName", "SetSteamName", "SetName", "set_PlayerName", "set_DisplayName", "set_Name" };
            Type type = instance.GetType();
            while (type != null)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { methods = new MethodInfo[0]; }
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.ContainsGenericParameters)
                    {
                        continue;
                    }
                    bool wanted = false;
                    for (int j = 0; j < methodNames.Length; j++)
                    {
                        if (String.Equals(method.Name, methodNames[j], StringComparison.Ordinal))
                        {
                            wanted = true;
                            break;
                        }
                    }
                    if (!wanted)
                    {
                        continue;
                    }
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters == null || parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }
                    try
                    {
                        method.Invoke(instance, new object[] { alias });
                        changes++;
                    }
                    catch { }
                }
                type = type.BaseType;
            }
            return changes;
        }

        private int ApplyAliasToStringMembers(object instance, string id)
        {
            if (instance == null)
            {
                return 0;
            }
            int changes = 0;
            Type type = instance.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            while (type != null)
            {
                FieldInfo[] fields = SafeFields(type, flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].FieldType == typeof(string) && IsNameMember(fields[i].Name))
                    {
                        try
                        {
                            string current = fields[i].GetValue(instance) as string;
                            string next = ResolveAlias(id, current);
                            if (!String.IsNullOrEmpty(next) && !String.Equals(current, next, StringComparison.Ordinal))
                            {
                                fields[i].SetValue(instance, next);
                                changes++;
                            }
                        }
                        catch { }
                    }
                }

                PropertyInfo[] props = SafeProps(type, flags);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType == typeof(string) && props[i].CanRead && props[i].CanWrite && props[i].GetIndexParameters().Length == 0 && IsNameMember(props[i].Name))
                    {
                        try
                        {
                            string current = props[i].GetValue(instance, null) as string;
                            string next = ResolveAlias(id, current);
                            if (!String.IsNullOrEmpty(next) && !String.Equals(current, next, StringComparison.Ordinal))
                            {
                                props[i].SetValue(instance, next, null);
                                changes++;
                            }
                        }
                        catch { }
                    }
                }
                type = type.BaseType;
            }
            return changes;
        }

        private int ApplyAliasToTextChildren(object instance, string id)
        {
            object gameObject = ReflectionTools.GetMemberValue(instance, "gameObject");
            if (gameObject == null)
            {
                return 0;
            }
            int changes = 0;
            Type[] textTypes = new Type[] { ReflectionTools.FindType("TMPro.TMP_Text"), ReflectionTools.FindType("UnityEngine.UI.Text") };
            for (int i = 0; i < textTypes.Length; i++)
            {
                Type textType = textTypes[i];
                if (textType == null)
                {
                    continue;
                }
                Array components = ReflectionTools.GetComponentsInChildren(gameObject, textType, true);
                if (components == null)
                {
                    continue;
                }
                foreach (object component in components)
                {
                    string current = ReflectionTools.GetMemberValue(component, "text") as string;
                    string next = ResolveAlias(id, current);
                    if (!String.IsNullOrEmpty(next) && !String.Equals(current, next, StringComparison.Ordinal))
                    {
                        if (ReflectionTools.SetMemberValue(component, "text", next))
                        {
                            changes++;
                        }
                    }
                }
            }
            return changes;
        }

        private string ResolveAlias(string id, string currentName)
        {
            string alias;
            if (!String.IsNullOrEmpty(id) && _aliases.TryGetValue(id, out alias))
            {
                return alias;
            }
            if (!String.IsNullOrEmpty(currentName) && _aliases.TryGetValue(currentName, out alias))
            {
                return alias;
            }
            return currentName;
        }

        internal string ExtractSteamId(object value)
        {
            if (value == null)
            {
                return null;
            }
            string direct = TryGetSteamIdFromValue(value);
            if (!String.IsNullOrEmpty(direct))
            {
                return direct;
            }
            string[] names = new string[] { "_steamId", "_steamID", "steamId", "SteamId", "SteamID", "SteamID64", "steamID64", "m_SteamID", "_playerSteamId", "PlayerSteamId", "GetPlayerSteamId", "ClientSteamId", "ClientSteamID", "CSteamId", "CSteamID", "OwnerSteamId", "Id", "IdString", "get_Id", "get_IdString", "Item1", "Item2" };
            for (int i = 0; i < names.Length; i++)
            {
                object member = ReflectionTools.GetMemberValue(value, names[i]);
                direct = TryGetSteamIdFromValue(member);
                if (!String.IsNullOrEmpty(direct)) return direct;
                member = ReflectionTools.InvokeNoArgMethod(value, names[i]);
                direct = TryGetSteamIdFromValue(member);
                if (!String.IsNullOrEmpty(direct)) return direct;
            }
            return null;
        }

        private static string ExtractNetworkId(object value)
        {
            if (value == null)
            {
                return "";
            }

            string[] names = new string[] { "NetworkId", "networkId", "NetworkID", "_networkId", "_networkID", "Sender", "EntityId", "Id" };
            for (int i = 0; i < names.Length; i++)
            {
                object member = ReflectionTools.GetMemberValue(value, names[i]) ?? ReflectionTools.InvokeNoArgMethod(value, names[i]);
                string text = NormalizeNetworkKey(member);
                if (!String.IsNullOrEmpty(text) && !System.Object.ReferenceEquals(member, value))
                {
                    return text;
                }
            }

            string typeName = value.GetType().FullName ?? value.GetType().Name;
            if (typeName.IndexOf("NetworkId", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NormalizeNetworkKey(value);
            }
            return "";
        }

        private string ExtractPlayerName(object value)
        {
            string[] names = new string[] { "Name", "name", "PlayerName", "DisplayName", "SteamName", "UserName", "Nickname", "_name", "_playerName", "_displayName", "_lastKnownPlayerName", "get_Name", "get_name", "get_PlayerName", "get_DisplayName", "GetPlayerName", "GetDisplayName", "GetSteamName", "Item2", "Item1" };
            for (int i = 0; i < names.Length; i++)
            {
                string text = CleanPlayerProbe(ReflectionTools.GetMemberValue(value, names[i]));
                if (!String.IsNullOrEmpty(text)) return text;
                text = CleanPlayerProbe(ReflectionTools.InvokeNoArgMethod(value, names[i]));
                if (!String.IsNullOrEmpty(text)) return text;
            }
            return "";
        }

        private static string TryGetSteamIdFromValue(object value)
        {
            if (value == null)
            {
                return null;
            }
            Type t = value.GetType();
            if (t == typeof(ulong) || t == typeof(long) || t == typeof(uint) || t == typeof(int))
            {
                string numeric = Convert.ToString(value, CultureInfo.InvariantCulture);
                return LooksLikeSteamId(numeric) ? numeric : null;
            }
            object nested = ReflectionTools.GetMemberValue(value, "m_SteamID");
            if (nested != null && !System.Object.ReferenceEquals(nested, value))
            {
                return TryGetSteamIdFromValue(nested);
            }
            string s = value.ToString();
            return LooksLikeSteamId(s) ? s : null;
        }

        private static bool LooksLikeSteamId(string text)
        {
            if (String.IsNullOrEmpty(text) || text.Length != 17)
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            ulong value;
            if (!UInt64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
            return value >= 76561197960265728UL && value <= 76561202255233023UL;
        }

        private static string CleanPlayerProbe(object value)
        {
            string text = value as string;
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            text = CleanName(text);
            if (String.IsNullOrEmpty(text) || LooksLikeSteamId(text))
            {
                return "";
            }
            if (IsNonPlayerObjectName(text))
            {
                return "";
            }
            if (text.IndexOf("Sons.", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("UnityEngine", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("System.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "";
            }
            return text;
        }

        private static bool IsNonPlayerObjectName(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return true;
            }
            string lower = text.Trim().ToLowerInvariant().Replace(" ", "");
            return String.Equals(lower, "controllerobjects", StringComparison.Ordinal)
                || String.Equals(lower, "controllerobject", StringComparison.Ordinal)
                || String.Equals(lower, "controller", StringComparison.Ordinal)
                || String.Equals(lower, "localplayer", StringComparison.Ordinal)
                || String.Equals(lower, "gameobject", StringComparison.Ordinal)
                || String.Equals(lower, "playernetwork", StringComparison.Ordinal)
                || String.Equals(lower, "playernetworkclone", StringComparison.Ordinal);
        }

        private bool InvokeRolesMethod(string methodName, object[] args)
        {
            object result = InvokeRolesMethodResult(methodName, args);
            return NativeActionAccepted(result);
        }

        private object InvokeRolesMethodResult(string methodName, object[] args)
        {
            Type type = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerPlayerRoles") ?? ReflectionTools.FindType("MultiplayerPlayerRoles");
            if (type == null)
            {
                return InvokeFailed.Value;
            }
            object result;
            object instance = FindRolesRuntimeObject(type);
            if (instance != null && InvokeMethod(instance, type, methodName, args, false, out result))
            {
                return result;
            }
            return InvokeMethod(null, type, methodName, args, true, out result) ? result : InvokeFailed.Value;
        }

        private bool TryCheckPlayerBanned(ulong steamId, out bool banned)
        {
            banned = false;
            object result = InvokeRolesMethodResult("CheckPlayerIsBanned", new object[] { steamId });
            if (result is bool)
            {
                banned = (bool)result;
                return true;
            }
            return false;
        }

        private bool InvokeStaticMethod(string typeName, string methodName, object[] args)
        {
            object result = InvokeStaticMethodResult(typeName, methodName, args);
            return !(result is InvokeFailed);
        }

        private object InvokeStaticMethodResult(string typeName, string methodName, object[] args)
        {
            Type type = ReflectionTools.FindType(typeName);
            if (type == null)
            {
                return InvokeFailed.Value;
            }
            object result;
            return InvokeMethod(null, type, methodName, args, true, out result) ? result : InvokeFailed.Value;
        }

        private object FindRuntimeObject(Type type)
        {
            string[] staticNames = new string[] { "Instance", "_instance", "Current", "CurrentInstance", "Singleton", "_singleton" };
            for (int i = 0; i < staticNames.Length; i++)
            {
                object value = ReflectionTools.GetStaticMemberValue(type, staticNames[i]);
                if (value != null)
                {
                    return value;
                }
            }
            Array objects = ReflectionTools.FindObjectsOfType(type);
            if (objects == null)
            {
                return null;
            }
            foreach (object obj in objects)
            {
                if (obj != null)
                {
                    return obj;
                }
            }
            return null;
        }

        private object FindRolesRuntimeObject(Type type)
        {
            return FindRuntimeObjectNoStaticProperties(type);
        }

        private object FindRuntimeObjectNoStaticProperties(Type type)
        {
            if (type == null)
            {
                return null;
            }

            string[] staticFieldNames = new string[] { "_instance", "Instance", "_singleton", "Singleton", "Current", "CurrentInstance" };
            for (int i = 0; i < staticFieldNames.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(staticFieldNames[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        continue;
                    }
                    object value = field.GetValue(null);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch { }
            }

            Array objects = ReflectionTools.FindObjectsOfType(type);
            if (objects == null)
            {
                return null;
            }
            foreach (object obj in objects)
            {
                if (obj != null)
                {
                    return obj;
                }
            }
            return null;
        }

        private bool InvokeMethod(object instance, Type type, string methodName, object[] args, bool staticOnly, out object result)
        {
            result = null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (staticOnly ? BindingFlags.Static : BindingFlags.Instance);
            while (type != null)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); } catch { methods = new MethodInfo[0]; }
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != methodName || method.ContainsGenericParameters)
                    {
                        continue;
                    }
                    if (!ParametersMatch(method.GetParameters(), args))
                    {
                        continue;
                    }
                    try
                    {
                        result = method.Invoke(instance, args);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Invoke failed for " + methodName + ": " + ex.Message);
                        return false;
                    }
                }
                type = type.BaseType;
            }
            return false;
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            args = args ?? new object[0];
            if (parameters == null || parameters.Length != args.Length)
            {
                return false;
            }
            for (int i = 0; i < parameters.Length; i++)
            {
                object arg = args[i];
                Type parameterType = parameters[i].ParameterType;
                Type effectiveType = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
                if (arg == null)
                {
                    if (!parameterType.IsByRef && effectiveType != null && effectiveType.IsValueType) return false;
                    continue;
                }
                Type argType = arg.GetType();
                if ((effectiveType != null && effectiveType.IsAssignableFrom(argType)) || effectiveType != null && effectiveType.IsEnum && argType == effectiveType)
                {
                    continue;
                }
                if ((effectiveType == typeof(ulong) && arg is ulong)
                    || (effectiveType == typeof(int) && arg is int)
                    || (effectiveType == typeof(uint) && arg is uint)
                    || (effectiveType == typeof(string) && arg is string)
                    || (effectiveType == typeof(bool) && arg is bool)
                    || (effectiveType == typeof(Vector3) && arg is Vector3)
                    || (effectiveType == typeof(Quaternion) && arg is Quaternion))
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private sealed class InvokeFailed
        {
            public static readonly InvokeFailed Value = new InvokeFailed();
        }

        private static object ExtractFirstMember(object value, string[] names)
        {
            if (value == null)
            {
                return null;
            }
            for (int i = 0; i < names.Length; i++)
            {
                object member = ReflectionTools.GetMemberValue(value, names[i]) ?? ReflectionTools.InvokeNoArgMethod(value, names[i]);
                if (member != null) return member;
            }
            return null;
        }

        private static object ExtractTransform(object value)
        {
            if (value == null)
            {
                return null;
            }
            object transform = ExtractFirstMember(value, new string[]
            {
                "transform",
                "Transform",
                "_transform",
                "targetTransform",
                "_connectedTransform",
                "ClientTransform",
                "RootTransform"
            });
            if (transform != null)
            {
                return transform;
            }
            object gameObject = ExtractFirstMember(value, new string[] { "gameObject", "GameObject", "Body", "PlayerBody", "_body" });
            transform = ExtractFirstMember(gameObject, new string[] { "transform", "Transform" });
            if (transform != null)
            {
                return transform;
            }
            string typeName = value.GetType().FullName ?? value.GetType().Name;
            if (typeName.IndexOf("Transform", StringComparison.OrdinalIgnoreCase) >= 0
                && ReflectionTools.GetMemberValue(value, "position") != null)
            {
                return value;
            }
            return null;
        }

        private static int ExtractConnectionId(object value)
        {
            object found = ExtractFirstMember(value, new string[] { "ConnectionId", "ConnectionID", "NetworkConnectionId", "_connectionId", "_connectionID", "get_ConnectionId" });
            try { return found == null ? -1 : Convert.ToInt32(found, CultureInfo.InvariantCulture); } catch { return -1; }
        }

        private static string ExtractRoleText(object value)
        {
            object found = ExtractFirstMember(value, new string[] { "PlayerRole", "Role", "_playerRole", "_role", "GetRoleValue", "get__playerRole" });
            return found == null ? "" : found.ToString();
        }

        private static bool ExtractBannedFlag(object value)
        {
            object found = ExtractFirstMember(value, new string[] { "IsBanned", "Banned", "_isBanned", "_banned", "get_IsBanned" });
            return found is bool && (bool)found;
        }

        public void ApplyRuntimePatches(Harmony harmony)
        {
            if (_runtimePatchesApplied || harmony == null)
            {
                return;
            }

            MethodInfo prefix = typeof(ChatEventHooks).GetMethod("ChatEventPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            Type chatType = ReflectionTools.FindType("ChatEvent");
            if (LoaderChatSubscribed)
            {
                LogInfo("Server chat policy uses the shared Modstoke SDK hook.");
            }
            else if (prefix != null && chatType != null)
            {
                _chatPatchCount += PatchChatListener(harmony, "CoopServerCallbacks", chatType, prefix);
            }
            else
            {
                LogWarning("ChatEvent patch target is not ready yet.");
            }

            MethodInfo chatSendPrefix = typeof(ChatEventHooks).GetMethod("ChatBoxSendLinePrefix", BindingFlags.Static | BindingFlags.NonPublic);
            _chatPatchCount += PatchInstanceMethod(harmony, "TheForest.UI.Multiplayer.ChatBox", "SendLine", chatSendPrefix, null, 1);
            MethodInfo chatSubmitPrefix = typeof(ChatEventHooks).GetMethod("ChatBoxSubmitPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            _chatPatchCount += PatchInstanceMethod(harmony, "TheForest.UI.Multiplayer.ChatBox", "OnSubmit", chatSubmitPrefix, null, 0);

            MethodInfo auditMessagePrefix = typeof(AuditEventHooks).GetMethod("SendMessageEventPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            Type sendMessageType = ReflectionTools.FindType("SendMessageEvent");
            _inventoryAuditPatchCount += PatchEventListener(harmony, "CoopBase", sendMessageType, auditMessagePrefix, "Inventory audit");

            MethodInfo registerClientPostfix = typeof(NetworkEventHooks).GetMethod("RegisterClientPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo unregisterClientPostfix = typeof(NetworkEventHooks).GetMethod("UnregisterClientPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo registerEntityPostfix = typeof(NetworkEventHooks).GetMethod("RegisterAttachedPlayerEntityPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo unregisterEntityPostfix = typeof(NetworkEventHooks).GetMethod("RegisterDetachedPlayerEntityPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo actorDisconnectedPostfix = typeof(NetworkEventHooks).GetMethod("PlayerActorDisconnectedPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            _networkPatchCount += PatchStaticMethod(harmony, "Sons.Multiplayer.GameServerManager", "RegisterClient", registerClientPostfix, 3);
            _networkPatchCount += PatchStaticMethod(harmony, "Sons.Multiplayer.GameServerManager", "UnregisterClient", unregisterClientPostfix, 2);
            _networkPatchCount += PatchStaticMethod(harmony, "Sons.Multiplayer.MultiplayerUtilities", "RegisterAttachedPlayerEntity", registerEntityPostfix, 1);
            _networkPatchCount += PatchStaticMethod(harmony, "Sons.Multiplayer.MultiplayerUtilities", "RegisterDetachedPlayerEntity", unregisterEntityPostfix, 1);
            _networkPatchCount += PatchStaticMethod(harmony, "CoopServerCallbacks", "RegisterPlayerConnection", registerEntityPostfix, 1);
            _networkPatchCount += PatchStaticMethod(harmony, "Sons.Gameplay.PlayerActorManager", "OnPlayerDisconnected", actorDisconnectedPostfix, 1);
            MethodInfo disconnectedPostfix = typeof(NetworkEventHooks).GetMethod("DisconnectedPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            _networkPatchCount += PatchInstanceMethod(harmony, "CoopServerCallbacks", "Disconnected", null, disconnectedPostfix, 1);
            _networkPatchCount += PatchInstanceMethod(harmony, "CoopAckChecker", "Disconnected", null, disconnectedPostfix, 1);

            MethodInfo chatRegisterPostfix = typeof(NetworkEventHooks).GetMethod("ChatBoxRegisterPlayerPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo chatUnregisterPostfix = typeof(NetworkEventHooks).GetMethod("ChatBoxUnregisterPlayerPostfix", BindingFlags.Static | BindingFlags.NonPublic);
            _networkPatchCount += PatchInstanceMethod(harmony, "TheForest.UI.Multiplayer.ChatBox", "RegisterPlayer", null, chatRegisterPostfix, 3);
            _networkPatchCount += PatchInstanceMethod(harmony, "TheForest.UI.Multiplayer.ChatBox", "UnregisterPlayer", null, chatUnregisterPostfix, 1);

            MethodInfo localMovementPostfix = typeof(MovementEventHooks).GetMethod("LocalMovementGracePostfix", BindingFlags.Static | BindingFlags.NonPublic);
            _networkPatchCount += PatchInstanceMethod(harmony, "Sons.Multiplayer.MultiplayerRespawnTimer", "DoRespawn", null, localMovementPostfix, 0);
            _networkPatchCount += PatchInstanceMethod(harmony, "Sons.Multiplayer.CoopDeathCutsceneHandler", "OnTiedUpStateChange", null, localMovementPostfix, 0);
            _networkPatchCount += PatchInstanceMethod(harmony, "Sons.Multiplayer.CoopDeathCutsceneHandler", "OnMarkerHashChange", null, localMovementPostfix, 0);

            MethodInfo cheatPrefix = typeof(CheatEventHooks).GetMethod("CheatEventPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            string[] cheatEventTypes = new string[]
            {
                "AdminCommand",
                "debugCommand",
                "DropItem",
                "SpawnLootItemEvent",
                "PlayerAddItem",
                "PlayerRemoveItem",
                "ItemRemoveFromPlayer",
                "ItemStorageAddItemEvent",
                "ItemStorageRemoveItemEvent",
                "PlayerGiveActorItem",
                "PlayerRequestActorItem",
                "AttachItemToZiplineEvent",
                "ItemHolderAddItem",
                "ItemHolderTakeItem",
                "PickupRequest",
                "PickupGlider",
                "StealItem",
                "AddItemToDoor",
                "FreeFormPlaceElement",
                "PlaceConstruction",
                "PlaceFoundationEx",
                "PlaceDryingFood",
                "PlaceRope",
                "PlaceTrophy",
                "StructureHitEvent",
                "DismantleScrewStructure",
                "DropVailActorEvent",
                "SpawnVailPrefabEvent",
                "SpawnCutTree",
                "StructureCraftingNodeAddIngredient",
                "StructureCraftingNodeCreate",
                "StructureCraftingNodeComplete",
                "ArtifactStructureActivateEvent",
                "ItemPlatingStructureActivateEvent",
                "DestroyPickUp",
                "DisablePickup"
            };
            for (int i = 0; i < cheatEventTypes.Length; i++)
            {
                if (LoaderAdminRequestsSubscribed && cheatEventTypes[i] == "AdminCommand") continue;
                Type eventType = ReflectionTools.FindType(cheatEventTypes[i]);
                _antiCheatPatchCount += PatchEventListener(harmony, "CoopServerCallbacks", eventType, cheatPrefix, "Anti-cheat");
            }
            _antiCheatPatchCount += PatchConstructionEventGuards(harmony, cheatPrefix);

            MethodInfo vehicleSpawnPrefix = typeof(VehicleSpawnHooks).GetMethod("BoltInstantiatePrefix", BindingFlags.Static | BindingFlags.NonPublic);
            _vehicleSpawnPatchCount += PatchBoltInstantiateGuard(harmony, vehicleSpawnPrefix);

            _runtimePatchesApplied = true;
            LogInfo("Runtime patches applied. ChatEvent patches=" + _chatPatchCount.ToString(CultureInfo.InvariantCulture)
                + ", network patches=" + _networkPatchCount.ToString(CultureInfo.InvariantCulture)
                + ", anti-cheat patches=" + _antiCheatPatchCount.ToString(CultureInfo.InvariantCulture)
                + ", construction patches=" + _constructionGuardPatchCount.ToString(CultureInfo.InvariantCulture)
                + ", audit patches=" + _inventoryAuditPatchCount.ToString(CultureInfo.InvariantCulture)
                + ", vehicle patches=" + _vehicleSpawnPatchCount.ToString(CultureInfo.InvariantCulture));
        }

        private int PatchBoltInstantiateGuard(Harmony harmony, MethodInfo prefix)
        {
            int count = 0;
            count += PatchStaticPrefixMethods(harmony, "BoltNetwork", "Instantiate", prefix, true, "Vehicle spawn");
            count += PatchStaticPrefixMethods(harmony, "Sons.Multiplayer.MultiplayerUtilities", "BoltInstantiateIfNeeded", prefix, false, "Vehicle spawn");
            count += PatchStaticPrefixMethods(harmony, "MultiplayerUtilities", "BoltInstantiateIfNeeded", prefix, false, "Vehicle spawn");
            return count;
        }

        private int PatchStaticPrefixMethods(Harmony harmony, string typeName, string methodName, MethodInfo prefix, bool requirePrefabIdFirst, string label)
        {
            Type type = ReflectionTools.FindType(typeName);
            if (type == null || prefix == null)
            {
                return 0;
            }
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); } catch { methods = new MethodInfo[0]; }
            int patched = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != methodName || method.ContainsGenericParameters)
                {
                    continue;
                }
                ParameterInfo[] parameters;
                try { parameters = method.GetParameters(); } catch { parameters = new ParameterInfo[0]; }
                if (parameters.Length == 0)
                {
                    continue;
                }
                if (requirePrefabIdFirst)
                {
                    string firstType = parameters[0].ParameterType == null ? "" : (parameters[0].ParameterType.FullName ?? parameters[0].ParameterType.Name);
                    if (firstType.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                    patched++;
                }
                catch (Exception ex)
                {
                    LogWarning((String.IsNullOrEmpty(label) ? "Static prefix" : label) + " patch failed for " + typeName + "." + methodName + ": " + ex.Message);
                }
            }
            return patched;
        }

        private int PatchStaticMethod(Harmony harmony, string typeName, string methodName, MethodInfo postfix, int parameterCount)
        {
            Type type = ReflectionTools.FindType(typeName);
            if (type == null || postfix == null)
            {
                return 0;
            }
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); } catch { methods = new MethodInfo[0]; }
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != methodName || method.ContainsGenericParameters)
                {
                    continue;
                }
                ParameterInfo[] parameters;
                try { parameters = method.GetParameters(); } catch { parameters = new ParameterInfo[0]; }
                if (parameters.Length != parameterCount)
                {
                    continue;
                }
                try
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    return 1;
                }
                catch (Exception ex)
                {
                    LogWarning("Network patch failed for " + typeName + "." + methodName + ": " + ex.Message);
                    return 0;
                }
            }
            return 0;
        }

        private int PatchInstanceMethod(Harmony harmony, string typeName, string methodName, MethodInfo prefix, MethodInfo postfix, int parameterCount)
        {
            Type type = ReflectionTools.FindType(typeName);
            if (type == null || prefix == null && postfix == null)
            {
                return 0;
            }
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { methods = new MethodInfo[0]; }
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != methodName || method.ContainsGenericParameters)
                {
                    continue;
                }
                ParameterInfo[] parameters;
                try { parameters = method.GetParameters(); } catch { parameters = new ParameterInfo[0]; }
                if (parameters.Length != parameterCount)
                {
                    continue;
                }
                try
                {
                    harmony.Patch(method, prefix == null ? null : new HarmonyMethod(prefix), postfix == null ? null : new HarmonyMethod(postfix));
                    return 1;
                }
                catch (Exception ex)
                {
                    LogWarning("Instance patch failed for " + typeName + "." + methodName + ": " + ex.Message);
                    return 0;
                }
            }
            return 0;
        }

        private int PatchChatListener(Harmony harmony, string typeName, Type chatType, MethodInfo prefix)
        {
            return PatchEventListener(harmony, typeName, chatType, prefix, "Chat");
        }

        private int PatchEventListener(Harmony harmony, string typeName, Type eventType, MethodInfo prefix, string label)
        {
            Type type = ReflectionTools.FindType(typeName);
            if (type == null || eventType == null || prefix == null)
            {
                return 0;
            }
            MethodInfo method = type.GetMethod("OnEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { eventType }, null);
            if (method == null)
            {
                return 0;
            }
            if (!System.Object.ReferenceEquals(method.DeclaringType, type))
            {
                return 0;
            }
            try
            {
                harmony.Patch(method, new HarmonyMethod(prefix));
                return 1;
            }
            catch (Exception ex)
            {
                LogWarning((String.IsNullOrEmpty(label) ? "Event" : label) + " patch failed for " + typeName + "." + eventType.Name + ": " + ex.Message);
                return 0;
            }
        }

        private void LoadModerationEntry(Dictionary<string, ModerationEntry> target, string steamId, string value, string source)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }

            string[] parts = (value ?? "").Split(new char[] { '|' });
            ModerationEntry entry = new ModerationEntry();
            entry.SteamId = steamId;
            entry.Name = parts.Length > 0 ? CleanConfigText(parts[0], 48) : "";
            entry.Reason = parts.Length > 1 ? CleanConfigText(parts[1], 96) : "";
            entry.AddedUtc = parts.Length > 2 ? CleanConfigText(parts[2], 48) : "";
            entry.Source = parts.Length > 3 ? CleanConfigText(parts[3], 48) : source;
            if (String.IsNullOrEmpty(entry.AddedUtc))
            {
                entry.AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
            target[steamId] = entry;
        }

        private static void WriteModerationSection(List<string> lines, string section, Dictionary<string, ModerationEntry> entries)
        {
            lines.Add("");
            lines.Add("[" + section + "]");
            List<string> keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                ModerationEntry entry = entries[keys[i]];
                lines.Add(keys[i] + "="
                    + CleanConfigText(entry.Name, 48) + "|"
                    + CleanConfigText(entry.Reason, 96) + "|"
                    + CleanConfigText(entry.AddedUtc, 48) + "|"
                    + CleanConfigText(entry.Source, 48));
            }
        }

        private void ImportPlayerHistoryForContext(string context, bool verbose)
        {
            string normalizedContext = NormalizeHistoryContext(context);
            _loadedPlayerHistoryContext = normalizedContext;
            if (String.IsNullOrEmpty(normalizedContext)) return;
            int imported = 0;
            foreach (PlayerEntry history in _playerHistory.Values)
            {
                if (history == null || !LooksLikeSteamId(history.SteamId) || !HistoryContextMatches(history.LastRosterContext, normalizedContext))
                {
                    continue;
                }

                PlayerEntry session;
                if (_sessionPlayers.TryGetValue(history.SteamId, out session) && session != null) continue;
                session = new PlayerEntry();
                session.Key = history.SteamId;
                session.FirstSeenUtc = history.FirstSeenUtc;
                session.LastSeenUtc = history.LastSeenUtc;
                _sessionPlayers[history.SteamId] = session;
                MergePlayerEntry(session, history);
                ClearPlayerHandles(session);
                session.Online = false;
                AddPlayerSource(session, "History");
                imported++;
            }
            RebuildPlayersFromSession();
            if (verbose && imported > 0)
            {
                LogInfo("Loaded " + imported.ToString(CultureInfo.InvariantCulture) + " saved offline player record(s)"
                    + (String.IsNullOrEmpty(normalizedContext) ? "" : " for this server context") + ".");
            }
        }

        private void EnsurePlayerHistoryForContext(string context)
        {
            string normalizedContext = NormalizeHistoryContext(context);
            if (String.Equals(_loadedPlayerHistoryContext, normalizedContext, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ImportPlayerHistoryForContext(normalizedContext, false);
        }

        private void TouchPlayerHistory(PlayerEntry entry, string activity, bool countActivity, bool forceSave)
        {
            if (entry == null)
            {
                return;
            }

            string steamId = CleanSteamId(entry.SteamId);
            if (!LooksLikeSteamId(steamId))
            {
                return;
            }

            string context = NormalizeHistoryContext(!String.IsNullOrEmpty(entry.LastRosterContext) ? entry.LastRosterContext : GetRosterContext());
            string key = BuildPlayerHistoryKey(context, steamId);
            PlayerEntry history;
            if (!_playerHistory.TryGetValue(key, out history) || history == null)
            {
                history = new PlayerEntry();
                history.Key = steamId;
                history.SteamId = steamId;
                history.FirstSeenUtc = DateTime.UtcNow;
                history.LastRosterContext = context;
                _playerHistory[key] = history;
            }

            MergePlayerEntry(history, entry);
            ClearPlayerHandles(history);
            history.Online = false;
            history.LastRosterContext = context;
            if (history.FirstSeenUtc == DateTime.MinValue)
            {
                history.FirstSeenUtc = entry.FirstSeenUtc == DateTime.MinValue ? DateTime.UtcNow : entry.FirstSeenUtc;
            }
            if (entry.LastSeenUtc != DateTime.MinValue)
            {
                history.LastSeenUtc = entry.LastSeenUtc;
            }
            else if (history.LastSeenUtc == DateTime.MinValue)
            {
                history.LastSeenUtc = DateTime.UtcNow;
            }

            string cleanActivity = CleanConfigText(activity, 180);
            if (!String.IsNullOrEmpty(cleanActivity))
            {
                history.LastActivity = cleanActivity;
                history.LastActivityUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (countActivity)
                {
                    history.ActivityCount++;
                }
                entry.LastActivity = history.LastActivity;
                entry.LastActivityUtc = history.LastActivityUtc;
                entry.ActivityCount = history.ActivityCount;
            }
            else if (String.IsNullOrEmpty(history.LastActivity))
            {
                history.LastActivity = "Seen online";
                history.LastActivityUtc = history.LastSeenUtc == DateTime.MinValue ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : history.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture);
            }

            entry.LastRosterContext = context;
            _pendingPlayerHistoryKeys.Add(key);
            SavePlayerHistoryThrottled(forceSave);
        }

        private void RecordPlayerActivity(PlayerEntry entry, string steamId, string activity)
        {
            steamId = CleanSteamId(steamId);
            RecordGuardEvidence(steamId, "Actions", "Player activity", activity, "", "Activity recorded");
            if (entry == null && LooksLikeSteamId(steamId))
            {
                entry = FindPlayerBySteamId(steamId);
            }
            if (entry == null && LooksLikeSteamId(steamId))
            {
                entry = new PlayerEntry();
                entry.Key = steamId;
                entry.SteamId = steamId;
                entry.Name = steamId;
                entry.Source = "Activity";
            }
            if (entry != null && LooksLikeSteamId(steamId) && !_sessionPlayers.ContainsKey(steamId))
            {
                PlayerEntry session = new PlayerEntry();
                session.Key = steamId;
                MergePlayerEntry(session, entry);
                session.Online = false;
                _sessionPlayers[steamId] = session;
                entry = session;
                RebuildPlayersFromSession();
            }
            TouchPlayerHistory(entry, activity, true, true);
        }

        private void RebuildPlayersFromSession()
        {
            _players.Clear();
            foreach (PlayerEntry entry in _sessionPlayers.Values)
            {
                EnrichPlayerEntry(entry);
                _players.Add(entry);
            }
            _players.Sort(delegate(PlayerEntry a, PlayerEntry b)
            {
                if (a.Online != b.Online)
                {
                    return a.Online ? -1 : 1;
                }
                return String.Compare(BestDisplayBaseName(a), BestDisplayBaseName(b), StringComparison.OrdinalIgnoreCase);
            });

            if (_players.Count > 0 && _selectedPlayerIndex >= _players.Count)
            {
                _selectedPlayerIndex = 0;
            }
        }

        private static string BuildPlayerHistoryKey(string context, string steamId)
        {
            return NormalizeHistoryContext(context) + "\t" + CleanSteamId(steamId);
        }

        private static string NormalizeHistoryContext(string context)
        {
            return CleanConfigText(context ?? "", 160);
        }

        private static bool HistoryContextMatches(string entryContext, string currentContext)
        {
            entryContext = NormalizeHistoryContext(entryContext);
            currentContext = NormalizeHistoryContext(currentContext);
            return !String.IsNullOrEmpty(currentContext)
                && String.Equals(entryContext, currentContext, StringComparison.OrdinalIgnoreCase);
        }

        private static string EncodeHistoryText(string text)
        {
            text = text ?? "";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private static string DecodeHistoryText(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch
            {
                return CleanConfigText(text, 180);
            }
        }

        private static DateTime ParseHistoryUtc(string text)
        {
            DateTime parsed;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) ? parsed : DateTime.MinValue;
        }

        private static string FormatHistoryUtc(DateTime value)
        {
            return value == DateTime.MinValue ? "" : value.ToString("o", CultureInfo.InvariantCulture);
        }

        private void RebuildStoryItemLocationRules()
        {
            _storyItemLocationRules.Clear();
            if (String.IsNullOrEmpty(_storyItemLocationRulesText))
            {
                return;
            }
            string[] parts = _storyItemLocationRulesText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                GuardedItemLocationRule rule;
                if (TryParseStoryItemLocationRule(parts[i], out rule))
                {
                    _storyItemLocationRules.Add(rule);
                }
                else
                {
                    LogWarning("Ignored malformed StoryItemLocationRules entry: " + Shorten(parts[i], 120));
                }
            }
        }

        private bool TryParseStoryItemLocationRule(string text, out GuardedItemLocationRule rule)
        {
            rule = null;
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            int at = text.IndexOf('@');
            if (at <= 0 || at >= text.Length - 1)
            {
                return false;
            }

            string token = text.Substring(0, at).Trim();
            string rest = text.Substring(at + 1).Trim();
            string label = token;
            int colon = rest.IndexOf(':');
            if (colon >= 0)
            {
                label = CleanConfigText(rest.Substring(colon + 1), 64);
                rest = rest.Substring(0, colon).Trim();
            }

            string[] coords = rest.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (coords.Length < 4)
            {
                return false;
            }

            float x;
            float y;
            float z;
            float radius;
            if (!Single.TryParse(coords[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !Single.TryParse(coords[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                || !Single.TryParse(coords[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z)
                || !Single.TryParse(coords[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out radius))
            {
                return false;
            }

            int itemId;
            if (!Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
            {
                itemId = ResolveItemIdByName(token);
            }

            rule = new GuardedItemLocationRule();
            rule.Token = CleanConfigText(token, 64);
            rule.NormalizedToken = NormalizeGuardText(token);
            rule.ItemId = itemId > 0 && itemId < 10000 ? itemId : 0;
            rule.Position = new Vector3(x, y, z);
            rule.Radius = Math.Max(5f, radius);
            rule.Label = String.IsNullOrEmpty(label) ? rule.Token : label;
            return !String.IsNullOrEmpty(rule.Token);
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null && AreUiTexturesAlive())
            {
                return;
            }
            _windowStyle = null;
            _whiteTex = MakePatternTex(new Color(0.035f, 0.039f, 0.036f, 1f), new Color(0.058f, 0.065f, 0.056f, 1f));
            _deepBlueTex = MakePatternTex(new Color(0.012f, 0.018f, 0.020f, 1f), new Color(0.035f, 0.051f, 0.056f, 1f));
            _blueTex = MakeTex(new Color(0.215f, 0.270f, 0.145f, 1f));
            _paleBlueTex = MakePatternTex(new Color(0.042f, 0.050f, 0.045f, 1f), new Color(0.065f, 0.076f, 0.060f, 1f));
            _rowActiveTex = MakePatternTex(new Color(0.098f, 0.125f, 0.070f, 1f), new Color(0.145f, 0.178f, 0.100f, 1f));
            _panelTex = MakePatternTex(new Color(0.012f, 0.014f, 0.013f, 1f), new Color(0.030f, 0.034f, 0.030f, 1f));
            _sidebarTex = MakePatternTex(new Color(0.022f, 0.026f, 0.024f, 1f), new Color(0.044f, 0.050f, 0.043f, 1f));
            _subtleTex = MakePatternTex(new Color(0.046f, 0.050f, 0.046f, 1f), new Color(0.072f, 0.080f, 0.066f, 1f));
            _buttonTex = MakePatternTex(new Color(0.068f, 0.074f, 0.068f, 1f), new Color(0.104f, 0.114f, 0.095f, 1f));
            _buttonHoverTex = MakeTex(new Color(0.145f, 0.170f, 0.125f, 1f));
            _inputTex = MakePatternTex(new Color(0.010f, 0.012f, 0.011f, 1f), new Color(0.031f, 0.037f, 0.033f, 1f));
            _inputFocusTex = MakePatternTex(new Color(0.044f, 0.052f, 0.043f, 1f), new Color(0.076f, 0.092f, 0.060f, 1f));
            _primaryTex = MakeTex(new Color(0.250f, 0.315f, 0.150f, 1f));
            _primaryHoverTex = MakeTex(new Color(0.345f, 0.430f, 0.205f, 1f));
            _goodTex = MakeTex(new Color(0.080f, 0.190f, 0.085f, 1f));
            _warnTex = MakeTex(new Color(0.335f, 0.245f, 0.060f, 1f));
            _dangerTex = MakeTex(new Color(0.420f, 0.090f, 0.070f, 1f));
            _dangerHoverTex = MakeTex(new Color(0.540f, 0.120f, 0.090f, 1f));
            _panelFrameTex = MakeFrameTex(new Color(0.030f, 0.035f, 0.032f, 1f), new Color(0.155f, 0.178f, 0.150f, 1f), new Color(0.030f, 0.085f, 0.115f, 1f));
            _sidebarFrameTex = MakeFrameTex(new Color(0.018f, 0.022f, 0.020f, 1f), new Color(0.105f, 0.125f, 0.105f, 1f), new Color(0.060f, 0.090f, 0.088f, 1f));
            _buttonFrameTex = MakeFrameTex(new Color(0.060f, 0.066f, 0.061f, 1f), new Color(0.132f, 0.148f, 0.125f, 1f), new Color(0.080f, 0.100f, 0.096f, 1f));
            _primaryFrameTex = MakeFrameTex(new Color(0.205f, 0.260f, 0.122f, 1f), new Color(0.365f, 0.455f, 0.220f, 1f), new Color(0.118f, 0.175f, 0.125f, 1f));
            _dangerFrameTex = MakeFrameTex(new Color(0.355f, 0.070f, 0.058f, 1f), new Color(0.650f, 0.165f, 0.125f, 1f), new Color(0.115f, 0.040f, 0.035f, 1f));

            Color iconColor = new Color(0.68f, 0.70f, 0.63f, 1f);
            Color actionIconColor = new Color(0.80f, 0.82f, 0.72f, 1f);
            _iconRoster = MakeIconTex("roster", iconColor);
            _iconHost = MakeIconTex("host", iconColor);
            _iconPlayers = MakeIconTex("players", iconColor);
            _iconWorld = MakeIconTex("world", iconColor);
            _iconComms = MakeIconTex("comms", iconColor);
            _iconBans = MakeIconTex("ban", iconColor);
            _iconNpc = MakeIconTex("npc", iconColor);
            _iconDebug = MakeIconTex("debug", iconColor);
            _iconItems = MakeIconTex("items", iconColor);
            _iconBuild = MakeIconTex("build", iconColor);
            _iconVisual = MakeIconTex("visual", iconColor);
            _iconServer = MakeIconTex("server", iconColor);
            _iconRaw = MakeIconTex("raw", iconColor);
            _iconHeal = MakeIconTex("heal", actionIconColor);
            _iconSpeed = MakeIconTex("speed", actionIconColor);
            _iconShield = MakeIconTex("shield", actionIconColor);
            _iconWarning = MakeIconTex("warning", actionIconColor);
            _iconSave = MakeIconTex("save", actionIconColor);
            _iconSummon = MakeIconTex("summon", actionIconColor);
            _iconGoTo = MakeIconTex("goto", actionIconColor);
            _iconKick = MakeIconTex("kick", actionIconColor);
            _iconMute = MakeIconTex("mute", actionIconColor);
            _iconBan = MakeIconTex("ban", actionIconColor);
            _iconCopy = MakeIconTex("copy", actionIconColor);

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _panelTex;
            _windowStyle.focused.background = _panelTex;
            _windowStyle.active.background = _panelTex;
            _windowStyle.hover.background = _panelTex;
            _windowStyle.onNormal.background = _panelTex;
            _windowStyle.onFocused.background = _panelTex;
            _windowStyle.onActive.background = _panelTex;
            _windowStyle.onHover.background = _panelTex;
            _windowStyle.normal.textColor = Color.clear;
            _windowStyle.focused.textColor = Color.clear;
            _windowStyle.padding = new RectOffset(0, 0, 0, 0);
            _windowStyle.border = new RectOffset(6, 6, 6, 6);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 24;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = new Color(0.68f, 0.82f, 0.90f, 1f);
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.wordWrap = false;
            _titleStyle.clipping = TextClipping.Clip;

            _headerMetaStyle = new GUIStyle(GUI.skin.label);
            _headerMetaStyle.fontSize = 12;
            _headerMetaStyle.fontStyle = FontStyle.Bold;
            _headerMetaStyle.normal.textColor = new Color(0.58f, 0.63f, 0.60f, 1f);
            _headerMetaStyle.alignment = TextAnchor.MiddleLeft;
            _headerMetaStyle.wordWrap = false;
            _headerMetaStyle.clipping = TextClipping.Clip;

            _smallHeaderStyle = new GUIStyle(GUI.skin.label);
            _smallHeaderStyle.fontSize = 16;
            _smallHeaderStyle.fontStyle = FontStyle.Bold;
            _smallHeaderStyle.normal.textColor = new Color(0.82f, 0.82f, 0.76f, 1f);
            _smallHeaderStyle.alignment = TextAnchor.MiddleLeft;
            _smallHeaderStyle.clipping = TextClipping.Clip;

            _sideHeaderStyle = new GUIStyle(_smallHeaderStyle);
            _sideHeaderStyle.fontSize = 12;
            _sideHeaderStyle.normal.textColor = new Color(0.58f, 0.67f, 0.48f, 1f);

            _sectionTitleStyle = new GUIStyle(_smallHeaderStyle);
            _sectionTitleStyle.fontSize = 13;
            _sectionTitleStyle.normal.textColor = new Color(0.78f, 0.78f, 0.72f, 1f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.wordWrap = true;
            _labelStyle.normal.textColor = new Color(0.76f, 0.76f, 0.70f, 1f);

            _hintStyle = new GUIStyle(_labelStyle);
            _hintStyle.fontSize = 12;
            _hintStyle.normal.textColor = new Color(0.52f, 0.54f, 0.50f, 1f);

            _monoStyle = new GUIStyle(_labelStyle);
            _monoStyle.fontSize = 12;
            _monoStyle.wordWrap = false;
            _monoStyle.clipping = TextClipping.Clip;

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = _panelFrameTex;
            _boxStyle.padding = new RectOffset(18, 18, 14, 16);
            _boxStyle.margin = new RectOffset(0, 0, 0, 10);
            _boxStyle.border = new RectOffset(7, 7, 7, 7);

            _sidebarStyle = new GUIStyle(GUI.skin.box);
            _sidebarStyle.normal.background = _sidebarFrameTex;
            _sidebarStyle.padding = new RectOffset(12, 12, 12, 12);
            _sidebarStyle.margin = new RectOffset(0, 0, 0, 0);
            _sidebarStyle.border = new RectOffset(7, 7, 7, 7);

            _contextStyle = new GUIStyle(GUI.skin.box);
            _contextStyle.normal.background = _panelFrameTex;
            _contextStyle.padding = new RectOffset(12, 12, 10, 8);
            _contextStyle.margin = new RectOffset(0, 0, 0, 0);
            _contextStyle.border = new RectOffset(7, 7, 7, 7);

            _contextLabelStyle = new GUIStyle(_hintStyle);
            _contextLabelStyle.fontSize = 11;
            _contextLabelStyle.fontStyle = FontStyle.Bold;
            _contextLabelStyle.normal.textColor = new Color(0.46f, 0.50f, 0.44f, 1f);
            _contextLabelStyle.clipping = TextClipping.Clip;

            _contextValueStyle = new GUIStyle(_labelStyle);
            _contextValueStyle.fontSize = 13;
            _contextValueStyle.fontStyle = FontStyle.Bold;
            _contextValueStyle.wordWrap = false;
            _contextValueStyle.clipping = TextClipping.Clip;

            _tableHeaderStyle = new GUIStyle(_sideHeaderStyle);
            _tableHeaderStyle.fontSize = 12;
            _tableHeaderStyle.alignment = TextAnchor.MiddleLeft;

            _pillStyle = new GUIStyle(GUI.skin.label);
            _pillStyle.fontSize = 12;
            _pillStyle.fontStyle = FontStyle.Bold;
            _pillStyle.alignment = TextAnchor.MiddleCenter;
            _pillStyle.normal.textColor = new Color(0.76f, 0.78f, 0.70f, 1f);
            _pillStyle.normal.background = _buttonFrameTex;
            _pillStyle.padding = new RectOffset(10, 10, 5, 5);
            _pillStyle.border = new RectOffset(5, 5, 5, 5);
            _pillStyle.clipping = TextClipping.Clip;

            _goodPillStyle = new GUIStyle(_pillStyle);
            _goodPillStyle.normal.background = _goodTex;
            _goodPillStyle.normal.textColor = new Color(0.72f, 0.92f, 0.58f, 1f);

            _warnPillStyle = new GUIStyle(_pillStyle);
            _warnPillStyle.normal.background = _warnTex;
            _warnPillStyle.normal.textColor = new Color(1f, 0.75f, 0.22f, 1f);

            _dangerPillStyle = new GUIStyle(_pillStyle);
            _dangerPillStyle.normal.background = _dangerTex;
            _dangerPillStyle.normal.textColor = new Color(1f, 0.72f, 0.65f, 1f);

            _rowStyle = new GUIStyle(GUI.skin.box);
            _rowStyle.normal.background = _buttonFrameTex;
            _rowStyle.padding = new RectOffset(8, 8, 7, 7);
            _rowStyle.margin = new RectOffset(0, 0, 0, 6);
            _rowStyle.border = new RectOffset(6, 6, 6, 6);

            _rowActiveStyle = new GUIStyle(_rowStyle);
            _rowActiveStyle.normal.background = _rowActiveTex;

            _emptyStateStyle = new GUIStyle(GUI.skin.box);
            _emptyStateStyle.normal.background = _panelFrameTex;
            _emptyStateStyle.padding = new RectOffset(12, 12, 10, 10);
            _emptyStateStyle.margin = new RectOffset(0, 0, 0, 6);
            _emptyStateStyle.border = new RectOffset(7, 7, 7, 7);

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = 13;
            _buttonStyle.fontStyle = FontStyle.Bold;
            _buttonStyle.alignment = TextAnchor.MiddleCenter;
            _buttonStyle.wordWrap = false;
            _buttonStyle.clipping = TextClipping.Clip;
            _buttonStyle.padding = new RectOffset(8, 8, 2, 2);
            _buttonStyle.imagePosition = ImagePosition.ImageAbove;
            _buttonStyle.normal.background = _buttonFrameTex;
            _buttonStyle.hover.background = _primaryFrameTex;
            _buttonStyle.active.background = _primaryHoverTex;
            _buttonStyle.focused.background = _buttonFrameTex;
            _buttonStyle.border = new RectOffset(7, 7, 7, 7);
            _buttonStyle.normal.textColor = new Color(0.74f, 0.76f, 0.70f, 1f);
            _buttonStyle.hover.textColor = new Color(0.90f, 0.90f, 0.82f, 1f);
            _buttonStyle.active.textColor = new Color(0.95f, 0.95f, 0.86f, 1f);

            _primaryButtonStyle = new GUIStyle(_buttonStyle);
            _primaryButtonStyle.normal.background = _primaryFrameTex;
            _primaryButtonStyle.hover.background = _primaryHoverTex;
            _primaryButtonStyle.active.background = _deepBlueTex;
            _primaryButtonStyle.focused.background = _primaryFrameTex;
            _primaryButtonStyle.normal.textColor = new Color(0.88f, 0.89f, 0.78f, 1f);
            _primaryButtonStyle.hover.textColor = new Color(1f, 1f, 0.88f, 1f);
            _primaryButtonStyle.active.textColor = new Color(1f, 1f, 0.88f, 1f);

            _quietButtonStyle = new GUIStyle(_buttonStyle);
            _quietButtonStyle.normal.background = _buttonFrameTex;
            _quietButtonStyle.hover.background = _primaryFrameTex;
            _quietButtonStyle.active.background = _buttonTex;
            _quietButtonStyle.normal.textColor = new Color(0.60f, 0.62f, 0.58f, 1f);

            _dangerButtonStyle = new GUIStyle(_buttonStyle);
            _dangerButtonStyle.normal.textColor = new Color(1f, 0.74f, 0.66f, 1f);
            _dangerButtonStyle.hover.textColor = new Color(1f, 0.86f, 0.78f, 1f);
            _dangerButtonStyle.active.textColor = new Color(1f, 0.86f, 0.78f, 1f);
            _dangerButtonStyle.normal.background = _dangerFrameTex;
            _dangerButtonStyle.hover.background = _dangerHoverTex;
            _dangerButtonStyle.active.background = _dangerHoverTex;

            _navStyle = new GUIStyle(_buttonStyle);
            _navStyle.alignment = TextAnchor.MiddleLeft;
            _navStyle.padding = new RectOffset(10, 8, 0, 0);
            _navStyle.imagePosition = ImagePosition.ImageLeft;
            _navStyle.normal.background = _sidebarTex;
            _navStyle.hover.background = _buttonFrameTex;
            _navStyle.normal.textColor = new Color(0.62f, 0.63f, 0.58f, 1f);
            _navStyle.hover.textColor = new Color(0.86f, 0.86f, 0.78f, 1f);

            _navActiveStyle = new GUIStyle(_navStyle);
            _navActiveStyle.normal.background = _primaryFrameTex;
            _navActiveStyle.hover.background = _primaryHoverTex;
            _navActiveStyle.normal.textColor = new Color(0.93f, 0.94f, 0.82f, 1f);
            _navActiveStyle.hover.textColor = new Color(1f, 1f, 0.90f, 1f);

            _inputStyle = new GUIStyle(GUI.skin.textField);
            _inputStyle.fontSize = 14;
            _inputStyle.alignment = TextAnchor.MiddleLeft;
            _inputStyle.clipping = TextClipping.Clip;
            _inputStyle.padding = new RectOffset(8, 8, 0, 0);
            _inputStyle.margin = new RectOffset(2, 6, 0, 0);
            _inputStyle.normal.background = _inputTex;
            _inputStyle.focused.background = _inputFocusTex;
            _inputStyle.hover.background = _inputFocusTex;
            _inputStyle.active.background = _inputFocusTex;
            _inputStyle.normal.textColor = new Color(0.82f, 0.84f, 0.78f, 1f);
            _inputStyle.focused.textColor = new Color(0.95f, 0.96f, 0.86f, 1f);
            _inputStyle.hover.textColor = new Color(0.95f, 0.96f, 0.86f, 1f);
            _inputStyle.active.textColor = new Color(0.95f, 0.96f, 0.86f, 1f);
        }

        private bool AreUiTexturesAlive()
        {
            return TextureAlive(_panelTex)
                && TextureAlive(_whiteTex)
                && TextureAlive(_deepBlueTex)
                && TextureAlive(_sidebarTex)
                && TextureAlive(_subtleTex)
                && TextureAlive(_inputTex)
                && TextureAlive(_buttonTex)
                && TextureAlive(_primaryTex)
                && TextureAlive(_dangerTex)
                && TextureAlive(_panelFrameTex)
                && TextureAlive(_sidebarFrameTex)
                && TextureAlive(_buttonFrameTex)
                && TextureAlive(_primaryFrameTex)
                && TextureAlive(_dangerFrameTex)
                && TextureAlive(_iconRoster)
                && TextureAlive(_iconHost)
                && TextureAlive(_iconPlayers)
                && TextureAlive(_iconWorld)
                && TextureAlive(_iconComms)
                && TextureAlive(_iconBans)
                && TextureAlive(_iconNpc)
                && TextureAlive(_iconDebug)
                && TextureAlive(_iconItems)
                && TextureAlive(_iconBuild)
                && TextureAlive(_iconVisual)
                && TextureAlive(_iconServer)
                && TextureAlive(_iconRaw)
                && TextureAlive(_iconHeal)
                && TextureAlive(_iconSpeed)
                && TextureAlive(_iconShield)
                && TextureAlive(_iconWarning)
                && TextureAlive(_iconSave)
                && TextureAlive(_iconSummon)
                && TextureAlive(_iconGoTo)
                && TextureAlive(_iconKick)
                && TextureAlive(_iconMute)
                && TextureAlive(_iconBan)
                && TextureAlive(_iconCopy);
        }

        private static bool TextureAlive(Texture2D texture)
        {
            return texture != null;
        }

        private static Texture2D MakeFrameTex(Color fill, Color edge, Color accent)
        {
            Texture2D tex = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 48; x++)
                {
                    bool outer = x == 0 || y == 0 || x == 47 || y == 47;
                    bool bevel = x == 1 || y == 1 || x == 46 || y == 46;
                    bool inner = x == 4 || y == 4 || x == 43 || y == 43;
                    int noiseSeed = ((x * 37) ^ (y * 59) ^ (x * y)) & 255;
                    float grain = (noiseSeed - 127) * 0.00012f;
                    Color color = OffsetColor(fill, grain);

                    if (outer)
                    {
                        color = edge;
                    }
                    else if (bevel)
                    {
                        color = Color.Lerp(edge, accent, 0.35f);
                    }
                    else if (inner)
                    {
                        color = accent;
                    }
                    else if ((y % 11) == 0)
                    {
                        color = Color.Lerp(color, accent, 0.035f);
                    }
                    tex.SetPixel(x, y, color);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakePatternTex(Color baseColor, Color lineColor)
        {
            Texture2D tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 128; y++)
            {
                float brushedBand = (((y * 37) + 19) & 31) * 0.00045f;
                for (int x = 0; x < 128; x++)
                {
                    int noiseSeed = ((x * 73) ^ (y * 41) ^ (x * y * 3)) & 255;
                    float fineNoise = (noiseSeed - 127) * 0.00010f;
                    float longScratch = ((((x + (y * 5)) * 11) & 63) == 0) ? 0.006f : 0f;
                    Color c = OffsetColor(baseColor, fineNoise + brushedBand + longScratch);
                    if ((y % 29) == 0)
                    {
                        c = Color.Lerp(c, lineColor, 0.025f);
                    }
                    if (((x * 3 + y * 7) % 97) == 0)
                    {
                        c = Color.Lerp(c, lineColor, 0.015f);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D MakeIconTex(string kind, Color color)
        {
            Texture2D tex = new Texture2D(28, 28, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            ClearIcon(tex);

            if (kind == "roster")
            {
                DrawCircle(tex, 8, 9, 3, color);
                DrawCircle(tex, 14, 8, 4, color);
                DrawCircle(tex, 20, 9, 3, color);
                DrawRect(tex, 5, 15, 18, 5, color);
                DrawRect(tex, 8, 20, 12, 3, color);
            }
            else if (kind == "host")
            {
                DrawRect(tex, 6, 6, 16, 5, color);
                DrawRect(tex, 6, 13, 16, 5, color);
                DrawRect(tex, 6, 20, 16, 3, color);
                DrawRect(tex, 8, 8, 2, 1, Color.black);
                DrawRect(tex, 8, 15, 2, 1, Color.black);
            }
            else if (kind == "players")
            {
                DrawCircle(tex, 14, 8, 5, color);
                DrawRect(tex, 9, 15, 10, 8, color);
                DrawRect(tex, 7, 19, 14, 4, color);
            }
            else if (kind == "world")
            {
                DrawRing(tex, 14, 14, 9, color);
                DrawLine(tex, 5, 14, 23, 14, color);
                DrawLine(tex, 14, 5, 14, 23, color);
                DrawLine(tex, 9, 7, 19, 21, color);
            }
            else if (kind == "comms")
            {
                DrawCircle(tex, 14, 18, 3, color);
                DrawLine(tex, 14, 15, 14, 8, color);
                DrawLine(tex, 9, 10, 6, 7, color);
                DrawLine(tex, 19, 10, 22, 7, color);
                DrawLine(tex, 7, 14, 4, 14, color);
                DrawLine(tex, 21, 14, 24, 14, color);
            }
            else if (kind == "npc")
            {
                DrawRing(tex, 14, 10, 4, color);
                DrawRect(tex, 11, 15, 6, 8, color);
                DrawLine(tex, 8, 18, 20, 18, color);
            }
            else if (kind == "debug")
            {
                DrawRing(tex, 14, 14, 8, color);
                DrawRect(tex, 12, 5, 4, 6, color);
                DrawRect(tex, 12, 17, 4, 6, color);
                DrawRect(tex, 5, 12, 6, 4, color);
                DrawRect(tex, 17, 12, 6, 4, color);
                DrawLine(tex, 9, 9, 19, 19, color);
                DrawLine(tex, 19, 9, 9, 19, color);
            }
            else if (kind == "items")
            {
                DrawRect(tex, 8, 9, 12, 12, color);
                DrawLine(tex, 8, 9, 14, 5, color);
                DrawLine(tex, 20, 9, 14, 5, color);
                DrawLine(tex, 14, 5, 14, 17, color);
                DrawLine(tex, 8, 21, 14, 25, color);
                DrawLine(tex, 20, 21, 14, 25, color);
            }
            else if (kind == "build")
            {
                DrawRect(tex, 7, 19, 14, 4, color);
                DrawRect(tex, 12, 8, 4, 12, color);
                DrawLine(tex, 6, 8, 18, 8, color);
                DrawLine(tex, 18, 8, 22, 12, color);
                DrawLine(tex, 9, 5, 14, 10, color);
            }
            else if (kind == "visual")
            {
                DrawLine(tex, 4, 14, 10, 8, color);
                DrawLine(tex, 10, 8, 18, 8, color);
                DrawLine(tex, 18, 8, 24, 14, color);
                DrawLine(tex, 24, 14, 18, 20, color);
                DrawLine(tex, 18, 20, 10, 20, color);
                DrawLine(tex, 10, 20, 4, 14, color);
                DrawRing(tex, 14, 14, 4, color);
            }
            else if (kind == "server")
            {
                DrawRect(tex, 6, 6, 16, 4, color);
                DrawRect(tex, 6, 12, 16, 4, color);
                DrawRect(tex, 6, 18, 16, 4, color);
                DrawCircle(tex, 9, 8, 1, Color.black);
                DrawCircle(tex, 9, 14, 1, Color.black);
                DrawCircle(tex, 9, 20, 1, Color.black);
                DrawLine(tex, 14, 10, 14, 12, color);
                DrawLine(tex, 14, 16, 14, 18, color);
            }
            else if (kind == "raw")
            {
                DrawRect(tex, 5, 6, 18, 16, color);
                DrawRect(tex, 7, 8, 14, 12, new Color(0f, 0f, 0f, 0.60f));
                DrawLine(tex, 9, 11, 12, 14, color);
                DrawLine(tex, 9, 17, 12, 14, color);
                DrawLine(tex, 14, 18, 19, 18, color);
            }
            else if (kind == "heal")
            {
                DrawRect(tex, 12, 5, 4, 18, color);
                DrawRect(tex, 5, 12, 18, 4, color);
                DrawRing(tex, 14, 14, 10, color);
            }
            else if (kind == "speed")
            {
                DrawLine(tex, 5, 18, 12, 11, color);
                DrawLine(tex, 12, 11, 5, 4, color);
                DrawLine(tex, 12, 18, 19, 11, color);
                DrawLine(tex, 19, 11, 12, 4, color);
                DrawLine(tex, 4, 23, 23, 23, color);
            }
            else if (kind == "shield")
            {
                DrawLine(tex, 14, 4, 22, 8, color);
                DrawLine(tex, 14, 4, 6, 8, color);
                DrawLine(tex, 6, 8, 8, 18, color);
                DrawLine(tex, 22, 8, 20, 18, color);
                DrawLine(tex, 8, 18, 14, 24, color);
                DrawLine(tex, 20, 18, 14, 24, color);
                DrawLine(tex, 9, 14, 13, 18, color);
                DrawLine(tex, 13, 18, 20, 10, color);
            }
            else if (kind == "warning")
            {
                DrawLine(tex, 14, 4, 24, 23, color);
                DrawLine(tex, 14, 4, 4, 23, color);
                DrawLine(tex, 4, 23, 24, 23, color);
                DrawRect(tex, 13, 11, 3, 7, color);
                DrawRect(tex, 13, 20, 3, 3, color);
            }
            else if (kind == "save")
            {
                DrawRect(tex, 6, 5, 16, 18, color);
                DrawRect(tex, 9, 7, 9, 5, new Color(0f, 0f, 0f, 0.55f));
                DrawRect(tex, 10, 16, 8, 6, new Color(0f, 0f, 0f, 0.55f));
                DrawRect(tex, 18, 7, 2, 5, color);
            }
            else if (kind == "summon")
            {
                DrawLine(tex, 14, 4, 14, 18, color);
                DrawLine(tex, 8, 12, 14, 18, color);
                DrawLine(tex, 20, 12, 14, 18, color);
                DrawRect(tex, 7, 21, 14, 3, color);
            }
            else if (kind == "goto")
            {
                DrawRing(tex, 14, 11, 6, color);
                DrawLine(tex, 14, 17, 14, 24, color);
                DrawLine(tex, 10, 21, 14, 24, color);
                DrawLine(tex, 18, 21, 14, 24, color);
            }
            else if (kind == "kick")
            {
                DrawRect(tex, 8, 16, 13, 5, color);
                DrawRect(tex, 15, 10, 5, 8, color);
                DrawRect(tex, 18, 20, 6, 3, color);
                DrawLine(tex, 6, 22, 24, 22, color);
            }
            else if (kind == "mute")
            {
                DrawRect(tex, 5, 11, 5, 7, color);
                DrawLine(tex, 10, 11, 16, 7, color);
                DrawLine(tex, 10, 18, 16, 22, color);
                DrawLine(tex, 19, 9, 24, 19, color);
                DrawLine(tex, 24, 9, 19, 19, color);
            }
            else if (kind == "ban")
            {
                DrawLine(tex, 14, 4, 22, 8, color);
                DrawLine(tex, 14, 4, 6, 8, color);
                DrawLine(tex, 6, 8, 8, 18, color);
                DrawLine(tex, 22, 8, 20, 18, color);
                DrawLine(tex, 8, 18, 14, 24, color);
                DrawLine(tex, 20, 18, 14, 24, color);
            }
            else if (kind == "copy")
            {
                DrawRect(tex, 8, 6, 10, 14, color);
                DrawRect(tex, 11, 9, 10, 14, color);
                DrawRect(tex, 10, 8, 6, 10, new Color(0f, 0f, 0f, 0.55f));
                DrawRect(tex, 13, 11, 6, 10, new Color(0f, 0f, 0f, 0.55f));
            }
            else
            {
                DrawRing(tex, 14, 14, 8, color);
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D MakeTex(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static Color OffsetColor(Color color, float amount)
        {
            return new Color(
                Clamp(color.r + amount, 0f, 1f),
                Clamp(color.g + amount, 0f, 1f),
                Clamp(color.b + amount, 0f, 1f),
                color.a);
        }

        private static void ClearIcon(Texture2D tex)
        {
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        private static void DrawPixel(Texture2D tex, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= tex.width || y >= tex.height)
            {
                return;
            }
            tex.SetPixel(x, y, color);
        }

        private static void DrawRect(Texture2D tex, int x, int y, int width, int height, Color color)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    DrawPixel(tex, px, py, color);
                }
            }
        }

        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawPixel(tex, x0, y0, color);
                DrawPixel(tex, x0 + 1, y0, color);
                DrawPixel(tex, x0, y0 + 1, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void DrawCircle(Texture2D tex, int cx, int cy, int radius, Color color)
        {
            int radiusSquared = radius * radius;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    int dx = x - cx;
                    int dy = y - cy;
                    if ((dx * dx) + (dy * dy) <= radiusSquared)
                    {
                        DrawPixel(tex, x, y, color);
                    }
                }
            }
        }

        private static void DrawRing(Texture2D tex, int cx, int cy, int radius, Color color)
        {
            int outer = radius * radius;
            int innerRadius = Math.Max(1, radius - 2);
            int inner = innerRadius * innerRadius;
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    int dx = x - cx;
                    int dy = y - cy;
                    int distance = (dx * dx) + (dy * dy);
                    if (distance <= outer && distance >= inner)
                    {
                        DrawPixel(tex, x, y, color);
                    }
                }
            }
        }

        private static void DrawSolidRect(Rect rect, Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            Color oldColor = GUI.color;
            Color oldBackground = GUI.backgroundColor;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            GUI.backgroundColor = oldBackground;
            GUI.color = oldColor;
        }

        private static void FillRect(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            Color oldBackground = GUI.backgroundColor;
            GUI.color = color;
            GUI.backgroundColor = Color.white;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.backgroundColor = oldBackground;
            GUI.color = oldColor;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string CleanName(string value)
        {
            if (value == null)
            {
                return "";
            }
            value = value.Trim();
            if (value.Length > 32)
            {
                value = value.Substring(0, 32);
            }
            return value.Replace("\r", "").Replace("\n", "");
        }

        private static string CleanSteamId(string value)
        {
            if (value == null)
            {
                return "";
            }
            value = value.Trim();
            string cleaned = "";
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] >= '0' && value[i] <= '9')
                {
                    cleaned += value[i];
                }
            }
            return cleaned;
        }

        private static string CleanConfigText(string value, int max)
        {
            if (value == null)
            {
                return "";
            }
            value = value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/").Trim();
            if (max > 0 && value.Length > max)
            {
                value = value.Substring(0, max);
            }
            return value;
        }

        private static string NormalizeNetworkKey(object value)
        {
            if (value == null)
            {
                return "";
            }
            string text = value as string;
            if (text == null)
            {
                text = value.ToString();
            }
            return (text ?? "").Trim().ToLowerInvariant();
        }

        private static bool IsAmbiguousNetworkKey(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return true;
            }
            string text = key.Trim().ToLowerInvariant();
            if (text == "0" || text == "null" || text == "none")
            {
                return true;
            }
            string compact = text.Replace("-", "").Replace(":", "").Replace(" ", "");
            if (compact.Length > 0)
            {
                bool allZero = true;
                for (int i = 0; i < compact.Length; i++)
                {
                    if (compact[i] != '0')
                    {
                        allZero = false;
                        break;
                    }
                }
                if (allZero)
                {
                    return true;
                }
            }
            return text.IndexOf("unassigned", StringComparison.Ordinal) >= 0
                || text.IndexOf("invalid", StringComparison.Ordinal) >= 0;
        }

        private static string Shorten(string text, int max)
        {
            if (String.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? "";
            }
            return max <= 3 ? text.Substring(0, max) : text.Substring(0, max - 3) + "...";
        }

        private static string DescribeObject(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is string)
            {
                return (string)value;
            }
            if (value is bool)
            {
                return (bool)value ? "yes" : "no";
            }
            if (value is char || value is byte || value is sbyte || value is short || value is ushort
                || value is int || value is uint || value is long || value is ulong)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            if (value is float || value is double || value is decimal)
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (value is Vector3)
            {
                return FormatVector((Vector3)value);
            }
            string typeName = "";
            try
            {
                Type type = value.GetType();
                typeName = type.FullName ?? type.Name;
            }
            catch { }
            string text = "";
            try
            {
                text = value.ToString();
            }
            catch { }
            if (String.IsNullOrEmpty(typeName))
            {
                return text ?? "";
            }
            if (typeName.StartsWith("System.", StringComparison.Ordinal))
            {
                return text ?? "";
            }
            if (String.IsNullOrEmpty(text) || String.Equals(text, typeName, StringComparison.Ordinal))
            {
                return typeName;
            }
            return SimplifyTypeNoise(typeName + ":" + text);
        }

        private static string SimplifyTypeNoise(string text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            string[] noisyPrefixes = new string[]
            {
                "System.Int32:",
                "System.UInt32:",
                "System.Int64:",
                "System.UInt64:",
                "System.Single:",
                "System.Double:",
                "System.Boolean:",
                "System.String:"
            };
            for (int i = 0; i < noisyPrefixes.Length; i++)
            {
                if (text.StartsWith(noisyPrefixes[i], StringComparison.Ordinal))
                {
                    return text.Substring(noisyPrefixes[i].Length);
                }
            }
            return text.Replace("System.Int32:", "").Replace("System.Single:", "").Replace("System.Boolean:", "");
        }

        private static bool IsNameMember(string memberName)
        {
            if (String.IsNullOrEmpty(memberName))
            {
                return false;
            }
            string lowered = memberName.ToLowerInvariant();
            return lowered.IndexOf("name", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("persona", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("display", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("nickname", StringComparison.Ordinal) >= 0;
        }

        private static bool IsTraversalMember(string memberName, Type memberType)
        {
            if (memberType == null || IsLeafType(memberType) || typeof(Delegate).IsAssignableFrom(memberType))
            {
                return false;
            }
            string lowered = (memberName ?? "").ToLowerInvariant();
            string typeName = (memberType.FullName ?? memberType.Name).ToLowerInvariant();
            return lowered.IndexOf("player", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("lobby", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("member", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("coop", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("data", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("list", StringComparison.Ordinal) >= 0
                || lowered.IndexOf("cache", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("player", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("lobby", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("coop", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("multiplayer", StringComparison.Ordinal) >= 0;
        }

        private static bool IsLeafType(Type type)
        {
            return type == null || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(IntPtr) || type == typeof(UIntPtr);
        }

        private static FieldInfo[] SafeFields(Type type, BindingFlags flags)
        {
            try { return type.GetFields(flags); } catch { return new FieldInfo[0]; }
        }

        private static PropertyInfo[] SafeProps(Type type, BindingFlags flags)
        {
            try { return type.GetProperties(flags); } catch { return new PropertyInfo[0]; }
        }

        private static void LogInfo(string message)
        {
            if (Plugin.LogSource != null) Plugin.LogSource.LogInfo(message);
        }

        private static void LogWarning(string message)
        {
            if (Plugin.LogSource != null) Plugin.LogSource.LogWarning(message);
        }

        private sealed class PlayerEntry
        {
            public string Key;
            public string SteamId;
            public string Name;
            public string Alias;
            public string Source;
            public readonly List<string> ObservedNames = new List<string>();
            public readonly List<string> Sources = new List<string>();
            public string Role;
            public string NetworkId;
            public string LastActivity;
            public string LastActivityUtc;
            public string LastRosterContext;
            public int ActivityCount;
            public bool? NativeBan;
            public DateTime NativeBanObservedUtc = DateTime.MinValue;
            public string NativeBanSource;
            public DateTime NativeBanAttemptUtc = DateTime.MinValue;
            public bool NativeBanQueryAvailable;
            public bool Online;
            // Keep the last known membership during a read gap without reporting it as current.
            public bool PresenceUnresolved;
            public string PresenceDetail;
#if NET11_0_OR_GREATER
            public OpenACAI.Sons.OpenACAISonsServerSession RosterSession;
#endif
            public DateTime FirstSeenUtc = DateTime.MinValue;
            public DateTime LastSeenUtc = DateTime.MinValue;
            public int ConnectionId = -1;
            public object SourceObject;
            public object BoltEntity;
            public object BoltConnection;
            public object Transform;
        }

        private sealed class GolfCartOccupant
        {
            public object Cart;
            public object Entity;
            public int SlotIndex;
            public bool Driver;
        }

        private sealed class ModerationEntry
        {
            public string SteamId;
            public string Name;
            public string Reason;
            public string AddedUtc;
            public string Source;
        }

        private sealed class JailEntry
        {
            public string SteamId;
            public string Name;
            public string Reason;
            public string AddedUtc;
            public string LastTriggerUtc;
            public string Source;
            public bool HasReturnPosition;
            public Vector3 ReturnPosition;
            public DateTime LastCareCheckUtc = DateTime.MinValue;
            public DateTime LastMoveQueuedUtc = DateTime.MinValue;
            public bool ReturnCaptureClosed;
            public string CareStatus = "Pending";
            public string CareDetail = "Player state has not been checked";
            public string MovementStatus = "Pending";
            public string MovementDetail = "Movement has not been requested";
#if NET11_0_OR_GREATER
            public PendingJailMove PendingMove;
            public PendingJailRevive PendingRevive;
#endif
            public int TriggerCount;
        }

        private sealed class ItemEventWindow
        {
            public string SteamId;
            public DateTime WindowStartUtc = DateTime.MinValue;
            public DateTime LastEventUtc = DateTime.MinValue;
            public int TotalCount;
            public int SuspiciousCount;
            public int ConstructionSalvageCount;
            public int EventCount;
            public string ItemIds;
            public string LastEventName;
        }

        private sealed class VehicleSpawnProbe
        {
            public Vector3 Position;
            public bool HasPosition;
            public DateTime ExpiresUtc = DateTime.MinValue;
            public string Reason;
        }

        private sealed class InventorySignalState
        {
            public string SteamId;
            public DateTime LastSeenUtc = DateTime.MinValue;
            public int SignalCount;
            public int BlockedCount;
            public int HighValueSignals;
            public int ArtifactSignals;
            public int LastItemId;
            public int LastCount;
            public string LastEventValidation;
            public string LastEventName;
            public string LastEventReason;
            public string LastEventDescriptor;
            public string LastReason;
            public string LastDescriptor;
            public DateTime LastAuditRequestUtc = DateTime.MinValue;
            public DateTime LastAuditResponseUtc = DateTime.MinValue;
            public string LastAuditNonce;
            public Guid PendingAuditSessionId;
            public string LastAuditStatus;
            public string LastAuditVersion;
            public string LastAuditItems;
            public int LastAuditItemCount;
            public int LastAuditHighValueCount;
            public int LastAuditArtifactCount;
            public DateTime LastPassiveStateScanUtc = DateTime.MinValue;
            public DateTime LastPassiveAttemptUtc = DateTime.MinValue;
            public DateTime LastPassiveViolationUtc = DateTime.MinValue;
            public string LastPassiveStateItems;
            public string LastPassiveReadStatus;
            public string LastPassiveShortcutItems;
#if NET11_0_OR_GREATER
            public OpenACAI.Sons.OpenACAISonsServerSession PassiveSession;
            public ulong PassiveNetworkId;
#else
            public UnityEngine.Object PassiveEntity;
#endif
            public bool PassiveBaselineReady;
            public readonly Dictionary<int, int> KnownItemDeltas = new Dictionary<int, int>();
            public readonly Dictionary<int, DateTime> KnownStateItems = new Dictionary<int, DateTime>();
        }

        private sealed class MovementSignalState
        {
            public string SteamId;
            public bool HasSample;
            public MovementMode ObservedMovementMode;
            public DateTime LastMovementContextUtc = DateTime.MinValue;
            public DateTime LastSampleAttemptUtc = DateTime.MinValue;
            public string PositionSource;
#if NET11_0_OR_GREATER
            public OpenACAI.Sons.OpenACAISonsServerSession SampleSession;
            public ulong SampleNetworkId;
#endif
            public Vector3 LastPosition;
            public DateTime LastSampleUtc = DateTime.MinValue;
            public DateTime LastEvaluationUtc = DateTime.MinValue;
            public double LastEvaluationSeconds;
            public string LastSamplePhase;
            public DateTime IgnoreUntilUtc = DateTime.MinValue;
            public string IgnoreReason;
            public string LastReason;
            public int ConsecutiveViolations;
            public int TotalViolations;
            public float LastHorizontalSpeed;
            public float LastVerticalSpeed;
            public float LastDistance;
            public string LastContext;
            public string LastContextEvidence;
            public float LastContextHorizontalLimit;
            public float LastContextVerticalLimit;
            public float LastContextTeleportLimit;
            public float LastNetworkedSpeed;
            public int GliderLikeSamples;
            public DateTime GliderLikeUntilUtc = DateTime.MinValue;
            public string GliderLikeReason;
            public DateTime FootClimbWindowStartUtc = DateTime.MinValue;
            public float FootClimbStartY;
            public float FootClimbLastY;
            public float FootClimbUpMeters;
            public float FootClimbDownMeters;
            public float FootClimbNetGain;
            public float FootClimbWindowAgeSeconds;
        }

        private sealed class MovementContext
        {
            public string Label;
            public string Evidence;
            public MovementMode ObservedMode;
            public float HorizontalLimit;
            public float VerticalLimit;
            public float TeleportBase;
            public float TeleportMultiplier;
            public float NetworkedSpeed;
            public bool HasVehicle;
            public bool AllowsAirborne;

            public static MovementContext Foot(float horizontalLimit, float verticalLimit, float teleportBase)
            {
                MovementContext context = new MovementContext();
                context.Label = "foot";
                context.Evidence = "No active transport observation";
                context.HorizontalLimit = horizontalLimit;
                context.VerticalLimit = verticalLimit;
                context.TeleportBase = teleportBase;
                context.TeleportMultiplier = 2.5f;
                context.NetworkedSpeed = 0f;
                context.HasVehicle = false;
                context.AllowsAirborne = false;
                return context;
            }
        }

        private sealed class LearnedSelfAdminPolicy
        {
            public readonly List<LearnedRule> FalsePositiveRules = new List<LearnedRule>();
            public readonly List<LearnedRule> AllowEventRules = new List<LearnedRule>();
            public readonly List<LearnedRule> ConfirmedCheatRules = new List<LearnedRule>();
            public readonly HashSet<string> RuleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public DateTime LastLoadedUtc = DateTime.MinValue;
            public int MemoryLineCount;
            public int RulesLoaded;
            public string Summary = "no learned rules";

            public void Clear()
            {
                FalsePositiveRules.Clear();
                AllowEventRules.Clear();
                ConfirmedCheatRules.Clear();
                RuleKeys.Clear();
                LastLoadedUtc = DateTime.MinValue;
                MemoryLineCount = 0;
                RulesLoaded = 0;
                Summary = "no learned rules";
            }

            public void CopyFrom(LearnedSelfAdminPolicy source)
            {
                Clear();
                if (source == null)
                {
                    return;
                }
                LastLoadedUtc = source.LastLoadedUtc;
                MemoryLineCount = source.MemoryLineCount;
                Summary = source.Summary;
                for (int i = 0; i < source.FalsePositiveRules.Count; i++)
                {
                    AddRule(source.FalsePositiveRules[i]);
                }
                for (int i = 0; i < source.AllowEventRules.Count; i++)
                {
                    AddRule(source.AllowEventRules[i]);
                }
                for (int i = 0; i < source.ConfirmedCheatRules.Count; i++)
                {
                    AddRule(source.ConfirmedCheatRules[i]);
                }
                FinalizeSummary();
            }

            public void AddRule(LearnedRule rule)
            {
                if (rule == null || String.IsNullOrEmpty(rule.Action) || String.IsNullOrEmpty(rule.NormalizedToken))
                {
                    return;
                }
                string key = rule.Action + "|" + rule.SteamId + "|" + rule.NormalizedToken;
                if (!RuleKeys.Add(key))
                {
                    return;
                }
                if (String.Equals(rule.Action, "allow_event", StringComparison.OrdinalIgnoreCase))
                {
                    AllowEventRules.Add(rule);
                }
                else if (String.Equals(rule.Action, "confirmed_cheat", StringComparison.OrdinalIgnoreCase))
                {
                    ConfirmedCheatRules.Add(rule);
                }
                else
                {
                    FalsePositiveRules.Add(rule);
                }
                RulesLoaded = RuleKeys.Count;
            }

            public void FinalizeSummary()
            {
                RulesLoaded = RuleKeys.Count;
                if (RulesLoaded <= 0)
                {
                    Summary = "no learned rules";
                    return;
                }
                Summary = FalsePositiveRules.Count.ToString(CultureInfo.InvariantCulture) + " false-positive, "
                    + AllowEventRules.Count.ToString(CultureInfo.InvariantCulture) + " allow-event, "
                    + ConfirmedCheatRules.Count.ToString(CultureInfo.InvariantCulture) + " confirmed-cheat rule(s)";
            }
        }

        private sealed class LearnedRule
        {
            public string Action;
            public string SteamId;
            public string Token;
            public string NormalizedToken;
            public string Category;
            public string Note;
            public bool Structured;
            public DateTime AddedUtc = DateTime.MinValue;
        }

        private sealed class GuardedItemLocationRule
        {
            public string Token;
            public string NormalizedToken;
            public int ItemId;
            public Vector3 Position;
            public float Radius;
            public string Label;
        }
    }

    internal static class NetworkEventHooks
    {
        private static void RegisterClientPostfix(object __0, object __1, object __2)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.RememberRegisteredClient(__0, __1, __2 as string, "GameServerManager.RegisterClient");
            }
        }

        private static void UnregisterClientPostfix(object __0, bool __result)
        {
            if (Plugin.Core != null && __result)
            {
                Plugin.Core.ForgetRegisteredClient(__0, "GameServerManager.UnregisterClient");
            }
        }

        private static void DisconnectedPostfix(object __0)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.ForgetRegisteredClient(__0, "Bolt.Disconnected");
            }
        }

        private static void RegisterAttachedPlayerEntityPostfix(object __0)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.RememberAttachedPlayerEntity(__0, "MultiplayerUtilities.RegisterAttachedPlayerEntity");
            }
        }

        private static void RegisterDetachedPlayerEntityPostfix(object __0)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.ForgetAttachedPlayerEntity(__0, "MultiplayerUtilities.RegisterDetachedPlayerEntity");
            }
        }

        private static void PlayerActorDisconnectedPostfix(object __0)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.ForgetAttachedPlayerEntity(__0, "PlayerActorManager.OnPlayerDisconnected");
            }
        }

        private static void ChatBoxRegisterPlayerPostfix(object __0, object __1)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.RememberChatPlayer(__0, __1, "ChatBox.RegisterPlayer");
            }
        }

        private static void ChatBoxUnregisterPlayerPostfix(object __0)
        {
            if (Plugin.Core != null)
            {
                Plugin.Core.ForgetChatPlayer(__0, "ChatBox.UnregisterPlayer");
            }
        }
    }

    internal static class ChatEventHooks
    {
        private static bool ChatEventPrefix(object __instance, object __0)
        {
            return Plugin.Core == null || Plugin.Core.FilterChatEvent(__instance, __0);
        }

        private static bool ChatBoxSendLinePrefix(object __0)
        {
            if (Plugin.Core == null)
            {
                return true;
            }
            return Plugin.Core.FilterLocalChatSend(__0 as string);
        }

        private static bool ChatBoxSubmitPrefix(object __instance)
        {
            if (Plugin.Core == null)
            {
                return true;
            }
            return Plugin.Core.FilterLocalChatSubmit(__instance);
        }
    }

    internal static class AuditEventHooks
    {
        private static bool SendMessageEventPrefix(object __instance, object __0)
        {
            return Plugin.Core == null || Plugin.Core.FilterSendMessageEvent(__instance, __0);
        }
    }

    internal static class CheatEventHooks
    {
        private static bool CheatEventPrefix(object __instance, object __0)
        {
            return Plugin.Core == null || Plugin.Core.FilterCheatEvent(__instance, __0);
        }
    }

    internal static class MovementEventHooks
    {
        private static void LocalMovementGracePostfix(object __instance)
        {
            if (Plugin.Core != null)
            {
                string reason = __instance == null ? "local movement event" : (__instance.GetType().Name ?? "local movement event");
                Plugin.Core.AllowMovementSpikeForLocalPlayer(12f, reason);
            }
        }
    }

    internal static class VehicleSpawnHooks
    {
        private static bool BoltInstantiatePrefix(object[] __args)
        {
            return Plugin.Core == null || !Plugin.Core.ShouldBlockBoltInstantiate(__args);
        }
    }

    internal struct VectorBox
    {
        public float X;
        public float Y;
        public float Z;

        public VectorBox(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public void Add(VectorBox other)
        {
            X += other.X;
            Y += other.Y;
            Z += other.Z;
        }

        public void Subtract(VectorBox other)
        {
            X -= other.X;
            Y -= other.Y;
            Z -= other.Z;
        }

        public float LengthSquared()
        {
            return X * X + Y * Y + Z * Z;
        }

        public void Normalize()
        {
            float len = (float)Math.Sqrt(LengthSquared());
            if (len > 0.0001f)
            {
                X /= len;
                Y /= len;
                Z /= len;
            }
        }

        public static float DistanceSquared(VectorBox a, VectorBox b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return x * x + y * y + z * z;
        }
    }

    internal static class ReflectionTools
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Type FindType(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return null;
            }
            Type direct = Type.GetType(name, false);
            if (direct != null)
            {
                return direct;
            }
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = null;
                try { type = assemblies[i].GetType(name, false); } catch { }
                if (type != null) return type;
            }
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try { types = assemblies[i].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type != null && (type.FullName == name || type.Name == name))
                    {
                        return type;
                    }
                }
            }
            return null;
        }

        public static object GetMemberValue(object instance, string name)
        {
            return instance == null ? null : GetMemberValue(instance.GetType(), instance, name, AnyInstance);
        }

        public static object GetStaticMemberValue(Type type, string name)
        {
            return type == null ? null : GetMemberValue(type, null, name, AnyStatic);
        }

        public static object InvokeNoArgMethod(object instance, string name)
        {
            if (instance == null || String.IsNullOrEmpty(name))
            {
                return null;
            }
            Type type = instance.GetType();
            while (type != null)
            {
                try
                {
                    MethodInfo method = type.GetMethod(name, AnyInstance, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        return method.Invoke(instance, null);
                    }
                }
                catch { }
                type = type.BaseType;
            }
            return null;
        }

        public static object InvokeStaticNoArgMethod(Type type, string name)
        {
            if (type == null || String.IsNullOrEmpty(name))
            {
                return null;
            }
            while (type != null)
            {
                try
                {
                    MethodInfo method = type.GetMethod(name, AnyStatic, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        return method.Invoke(null, null);
                    }
                }
                catch { }
                type = type.BaseType;
            }
            return null;
        }

        public static bool InvokeStaticMethod(Type type, string name, object[] args)
        {
            object result;
            return TryInvokeMethod(type, null, name, args, true, out result);
        }

        public static object InvokeStaticMethodResult(Type type, string name, object[] args)
        {
            object result;
            return TryInvokeMethod(type, null, name, args, true, out result) ? result : null;
        }

        public static bool InvokeInstanceMethod(object instance, string name, object[] args)
        {
            if (instance == null)
            {
                return false;
            }
            object result;
            return TryInvokeMethod(instance.GetType(), instance, name, args, false, out result);
        }

        public static object InvokeInstanceMethodResult(object instance, string name, object[] args)
        {
            if (instance == null)
            {
                return null;
            }
            object result;
            return TryInvokeMethod(instance.GetType(), instance, name, args, false, out result) ? result : null;
        }

        private static bool TryInvokeMethod(Type type, object instance, string name, object[] args, bool staticOnly, out object result)
        {
            result = null;
            if (type == null || String.IsNullOrEmpty(name))
            {
                return false;
            }
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (staticOnly ? BindingFlags.Static : BindingFlags.Instance);
            while (type != null)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(flags); } catch { methods = new MethodInfo[0]; }
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != name || method.ContainsGenericParameters)
                    {
                        continue;
                    }
                    if (!SimpleParametersMatch(method.GetParameters(), args))
                    {
                        continue;
                    }
                    try
                    {
                        result = method.Invoke(instance, args ?? new object[0]);
                        return true;
                    }
                    catch { return false; }
                }
                type = type.BaseType;
            }
            return false;
        }

        private static bool SimpleParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            args = args ?? new object[0];
            if (parameters == null || parameters.Length != args.Length)
            {
                return false;
            }
            for (int i = 0; i < parameters.Length; i++)
            {
                object arg = args[i];
                Type parameterType = parameters[i].ParameterType;
                Type effectiveType = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
                if (arg == null)
                {
                    if (!parameterType.IsByRef && effectiveType != null && effectiveType.IsValueType)
                    {
                        return false;
                    }
                    continue;
                }
                Type argType = arg.GetType();
                if (effectiveType != null && effectiveType.IsAssignableFrom(argType))
                {
                    continue;
                }
                if (effectiveType != null && effectiveType.IsEnum && argType == effectiveType)
                {
                    continue;
                }
                if ((effectiveType == typeof(Vector3) && arg is Vector3)
                    || (effectiveType == typeof(Quaternion) && arg is Quaternion)
                    || (effectiveType == typeof(bool) && arg is bool)
                    || (effectiveType == typeof(float) && arg is float)
                    || (effectiveType == typeof(int) && arg is int)
                    || (effectiveType == typeof(uint) && arg is uint)
                    || (effectiveType == typeof(ulong) && arg is ulong))
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        private static object GetMemberValue(Type type, object instance, string name, BindingFlags flags)
        {
            if (type == null || String.IsNullOrEmpty(name))
            {
                return null;
            }
            while (type != null)
            {
                try
                {
                    FieldInfo field = type.GetField(name, flags);
                    if (field != null) return field.GetValue(instance);
                    PropertyInfo prop = type.GetProperty(name, flags);
                    if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0) return prop.GetValue(instance, null);
                }
                catch { }
                type = type.BaseType;
            }
            return null;
        }

        public static bool SetMemberValue(object instance, string name, object value)
        {
            if (instance == null || String.IsNullOrEmpty(name))
            {
                return false;
            }
            Type type = instance.GetType();
            while (type != null)
            {
                try
                {
                    FieldInfo field = type.GetField(name, AnyInstance);
                    if (field != null)
                    {
                        field.SetValue(instance, value);
                        return true;
                    }
                    PropertyInfo prop = type.GetProperty(name, AnyInstance);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(instance, value, null);
                        return true;
                    }
                }
                catch { }
                type = type.BaseType;
            }
            return false;
        }

        public static Array FindObjectsOfType(Type wantedType)
        {
            Type objectType = FindType("UnityEngine.Object");
            if (objectType == null || wantedType == null)
            {
                return null;
            }
            MethodInfo methodWithInactive = objectType.GetMethod("FindObjectsOfType", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(Type), typeof(bool) }, null);
            if (methodWithInactive != null)
            {
                try
                {
                    Array found = methodWithInactive.Invoke(null, new object[] { wantedType, true }) as Array;
                    if (found != null && found.Length > 0) return found;
                }
                catch { }
            }
            MethodInfo method = objectType.GetMethod("FindObjectsOfType", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(Type) }, null);
            if (method != null)
            {
                try
                {
                    Array found = method.Invoke(null, new object[] { wantedType }) as Array;
                    if (found != null && found.Length > 0) return found;
                }
                catch { }
            }
            Type resourcesType = FindType("UnityEngine.Resources");
            MethodInfo resourceMethod = resourcesType == null ? null : resourcesType.GetMethod("FindObjectsOfTypeAll", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(Type) }, null);
            if (resourceMethod == null)
            {
                return null;
            }
            try { return resourceMethod.Invoke(null, new object[] { wantedType }) as Array; } catch { return null; }
        }

        public static Array GetComponentsInChildren(object gameObject, Type componentType, bool includeInactive)
        {
            if (gameObject == null || componentType == null)
            {
                return null;
            }
            MethodInfo method = gameObject.GetType().GetMethod("GetComponentsInChildren", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(Type), typeof(bool) }, null);
            if (method == null)
            {
                return null;
            }
            try { return method.Invoke(gameObject, new object[] { componentType, includeInactive }) as Array; } catch { return null; }
        }

        public static object GetComponentInParent(object componentOrGameObject, Type componentType)
        {
            if (componentOrGameObject == null || componentType == null)
            {
                return null;
            }
            string[] methodNames = new string[] { "GetComponentInParent", "GetComponent" };
            for (int i = 0; i < methodNames.Length; i++)
            {
                try
                {
                    MethodInfo method = componentOrGameObject.GetType().GetMethod(methodNames[i], BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(Type), typeof(bool) }, null);
                    if (method != null)
                    {
                        object result = method.Invoke(componentOrGameObject, new object[] { componentType, true });
                        if (result != null) return result;
                    }
                }
                catch { }
                try
                {
                    MethodInfo method = componentOrGameObject.GetType().GetMethod(methodNames[i], BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(Type) }, null);
                    if (method != null)
                    {
                        object result = method.Invoke(componentOrGameObject, new object[] { componentType });
                        if (result != null) return result;
                    }
                }
                catch { }
            }
            object gameObject = GetMemberValue(componentOrGameObject, "gameObject");
            if (gameObject != null && !System.Object.ReferenceEquals(gameObject, componentOrGameObject))
            {
                return GetComponentInParent(gameObject, componentType);
            }
            return null;
        }

        public static VectorBox GetPosition(object transform)
        {
            return GetVectorMember(transform, "position");
        }

        public static void SetPosition(object transform, VectorBox value)
        {
            if (transform == null)
            {
                return;
            }
            SetMemberValue(transform, "position", new Vector3(value.X, value.Y, value.Z));
        }

        public static VectorBox GetVectorMember(object instance, string name)
        {
            object boxed = GetMemberValue(instance, name);
            if (boxed == null)
            {
                return new VectorBox(0f, 0f, 0f);
            }
            return new VectorBox(ReadFloat(boxed, "x"), ReadFloat(boxed, "y"), ReadFloat(boxed, "z"));
        }

        private static float ReadFloat(object boxed, string name)
        {
            object value = GetMemberValue(boxed, name);
            if (value == null)
            {
                return 0f;
            }
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); } catch { return 0f; }
        }
    }
}
