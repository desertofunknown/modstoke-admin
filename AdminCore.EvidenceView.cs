using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private string _evidenceCategory = "All";
        private string _evidenceSelectedSteamId = "";
        private int _evidencePage;
        private const int EvidencePageSize = 24;
        private static readonly string[] EvidenceCategories = { "All", "Movement", "Items", "Construction", "Server", "Actions" };

        private void DrawEvidenceTab()
        {
            PlayerEntry entry = GetSelectedPlayer();
            string steamId = entry == null ? "" : CleanSteamId(entry.SteamId);
            if (!String.Equals(_evidenceSelectedSteamId, steamId, StringComparison.OrdinalIgnoreCase))
            {
                _evidenceSelectedSteamId = steamId;
                _evidencePage = 0;
            }
            DateTime now = DateTime.UtcNow;
            BeginPanel("Evidence / Selected Player");
            if (entry == null || !LooksLikeSteamId(steamId))
            {
                EvidenceText("Select a player with a valid Steam64 ID in the roster to inspect observations and recorded decisions.");
                if (GUILayout.Button("Open Roster", _primaryButtonStyle, GUILayout.Width(150f), GUILayout.Height(34f))) _activeTab = "Players";
                EndPanel();
                DrawEvidenceCoverage(now);
                return;
            }

            EvidenceText(DisplayPlayerName(entry));
            GUILayout.Label(new GUIContent(steamId + "  |  " + GetPlayerPresenceText(entry), null, entry.PresenceDetail), _labelStyle, GUILayout.ExpandWidth(true));
            ModerationEntry exemption;
            bool configuredExemption = _antiCheatExemptPlayers.TryGetValue(steamId, out exemption);
            EvidenceText("Configured exemption: " + (configuredExemption ? "Yes" : "No") + "  |  Cached role: " + EvidenceValue(entry.Role));
            if (configuredExemption && exemption != null)
                EvidenceText("Exemption record: " + EvidenceValue(exemption.Reason) + " | Source: " + EvidenceValue(exemption.Source) + " | Added UTC: " + EvidenceValue(exemption.AddedUtc));
            EvidenceHint("Cached role alone does not establish a role exemption. Recorded decisions below describe the policy applied at the time.");
            EvidenceText("Last activity: " + EvidenceValue(entry.LastActivity));
            EvidenceHint("Activity UTC: " + EvidenceValue(entry.LastActivityUtc));
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Roster", _quietButtonStyle, GUILayout.Width(120f), GUILayout.Height(34f))) _activeTab = "Players";
            if (GUILayout.Button("Host Controls", _buttonStyle, GUILayout.Width(150f), GUILayout.Height(34f))) _activeTab = "Admin";
            if (GUILayout.Button(new GUIContent("Audit Inventory", null, "Request the existing client-provided inventory audit."), _buttonStyle, GUILayout.Width(160f), GUILayout.Height(34f))) RequestSelectedInventoryAudit();
            GUILayout.EndHorizontal();
            EndPanel();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(620f));
            DrawEvidenceMovement(steamId, now);
            DrawEvidenceItemWindow(steamId, now);
            GUILayout.EndVertical();
            GUILayout.Space(16f);
            GUILayout.BeginVertical(GUILayout.Width(620f));
            DrawConnectionEvidence(entry, steamId, now);
            DrawEvidenceInventory(steamId, now);
            DrawEvidenceCoverage(now);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            DrawEvidenceTimeline(steamId, now);
        }

        private void DrawEvidenceMovement(string steamId, DateTime now)
        {
            BeginPanel("Movement / Retained Sample");
            MovementSignalState state;
            if (!_movementSignals.TryGetValue(steamId, out state) || state == null)
            {
                EvidenceHint("No movement sample retained for this player. Missing data is not evidence of cheating.");
                EndPanel();
                return;
            }

            EvidenceText("Sampler phase: " + EvidenceValue(state.LastSamplePhase));
            EvidenceHint("Last attempt: " + EvidenceTime(state.LastSampleAttemptUtc, now));
            EvidenceText("Position source: " + EvidenceValue(state.PositionSource));
            EvidenceText("Current sampler reason: " + EvidenceValue(state.LastReason));
            EvidenceHint("Sampler state is separate from recorded decisions in the timeline.");
            EvidenceText("Baseline / grace timestamp: " + EvidenceTime(state.LastSampleUtc, now));
            EvidenceHint("Read times do not establish when a replicated position changed.");
            if (state.HasSample) EvidenceText("Last baseline position: " + FormatVector(state.LastPosition));
            if (state.IgnoreUntilUtc != DateTime.MinValue)
            {
                EvidenceText("Grace: " + (state.IgnoreUntilUtc > now ? "active" : "expired") + " | Until " + EvidenceTime(state.IgnoreUntilUtc, now));
                EvidenceText("Grace reason: " + EvidenceValue(state.IgnoreReason));
            }
            else EvidenceHint("No grace deadline retained.");

            GUILayout.Space(8f);
            if (state.LastEvaluationUtc == DateTime.MinValue)
                EvidenceHint("No computed movement evaluation retained. A baseline or grace update does not measure speed.");
            else
            {
                EvidenceText("Evaluated: " + EvidenceTime(state.LastEvaluationUtc, now));
                EvidenceHint("Retained evaluation, not a live reading. Compare its age with the configured " + EvidenceNumber(_movementSampleSeconds) + "s sampling interval.");
                EvidenceText("Sample duration: " + EvidenceNumber(state.LastEvaluationSeconds) + "s | Context: " + EvidenceValue(state.LastContext));
                EvidenceText("Context source: " + EvidenceValue(state.LastContextEvidence));
                EvidenceText("Horizontal: " + EvidenceNumber(state.LastHorizontalSpeed) + " m/s | Effective limit: " + EvidenceNumber(state.LastContextHorizontalLimit) + " m/s");
                EvidenceText("Vertical: " + EvidenceNumber(state.LastVerticalSpeed) + " m/s | Effective limit: " + EvidenceNumber(state.LastContextVerticalLimit) + " m/s");
                EvidenceText("Distance: " + EvidenceNumber(state.LastDistance) + " m | Effective teleport limit: " + EvidenceNumber(state.LastContextTeleportLimit) + " m");
            }
            EvidenceText("Violation counters: " + state.ConsecutiveViolations.ToString(CultureInfo.InvariantCulture) + " consecutive / " + state.TotalViolations.ToString(CultureInfo.InvariantCulture) + " total");
            EndPanel();
        }

        private void DrawEvidenceInventory(string steamId, DateTime now)
        {
            BeginPanel("Items / Source and Freshness");
            EvidenceHint("Server event estimates, client-provided audits and observed equipment are partial sources. None proves total actual inventory.");
            InventorySignalState state;
            if (!_inventorySignals.TryGetValue(steamId, out state) || state == null)
            {
                EvidenceHint("No inventory observations or audit state retained for this player.");
                EndPanel();
                return;
            }
            EvidenceText("Last server event estimate: " + EvidenceTime(state.LastSeenUtc, now));
            EvidenceText("Event decisions: " + state.SignalCount + " | Blocked: " + state.BlockedCount);
            EvidenceHint("Watch counters across events and equipment: high value " + state.HighValueSignals + " | Artifacts " + state.ArtifactSignals);
            EvidenceText("Last event: " + EvidenceValue(state.LastEventName) + " | Item ID: " + state.LastItemId + " | Quantity: " + FormatEventItemCount(state.LastCount));
            EvidenceText("Range validation: " + EvidenceValue(state.LastEventValidation));
            EvidenceText("Event detail: " + EvidenceValue(state.LastEventDescriptor));
            EvidenceText("Event reason: " + EvidenceValue(state.LastEventReason));
            GUILayout.Space(8f);
            EvidenceText("Client-provided audit: " + EvidenceValue(state.LastAuditStatus));
            EvidenceHint("Requested: " + EvidenceTime(state.LastAuditRequestUtc, now));
            EvidenceHint("Last accepted report: " + EvidenceTime(state.LastAuditResponseUtc, now));
            EvidenceHint("Failed or incomplete audits retain the previous report.");
            if (state.LastAuditResponseUtc != DateTime.MinValue)
            {
                EvidenceText("Reported item types: " + state.LastAuditItemCount + " | High-value quantity " + state.LastAuditHighValueCount + " | Artifact quantity " + state.LastAuditArtifactCount);
                EvidenceText("Reported items (preview): " + EvidenceValue(state.LastAuditItems));
            }
            EvidenceText("Equipment read: " + EvidenceValue(state.LastPassiveReadStatus));
            EvidenceHint("Attempt: " + EvidenceTime(state.LastPassiveAttemptUtc, now) + " | Last successful snapshot: " + EvidenceTime(state.LastPassiveStateScanUtc, now));
            EvidenceText("Replicated held items: " + EvidenceValue(state.LastPassiveStateItems));
            EvidenceText("Current connection baseline: " + (state.PassiveBaselineReady ? "recorded" : "awaiting a nonempty held-item snapshot"));
            EvidenceText("Shortcuts: " + EvidenceValue(state.LastPassiveShortcutItems));
            EvidenceHint("Shortcut assignments do not prove possession and never trigger equipment enforcement. Clothing, armour, upgrade, map and transfer metadata are not interpreted as held items.");
            EvidenceHint("Last equipment watch or flag: " + EvidenceTime(state.LastPassiveViolationUtc, now));
            GUILayout.Space(8f);
            EvidenceText("Mixed retained item estimates (ID: count)");
            EvidenceHint("Event deltas and observations can be mixed here; these are not a complete inventory. Old values remain until updated or the session resets.");
            if (state.KnownItemDeltas.Count == 0) EvidenceHint("No item estimates retained.");
            else
            {
                List<int> ids = new List<int>(state.KnownItemDeltas.Keys);
                ids.Sort();
                List<string> values = new List<string>();
                foreach (int id in ids) values.Add(id.ToString(CultureInfo.InvariantCulture) + ": " + state.KnownItemDeltas[id].ToString(CultureInfo.InvariantCulture));
                EvidenceText(String.Join(", ", values.ToArray()));
            }
            EndPanel();
        }

        private void DrawEvidenceItemWindow(string steamId, DateTime now)
        {
            BeginPanel("Items / Construction Window");
            ItemEventWindow window;
            if (!_itemEventWindows.TryGetValue(steamId, out window) || window == null)
                EvidenceHint("No item burst window retained. Construction has no standalone per-player counter; salvage is counted within item windows.");
            else
            {
                double duration = Math.Max(1f, _itemBurstWindowSeconds);
                bool expired = window.WindowStartUtc == DateTime.MinValue || (now - window.WindowStartUtc).TotalSeconds >= duration;
                EvidenceText("Window: " + (expired ? "expired / retained" : "within configured duration") + " | Duration: " + EvidenceNumber(duration) + "s");
                EvidenceHint("Started: " + EvidenceTime(window.WindowStartUtc, now));
                EvidenceHint("Last event: " + EvidenceTime(window.LastEventUtc, now));
                EvidenceText("Events: " + window.EventCount + " | Burst units: " + window.TotalCount + " | Suspicious units: " + window.SuspiciousCount);
                EvidenceText("Construction salvage units: " + window.ConstructionSalvageCount);
                EvidenceHint("An explicit item quantity contributes that many units. Events without a readable quantity contribute one unit; this is not an inventory count.");
                EvidenceText("Last event: " + EvidenceValue(window.LastEventName));
                EvidenceText("Item IDs: " + EvidenceValue(window.ItemIds));
                EvidenceHint("These are window counters, not a complete construction history. Expired windows do not describe current activity.");
            }
            EndPanel();
        }

        private void DrawEvidenceCoverage(DateTime now)
        {
            BeginPanel("Coverage / Current Policy");
            EvidenceText(GetGuardStatusText());
            EvidenceText("Modstoke Loader session lifecycle: " + LoaderSessionStatus);
            EvidenceText("Session notifications: opened " + _loaderSessionsOpened + " | Closed " + _loaderSessionsClosed + " | Registration changed " + _loaderSessionRegistrationsChanged);
            EvidenceText("Cleared state records: " + _loaderSessionRecordsCleared + " (not a player count)");
            if (_lastLoaderSessionNoticeUtc != DateTime.MinValue)
            {
                EvidenceHint("Last session notification: " + EvidenceTime(_lastLoaderSessionNoticeUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderSessionNotice));
            }
            EvidenceHint("Queued notifications may arrive late; cleanup follows the exact connection lifetime and polling refreshes tracking. This is not authentication or instant online proof. Manual roster refresh stays manual.");
            EvidenceText("Modstoke Loader native admin commands: " + LoaderAdminRequestStatus + " | Policy report: " + LoaderPolicyStatus);
            EvidenceText("Command requests observed: " + _loaderAdminRequestsSeen + " | Rejected: " + _loaderAdminRequestsBlocked + " | Unresolved: " + _loaderAdminRequestsDeferred);
            if (_lastLoaderAdminRequestUtc != DateTime.MinValue)
            {
                EvidenceHint("Last command request: " + EvidenceTime(_lastLoaderAdminRequestUtc, now));
                EvidenceHint(_lastLoaderAdminDecision);
            }
            EvidenceHint("Continuing a command resumes native permission checks; it does not confirm execution. Unresolved identity or role never causes Admin punishment.");
            EvidenceText("Modstoke Loader shared server chat: " + LoaderChatStatus);
            EvidenceText("Chat requests observed: " + _loaderChatRequestsSeen + " | Consumed/rejected: " + _loaderChatRequestsRejected + " | Unresolved: " + _loaderChatRequestsUnresolved);
            if (_lastLoaderChatRequestUtc != DateTime.MinValue)
            {
                EvidenceHint("Last chat request: " + EvidenceTime(_lastLoaderChatRequestUtc, now));
                EvidenceHint(_lastLoaderChatDecision);
            }
            EvidenceHint("Chat moderation uses the actual source session identity. Unresolved identity never causes attributed punishment. Private inventory replies are consumed before command dispatch.");
            EvidenceText("Modstoke Loader shared player-hit hook: " + LoaderPlayerHitStatus);
            EvidenceText("Player-hit requests observed: " + _loaderPlayerHitsSeen + " | Rejected: " + _loaderPlayerHitsRejected + " | Unresolved: " + _loaderPlayerHitsUnresolved);
            if (_lastLoaderPlayerHitUtc != DateTime.MinValue)
            {
                EvidenceHint("Last player-hit request: " + EvidenceTime(_lastLoaderPlayerHitUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderPlayerHitDecision));
            }
            EvidenceText("Modstoke Loader shared structure-hit hook: " + LoaderStructureHitStatus);
            EvidenceText("Structure-hit requests observed: " + _loaderStructureHitsSeen + " | Rejected: " + _loaderStructureHitsRejected + " | Unresolved: " + _loaderStructureHitsUnresolved);
            if (_lastLoaderStructureHitUtc != DateTime.MinValue)
            {
                EvidenceHint("Last structure-hit request: " + EvidenceTime(_lastLoaderStructureHitUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderStructureHitDecision));
            }
            EvidenceText("Modstoke Loader shared NPC-hit hook: " + LoaderVailHitStatus);
            EvidenceText("NPC-hit requests observed: " + _loaderVailHitsSeen + " | Rejected: " + _loaderVailHitsRejected + " | Unresolved: " + _loaderVailHitsUnresolved);
            if (_lastLoaderVailHitUtc != DateTime.MinValue)
            {
                EvidenceHint("Last NPC-hit request: " + EvidenceTime(_lastLoaderVailHitUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderVailHitDecision));
            }
            EvidenceText("Modstoke Loader shared dismemberment hook: " + LoaderDismemberHitStatus);
            EvidenceText("Dismemberment requests observed: " + _loaderDismemberHitsSeen + " | Rejected: " + _loaderDismemberHitsRejected + " | Unresolved: " + _loaderDismemberHitsUnresolved);
            if (_lastLoaderDismemberHitUtc != DateTime.MinValue)
            {
                EvidenceHint("Last dismemberment request: " + EvidenceTime(_lastLoaderDismemberHitUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderDismemberHitDecision));
            }
            EvidenceText("Modstoke Loader shared container-open hook: " + LoaderContainerOpenStatus);
            EvidenceText("Container-open requests observed: " + _loaderContainerOpensSeen + " | Rejected: " + _loaderContainerOpensRejected + " | Unresolved: " + _loaderContainerOpensUnresolved);
            if (_lastLoaderContainerOpenUtc != DateTime.MinValue)
            {
                EvidenceHint("Last container-open request: " + EvidenceTime(_lastLoaderContainerOpenUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderContainerOpenDecision));
            }
            EvidenceText("Modstoke Loader shared object-break hook: " + LoaderObjectBreakStatus);
            EvidenceText("Object-break requests observed: " + _loaderObjectBreaksSeen + " | Rejected: " + _loaderObjectBreaksRejected + " | Unresolved: " + _loaderObjectBreaksUnresolved);
            if (_lastLoaderObjectBreakUtc != DateTime.MinValue)
            {
                EvidenceHint("Last object-break request: " + EvidenceTime(_lastLoaderObjectBreakUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderObjectBreakDecision));
            }
            EvidenceText("Modstoke Loader ingredient request hook: " + LoaderIngredientStatus);
            EvidenceText("Ingredient requests observed: " + _loaderIngredientsSeen + " | Rejected: " + _loaderIngredientsRejected + " | Unresolved: " + _loaderIngredientsUnresolved);
            if (_lastLoaderIngredientUtc != DateTime.MinValue)
            {
                EvidenceHint("Last ingredient request: " + EvidenceTime(_lastLoaderIngredientUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderIngredientDecision));
            }
            EvidenceText("Modstoke Loader completion request hook: " + LoaderCompletionStatus);
            EvidenceText("Completion requests observed: " + _loaderCompletionsSeen + " | Rejected: " + _loaderCompletionsRejected + " | Unresolved: " + _loaderCompletionsUnresolved);
            if (_lastLoaderCompletionUtc != DateTime.MinValue)
            {
                EvidenceHint("Last completion request: " + EvidenceTime(_lastLoaderCompletionUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderCompletionDecision));
            }
            EvidenceHint("Jail lock rejects the shared hit, world-object and construction-resource requests listed above when an exact source session, matching available registration and a known jailed sender are available. Host authority and both guard settings are required; configured, Admin/Owner and learned allowances remain exempt. Rejection adds no jail or ban.");
            EvidenceHint("Shared jail rules pass unknown identity or role and local requests without attributed punishment. Separate native or optional legacy validation may still reject them. Direct/local damage, undo and other routes remain outside this coverage. Registration is not runtime proof.");
            EvidenceHint("Ingredient and completion jail rules stop dispatch before routing. They do not check range, confirm paid resources or completed construction, refund inventory, reset client prediction or add punishment. Item and routing fields remain request diagnostics.");
            EvidenceHint("Structure-hit rules cover jail restrictions, payload limits and native range checks for StructureHitEvent impact dispatch. Unavailable checks do not reject. Requested damage is not actual health loss; direct damage and undo are outside this coverage.");
            EvidenceText("Modstoke Loader shared blueprint hook: " + LoaderBlueprintStatus);
            EvidenceText("Blueprint requests observed: " + _loaderBlueprintsSeen + " | Rejected: " + _loaderBlueprintsRejected + " | Unresolved: " + _loaderBlueprintsUnresolved);
            if (_lastLoaderBlueprintUtc != DateTime.MinValue)
            {
                EvidenceHint("Last blueprint request: " + EvidenceTime(_lastLoaderBlueprintUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderBlueprintDecision));
            }
            EvidenceHint("Blueprint requests retain the existing jail and 45m range rules. Parented positions are resolved in world space; unavailable reads pass without punishment. Rejection can trigger configured moderation, but cannot undo earlier relocation or client effects. Prefab IDs are not inventory quantities.");
            EvidenceText("Modstoke Loader structure relocation hook: " + LoaderStructureRelocationStatus);
            EvidenceText("Structure relocation requests observed: " + _loaderStructureRelocationsSeen + " | Rejected: " + _loaderStructureRelocationsRejected + " | Unresolved: " + _loaderStructureRelocationsUnresolved);
            if (_lastLoaderStructureRelocationUtc != DateTime.MinValue)
            {
                EvidenceHint("Last structure relocation request: " + EvidenceTime(_lastLoaderStructureRelocationUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderStructureRelocationDecision));
            }
            EvidenceHint("Structure relocation retains the existing jail and 50m position-range rules. The game's separate 4m relocation check still applies. Unavailable reads pass without punishment; rejection can trigger configured moderation. A target position does not prove ownership or successful relocation.");
            EvidenceText("Modstoke Loader storage-add hook: " + LoaderStorageAddStatus);
            EvidenceText("Storage-add requests observed: " + _loaderStorageAddsSeen + " | Rejected: " + _loaderStorageAddsRejected + " | Unresolved: " + _loaderStorageAddsUnresolved);
            if (_lastLoaderStorageAddUtc != DateTime.MinValue)
            {
                EvidenceHint("Last storage-add request: " + EvidenceTime(_lastLoaderStorageAddUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderStorageAddDecision));
            }
            EvidenceText("Modstoke Loader storage-remove hook: " + LoaderStorageRemoveStatus);
            EvidenceText("Storage-remove requests observed: " + _loaderStorageRemovesSeen + " | Rejected: " + _loaderStorageRemovesRejected + " | Unresolved: " + _loaderStorageRemovesUnresolved);
            if (_lastLoaderStorageRemoveUtc != DateTime.MinValue)
            {
                EvidenceHint("Last storage-remove request: " + EvidenceTime(_lastLoaderStorageRemoveUtc, now));
                EvidenceHint(EvidenceValue(_lastLoaderStorageRemoveDecision));
            }
            EvidenceHint("Storage requests retain jail, 40m native range and configured item rules. Unavailable range or location checks do not reject by themselves; other item rules can still apply. Existing exemptions and learned decisions remain.");
            EvidenceHint("Storage identity comes from the consumed item token. The separate event item ID is diagnostic-only; zero or negative quantities remain unknown in inventory evidence. Known deltas describe allowed requests, not confirmed transfers. Rejection can trigger configured moderation and does not refund client inventory.");
            EvidenceHint("NPC hits and dismemberment are separate requests. Target/body IDs, pose and damage are raw request values, not item or source identity, validity or health-loss evidence.");
            EvidenceHint("Container-open and object-break coverage stops only StorageContainerOpened and BreakableObjectBroken server callbacks. It cannot undo earlier client changes, stop separate locator-state messages or block direct component calls.");
            EvidenceHint("World-object target IDs and container seeds are request diagnostics, not validated targets or inventory observations. A negative container seed is allowed by the game.");
            EvidenceText("Runtime patches applied: " + EvidenceFlag(_runtimePatchesApplied));
            EvidenceText("Installed hooks: guard " + _antiCheatPatchCount + " | Network " + _networkPatchCount + " | Audit " + _inventoryAuditPatchCount + " | Vehicle " + _vehicleSpawnPatchCount);
            EvidenceText("Construction receivers: " + _constructionGuardPatchCount + " / 3 hooks installed");
            EvidenceHint("Installed hooks are not proof of protection. Host authority, enabled policy and observed events determine coverage.");
            EvidenceText("Guard " + EvidenceFlag(_disallowCheats) + " | Automatic jail " + EvidenceFlag(_autoJailCheaters) + " | Repeat ban " + EvidenceFlag(_autoBanRepeatCheaters));
            EvidenceText("Movement " + EvidenceFlag(_movementGuard) + " | Speed " + EvidenceFlag(_speedHackGuard) + " | Teleport " + EvidenceFlag(_teleportGuard) + " | Noclip " + EvidenceFlag(_noclipGuard));
            EvidenceText("Strict items " + EvidenceFlag(_strictItemEventGuard) + " | Early items " + EvidenceFlag(_earlyItemGuard) + " | Burst " + EvidenceFlag(_itemBurstGuard));
            EvidenceText("High value " + EvidenceFlag(_highValueItemGuard) + " | Artifacts " + EvidenceFlag(_artifactItemGuard) + " | Story locations " + EvidenceFlag(_storyItemLocationGuard));
            EvidenceText("Client audit " + EvidenceFlag(_clientInventoryAudit) + " | Equipment scan " + EvidenceFlag(_passivePlayerStateAudit));
            EvidenceText("Modstoke SDK audit transport: " + (LoaderAuditTransportAvailable ? "registered" : "unavailable") + "; client response remains unverified.");
            EvidenceText("Jail lock " + EvidenceFlag(_jailedWorldEventGuard) + " | Native validation " + EvidenceFlag(_nativeServerValidationGuard));
            EvidenceText("Vehicle guard " + EvidenceFlag(_vehicleSpawnGuard) + " | Cheats-off enforcement " + EvidenceFlag(_enforceServerCheatsOff));
            EvidenceText("Unattributed server events: " + _unattributedGuardEvents.ToString(CultureInfo.InvariantCulture));
            if (_lastUnattributedGuardUtc != DateTime.MinValue)
                EvidenceHint("Last: " + _lastUnattributedGuardEvent + " | " + EvidenceTime(_lastUnattributedGuardUtc, now));
            EvidenceHint("Events without a resolved sender pass through this guard without assigning blame to a target or owner.");
            GUILayout.Space(8f);
            EvidenceText("Global vehicle diagnostics / unattributed");
            EvidenceHint("Vehicle guard: " + VehicleGuardStatus() + ". The policy blocks new golf-cart spawns on the host. Existing nearby carts are retained.");
            EvidenceHint("Retained spawn probes: " + _blockedVehicleSpawnProbes.Count + ". These short-lived session diagnostics have no player assignment and are not a lifetime blocked-spawn count.");
            EvidenceHint("Prefab names read: " + _boltPrefabNameById.Count + " | Pickup item mappings: " + _prefabItemIds.Count + ". Unresolved prefab IDs stay numeric.");
            for (int i = _blockedVehicleSpawnProbes.Count - 1; i >= 0; i--)
            {
                VehicleSpawnProbe probe = _blockedVehicleSpawnProbes[i];
                if (probe == null) continue;
                EvidenceText((probe.ExpiresUtc > now ? "Active" : "Expired") + " probe | Expires " + EvidenceTime(probe.ExpiresUtc, now));
                EvidenceHint(EvidenceValue(probe.Reason) + (probe.HasPosition ? " | Position " + FormatVector(probe.Position) : " | Position unavailable"));
            }
            EndPanel();
        }

        private void DrawEvidenceTimeline(string steamId, DateTime now)
        {
            BeginPanel("Recorded Decisions / Newest First");
            EvidenceHint("The latest 256 records across all players are retained for this session. This timeline clears when the session changes or the tool restarts.");
            GUILayout.BeginHorizontal();
            foreach (string category in EvidenceCategories)
            {
                if (GUILayout.Button(category, _evidenceCategory == category ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(130f), GUILayout.Height(30f)))
                {
                    _evidenceCategory = category;
                    _evidencePage = 0;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            int matching = 0;
            foreach (GuardEvidence evidence in _guardEvidence)
                if (MatchesEvidenceFilter(evidence, steamId)) matching++;
            int pages = Math.Max(1, (matching + EvidencePageSize - 1) / EvidencePageSize);
            _evidencePage = Math.Min(_evidencePage, pages - 1);
            GUILayout.BeginHorizontal();
            if (DrawModerationButton("Newer", _quietButtonStyle, _evidencePage > 0, GUILayout.Width(90f), GUILayout.Height(28f))) _evidencePage--;
            if (DrawModerationButton("Older", _quietButtonStyle, _evidencePage + 1 < pages, GUILayout.Width(90f), GUILayout.Height(28f))) _evidencePage++;
            EvidenceHint("Page " + (_evidencePage + 1) + " / " + pages + " | " + matching + " matching records");
            GUILayout.EndHorizontal();
            int shown = 0;
            int skipped = 0;
            for (int i = _guardEvidence.Count - 1; i >= 0; i--)
            {
                GuardEvidence evidence = _guardEvidence[i];
                if (!MatchesEvidenceFilter(evidence, steamId)) continue;
                if (skipped++ < _evidencePage * EvidencePageSize) continue;
                if (shown == EvidencePageSize) break;
                shown++;
                GUILayout.BeginVertical(_boxStyle);
                EvidenceText(EvidenceValue(evidence.Category) + " | " + EvidenceTime(evidence.Utc, now));
                EvidenceHint("Source: " + EvidenceValue(evidence.Source));
                EvidenceText("Observation: " + EvidenceValue(evidence.Observation));
                if (!String.IsNullOrEmpty(evidence.Policy)) EvidenceText("Policy at decision: " + evidence.Policy);
                EvidenceText("Decision: " + EvidenceValue(evidence.Decision));
                GUILayout.EndVertical();
            }
            if (shown == 0) EvidenceHint("No retained decisions for this player" + (_evidenceCategory == "All" ? "." : " in " + _evidenceCategory + ". Select All to include other categories."));
            EndPanel();
        }

        private bool MatchesEvidenceFilter(GuardEvidence evidence, string steamId)
        {
            return evidence != null && String.Equals(evidence.SteamId, steamId, StringComparison.OrdinalIgnoreCase)
                && (_evidenceCategory == "All" || String.Equals(evidence.Category, _evidenceCategory, StringComparison.OrdinalIgnoreCase));
        }

        private void EvidenceText(string value)
        {
            GUILayout.Label(new GUIContent(value), _labelStyle, GUILayout.ExpandWidth(true));
        }

        private void EvidenceHint(string value)
        {
            GUILayout.Label(new GUIContent(value), _hintStyle, GUILayout.ExpandWidth(true));
        }

        private static string EvidenceValue(string value) { return String.IsNullOrEmpty(value) ? "Not recorded" : value; }
        private static string EvidenceFlag(bool value) { return value ? "On" : "Off"; }
        private static string EvidenceNumber(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string EvidenceTime(DateTime value, DateTime now)
        {
            if (value == DateTime.MinValue) return "Not recorded";
            double seconds = (now - value).TotalSeconds;
            return value.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture)
                + " (" + Math.Abs(seconds).ToString("0.0", CultureInfo.InvariantCulture) + (seconds < 0 ? "s from now)" : "s ago)");
        }
    }
}
