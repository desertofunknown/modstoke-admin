using System;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool HasSavedBanRecord(PlayerEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.SteamId)
                && _bannedPlayers.ContainsKey(entry.SteamId);
        }

        private static void RecordRosterBanFlag(PlayerEntry entry)
        {
            if (entry == null) return;
            entry.NativeBan = true;
            entry.NativeBanObservedUtc = DateTime.UtcNow;
            entry.NativeBanSource = "Roster flag";
        }

        private static void RecordNativeBanQuery(PlayerEntry entry, bool available, bool banned)
        {
            if (entry == null) return;
            DateTime now = DateTime.UtcNow;
            entry.NativeBanAttemptUtc = now;
            entry.NativeBanQueryAvailable = available;
            if (!available) return;
            entry.NativeBan = banned;
            entry.NativeBanObservedUtc = now;
            entry.NativeBanSource = "Game query";
        }

        private static void MergeNativeBanObservation(PlayerEntry target, PlayerEntry source)
        {
            if (target == null || source == null) return;
            if (source.NativeBan.HasValue && source.NativeBanObservedUtc != DateTime.MinValue
                && source.NativeBanObservedUtc >= target.NativeBanObservedUtc)
            {
                target.NativeBan = source.NativeBan;
                target.NativeBanObservedUtc = source.NativeBanObservedUtc;
                target.NativeBanSource = source.NativeBanSource;
            }
            if (source.NativeBanAttemptUtc != DateTime.MinValue
                && source.NativeBanAttemptUtc >= target.NativeBanAttemptUtc)
            {
                target.NativeBanAttemptUtc = source.NativeBanAttemptUtc;
                target.NativeBanQueryAvailable = source.NativeBanQueryAvailable;
            }
        }

        private void DrawNativeBanObservation(PlayerEntry entry)
        {
            GUILayout.Label(new GUIContent(FormatNativeBanObservation(entry, "HH:mm:ss 'UTC'"), null,
                GetNativeBanObservationText(entry)), _hintStyle);
        }

        private static string GetNativeBanObservationText(PlayerEntry entry)
        {
            return FormatNativeBanObservation(entry, "yyyy-MM-dd HH:mm:ss 'UTC'")
                + "\nNative observations are retained reads, separate from saved ban records.";
        }

        private static string FormatNativeBanObservation(PlayerEntry entry, string timeFormat)
        {
            string text = "No native ban observation.";
            if (entry != null && entry.NativeBan.HasValue && entry.NativeBanObservedUtc != DateTime.MinValue)
                text = "Last native ban observation: " + (entry.NativeBan.Value ? "banned" : "not banned")
                    + " (" + entry.NativeBanSource + ", "
                    + entry.NativeBanObservedUtc.ToString(timeFormat, CultureInfo.InvariantCulture) + ").";
            if (entry != null && entry.NativeBanAttemptUtc != DateTime.MinValue && !entry.NativeBanQueryAvailable)
                text += "\nLatest game query unavailable ("
                    + entry.NativeBanAttemptUtc.ToString(timeFormat, CultureInfo.InvariantCulture) + ").";
            return text;
        }
    }
}
