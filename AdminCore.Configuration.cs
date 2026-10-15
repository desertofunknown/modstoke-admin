using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _settingsLoaded;
        private string _settingsStatus = "Settings have not been loaded.";
        private string _settingsProblem = "";

        public bool LoadConfig()
        {
            Dictionary<string, string> aliases = new Dictionary<string, string>(_aliases.Comparer);
            Dictionary<string, ModerationEntry> bannedPlayers = new Dictionary<string, ModerationEntry>(_bannedPlayers.Comparer);
            Dictionary<string, ModerationEntry> mutedPlayers = new Dictionary<string, ModerationEntry>(_mutedPlayers.Comparer);
            Dictionary<string, ModerationEntry> antiCheatExemptPlayers = new Dictionary<string, ModerationEntry>(_antiCheatExemptPlayers.Comparer);
            List<Action> settings = new List<Action>();
            try
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(_settingsPath);
                }
                catch (FileNotFoundException)
                {
                    lines = ReadMissingSettings();
                }
                catch (DirectoryNotFoundException)
                {
                    lines = ReadMissingSettings();
                }
                string section = "";
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }
                    int equals = line.IndexOf('=');
                    if (equals < 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    if (section == "aliases")
                    {
                        if (key.Length > 0 && value.Length > 0)
                        {
                            aliases[key] = CleanName(value);
                        }
                    }
                    else if (section == "banned")
                    {
                        LoadModerationEntry(bannedPlayers, key, value, "settings.ini");
                    }
                    else if (section == "muted")
                    {
                        LoadModerationEntry(mutedPlayers, key, value, "settings.ini");
                    }
                    else if (section == "anticheat_exempt")
                    {
                        LoadModerationEntry(antiCheatExemptPlayers, key, value, "settings.ini");
                    }
                    else
                    {
                        try
                        {
                            Action apply = PrepareSetting(key, value);
                            if (apply != null) settings.Add(apply);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException("Setting " + key + " on line "
                                + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + ex.Message, ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _settingsStatus = "Settings reload failed.";
                _settingsProblem = "Could not load settings. Active settings are unchanged. " + ex.Message;
                ActionFail(_settingsProblem);
                return false;
            }

            float aliasSweep = _aliasSweepSeconds;
            float rosterRefresh = _rosterRefreshSeconds;
            float jailTick = _jailTickSeconds;
            float movementSample = _movementSampleSeconds;
            float telemetrySample = _movementTelemetrySampleSeconds;
            float bridgePoll = _codexBridgePollSeconds;
            float learnedPolicyRefresh = _learnedPolicyRefreshSeconds;
            float equipmentAudit = _passivePlayerStateAuditSeconds;
            for (int i = 0; i < settings.Count; i++) settings[i]();
            if (aliasSweep != _aliasSweepSeconds) _nextAliasSweepUtc = DateTime.MinValue;
            if (rosterRefresh != _rosterRefreshSeconds) _nextRosterRefreshUtc = DateTime.MinValue;
            if (jailTick != _jailTickSeconds) _nextJailTickUtc = DateTime.MinValue;
            if (movementSample != _movementSampleSeconds) _nextMovementSampleUtc = DateTime.MinValue;
            if (telemetrySample != _movementTelemetrySampleSeconds) _nextMovementTelemetryUtc = DateTime.MinValue;
            if (bridgePoll != _codexBridgePollSeconds) _nextCodexBridgePollUtc = DateTime.MinValue;
            if (learnedPolicyRefresh != _learnedPolicyRefreshSeconds) _nextLearnedPolicyRefreshUtc = DateTime.MinValue;
            if (equipmentAudit != _passivePlayerStateAuditSeconds) _nextPassivePlayerStateAuditUtc = DateTime.MinValue;
            _aliases.Clear();
            foreach (KeyValuePair<string, string> entry in aliases) _aliases.Add(entry.Key, entry.Value);
            _bannedPlayers.Clear();
            foreach (KeyValuePair<string, ModerationEntry> entry in bannedPlayers) _bannedPlayers.Add(entry.Key, entry.Value);
            _mutedPlayers.Clear();
            foreach (KeyValuePair<string, ModerationEntry> entry in mutedPlayers) _mutedPlayers.Add(entry.Key, entry.Value);
            _antiCheatExemptPlayers.Clear();
            foreach (KeyValuePair<string, ModerationEntry> entry in antiCheatExemptPlayers) _antiCheatExemptPlayers.Add(entry.Key, entry.Value);
            _itemNameIdCache.Clear();
            _storyItemLocationRules.Clear();
            RebuildStoryItemLocationRules();
            _localAliasInput = _localAlias;
            _settingsLoaded = true;
            _settingsStatus = "Settings loaded at " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ".";
            _settingsProblem = "";
            LogInfo("Loaded " + _aliases.Count.ToString(CultureInfo.InvariantCulture) + " alias rule(s), "
                + _bannedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " ban record(s), "
                + _mutedPlayers.Count.ToString(CultureInfo.InvariantCulture) + " mute record(s), "
                + _antiCheatExemptPlayers.Count.ToString(CultureInfo.InvariantCulture) + " anti-cheat exemption(s), "
                + _storyItemLocationRules.Count.ToString(CultureInfo.InvariantCulture) + " story item location rule(s).");

            try
            {
                LoadPlayerHistory();
                ImportPlayerHistoryForContext(GetRosterContext(), true);
            }
            catch (Exception ex)
            {
                LogWarning("Settings loaded, but player history reload or import failed: " + ex.Message);
            }
            return true;
        }

        private static DateTime GetSettingsDeadline(DateTime now, double seconds)
        {
            double ticks = seconds * TimeSpan.TicksPerSecond;
            long remainingTicks = DateTime.MaxValue.Ticks - now.Ticks;
            // A long configured interval can exceed the calendar without being an invalid number.
            if (ticks >= remainingTicks) return DateTime.SpecifyKind(DateTime.MaxValue, now.Kind);
            return now.AddTicks((long)ticks);
        }

        private string[] ReadMissingSettings()
        {
            if (_settingsLoaded)
            {
                throw new FileNotFoundException("The settings file is missing. Save Settings can recreate it from the active settings.");
            }
            WriteSettingsFile(GetDefaultSettingsLines(), false);
            return File.ReadAllLines(_settingsPath);
        }

        private void SaveSettingsFromConsole(string change = null)
        {
            bool saved = SaveConfig();
            ReportSettingsSave(saved, change);
        }

        private void ReportSettingsSave(bool saved, string change)
        {
            string outcome = saved ? "Admin settings saved"
                : "Admin settings not saved; active changes remain in memory";
            if (!String.IsNullOrEmpty(change)) outcome = change + "; " + outcome;
            ActionResult(saved, outcome, outcome);
        }

        public bool SaveConfig()
        {
            if (!_settingsLoaded)
            {
                _settingsStatus = "Settings save blocked.";
                _settingsProblem = "Reload Settings must succeed before saving. Active changes remain in memory.";
                ActionFail(_settingsProblem);
                return false;
            }

            try
            {
                List<string> lines = new List<string>();
                lines.Add("# Modstoke Admin by OpenACAI - BepInEx settings");
                lines.Add("[settings]");
                lines.Add("LocalAlias=" + _localAlias);
                lines.Add("ToggleMenuKey=" + _toggleMenuKey);
                lines.Add("ReloadKey=" + _reloadKey);
                lines.Add("PossessKey=" + _possessKey);
                lines.Add("ServerPushAliases=" + _serverPushAliases.ToString(CultureInfo.InvariantCulture));
                lines.Add("AliasSweepSeconds=" + _aliasSweepSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("ExperimentalPossession=" + _experimentalPossession.ToString(CultureInfo.InvariantCulture));
                lines.Add("PossessSpeed=" + _possessSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("MuteMessage=" + CleanConfigText(_muteMessageInput, 120));
                lines.Add("AutoRosterRefresh=" + _autoRosterRefresh.ToString(CultureInfo.InvariantCulture));
                lines.Add("RosterRefreshSeconds=" + _rosterRefreshSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("OfflineRetentionMinutes=" + _offlineRetentionMinutes.ToString(CultureInfo.InvariantCulture));
                lines.Add("DisallowCheats=" + _disallowCheats.ToString(CultureInfo.InvariantCulture));
                lines.Add("AutoJailCheaters=" + _autoJailCheaters.ToString(CultureInfo.InvariantCulture));
                lines.Add("StrictItemEventGuard=" + _strictItemEventGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("EarlyItemGuard=" + _earlyItemGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("ItemBurstGuard=" + _itemBurstGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("HighValueItemGuard=" + _highValueItemGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("ArtifactItemGuard=" + _artifactItemGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("JailedWorldEventGuard=" + _jailedWorldEventGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("ServerGuardAnnouncements=" + _serverGuardAnnouncements.ToString(CultureInfo.InvariantCulture));
                lines.Add("ClientInventoryAudit=" + _clientInventoryAudit.ToString(CultureInfo.InvariantCulture));
                lines.Add("PassivePlayerStateAudit=" + _passivePlayerStateAudit.ToString(CultureInfo.InvariantCulture));
                lines.Add("NativeServerValidationGuard=" + _nativeServerValidationGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("MovementGuard=" + _movementGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("MovementTelemetryLog=" + _movementTelemetryLog.ToString(CultureInfo.InvariantCulture));
                lines.Add("CodexBridgeEnabled=" + _codexBridgeEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("ExternalAiReviewEnabled=" + _externalAiReviewEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("LearnedSelfAdminPolicy=" + _learnedSelfAdminPolicyEnabled.ToString(CultureInfo.InvariantCulture));
                lines.Add("SelfAdminFallback=" + _selfAdminFallback.ToString(CultureInfo.InvariantCulture));
                lines.Add("CodexAdminQuietSeconds=" + _codexAdminQuietSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("LearnedPolicyRefreshSeconds=" + _learnedPolicyRefreshSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("SpeedHackGuard=" + _speedHackGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("TeleportGuard=" + _teleportGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("NoclipGuard=" + _noclipGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("EnforceServerCheatsOff=" + _enforceServerCheatsOff.ToString(CultureInfo.InvariantCulture));
                lines.Add("VehicleSpawnGuard=" + _vehicleSpawnGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("AutoBanRepeatCheaters=" + _autoBanRepeatCheaters.ToString(CultureInfo.InvariantCulture));
                lines.Add("AutoBanTriggerCount=" + _autoBanTriggerCount.ToString(CultureInfo.InvariantCulture));
                lines.Add("ItemEventCountThreshold=" + _itemEventCountThreshold.ToString(CultureInfo.InvariantCulture));
                lines.Add("ItemBurstCountThreshold=" + _itemBurstCountThreshold.ToString(CultureInfo.InvariantCulture));
                lines.Add("ConstructionSalvageBurstCountThreshold=" + _constructionSalvageBurstCountThreshold.ToString(CultureInfo.InvariantCulture));
                lines.Add("ItemBurstWindowSeconds=" + _itemBurstWindowSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("PassivePlayerStateAuditSeconds=" + _passivePlayerStateAuditSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("EarlyItemGuardMinutes=" + _earlyItemGuardMinutes.ToString(CultureInfo.InvariantCulture));
                lines.Add("HighValueGuardMinutes=" + _highValueGuardMinutes.ToString(CultureInfo.InvariantCulture));
                lines.Add("MovementSampleSeconds=" + _movementSampleSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("MovementTelemetrySampleSeconds=" + _movementTelemetrySampleSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("CodexBridgePollSeconds=" + _codexBridgePollSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("MaxHorizontalSpeed=" + _maxHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("MaxVerticalSpeed=" + _maxVerticalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("TeleportDistanceThreshold=" + _teleportDistanceThreshold.ToString(CultureInfo.InvariantCulture));
                lines.Add("GolfCartHorizontalSpeed=" + _golfCartHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("KnightVHorizontalSpeed=" + _knightVHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("GliderHorizontalSpeed=" + _gliderHorizontalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("VehicleVerticalSpeed=" + _vehicleVerticalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("GliderVerticalSpeed=" + _gliderVerticalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("GliderFallbackVerticalSpeed=" + _gliderFallbackVerticalSpeed.ToString(CultureInfo.InvariantCulture));
                lines.Add("GliderFallbackTeleportDistance=" + _gliderFallbackTeleportDistance.ToString(CultureInfo.InvariantCulture));
                lines.Add("FootClimbWindowSeconds=" + _footClimbWindowSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("FootClimbLimitMeters=" + _footClimbLimitMeters.ToString(CultureInfo.InvariantCulture));
                lines.Add("LegalTeleportGraceSeconds=" + _legalTeleportGraceSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("VehicleTransitionGraceSeconds=" + _vehicleTransitionGraceSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("JoinMovementGraceSeconds=" + _joinMovementGraceSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("NoclipFloorY=" + _noclipFloorY.ToString(CultureInfo.InvariantCulture));
                lines.Add("MovementViolationThreshold=" + _movementViolationThreshold.ToString(CultureInfo.InvariantCulture));
                lines.Add("RestrictedEarlyItems=" + CleanConfigText(_restrictedEarlyItemTokens, 2048));
                lines.Add("HighValueItems=" + CleanConfigText(_highValueItemTokens, 2048));
                lines.Add("ArtifactItems=" + CleanConfigText(_artifactItemTokens, 1024));
                lines.Add("StoryItemLocationGuard=" + _storyItemLocationGuard.ToString(CultureInfo.InvariantCulture));
                lines.Add("StoryItemLocationRules=" + CleanConfigText(_storyItemLocationRulesText, 4096));
                lines.Add("JailTickSeconds=" + _jailTickSeconds.ToString(CultureInfo.InvariantCulture));
                lines.Add("JailPositionX=" + _jailPosition.x.ToString(CultureInfo.InvariantCulture));
                lines.Add("JailPositionY=" + _jailPosition.y.ToString(CultureInfo.InvariantCulture));
                lines.Add("JailPositionZ=" + _jailPosition.z.ToString(CultureInfo.InvariantCulture));
                lines.Add("");
                lines.Add("[aliases]");
                List<string> keys = new List<string>(_aliases.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < keys.Count; i++)
                {
                    lines.Add(keys[i] + "=" + _aliases[keys[i]]);
                }
                WriteModerationSection(lines, "banned", _bannedPlayers);
                WriteModerationSection(lines, "muted", _mutedPlayers);
                WriteModerationSection(lines, "anticheat_exempt", _antiCheatExemptPlayers);
                WriteSettingsFile(lines, true);
            }
            catch (Exception ex)
            {
                _settingsStatus = "Settings save failed.";
                _settingsProblem = "Could not save settings. Active changes remain in memory. " + ex.Message;
                ActionFail(_settingsProblem);
                return false;
            }

            _settingsStatus = "Settings saved at " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ".";
            _settingsProblem = "";
            LogInfo(_settingsStatus);
            try
            {
                SavePlayerHistoryThrottled(false);
            }
            catch (Exception ex)
            {
                LogWarning("Settings saved, but player history save failed: " + ex.Message);
            }
            return true;
        }

        private void WriteSettingsFile(IList<string> lines, bool replaceExisting)
        {
            string tempPath = null;
            bool tempCreated = false;
            try
            {
                Directory.CreateDirectory(_modDir);
                tempPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    tempCreated = true;
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        for (int i = 0; i < lines.Count; i++) writer.WriteLine(lines[i]);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                if (replaceExisting && File.Exists(_settingsPath)) File.Replace(tempPath, _settingsPath, null);
                else File.Move(tempPath, _settingsPath);
                tempCreated = false;
            }
            finally
            {
                try
                {
                    if (tempCreated) File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    LogWarning("Settings temporary file cleanup skipped: " + ex.Message);
                }
            }
        }

        private static string[] GetDefaultSettingsLines()
        {
            return new string[]
            {
                "# Modstoke Admin by OpenACAI - BepInEx settings",
                "[settings]",
                "LocalAlias=",
                "ToggleMenuKey=F7",
                "ReloadKey=F8",
                "PossessKey=F10",
                "ServerPushAliases=false",
                "AliasSweepSeconds=2",
                "ExperimentalPossession=true",
                "PossessSpeed=5",
                "MuteMessage=You are muted on this server.",
                "AutoRosterRefresh=true",
                "RosterRefreshSeconds=3",
                "OfflineRetentionMinutes=30",
                "DisallowCheats=false",
                "AutoJailCheaters=true",
                "StrictItemEventGuard=true",
                "EarlyItemGuard=true",
                "ItemBurstGuard=true",
                "HighValueItemGuard=true",
                "ArtifactItemGuard=true",
                "JailedWorldEventGuard=true",
                "ServerGuardAnnouncements=true",
                "ClientInventoryAudit=true",
                "PassivePlayerStateAudit=true",
                "NativeServerValidationGuard=true",
                "MovementGuard=true",
                "MovementTelemetryLog=true",
                "CodexBridgeEnabled=true",
                "ExternalAiReviewEnabled=false",
                "LearnedSelfAdminPolicy=true",
                "SelfAdminFallback=true",
                "CodexAdminQuietSeconds=180",
                "LearnedPolicyRefreshSeconds=20",
                "SpeedHackGuard=true",
                "TeleportGuard=true",
                "NoclipGuard=true",
                "EnforceServerCheatsOff=true",
                "VehicleSpawnGuard=true",
                "AutoBanRepeatCheaters=false",
                "AutoBanTriggerCount=3",
                "ItemEventCountThreshold=25",
                "ItemBurstCountThreshold=10",
                "ConstructionSalvageBurstCountThreshold=48",
                "ItemBurstWindowSeconds=12",
                "PassivePlayerStateAuditSeconds=5",
                "EarlyItemGuardMinutes=30",
                "HighValueGuardMinutes=1440",
                "MovementSampleSeconds=1",
                "MovementTelemetrySampleSeconds=1",
                "CodexBridgePollSeconds=2",
                "MaxHorizontalSpeed=24",
                "MaxVerticalSpeed=45",
                "TeleportDistanceThreshold=70",
                "GolfCartHorizontalSpeed=42",
                "KnightVHorizontalSpeed=42",
                "GliderHorizontalSpeed=85",
                "VehicleVerticalSpeed=70",
                "GliderVerticalSpeed=95",
                "GliderFallbackVerticalSpeed=550",
                "GliderFallbackTeleportDistance=650",
                "FootClimbWindowSeconds=8",
                "FootClimbLimitMeters=12",
                "LegalTeleportGraceSeconds=12",
                "VehicleTransitionGraceSeconds=5",
                "JoinMovementGraceSeconds=20",
                "NoclipFloorY=-120",
                "MovementViolationThreshold=2",
                "RestrictedEarlyItems=" + DefaultHighValueItemTokens,
                "HighValueItems=" + DefaultHighValueItemTokens,
                "ArtifactItems=" + DefaultArtifactItemTokens,
                "StoryItemLocationGuard=true",
                "# StoryItemLocationRules format: item@x,y,z,radius:label; item can be an item id or name",
                "# Example after verifying coordinates in-game: StoryItemLocationRules=355@0,0,0,80:Pistol story pickup",
                "StoryItemLocationRules=" + DefaultStoryItemLocationRules,
                "JailTickSeconds=5",
                "JailPositionX=-378.1",
                "JailPositionY=21.6",
                "JailPositionZ=1621.5",
                "",
                "[aliases]",
                "# 76561198000000000=Server Name",
                "# Current Steam Name=Server Name",
                "",
                "[banned]",
                "# 76561198000000000=Name|Reason|UTC|Source",
                "",
                "[muted]",
                "# 76561198000000000=Name|Reason|UTC|Source",
                "",
                "[anticheat_exempt]",
                "# 76561198000000000=Name|Reason|UTC|Source"
            };
        }
    }
}
