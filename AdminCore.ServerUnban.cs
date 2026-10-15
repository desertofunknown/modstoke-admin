using System;
using System.Globalization;
using System.Reflection;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool UnbanSteamId(ulong steamId, string reason, out string outcome)
        {
            string id = steamId.ToString(CultureInfo.InvariantCulture);
            if (steamId == 0)
            {
                outcome = "Unban not attempted: invalid Steam64 ID; Admin ban record unchanged";
                return false;
            }

            bool returned = InvokeNativeUnban(steamId, out bool attempted, out string nativeOutcome);
            outcome = "Unban for " + id + ": " + nativeOutcome;
            PlayerEntry entry = FindPlayerBySteamId(id);
            if (attempted)
            {
                bool available = false;
                bool banned = false;
                string observation = "game query unavailable";
                try
                {
                    if (HasHostSession())
                    {
                        available = TryCheckPlayerBanned(steamId, out banned);
                        if (available)
                            observation = "game query reports " + (banned ? "banned" : "not banned");
                    }
                    else
                        observation += " (host session ended)";
                }
                catch (Exception ex)
                {
                    LogWarning("Unban follow-up query for " + id + " failed: " + (ex.InnerException ?? ex).Message);
                    observation = "game query unavailable (read failed)";
                }
                DateTime readUtc = DateTime.UtcNow;
                RecordNativeBanQuery(entry, available, banned);
                outcome += "; " + observation + " at "
                    + readUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
            }
            else
                outcome += "; game query not attempted";

            if (returned)
            {
                bool removed = _bannedPlayers.Remove(id);
                bool saved = SaveConfig();
                outcome += removed ? "; Admin ban record removed" : "; no Admin ban record";
                outcome += saved ? "; Admin settings saved" : "; Admin settings not saved";
            }
            else
                outcome += "; Admin ban record unchanged";

            RecordPlayerActivity(entry, id, outcome + "; reason: " + CleanConfigText(reason, 96));
            return returned;
        }

        private bool InvokeNativeUnban(ulong steamId, out bool attempted, out string outcome)
        {
            attempted = false;
            MethodInfo method;
            object[] arguments;
            try
            {
                outcome = "unavailable: an active multiplayer host session and nonzero Steam64 are required; not attempted";
                if (steamId == 0 || !HasHostSession()) return false;
                Type type = ReflectionTools.FindType("CoopKick");
                outcome = "unavailable: native unban type missing; not attempted";
                if (type == null) return false;
                method = type.GetMethod("UnBanPlayer", BindingFlags.Public | BindingFlags.Static | BindingFlags.ExactBinding,
                    null, new[] { typeof(ulong) }, null);
                outcome = "unavailable: native UInt64 unban overload missing or return type unsupported; not attempted";
                if (method == null || method.ReturnType != typeof(void)) return false;
                arguments = new object[] { steamId };
            }
            catch (Exception ex)
            {
                LogWarning("Unban preparation for " + steamId.ToString(CultureInfo.InvariantCulture)
                    + " failed: " + (ex.InnerException ?? ex).Message);
                outcome = "unavailable: native unban preparation failed; not attempted";
                return false;
            }

            // The UInt64 overload returns void. Do not use the string overload or retry.
            attempted = true;
            try
            {
                method.Invoke(null, arguments);
                outcome = "native call returned";
                return true;
            }
            catch (Exception ex)
            {
                LogWarning("Unban invocation for " + steamId.ToString(CultureInfo.InvariantCulture)
                    + " failed: " + (ex.InnerException ?? ex).Message);
                outcome = "native call failed; outcome uncertain; no second attempt was made";
                return false;
            }
        }
    }
}
