using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly Dictionary<string, PendingJailReturn> _pendingJailReturns =
            new Dictionary<string, PendingJailReturn>(StringComparer.OrdinalIgnoreCase);

        private enum JailReturnStatus
        {
            NotRequested,
            NoPosition,
            NotQueued,
            Queued,
            Uncertain
        }

        private sealed class PendingJailReturn
        {
            public string SteamId;
            public string Name;
            public Vector3 Position;
            public string Detail;
            public DateTime LastAttemptUtc;
        }

        private bool ReleaseJailedSteamId(string steamId, bool showAction,
            out JailReturnStatus returnStatus, out string outcome)
        {
            returnStatus = JailReturnStatus.NotRequested;
            outcome = "Release requires an active multiplayer host session";
            if (!RequireHostAction("Release jailed player")) return false;

            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId))
            {
                outcome = "Release failed: invalid Steam64 ID";
                if (showAction) ActionFail(outcome);
                return false;
            }
            if (!_jailedPlayers.TryGetValue(steamId, out var jail))
            {
                outcome = "No jail record for " + steamId;
                if (_pendingJailReturns.ContainsKey(steamId))
                    outcome += "; a return that was not sent is available in Host Controls";
                if (showAction) ActionFail(outcome);
                return false;
            }

            string returnOutcome = "no saved return position";
            returnStatus = jail != null && jail.HasReturnPosition
                ? QueueJailReturn(steamId, jail.ReturnPosition, out returnOutcome)
                : JailReturnStatus.NoPosition;
            bool removed = _jailedPlayers.Remove(steamId);
            _antiCheatNoticeTimes.Remove(steamId);

            // A failed return must not keep the player jailed. Retain only requests known not to have been sent.
            if (removed && returnStatus == JailReturnStatus.NotQueued)
            {
                _pendingJailReturns[steamId] = new PendingJailReturn
                {
                    SteamId = steamId,
                    Name = String.IsNullOrEmpty(jail.Name) ? steamId : jail.Name,
                    Position = jail.ReturnPosition,
                    Detail = returnOutcome,
                    LastAttemptUtc = DateTime.UtcNow
                };
                returnOutcome += "; saved return retained for Retry Return in Host Controls";
            }
            outcome = "Jail record " + (removed ? "removed" : "not removed") + " for " + steamId + "; " + returnOutcome;
            RecordPlayerActivity(FindPlayerBySteamId(steamId), steamId, outcome);
            if (showAction)
                ActionResult(removed && returnStatus == JailReturnStatus.Queued, outcome, outcome);
            return removed;
        }

        private bool ReleaseAllJailedPlayers(bool showAction, out string outcome)
        {
            outcome = "Release all requires an active multiplayer host session";
            if (!RequireHostAction("Release all jailed players")) return false;

            var steamIds = new List<string>(_jailedPlayers.Keys);
            int removed = 0;
            int queued = 0;
            int uncertain = 0;
            int notQueued = 0;
            int noPosition = 0;
            foreach (string steamId in steamIds)
            {
                if (!ReleaseJailedSteamId(steamId, false, out var status, out _)) continue;
                removed++;
                switch (status)
                {
                    case JailReturnStatus.Queued: queued++; break;
                    case JailReturnStatus.Uncertain: uncertain++; break;
                    case JailReturnStatus.NotQueued: notQueued++; break;
                    case JailReturnStatus.NoPosition: noPosition++; break;
                }
            }

            outcome = "Removed " + removed.ToString(CultureInfo.InvariantCulture) + " of "
                + steamIds.Count.ToString(CultureInfo.InvariantCulture) + " jail record(s); returns: "
                + queued.ToString(CultureInfo.InvariantCulture) + " queued, "
                + uncertain.ToString(CultureInfo.InvariantCulture) + " uncertain, "
                + notQueued.ToString(CultureInfo.InvariantCulture) + " not queued, "
                + noPosition.ToString(CultureInfo.InvariantCulture) + " without a saved position";
            if (queued + uncertain > 0) outcome += "; client arrival is unconfirmed";
            if (notQueued > 0) outcome += "; unsent returns retained in Host Controls";
            bool allRemoved = _jailedPlayers.Count == 0;
            if (showAction) ActionResult(allRemoved && queued == removed, outcome, outcome);
            return allRemoved;
        }

        private void RetryJailReturn(string steamId)
        {
            if (!RequireHostAction("Retry return")) return;
            steamId = CleanSteamId(steamId);
            if (!_pendingJailReturns.TryGetValue(steamId, out var pending))
            {
                ActionFail("No unsent return is saved for " + steamId);
                return;
            }
            if (IsJailedSteamId(steamId))
            {
                ActionFail("Return refused: " + steamId + " is currently jailed");
                return;
            }

            var status = QueueJailReturn(steamId, pending.Position, out string detail);
            pending.LastAttemptUtc = DateTime.UtcNow;
            pending.Detail = detail;
            if (status != JailReturnStatus.NotQueued) _pendingJailReturns.Remove(steamId);
            string outcome = "Return for released player " + steamId + ": " + detail;
            if (status == JailReturnStatus.NotQueued) outcome += "; saved return retained";
            RecordPlayerActivity(FindPlayerBySteamId(steamId), steamId, outcome);
            ActionResult(status == JailReturnStatus.Queued, outcome, outcome);
        }

        private void ForgetJailReturn(string steamId)
        {
            steamId = CleanSteamId(steamId);
            if (!_pendingJailReturns.Remove(steamId))
            {
                ActionFail("No unsent return is saved for " + steamId);
                return;
            }
            string outcome = "Saved return forgotten for " + steamId + "; no movement requested";
            RecordPlayerActivity(FindPlayerBySteamId(steamId), steamId, outcome);
            ActionOk(outcome);
        }

        internal void ClearJailReturns()
        {
            _pendingJailReturns.Clear();
        }
    }
}
