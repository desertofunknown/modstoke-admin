using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool JailSteamId(string steamId, PlayerEntry entry, string reason, string source, bool notify)
        {
            if (!HasHostSession())
            {
                return false;
            }
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                return false;
            }

            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }

            if (String.Equals(steamId, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                LogWarning("Jail request rejected for the local host " + steamId);
                return false;
            }

            _pendingJailReturns.Remove(steamId);
            JailEntry jail;
            if (!_jailedPlayers.TryGetValue(steamId, out jail) || jail == null)
            {
                jail = new JailEntry();
                jail.SteamId = steamId;
                jail.AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                _jailedPlayers[steamId] = jail;
            }

            jail.Name = entry == null ? jail.Name : DisplayPlayerName(entry);
            if (String.IsNullOrEmpty(jail.Name))
            {
                jail.Name = steamId;
            }
            jail.Reason = String.IsNullOrEmpty(reason) ? "Unauthorized cheat event" : reason;
            jail.Source = String.IsNullOrEmpty(source) ? "Anti-cheat" : source;
            jail.LastTriggerUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            jail.TriggerCount++;

            bool queued = QueueJailMove(steamId);
            _nextJailTickUtc = DateTime.MinValue;
            bool repeatBanned = TryAutoBanRepeatCheater(steamId, entry, jail, jail.Source);
            if (notify && queued && !repeatBanned)
            {
                SendAntiCheatNotice(entry, steamId, "Anti-cheat: " + jail.Reason
                    + ". A jail move has been requested for admin review.", null, null);
            }
            if (queued || repeatBanned)
            {
                AnnounceCheatEnforcementRequest(entry, steamId, jail.Reason, jail.Source, repeatBanned);
            }
            string outcome = repeatBanned ? "Repeat ban sent" : queued ? "Jail move queued" : "Jail " + jail.MovementStatus;
            LogWarning(outcome + " for " + steamId + " via " + jail.Source + " (" + jail.Reason + "); " + jail.MovementDetail);
            RecordPlayerActivity(entry, steamId, outcome + " via " + jail.Source + ": " + CleanConfigText(jail.Reason, 96) + "; " + jail.MovementDetail);
            return queued;
        }

        private void TickJailEnforcement()
        {
            _nextJailTickUtc = GetSettingsDeadline(DateTime.UtcNow, Math.Max(1f, _jailTickSeconds));
            if (!HasHostSession() || _jailedPlayers.Count == 0)
            {
                return;
            }

            RefreshPlayers(false);
            List<string> keys = new List<string>(_jailedPlayers.Keys);
            int queued = 0;
            int revivesQueued = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                PlayerEntry entry = FindPlayerBySteamId(keys[i]);
                JailEntry jail;
                _jailedPlayers.TryGetValue(keys[i], out jail);
                bool firstQueue = jail != null && jail.LastMoveQueuedUtc == DateTime.MinValue;
                if (TryQueueJailRevive(keys[i], entry))
                {
                    revivesQueued++;
                }
                if (QueueJailMove(keys[i]))
                {
                    queued++;
                    if (firstQueue)
                    {
                        RecordPlayerActivity(entry, keys[i], "Pending jail move queued: " + CleanConfigText(jail.Reason, 96)
                            + "; " + jail.MovementDetail);
                    }
                }
            }
            if (queued > 0)
            {
                LogInfo("Jail enforcement queued movement requests for " + queued.ToString(CultureInfo.InvariantCulture)
                    + " player(s) to " + DescribeJailPosition());
            }
            if (revivesQueued > 0)
            {
                LogInfo("Jail care queued " + revivesQueued.ToString(CultureInfo.InvariantCulture)
                    + " revive request(s); recovery is unconfirmed");
            }
        }

        private bool TryAutoBanRepeatCheater(string steamId, PlayerEntry entry, JailEntry jail, string source)
        {
            if (!HasHostSession() || !_autoBanRepeatCheaters || jail == null || String.IsNullOrEmpty(source)
                || source.IndexOf("Anti-cheat", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            int threshold = Math.Max(2, _autoBanTriggerCount);
            if (jail.TriggerCount < threshold || _bannedPlayers.ContainsKey(steamId))
            {
                return false;
            }

            ulong parsed;
            if (!UInt64.TryParse(steamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            if (entry == null)
            {
                entry = FindPlayerBySteamId(steamId);
            }
            string reason = "Repeat anti-cheat triggers (" + jail.TriggerCount.ToString(CultureInfo.InvariantCulture) + "): " + jail.Reason;
            if (!SendBan(parsed, entry, reason, "Anti-cheat repeat", out _, out string outcome))
            {
                LogWarning("Anti-cheat repeat: " + outcome + "; jail enforcement remains active");
                return false;
            }

            SendAntiCheatNotice(entry, steamId, "Anti-cheat: repeat violation threshold reached. A ban has been requested.", null, null);
            LogWarning("Anti-cheat repeat: " + outcome + " after " + jail.TriggerCount.ToString(CultureInfo.InvariantCulture) + " trigger(s)");
            return true;
        }

    }
}
