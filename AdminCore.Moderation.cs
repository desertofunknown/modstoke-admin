using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _hostActionsAvailable;
        private bool _serverCommandsAvailable;

        private bool CanSendServerCommands()
        {
            _boltNetworkType ??= ReflectionTools.FindType("BoltNetwork");
            if (ReadNetworkFlag("isRunning") != true || !IsLocalPlayerInWorld()) return false;
            if (ReadNetworkFlag("isServer") == true) return true;
            return InvokeStaticMethodResult("CoopAdminCommand", "CheckLocalPlayerIsAdminOrAbove", Array.Empty<object>()) is true;
        }

        private bool RequireHostAction(string action)
        {
            if (HasHostSession()) return true;
            ActionFail(action + " requires an active multiplayer host session");
            return false;
        }

        private bool SendServerCommand(string commandProperty, string fallbackCommand, object argument, out string outcome)
        {
            MethodInfo method;
            string command;
            try
            {
                outcome = "unavailable: permission or session requirements not met; not attempted";
                if (!CanSendServerCommands()) return false;
                outcome = "unavailable: unsupported argument type; not attempted";
                if (!(argument is ulong) && !(argument is bool)) return false;
                Type type = ReflectionTools.FindType("CoopAdminCommand");
                outcome = "unavailable: native command type not found; not attempted";
                if (type == null) return false;
                command = ReflectionTools.GetStaticMemberValue(type, commandProperty) as string;
                if (String.IsNullOrEmpty(command)) command = fallbackCommand;
                outcome = "unavailable: command name not found; not attempted";
                if (String.IsNullOrEmpty(command)) return false;
                method = type.GetMethod("Send", BindingFlags.Public | BindingFlags.Static | BindingFlags.ExactBinding, null,
                    new[] { typeof(string), argument.GetType() }, null);
                outcome = "unavailable: native Send overload missing or return type unsupported; not attempted";
                if (method == null || method.ReturnType != typeof(bool)) return false;
            }
            catch (Exception ex)
            {
                LogWarning("Admin command preparation failed: " + (ex.InnerException ?? ex).Message);
                outcome = "unavailable: command preparation failed; not attempted";
                return false;
            }

            // Send reports whether the request was accepted locally. The server still validates it.
            try
            {
                bool accepted = (bool)method.Invoke(null, new object[] { command, argument });
                outcome = accepted
                    ? "accepted locally; server effect unconfirmed"
                    : "not accepted locally; server effect unconfirmed";
                return accepted;
            }
            catch (Exception ex)
            {
                LogWarning("Admin command " + command + " invocation failed: " + (ex.InnerException ?? ex).Message);
                outcome = "outcome uncertain after the call failed; no second attempt was made";
                return false;
            }
        }

        private bool SendAdminPlayerCommand(string commandProperty, string fallbackCommand, ulong steamId, out string outcome)
        {
            string dispatch;
            bool sent = false;
            if (steamId == 0) dispatch = "unavailable: Steam64 must be nonzero; not attempted";
            else sent = SendServerCommand(commandProperty, fallbackCommand, steamId, out dispatch);
            outcome = "for " + steamId.ToString(CultureInfo.InvariantCulture) + ": " + dispatch;
            return sent;
        }

        private bool SendAdminBoolCommand(string commandProperty, string fallbackCommand, bool value, out string outcome)
        {
            return SendServerCommand(commandProperty, fallbackCommand, value, out outcome);
        }

        private void RequestServerToggle(string commandProperty, string fallbackCommand, bool value, string label)
        {
            bool sent = SendAdminBoolCommand(commandProperty, fallbackCommand, value, out string dispatch);
            string outcome = label + ": " + dispatch;
            ActionResult(sent, outcome, outcome);
        }

        private bool SendKick(ulong steamId, PlayerEntry entry, string reason, out string outcome)
        {
            bool sent = SendKickRequest(steamId, out outcome);
            RecordPlayerActivity(entry, steamId.ToString(CultureInfo.InvariantCulture),
                outcome + "; reason: " + CleanConfigText(reason, 96));
            return sent;
        }

        private bool SendBan(ulong steamId, PlayerEntry entry, string reason, string source, out bool settingsSaved, out string outcome)
        {
            settingsSaved = false;
            bool sent = SendAdminPlayerCommand("Command_playerban", "playerban", steamId, out string dispatch);
            string canonicalSteamId = steamId.ToString(CultureInfo.InvariantCulture);
            outcome = "Ban request " + dispatch;
            if (sent)
            {
                RecordModerationEntry(_bannedPlayers, canonicalSteamId, entry, reason, source);
                settingsSaved = SaveConfig();
                outcome += "; Admin ban record updated in memory; " + DescribeModerationSettingsSave(settingsSaved);
            }
            else
            {
                outcome += "; Admin ban record unchanged";
            }
            RecordPlayerActivity(entry, canonicalSteamId,
                outcome + "; reason: " + CleanConfigText(reason, 96));
            return sent;
        }

        private static string DescribeModerationSettingsSave(bool saved)
        {
            return saved ? "Admin settings saved" : "Admin settings not saved; current records remain in memory";
        }

        private bool SetPlayerRole(string roleName, ulong steamId, out string outcome)
        {
            bool sent;
            string dispatch;
            switch ((roleName ?? "").ToLowerInvariant())
            {
                case "admin": sent = SendAdminPlayerCommand("Command_setplayeradmin", "setplayeradmin", steamId, out dispatch); break;
                case "trusted": sent = SendAdminPlayerCommand("Command_setplayertrusted", "setplayertrusted", steamId, out dispatch); break;
                case "guest": sent = SendAdminPlayerCommand("Command_setplayerguest", "setplayerguest", steamId, out dispatch); break;
                default:
                    outcome = "Role request not sent: choose Admin, Trusted or Guest";
                    return false;
            }
            outcome = "Role " + roleName + " request " + dispatch;
            if (HasHostSession())
            {
                ObserveServerRole(steamId, out string observation);
                outcome += ". " + observation;
            }
            return sent;
        }

        private static bool NativeActionAccepted(object result)
        {
            return result is bool accepted ? accepted : !(result is InvokeFailed);
        }

        private void DrawModerationAvailability()
        {
            string status = _hostActionsAvailable
                ? "Host actions available. Sent commands still require an observed result."
                : _serverCommandsAvailable
                    ? "Admin requests available. Jail, release, summon and unban require the host."
                    : "Server actions unavailable: join as host or an authorized admin.";
            GUILayout.Label(status, _hintStyle, GUILayout.Height(38f));
        }

        private bool DrawModerationButton(string label, GUIStyle style, bool available, params GUILayoutOption[] options)
        {
            return DrawModerationButton(new GUIContent(label), style, available, options);
        }

        private bool DrawModerationButton(GUIContent content, GUIStyle style, bool available, params GUILayoutOption[] options)
        {
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && available;
                return GUILayout.Button(content, style, options);
            }
            finally
            {
                GUI.enabled = enabled;
            }
        }

        private bool DrawModerationIconButton(Texture2D icon, string label, GUIStyle style, bool available, float width, float height)
        {
            GUIContent content = icon == null ? new GUIContent(label) : new GUIContent(label, icon, label);
            return DrawModerationButton(content, style, available, GUILayout.Width(width), GUILayout.Height(height));
        }
    }
}
