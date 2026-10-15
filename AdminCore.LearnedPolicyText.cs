using System;
using System.Text;
using System.Text.Json;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private static bool TryReadLearnedMemoryLine(string line, out string category, out string note, out string steamId)
        {
            category = note = steamId = "";
            if (String.IsNullOrWhiteSpace(line)) return false;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(line))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
                    bool seenCategory = false, seenNote = false, seenSteamId = false;
                    string parsedCategory = "", parsedNote = "", parsedSteamId = "";
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        switch (property.Name)
                        {
                            case "category":
                                if (seenCategory || property.Value.ValueKind != JsonValueKind.String) return false;
                                seenCategory = true;
                                parsedCategory = property.Value.GetString();
                                break;
                            case "note":
                                if (seenNote || property.Value.ValueKind != JsonValueKind.String) return false;
                                seenNote = true;
                                parsedNote = property.Value.GetString();
                                break;
                            case "steam_id":
                                if (seenSteamId || property.Value.ValueKind != JsonValueKind.String) return false;
                                seenSteamId = true;
                                parsedSteamId = property.Value.GetString();
                                break;
                        }
                    }
                    category = parsedCategory;
                    note = parsedNote;
                    steamId = parsedSteamId;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryReadLearnedRuleFields(string note, out bool hasFields, out string action, out string tokens)
        {
            hasFields = false;
            action = tokens = "";
            if (String.IsNullOrWhiteSpace(note)) return false;
            bool seenAction = false, seenToken = false, seenEvent = false;
            string parsedAction = "", parsedToken = "", parsedEvent = "";
            int position = 0;
            while (position < note.Length)
            {
                int nameStart = position;
                while (position < note.Length && note[position] != '=' && note[position] != ';'
                    && note[position] != '|' && note[position] != '\t') position++;
                if (position == note.Length) break;
                if (note[position] != '=')
                {
                    position++;
                    continue;
                }
                string name = note.Substring(nameStart, position - nameStart).Trim().ToLowerInvariant();
                bool recognized = name == "action" || name == "token" || name == "event";
                hasFields |= recognized;
                position++;
                while (position < note.Length && Char.IsWhiteSpace(note[position]) && note[position] != '\t') position++;
                string value;
                if (position < note.Length && (note[position] == '\'' || note[position] == '"'))
                {
                    char quote = note[position++];
                    var text = new StringBuilder();
                    bool closed = false;
                    while (position < note.Length)
                    {
                        char character = note[position++];
                        if (character == quote)
                        {
                            closed = true;
                            break;
                        }
                        if (character == '\\' && position < note.Length) character = note[position++];
                        text.Append(character);
                    }
                    while (position < note.Length && Char.IsWhiteSpace(note[position]) && note[position] != '\t') position++;
                    if (!closed || (position < note.Length && note[position] != ';' && note[position] != '|' && note[position] != '\t'))
                    {
                        // Broken quotes must not expose their contents to prose matching.
                        hasFields = true;
                        return false;
                    }
                    value = text.ToString().Trim();
                }
                else
                {
                    int valueStart = position;
                    while (position < note.Length && note[position] != ';' && note[position] != '|' && note[position] != '\t') position++;
                    value = note.Substring(valueStart, position - valueStart).Trim();
                }
                switch (name)
                {
                    case "action":
                        if (seenAction) return false;
                        seenAction = true;
                        parsedAction = value;
                        break;
                    case "token":
                        if (seenToken) return false;
                        seenToken = true;
                        parsedToken = value;
                        break;
                    case "event":
                        if (seenEvent) return false;
                        seenEvent = true;
                        parsedEvent = value;
                        break;
                }
                if (position < note.Length) position++;
            }
            switch (parsedAction.ToLowerInvariant())
            {
                case "false_positive":
                case "falsepositive":
                case "false positive":
                case "suppress":
                    parsedAction = "false_positive";
                    break;
                case "allow_event":
                case "allowevent":
                case "allow event":
                    parsedAction = "allow_event";
                    break;
                case "confirmed":
                case "confirmed_cheat":
                case "confirmed cheat":
                case "cheat":
                    parsedAction = "confirmed_cheat";
                    break;
                default:
                    return false;
            }
            string parsedTokens = String.IsNullOrWhiteSpace(parsedToken) ? parsedEvent : parsedToken;
            if (String.IsNullOrWhiteSpace(parsedTokens)) return false;
            action = parsedAction;
            tokens = parsedTokens;
            return true;
        }
    }
}
