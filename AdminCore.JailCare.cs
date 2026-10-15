using System;
using System.Globalization;
using UnityEngine;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool TryQueueJailRevive(string steamId, PlayerEntry entry)
        {
            steamId = CleanSteamId(steamId);
            if (!_jailedPlayers.TryGetValue(steamId, out var jail) || jail == null) return false;
            DateTime now = DateTime.UtcNow;
            if (now.Subtract(jail.LastCareCheckUtc).TotalSeconds < 8.0) return false;
            jail.LastCareCheckUtc = now;

            if (!HasHostSession())
            {
                SetJailCare(jail, "Unavailable", "An active multiplayer host is required");
                return false;
            }
#if NET11_0_OR_GREATER
            if (!LooksLikeSteamId(steamId)
                || !UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                || String.Equals(steamId, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal))
            {
                SetJailCare(jail, "Unavailable", "A resolved remote player's Steam64 ID is required");
                return false;
            }
            if (!TryResolveServerIdentity(parsed, out var identity, out string failure))
            {
                SetJailCare(jail, "Unavailable", failure);
                return false;
            }
            if (!OpenACAISonsServerWorld.TryReadPlayerState(identity.Session, out var state, out var readStatus))
            {
                SetJailCare(jail, "Unavailable", "Player state could not be read (" + readStatus + ")");
                return false;
            }

            switch (state.LifeState)
            {
                case OpenACAISonsPlayerLifeState.Alive:
                    // An observed transition can permit another request; it is not a revive receipt.
                    jail.PendingRevive = null;
                    SetJailCare(jail, "Alive observed", "Replicated state reads Alive; no revive requested. Health is not measured");
                    return false;
                case OpenACAISonsPlayerLifeState.Dead:
                    SetJailCare(jail, "Dead observed", "Replicated state reads Dead; the native revive request cannot resurrect this player");
                    return false;
                case OpenACAISonsPlayerLifeState.Dying:
                    break;
                default:
                    SetJailCare(jail, "Unknown state", "Unrecognized replicated life state; no revive requested");
                    return false;
            }

            var pending = jail.PendingRevive;
            if (pending != null && ReferenceEquals(pending.Session, identity.Session))
            {
                SetJailCare(jail, pending.Uncertain ? "Revive uncertain" : "Awaiting revive",
                    "A revive " + (pending.Uncertain ? "may have queued" : "was queued")
                    + " for this session. No Alive state has been observed since; no duplicate request sent");
                return false;
            }

            var status = OpenACAISonsServerPlayerRevives.QueueRevive(identity.Session);
            switch (status)
            {
                case OpenACAISonsPlayerReviveStatus.Queued:
                case OpenACAISonsPlayerReviveStatus.SendUncertain:
                    bool uncertain = status == OpenACAISonsPlayerReviveStatus.SendUncertain;
                    jail.PendingRevive = new PendingJailRevive { Session = identity.Session, Uncertain = uncertain };
                    SetJailCare(jail, uncertain ? "Revive uncertain" : "Revive queued",
                        uncertain ? "Native revive may have queued; send result and recovery are unconfirmed"
                            : "Native revive queued; policy approval, delivery and recovery are unconfirmed");
                    RecordPlayerActivity(entry, steamId, "Jail care: " + jail.CareDetail);
                    if (uncertain) LogWarning("Jail care for " + steamId + ": " + jail.CareDetail);
                    return !uncertain;
                case OpenACAISonsPlayerReviveStatus.NotDying:
                    SetJailCare(jail, "Not queued", "The native request no longer observed Dying; recovery is unconfirmed");
                    break;
                case OpenACAISonsPlayerReviveStatus.Unavailable:
                    SetJailCare(jail, "Unavailable", "Modstoke Loader's player-revive service is unavailable");
                    break;
                case OpenACAISonsPlayerReviveStatus.SessionUnavailable:
                    SetJailCare(jail, "Unavailable", "The selected session is no longer current");
                    break;
                case OpenACAISonsPlayerReviveStatus.PlayerUnavailable:
                case OpenACAISonsPlayerReviveStatus.PlayerStateUnavailable:
                    SetJailCare(jail, "Unavailable", "The current player or replicated state is unavailable");
                    break;
                case OpenACAISonsPlayerReviveStatus.NotServer:
                    SetJailCare(jail, "Unavailable", "An active multiplayer host is required");
                    break;
                case OpenACAISonsPlayerReviveStatus.WrongThread:
                    SetJailCare(jail, "Not queued", "Revive requests must run on the game thread");
                    break;
                default:
                    SetJailCare(jail, "Not queued", "Native revive was not queued (" + status + ")");
                    break;
            }
#else
            SetJailCare(jail, "Unavailable", "Jail care requires the current Modstoke Loader runtime");
#endif
            return false;
        }

        private static void SetJailCare(JailEntry jail, string status, string detail)
        {
            jail.CareStatus = status;
            jail.CareDetail = detail;
        }

        private static string GetJailCareDetail(JailEntry jail)
        {
            string checkedAt = jail.LastCareCheckUtc == DateTime.MinValue ? "Not checked yet. "
                : "Last check: " + jail.LastCareCheckUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ". ";
            return checkedAt + jail.CareDetail + ". Jail care requests revives only; it does not maintain health, food or water.";
        }

        private void DrawJailCareStatus(JailEntry jail)
        {
            string text = "Care: " + jail.CareStatus;
            if (jail.LastCareCheckUtc != DateTime.MinValue)
                text += " | checked " + jail.LastCareCheckUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            GUILayout.Label(new GUIContent(text, null, GetJailCareDetail(jail)), _hintStyle, GUILayout.Height(18f));
        }

#if NET11_0_OR_GREATER
        private sealed class PendingJailRevive
        {
            internal OpenACAISonsServerSession Session;
            internal bool Uncertain;
        }
#endif
    }
}
