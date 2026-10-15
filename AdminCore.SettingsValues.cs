using System;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private Action PrepareSetting(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "localalias":
                    return DeferSetting(CleanName(value), prepared => _localAlias = prepared);
                case "togglemenukey":
                    return DeferSetting(value, prepared => _toggleMenuKey = prepared);
                case "reloadkey":
                    return DeferSetting(value, prepared => _reloadKey = prepared);
                case "possesskey":
                    return DeferSetting(value, prepared => _possessKey = prepared);
                case "serverpushaliases":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _serverPushAliases = prepared);
                case "aliassweepseconds":
                    return DeferSetting(Math.Max(0.5f, ParseSettingNumber(value)), prepared => _aliasSweepSeconds = prepared);
                case "experimentalpossession":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _experimentalPossession = prepared);
                case "possessspeed":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _possessSpeed = prepared);
                case "mutemessage":
                    return DeferSetting(CleanConfigText(value, 120), prepared => _muteMessageInput = prepared);
                case "autorosterrefresh":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _autoRosterRefresh = prepared);
                case "rosterrefreshseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _rosterRefreshSeconds = prepared);
                case "offlineretentionminutes":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _offlineRetentionMinutes = prepared);
                case "disallowcheats":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _disallowCheats = prepared);
                case "autojailcheaters":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _autoJailCheaters = prepared);
                case "strictitemeventguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _strictItemEventGuard = prepared);
                case "earlyitemguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _earlyItemGuard = prepared);
                case "itemburstguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _itemBurstGuard = prepared);
                case "highvalueitemguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _highValueItemGuard = prepared);
                case "artifactitemguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _artifactItemGuard = prepared);
                case "jailedworldeventguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _jailedWorldEventGuard = prepared);
                case "serverguardannouncements":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _serverGuardAnnouncements = prepared);
                case "clientinventoryaudit":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _clientInventoryAudit = prepared);
                case "passiveplayerstateaudit":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _passivePlayerStateAudit = prepared);
                case "nativeservervalidationguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _nativeServerValidationGuard = prepared);
                case "movementguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _movementGuard = prepared);
                case "movementtelemetrylog":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _movementTelemetryLog = prepared);
                case "codexbridgeenabled":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _codexBridgeEnabled = prepared);
                case "externalaireviewenabled":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _externalAiReviewEnabled = prepared);
                case "learnedselfadminpolicy":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _learnedSelfAdminPolicyEnabled = prepared);
                case "selfadminfallback":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _selfAdminFallback = prepared);
                case "codexadminquietseconds":
                    return DeferSetting(Math.Max(30f, ParseSettingNumber(value)), prepared => _codexAdminQuietSeconds = prepared);
                case "learnedpolicyrefreshseconds":
                    return DeferSetting(Math.Max(5f, ParseSettingNumber(value)), prepared => _learnedPolicyRefreshSeconds = prepared);
                case "speedhackguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _speedHackGuard = prepared);
                case "teleportguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _teleportGuard = prepared);
                case "noclipguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _noclipGuard = prepared);
                case "enforceservercheatsoff":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _enforceServerCheatsOff = prepared);
                case "vehiclespawnguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _vehicleSpawnGuard = prepared);
                case "autobanrepeatcheaters":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _autoBanRepeatCheaters = prepared);
                case "autobantriggercount":
                    return DeferSetting(Math.Max(2, ParseSettingInteger(value)), prepared => _autoBanTriggerCount = prepared);
                case "itemeventcountthreshold":
                    return DeferSetting(Math.Max(2, ParseSettingInteger(value)), prepared => _itemEventCountThreshold = prepared);
                case "itemburstcountthreshold":
                    return DeferSetting(Math.Max(2, ParseSettingInteger(value)), prepared => _itemBurstCountThreshold = prepared);
                case "constructionsalvageburstcountthreshold":
                    return DeferSetting(Math.Max(12, ParseSettingInteger(value)), prepared => _constructionSalvageBurstCountThreshold = prepared);
                case "itemburstwindowseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _itemBurstWindowSeconds = prepared);
                case "passiveplayerstateauditseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _passivePlayerStateAuditSeconds = prepared);
                case "earlyitemguardminutes":
                    return DeferSetting(Math.Max(0f, ParseSettingNumber(value)), prepared => _earlyItemGuardMinutes = prepared);
                case "highvalueguardminutes":
                    return DeferSetting(Math.Max(0f, ParseSettingNumber(value)), prepared => _highValueGuardMinutes = prepared);
                case "movementsampleseconds":
                    return DeferSetting(Math.Max(0.25f, ParseSettingNumber(value)), prepared => _movementSampleSeconds = prepared);
                case "movementtelemetrysampleseconds":
                    return DeferSetting(Math.Max(0.25f, ParseSettingNumber(value)), prepared => _movementTelemetrySampleSeconds = prepared);
                case "codexbridgepollseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _codexBridgePollSeconds = prepared);
                case "maxhorizontalspeed":
                    return DeferSetting(Math.Max(5f, ParseSettingNumber(value)), prepared => _maxHorizontalSpeed = prepared);
                case "maxverticalspeed":
                    return DeferSetting(Math.Max(5f, ParseSettingNumber(value)), prepared => _maxVerticalSpeed = prepared);
                case "teleportdistancethreshold":
                    return DeferSetting(Math.Max(25f, ParseSettingNumber(value)), prepared => _teleportDistanceThreshold = prepared);
                case "golfcarthorizontalspeed":
                    return DeferSetting(Math.Max(10f, ParseSettingNumber(value)), prepared => _golfCartHorizontalSpeed = prepared);
                case "knightvhorizontalspeed":
                    return DeferSetting(Math.Max(10f, ParseSettingNumber(value)), prepared => _knightVHorizontalSpeed = prepared);
                case "gliderhorizontalspeed":
                    return DeferSetting(Math.Max(20f, ParseSettingNumber(value)), prepared => _gliderHorizontalSpeed = prepared);
                case "vehicleverticalspeed":
                    return DeferSetting(Math.Max(10f, ParseSettingNumber(value)), prepared => _vehicleVerticalSpeed = prepared);
                case "gliderverticalspeed":
                    return DeferSetting(Math.Max(20f, ParseSettingNumber(value)), prepared => _gliderVerticalSpeed = prepared);
                case "gliderfallbackverticalspeed":
                    return DeferSetting(Math.Max(120f, ParseSettingNumber(value)), prepared => _gliderFallbackVerticalSpeed = prepared);
                case "gliderfallbackteleportdistance":
                    return DeferSetting(Math.Max(220f, ParseSettingNumber(value)), prepared => _gliderFallbackTeleportDistance = prepared);
                case "footclimbwindowseconds":
                    return DeferSetting(Math.Max(3f, ParseSettingNumber(value)), prepared => _footClimbWindowSeconds = prepared);
                case "footclimblimitmeters":
                    return DeferSetting(Math.Max(6f, ParseSettingNumber(value)), prepared => _footClimbLimitMeters = prepared);
                case "legalteleportgraceseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _legalTeleportGraceSeconds = prepared);
                case "vehicletransitiongraceseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _vehicleTransitionGraceSeconds = prepared);
                case "joinmovementgraceseconds":
                    return DeferSetting(Math.Max(5f, ParseSettingNumber(value)), prepared => _joinMovementGraceSeconds = prepared);
                case "noclipfloory":
                    return DeferSetting(ParseSettingNumber(value), prepared => _noclipFloorY = prepared);
                case "movementviolationthreshold":
                    return DeferSetting(Math.Max(1, ParseSettingInteger(value)), prepared => _movementViolationThreshold = prepared);
                case "restrictedearlyitems":
                    return DeferSetting(CleanConfigText(value, 2048), prepared => _restrictedEarlyItemTokens = prepared);
                case "highvalueitems":
                    return DeferSetting(CleanConfigText(value, 2048), prepared => _highValueItemTokens = prepared);
                case "artifactitems":
                    return DeferSetting(CleanConfigText(value, 1024), prepared => _artifactItemTokens = prepared);
                case "storyitemlocationguard":
                    return DeferSetting(ParseSettingBoolean(value), prepared => _storyItemLocationGuard = prepared);
                case "storyitemlocationrules":
                    return DeferSetting(CleanConfigText(value, 4096), prepared => _storyItemLocationRulesText = prepared);
                case "jailtickseconds":
                    return DeferSetting(Math.Max(1f, ParseSettingNumber(value)), prepared => _jailTickSeconds = prepared);
                case "jailpositionx":
                    return DeferSetting(ParseSettingNumber(value), prepared => _jailPosition.x = prepared);
                case "jailpositiony":
                    return DeferSetting(ParseSettingNumber(value), prepared => _jailPosition.y = prepared);
                case "jailpositionz":
                    return DeferSetting(ParseSettingNumber(value), prepared => _jailPosition.z = prepared);
                default:
                    return null;
            }
        }

        private static Action DeferSetting<T>(T value, Action<T> assign)
        {
            return () => assign(value);
        }

        private static bool ParseSettingBoolean(string value)
        {
            bool parsed;
            if (!Boolean.TryParse(value, out parsed))
            {
                throw new FormatException("Expected a boolean (true or false).");
            }
            return parsed;
        }

        private static int ParseSettingInteger(string value)
        {
            int parsed;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                throw new FormatException("Expected an integer.");
            }
            return parsed;
        }

        private static float ParseSettingNumber(string value)
        {
            float parsed;
            if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || Single.IsNaN(parsed) || Single.IsInfinity(parsed))
            {
                throw new FormatException("Expected a finite number.");
            }
            return parsed;
        }
    }
}
