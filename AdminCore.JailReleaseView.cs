using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void DrawJailReturnList()
        {
            if (_pendingJailReturns.Count == 0) return;

            BeginPanel("Pending release returns");
            GUILayout.Label("These players are released. Their return requests were not sent. Saved destinations last only for this world. Returns are retried only when you choose Retry Return.", _hintStyle, GUILayout.ExpandWidth(true));
            List<string> keys = new List<string>(_pendingJailReturns.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                PendingJailReturn pending;
                if (_pendingJailReturns.TryGetValue(keys[i], out pending))
                    DrawJailReturnRow(keys[i], pending);
            }
            EndPanel();
        }

        private void DrawSelectedJailReturn(string steamId)
        {
            steamId = CleanSteamId(steamId);
            PendingJailReturn pending;
            if (String.IsNullOrEmpty(steamId) || !_pendingJailReturns.TryGetValue(steamId, out pending)) return;

            BeginPanel("Pending release return");
            GUILayout.Label("This player is released. The return request was not sent. The saved destination lasts only for this world; retry is manual.", _hintStyle, GUILayout.ExpandWidth(true));
            DrawJailReturnRow(steamId, pending);
            EndPanel();
        }

        private void DrawJailReturnRow(string steamId, PendingJailReturn pending)
        {
            GUILayout.BeginVertical(_rowStyle);
            GUILayout.Label((String.IsNullOrEmpty(pending.Name) ? "Unknown player" : pending.Name) + " | Steam64: " + pending.SteamId, _labelStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Saved destination: " + FormatVector(pending.Position), _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label("Last attempt: " + EvidenceTime(pending.LastAttemptUtc, DateTime.UtcNow), _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(pending.Detail ?? "", _hintStyle, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            if (DrawModerationButton("Retry Return", _buttonStyle, _hostActionsAvailable, GUILayout.Width(120f), GUILayout.Height(30f)))
                RetryJailReturn(steamId);
            if (GUILayout.Button(new GUIContent("Forget Return", null, "Discards the saved return only. Does not move or re-jail the player."), _quietButtonStyle, GUILayout.Width(120f), GUILayout.Height(30f)))
                ForgetJailReturn(steamId);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
