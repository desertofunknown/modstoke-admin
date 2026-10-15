using System;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void SendSelectedAdminDebugCommand()
        {
            if (!TryGetSelectedModerationTarget(out var entry, out ulong steamId))
            {
                ActionFail("Target command requires a selected player with a resolved Steam64 ID");
                return;
            }

            string command = CleanConfigText(_debugPlayerCommandInput, 96);
            string data = CleanConfigText(_debugPlayerCommandDataInput, 240);
            if (String.IsNullOrEmpty(command))
            {
                ActionFail("Target command not attempted: command is empty");
                return;
            }

            bool sent = SendAdminDebugCommand(steamId, command, data, out string dispatch);
            string outcome = "Target command for " + steamId.ToString(CultureInfo.InvariantCulture) + ": " + dispatch;
            RecordPlayerActivity(entry, steamId.ToString(CultureInfo.InvariantCulture),
                outcome + "; command: " + command + "; data: " + data);
            ActionResult(sent, outcome, outcome);
        }

        private bool SendAdminDebugCommand(ulong steamId, string command, string data, out string outcome)
        {
            object request;
            MethodInfo send;
            try
            {
                outcome = "not attempted: an active multiplayer host session is required";
                if (!HasHostSession()) return false;
                string id = steamId.ToString(CultureInfo.InvariantCulture);
                outcome = "not attempted: a remote player and a nonempty command are required";
                if (steamId == 0 || String.IsNullOrEmpty(command)
                    || String.Equals(id, CleanSteamId(GetLocalSteamIdFromRuntime()), StringComparison.Ordinal)) return false;

                Type eventType = ReflectionTools.FindType("AdminCommand");
                Type connectionType = ReflectionTools.FindType("BoltConnection");
                Type utilities = ReflectionTools.FindType("Sons.Multiplayer.MultiplayerUtilities");
                outcome = "not attempted: native command or connection APIs are unavailable";
                if (eventType == null || connectionType == null || utilities == null) return false;

                const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.ExactBinding;
                const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.ExactBinding;
                MethodInfo lookup = utilities.GetMethod("GetConnectionFromSteamId", staticFlags, null, new[] { typeof(ulong) }, null);
                MethodInfo readSteamId = utilities.GetMethod("GetSteamId", staticFlags, null, new[] { connectionType }, null);
                PropertyInfo connectionId = connectionType.GetProperty("ConnectionId", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo create = eventType.GetMethod("Create", staticFlags, null, new[] { connectionType }, null);
                MethodInfo setCommand = eventType.GetMethod("set_Command", instanceFlags, null, new[] { typeof(string) }, null);
                MethodInfo setData = eventType.GetMethod("set_Data", instanceFlags, null, new[] { typeof(string) }, null);
                send = eventType.GetMethod("Send", instanceFlags, null, Type.EmptyTypes, null);
                outcome = "not attempted: native command signatures are unavailable or unsupported";
                if (lookup == null || lookup.ReturnType != connectionType
                    || readSteamId == null || readSteamId.ReturnType != typeof(ulong)
                    || connectionId == null || connectionId.PropertyType != typeof(uint) || connectionId.GetGetMethod() == null
                    || create == null || create.ReturnType != eventType
                    || setCommand == null || setCommand.ReturnType != typeof(void)
                    || setData == null || setData.ReturnType != typeof(void)
                    || send == null || send.ReturnType != typeof(void)) return false;

                object connection = lookup.Invoke(null, new object[] { steamId });
                outcome = "not attempted: no connected native recipient reports the selected Steam64 ID";
                if (!TryReadTargetCommandRecipient(connection, steamId, readSteamId, connectionId,
                    out IntPtr pointer, out uint nativeId)) return false;

                // Create one connection-targeted event and require both payload writes before Send.
                request = create.Invoke(null, new[] { connection });
                outcome = "not attempted: native event creation returned no AdminCommand";
                if (request == null || !eventType.IsInstanceOfType(request)) return false;
                setCommand.Invoke(request, new object[] { command });
                setData.Invoke(request, new object[] { data ?? "" });

                outcome = "not attempted: host session ended during command preparation";
                if (!HasHostSession()) return false;
                object current = lookup.Invoke(null, new object[] { steamId });
                outcome = "not attempted: recipient changed or could not be verified before sending";
                if (!TryReadTargetCommandRecipient(current, steamId, readSteamId, connectionId,
                    out IntPtr currentPointer, out uint currentId)
                    || currentPointer != pointer || currentId != nativeId) return false;
            }
            catch (Exception ex)
            {
                LogWarning("Target AdminCommand preparation for " + steamId.ToString(CultureInfo.InvariantCulture)
                    + " failed: " + (ex.InnerException ?? ex).Message);
                outcome = "not attempted: native command preparation failed";
                return false;
            }

            try
            {
                send.Invoke(request, Array.Empty<object>());
                outcome = "native Send returned; client delivery and execution are unconfirmed";
                return true;
            }
            catch (Exception ex)
            {
                LogWarning("Target AdminCommand Send for " + steamId.ToString(CultureInfo.InvariantCulture)
                    + " failed: " + (ex.InnerException ?? ex).Message);
                outcome = "Send failed with an uncertain outcome; no second attempt was made";
                return false;
            }
        }

        private static bool TryReadTargetCommandRecipient(object connection, ulong steamId, MethodInfo readSteamId,
            PropertyInfo connectionId, out IntPtr pointer, out uint nativeId)
        {
            pointer = IntPtr.Zero;
            nativeId = 0;
            if (!(connection is Il2CppObjectBase nativeConnection) || nativeConnection.Pointer == IntPtr.Zero) return false;
            object udp = ReflectionTools.GetMemberValue(connection, "udpConnection");
            if (ReflectionTools.GetMemberValue(udp, "IsConnected") is not true
                || readSteamId.Invoke(null, new[] { connection }) is not ulong observed || observed != steamId
                || connectionId.GetValue(connection) is not uint observedId) return false;
            pointer = nativeConnection.Pointer;
            nativeId = observedId;
            return true;
        }
    }
}
