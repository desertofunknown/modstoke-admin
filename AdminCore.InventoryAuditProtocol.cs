using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private const string LoaderAuditCommandName = "openacai.audit";
        // Older clients match this notice exactly before sending their audit report.
        private const string LoaderAuditNoticeMessage = "OpenACAI Admin: the host requested an inventory audit.";
        private const int LoaderAuditChunkLimit = 12;
        private const int LoaderAuditChunkCharacters = 160;

        private static bool TryBuildLoaderAuditCommands(Guid noticeId, Dictionary<int, int> items, out List<string> commands)
        {
            commands = new List<string>();
            if (noticeId == Guid.Empty || items == null || !IsLoaderAuditVersion(Plugin.Version)) return false;
            var ids = new List<int>(items.Keys);
            ids.Sort();
            var payloads = new List<string>();
            var chunk = new StringBuilder();
            foreach (int id in ids)
            {
                int count = items[id];
                if (id <= 0 || count <= 0) return false;
                string pair = id.ToString(CultureInfo.InvariantCulture) + ":" + count.ToString(CultureInfo.InvariantCulture);
                if (chunk.Length > 0 && chunk.Length + 1 + pair.Length > LoaderAuditChunkCharacters)
                {
                    payloads.Add(chunk.ToString());
                    chunk.Clear();
                    if (payloads.Count >= LoaderAuditChunkLimit) return false;
                }
                if (chunk.Length > 0) chunk.Append(',');
                chunk.Append(pair);
            }
            payloads.Add(chunk.Length == 0 ? "-" : chunk.ToString());
            for (int index = 0; index < payloads.Count; index++)
            {
                string command = BuildLoaderAuditCommand(noticeId, index, payloads.Count, payloads[index]);
                if (Encoding.UTF8.GetByteCount(command) > 256)
                {
                    commands.Clear();
                    return false;
                }
                commands.Add(command);
            }
            return true;
        }

        private static string BuildLoaderAuditFailureCommand(Guid noticeId, string failure)
        {
            if (noticeId == Guid.Empty) throw new ArgumentException("An audit notice ID is required.", nameof(noticeId));
            if (failure != "!unavailable" && failure != "!too-large") throw new ArgumentException("Unknown inventory audit failure.", nameof(failure));
            if (!IsLoaderAuditVersion(Plugin.Version)) throw new InvalidOperationException("Invalid inventory audit version.");
            return BuildLoaderAuditCommand(noticeId, 0, 1, failure);
        }

        private static string BuildLoaderAuditCommand(Guid noticeId, int index, int total, string payload)
        {
            return "/" + LoaderAuditCommandName + " " + noticeId.ToString("N") + " "
                + index.ToString(CultureInfo.InvariantCulture) + " " + total.ToString(CultureInfo.InvariantCulture)
                + " " + Plugin.Version + " " + payload;
        }

        private static bool TryParseLoaderAuditChunk(string arguments, out Guid noticeId, out int index, out int total, out string version, out string payload)
        {
            noticeId = Guid.Empty;
            index = 0;
            total = 0;
            version = "";
            payload = "";
            if (String.IsNullOrEmpty(arguments) || arguments.Length > 256
                || Encoding.UTF8.GetByteCount("/" + LoaderAuditCommandName + " " + arguments) > 256) return false;
            string[] fields = arguments.Split(' ');
            if (fields.Length != 5 || fields[0].Length != 32
                || !Guid.TryParseExact(fields[0], "N", out noticeId) || noticeId == Guid.Empty
                || !Int32.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
                || !Int32.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out total)
                || total < 1 || total > LoaderAuditChunkLimit || index < 0 || index >= total
                || !IsLoaderAuditVersion(fields[3]) || fields[4].Length < 1 || fields[4].Length > LoaderAuditChunkCharacters) return false;
            foreach (char character in fields[4])
                if (character < '!' || character > '~') return false;
            foreach (char character in fields[1])
                if (character < '0' || character > '9') return false;
            foreach (char character in fields[2])
                if (character < '0' || character > '9') return false;
            version = fields[3];
            payload = fields[4];
            return true;
        }

        private static bool IsLoaderAuditVersion(string version)
        {
            if (String.IsNullOrEmpty(version) || version.Length > 24) return false;
            foreach (char character in version)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '.' || character == '-')) return false;
            return true;
        }
    }
}
