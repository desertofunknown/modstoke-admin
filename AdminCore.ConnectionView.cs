using System;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawSelectedConnectionObservation(PlayerEntry entry)
        {
            GUILayout.BeginHorizontal();
            if (DrawModerationButton(new GUIContent("Check Connection", null,
                "Requires a player-host world and Modstoke Loader session tracking. The local host has no remote measurement. Values remain retained until another explicit read or the session resets."),
                _quietButtonStyle, CanReadServerConnection(entry), GUILayout.Width(150f), GUILayout.Height(28f)))
                ReadSelectedServerConnection();

            ServerConnectionObservation observation;
            _serverConnectionObservations.TryGetValue(CleanSteamId(entry == null ? "" : entry.SteamId), out observation);
            DateTime now = DateTime.UtcNow;
            bool hasSample = observation != null && observation.SampleUtc != DateTime.MinValue;
            string age = hasSample ? "Read " + EvidenceNumber(Math.Max(0, (now - observation.SampleUtc).TotalSeconds)) + "s ago" : "No retained read";
            GUILayout.Label(new GUIContent(Shorten(age, 25), null,
                hasSample ? "Read completed: " + EvidenceTime(observation.SampleUtc, now) : age),
                _hintStyle, GUILayout.Height(28f));
            GUILayout.EndHorizontal();

            string status = observation == null ? "Connection has not been checked." :
                EvidenceValue(observation.Status) + " | " + GetServerConnectionSampleStatus(observation);
            string detail = status;
            if (observation != null)
                detail += "\nLast attempt: " + EvidenceTime(observation.LastAttemptUtc, now)
                    + "\nRead completed: " + EvidenceTime(observation.SampleUtc, now);
            string summary = hasSample ? "Last read RTT: " + observation.NetworkRtt + "\n" + Shorten(status, 47)
                : Shorten(status, 65);
            GUILayout.Label(new GUIContent(summary, null, detail), _hintStyle, MinimumHeight(36f));
        }

        private void DrawConnectionEvidence(PlayerEntry entry, string steamId, DateTime now)
        {
            BeginPanel("Connection / Retained Read");
            if (DrawModerationButton(new GUIContent("Check Connection", null,
                "Requires a player-host world and Modstoke Loader session tracking. The local host has no remote measurement. Opening this panel does not refresh the values."),
                _quietButtonStyle, CanReadServerConnection(entry), GUILayout.Width(150f), GUILayout.Height(28f)))
                ReadSelectedServerConnection();

            ServerConnectionObservation observation;
            if (!_serverConnectionObservations.TryGetValue(steamId, out observation) || observation == null)
            {
                EvidenceHint("No connection read retained for this player. Use Check Connection while a supported remote connection is available on the host.");
                EndPanel();
                return;
            }

            EvidenceText("Latest status: " + EvidenceValue(observation.Status));
            EvidenceHint("Last attempt: " + EvidenceTime(observation.LastAttemptUtc, now));
            EvidenceText("Sample state: " + GetServerConnectionSampleStatus(observation));
            if (observation.SampleUtc == DateTime.MinValue)
            {
                EvidenceHint("No completed read retained.");
                EndPanel();
                return;
            }

            EvidenceText("Read completed: " + EvidenceTime(observation.SampleUtc, now));
            EvidenceHint("Read completion time does not establish when the native metrics last updated.");
            EvidenceText("Network RTT estimate: " + EvidenceValue(observation.NetworkRtt));
            EvidenceText("Aliased RTT estimate: " + EvidenceValue(observation.AliasedRtt));
            EvidenceHint("Network RTT is after native ACK-delay deduction; aliased RTT is before ACK adjustment. Either estimate may still be a native default.");
            EvidenceText("Inbound published Bolt bits: " + EvidenceValue(observation.IncomingBits));
            EvidenceText("Outbound published Bolt bits: " + EvidenceValue(observation.OutgoingBits));
            EvidenceHint("Bit counters cover the last native published frame-based interval; its duration is not exposed. They are not rates, cumulative totals or proof of delivery.");
            EvidenceText("Connection first observed: " + EvidenceTime(observation.FirstObservedUtc, now));
            EvidenceHint("First observed is the tracking timestamp, not an exact connection start time.");
            EndPanel();
        }
    }
}
