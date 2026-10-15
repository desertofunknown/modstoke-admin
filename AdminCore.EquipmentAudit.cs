using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void TickPassivePlayerStateAudit()
        {
            DateTime now = DateTime.UtcNow;
            _nextPassivePlayerStateAuditUtc = GetSettingsDeadline(now, Math.Max(1f, _passivePlayerStateAuditSeconds));
            if (!_disallowCheats || !_passivePlayerStateAudit || !HasHostSession()) return;
            if (_players.Count == 0) RefreshPlayers(false);

            for (int i = 0; i < _players.Count; i++)
            {
                PlayerEntry entry = _players[i];
                if (entry == null || !entry.Online) continue;
                string steamId = CleanSteamId(entry.SteamId);
                if (!LooksLikeSteamId(steamId) || IsLocalPlayerEntry(entry)
                    || IsSteamIdAdminOrOwner(steamId, entry) || IsSteamIdAntiCheatExempt(steamId)
                    || IsJailedSteamId(steamId)) continue;

                InventorySignalState signal = GetOrCreateInventorySignal(steamId);
                signal.LastPassiveAttemptUtc = now;
                EquipmentSnapshot snapshot;
                string status;
#if NET11_0_OR_GREATER
                OpenACAI.Sons.OpenACAISonsPlayerStateSnapshot state;
                if (!TryReadServerEquipmentSnapshot(entry, out snapshot, out state, out status))
                {
                    signal.LastPassiveReadStatus = status + "; previous snapshot retained";
                    continue;
                }
                if (!ReferenceEquals(signal.PassiveSession, state.Session) || signal.PassiveNetworkId != state.NetworkId)
                {
                    signal.KnownStateItems.Clear();
                    signal.PassiveBaselineReady = false;
                }
                signal.PassiveSession = state.Session;
                signal.PassiveNetworkId = state.NetworkId;
                signal.LastPassiveStateScanUtc = state.ObservedAt.UtcDateTime;
#else
                UnityEngine.Object entity = entry.BoltEntity as UnityEngine.Object;
                if (entity == null || !HasLiveEntityProof(entry.BoltEntity, steamId))
                {
                    signal.LastPassiveReadStatus = "Unavailable: no matching attached player entity; previous snapshot retained";
                    continue;
                }
                if (signal.PassiveEntity != entity)
                {
                    signal.KnownStateItems.Clear();
                    signal.PassiveBaselineReady = false;
                    signal.PassiveEntity = entity;
                }

                if (!TryReadEquipmentSnapshot(GetNetworkedPlayerState(entry), out snapshot, out status))
                {
                    signal.LastPassiveReadStatus = status + "; previous snapshot retained";
                    continue;
                }
                signal.LastPassiveStateScanUtc = now;
#endif

                bool baseline = !signal.PassiveBaselineReady;
                signal.LastPassiveShortcutItems = BuildShortcutSummary(snapshot);
                List<string> observed = new List<string>();
                int unresolved = 0;
                foreach (KeyValuePair<int, string> item in snapshot.Items)
                {
                    string descriptor;
                    bool resolved = TryDescribeEquipmentItem(item.Key, out descriptor);
                    if (!resolved)
                    {
                        descriptor = "itemId=" + item.Key.ToString(CultureInfo.InvariantCulture) + " (definition unavailable)";
                        unresolved++;
                    }
                    descriptor += " via " + item.Value;
                    observed.Add(descriptor);

                    // A later catalog lookup does not make an already reported ID a new observation.
                    if (signal.KnownStateItems.ContainsKey(item.Key)) continue;
                    signal.KnownStateItems[item.Key] = now;
                    if (!resolved) continue;
                    EvaluateEquipmentObservation(entry, signal, item.Key, descriptor, baseline, now);
                }
                signal.LastPassiveStateItems = observed.Count == 0 ? "None in the held-item fields" : String.Join("; ", observed);
                signal.LastPassiveReadStatus = unresolved == 0 ? status
                    : status + "; " + unresolved.ToString(CultureInfo.InvariantCulture) + " item definition(s) unavailable and not evaluated";
                if (snapshot.Items.Count > 0) signal.PassiveBaselineReady = true;
            }
        }

        private void EvaluateEquipmentObservation(PlayerEntry entry, InventorySignalState signal, int itemId,
            string descriptor, bool baseline, DateTime now)
        {
            string steamId = signal.SteamId;
            if (IsJailedSteamId(steamId)) return;
            bool allowed = IsAllowedCommonTransportItem(itemId, descriptor);
            bool highValue = ItemMatchesTokens(itemId, descriptor, _highValueItemTokens);
            bool artifact = ItemMatchesTokens(itemId, descriptor, _artifactItemTokens);
            if (!allowed && highValue) signal.HighValueSignals++;
            if (artifact) signal.ArtifactSignals++;

            if (IsServerApprovedItem(steamId, itemId))
            {
                RecordGuardEvidence(steamId, "Items", "Replicated held-item fields", descriptor, CaptureItemGuardPolicy(),
                    "Host-approved item for this session");
                return;
            }

            string reason = "";
            float sessionMinutes = GetPlayerSessionMinutes(steamId, entry);
            string age = sessionMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m online";
            if (!allowed && _artifactItemGuard && artifact)
                reason = "PlayerState reported artifact gear: " + descriptor + " after " + age;
            else if (!allowed && _highValueItemGuard && highValue && sessionMinutes <= _highValueGuardMinutes)
                reason = "PlayerState reported high-value gear too early: " + descriptor + " after " + age;
            else if (!allowed && _earlyItemGuard && ItemMatchesTokens(itemId, descriptor, _restrictedEarlyItemTokens) && sessionMinutes <= _earlyItemGuardMinutes)
                reason = "PlayerState reported restricted early item: " + descriptor + " after " + age;

            const string source = "Replicated held-item fields";
            if (String.IsNullOrEmpty(reason))
            {
                RecordGuardEvidence(steamId, "Items", source, descriptor, CaptureItemGuardPolicy(),
                    allowed ? "Common transport allowance" : "No configured rule triggered");
                return;
            }
            signal.LastDescriptor = descriptor;
            signal.LastReason = reason;
            signal.LastPassiveViolationUtc = now;
            if (baseline)
            {
                RecordGuardEvidence(steamId, "Items", source, descriptor, CaptureItemGuardPolicy(), "Baseline watch only: " + reason);
                AppendEventLog("AUDIT", Shorten(DisplayPlayerName(entry) + " baseline PlayerState watch item: " + descriptor, 140));
                return;
            }
            string learnedNote;
            if (LearnedPolicySuppressesSelfAdmin(steamId, "PlayerStateAudit", reason, descriptor, out learnedNote))
            {
                signal.LastReason = "learned allow: " + Shorten(learnedNote, 96);
                RecordGuardEvidence(steamId, "Items", source, descriptor, CaptureItemGuardPolicy(), "Held by learned policy: " + learnedNote);
                AppendEventLog("LEARN", Shorten("PlayerState audit held for " + DisplayPlayerName(entry) + ": " + learnedNote, 140));
                return;
            }
            if (LearnedPolicyConfirmsCheat(steamId, "PlayerStateAudit", reason, descriptor, out learnedNote))
            {
                reason += " | learned confirmed pattern: " + Shorten(learnedNote, 80);
                signal.LastReason = reason;
            }
            RecordGuardEvidence(steamId, "Items", source, descriptor, CaptureItemGuardPolicy(), "Flagged for review: " + reason);
            AnnounceGuardBlock(entry, steamId, "PlayerStateAudit", reason);
            if (ShouldGuardAutoJail())
            {
                bool moved = JailSteamId(steamId, entry, reason, "PlayerState audit", false);
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + reason
                    + (moved ? ". A jail move has been requested for review." : ". Jail movement is pending admin review."), entry.BoltConnection, entry.BoltEntity);
            }
            else
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: suspicious equipment state detected. " + reason, entry.BoltConnection, entry.BoltEntity);
            LogWarning("PlayerState audit triggered for " + steamId + ": " + reason);
        }

        private string BuildShortcutSummary(EquipmentSnapshot snapshot)
        {
            List<string> items = new List<string>();
            foreach (int id in snapshot.Shortcuts)
            {
                string description;
                if (!TryDescribeEquipmentItem(id, out description))
                    description = "itemId=" + id.ToString(CultureInfo.InvariantCulture) + " (definition unavailable)";
                items.Add(description);
            }
            return snapshot.ShortcutStatus + (items.Count == 0 ? "" : ": " + String.Join("; ", items));
        }

        private void ResetPassiveEquipmentBaseline(string steamId)
        {
            InventorySignalState signal;
            if (!_inventorySignals.TryGetValue(steamId, out signal) || signal == null) return;
#if NET11_0_OR_GREATER
            signal.PassiveSession = null;
            signal.PassiveNetworkId = 0;
#else
            signal.PassiveEntity = null;
#endif
            signal.PassiveBaselineReady = false;
            signal.KnownStateItems.Clear();
            signal.LastPassiveReadStatus = "Offline; last successful snapshot retained";
        }
    }
}
