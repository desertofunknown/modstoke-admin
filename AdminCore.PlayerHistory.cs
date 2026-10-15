using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool _historyFileReady;
        private bool _savingPlayerHistory;
        private readonly HashSet<string> _pendingPlayerHistoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void LoadPlayerHistory()
        {
            try
            {
                Dictionary<string, PlayerEntry> staged = new Dictionary<string, PlayerEntry>(StringComparer.OrdinalIgnoreCase);
                string[] lines;
                bool missingFile = false;
                try
                {
                    lines = File.ReadAllLines(_playerHistoryPath);
                }
                catch (FileNotFoundException)
                {
                    lines = new string[0];
                    missingFile = true;
                }
                catch (DirectoryNotFoundException)
                {
                    lines = new string[0];
                    missingFile = true;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (String.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 9) continue;
                    string steamId = CleanSteamId(parts[0]);
                    if (!LooksLikeSteamId(steamId)) continue;

                    PlayerEntry entry = new PlayerEntry();
                    entry.Key = steamId;
                    entry.SteamId = steamId;
                    entry.Name = DecodeHistoryText(parts[1]);
                    entry.Alias = DecodeHistoryText(parts[2]);
                    entry.FirstSeenUtc = ParseHistoryUtc(parts[3]);
                    entry.LastSeenUtc = ParseHistoryUtc(parts[4]);
                    entry.LastActivityUtc = CleanConfigText(parts[5], 48);
                    Int32.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out entry.ActivityCount);
                    entry.LastRosterContext = DecodeHistoryText(parts[7]);
                    entry.LastActivity = DecodeHistoryText(parts[8]);
                    if (parts.Length > 9)
                    {
                        string[] names = DecodeHistoryText(parts[9]).Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int n = 0; n < names.Length; n++) AddObservedPlayerName(entry, names[n]);
                    }
                    entry.Online = false;
                    entry.Source = "History";
                    AddPlayerSource(entry, "History");
                    staged[BuildPlayerHistoryKey(entry.LastRosterContext, steamId)] = entry;
                }

                // A missing file can be recreated from the retained cache, including older contexts.
                if (missingFile)
                {
                    foreach (var pair in _playerHistory) staged[pair.Key] = pair.Value;
                }
                foreach (string key in _pendingPlayerHistoryKeys)
                {
                    PlayerEntry pending;
                    if (!_playerHistory.TryGetValue(key, out pending) || pending == null) continue;
                    PlayerEntry entry;
                    if (!staged.TryGetValue(key, out entry))
                    {
                        staged[key] = pending;
                        continue;
                    }
                    if (ReferenceEquals(entry, pending)) continue;

                    string diskActivityUtc = entry.LastActivityUtc;
                    string diskActivity = entry.LastActivity;
                    DateTime diskActivityTime = ParseHistoryUtc(diskActivityUtc);
                    DateTime pendingActivityTime = ParseHistoryUtc(pending.LastActivityUtc);
                    MergePlayerEntry(entry, pending);
                    if (pending.FirstSeenUtc != DateTime.MinValue
                        && (entry.FirstSeenUtc == DateTime.MinValue || pending.FirstSeenUtc < entry.FirstSeenUtc))
                    {
                        entry.FirstSeenUtc = pending.FirstSeenUtc;
                    }
                    if (pending.LastSeenUtc > entry.LastSeenUtc) entry.LastSeenUtc = pending.LastSeenUtc;
                    if (pendingActivityTime != DateTime.MinValue && pendingActivityTime >= diskActivityTime)
                    {
                        entry.LastActivityUtc = pending.LastActivityUtc;
                        entry.LastActivity = pending.LastActivity;
                    }
                    else
                    {
                        entry.LastActivityUtc = diskActivityUtc;
                        entry.LastActivity = diskActivity;
                    }
                    ClearPlayerHandles(entry);
                    entry.Online = false;
                }

                _playerHistory = staged;
                _historyFileReady = true;
                _loadedPlayerHistoryContext = null;
            }
            catch (Exception ex)
            {
                _historyFileReady = false;
                LogWarning("Player history load skipped: " + ex.Message);
            }
        }

        private void SavePlayerHistoryThrottled(bool force)
        {
            if (_savingPlayerHistory) return;
            if (!force && DateTime.UtcNow < _nextPlayerHistorySaveUtc) return;
            _nextPlayerHistorySaveUtc = DateTime.UtcNow.AddSeconds(10.0);
            SavePlayerHistory();
        }

        internal void FlushPlayerHistory() => SavePlayerHistoryThrottled(true);

        private void SavePlayerHistory()
        {
            if (_savingPlayerHistory) return;
            _savingPlayerHistory = true;
            string tempPath = null;
            bool tempCreated = false;
            try
            {
                foreach (PlayerEntry entry in _sessionPlayers.Values)
                {
                    TouchPlayerHistory(entry, null, false, false);
                }
                if (!_historyFileReady) LoadPlayerHistory();
                if (!_historyFileReady) return;

                List<string> keys = new List<string>(_playerHistory.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                List<string> lines = new List<string>();
                lines.Add("# steam64\tname\talias\tfirstSeenUtc\tlastSeenUtc\tlastActivityUtc\tactivityCount\tcontext\tlastActivity\tobservedNames");
                for (int i = 0; i < keys.Count; i++)
                {
                    PlayerEntry entry = _playerHistory[keys[i]];
                    if (entry == null || !LooksLikeSteamId(entry.SteamId)) continue;
                    lines.Add(CleanSteamId(entry.SteamId)
                        + "\t" + EncodeHistoryText(entry.Name)
                        + "\t" + EncodeHistoryText(entry.Alias)
                        + "\t" + FormatHistoryUtc(entry.FirstSeenUtc)
                        + "\t" + FormatHistoryUtc(entry.LastSeenUtc)
                        + "\t" + CleanConfigText(entry.LastActivityUtc, 48)
                        + "\t" + Math.Max(0, entry.ActivityCount).ToString(CultureInfo.InvariantCulture)
                        + "\t" + EncodeHistoryText(entry.LastRosterContext)
                        + "\t" + EncodeHistoryText(entry.LastActivity)
                        + "\t" + EncodeHistoryText(String.Join("|", entry.ObservedNames.ToArray())));
                }

                Directory.CreateDirectory(_modDir);
                tempPath = _playerHistoryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    tempCreated = true;
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        for (int i = 0; i < lines.Count; i++) writer.WriteLine(lines[i]);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                if (File.Exists(_playerHistoryPath)) File.Replace(tempPath, _playerHistoryPath, null);
                else File.Move(tempPath, _playerHistoryPath);
                tempCreated = false;
                _pendingPlayerHistoryKeys.Clear();
            }
            catch (Exception ex)
            {
                LogWarning("Player history save skipped: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (tempCreated) File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    LogWarning("Player history temporary file cleanup skipped: " + ex.Message);
                }
                finally
                {
                    _savingPlayerHistory = false;
                }
            }
        }
    }
}
