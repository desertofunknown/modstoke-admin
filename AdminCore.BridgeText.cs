using System;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private static string CleanBridgeText(string value, int max)
        {
            if (value == null)
            {
                return "";
            }
            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (max > 0 && value.Length > max)
            {
                int length = max;
                if (Char.IsHighSurrogate(value[length - 1]) && Char.IsLowSurrogate(value[length])) length--;
                value = value.Substring(0, length);
            }
            return value;
        }

        private static bool IsBridgeTextCommand(string action)
        {
            return action == "say" || action == "announce" || action == "broadcast"
                || action == "chat" || action == "localchat" || action == "hostchat"
                || action == "acchat" || action == "anticheat" || action == "notice"
                || action == "adminnotice";
        }

        private static string[] SplitBridgeCommand(string raw)
        {
            int separatorIndex = raw.IndexOfAny(new[] { '/', '|' });
            string action = CleanConfigText(separatorIndex < 0 ? raw : raw.Substring(0, separatorIndex), 32)
                .ToLowerInvariant();
            if (separatorIndex < 0)
            {
                return new[] { action };
            }

            string[] parts = raw.Split(new[] { raw[separatorIndex] }, IsBridgeTextCommand(action) ? 2 : 3,
                StringSplitOptions.None);
            parts[0] = action;
            return parts;
        }
    }
}
