using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private const int GuardEvidenceCapacity = 256;
        private readonly List<GuardEvidence> _guardEvidence = new List<GuardEvidence>();
        private int _unattributedGuardEvents;
        private DateTime _lastUnattributedGuardUtc = DateTime.MinValue;
        private string _lastUnattributedGuardEvent = "";

        private sealed class GuardEvidence
        {
            public string SteamId;
            public string Category;
            public string Source;
            public string Observation;
            public string Policy;
            public string Decision;
            public DateTime Utc;
        }

        private void RecordGuardEvidence(string steamId, string category, string source, string observation, string policy, string decision)
        {
            steamId = CleanSteamId(steamId);
            if (!LooksLikeSteamId(steamId)) return;
            if (_guardEvidence.Count >= GuardEvidenceCapacity) _guardEvidence.RemoveAt(0);
            _guardEvidence.Add(new GuardEvidence
            {
                SteamId = steamId,
                Category = category,
                Source = CleanConfigText(source, 96),
                Observation = CleanConfigText(observation, 420),
                Policy = CleanConfigText(policy, 640),
                Decision = CleanConfigText(decision, 320),
                Utc = DateTime.UtcNow
            });
        }

        private bool TryResolveGuardSender(object evnt, out string steamId, out object entity, out object connection)
        {
            // RaisedBy is transport metadata; target and owner fields belong to the event payload.
            connection = UnwrapNullable(ReflectionTools.GetMemberValue(evnt, "RaisedBy")
                ?? ReflectionTools.InvokeNoArgMethod(evnt, "get_RaisedBy"));
            steamId = CleanSteamId(GetSteamIdFromConnection(connection));
            entity = LooksLikeSteamId(steamId) ? GetEntityFromConnection(connection) : null;
            return LooksLikeSteamId(steamId);
        }

        private string CaptureItemGuardPolicy()
        {
            return "Per event " + (_strictItemEventGuard ? _itemEventCountThreshold.ToString(CultureInfo.InvariantCulture) : "off")
                + "; burst " + (_itemBurstGuard ? _itemBurstCountThreshold.ToString(CultureInfo.InvariantCulture)
                    + " / " + _itemBurstWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s" : "off")
                + "; salvage " + Math.Max(_constructionSalvageBurstCountThreshold, (long)_itemBurstCountThreshold * 3).ToString(CultureInfo.InvariantCulture)
                + " / " + _itemBurstWindowSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s"
                + "; early items " + (_earlyItemGuard ? _earlyItemGuardMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m" : "off")
                + "; high gear " + (_highValueItemGuard ? _highValueGuardMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m" : "off")
                + "; artifact rule " + (_artifactItemGuard ? "on" : "off")
                + "; story locations " + (_storyItemLocationGuard ? "on" : "off")
                + "; age basis: recorded first sighting";
        }

        private string CaptureEventGuardPolicy(string eventName, object evnt)
        {
            string policy = CaptureItemGuardPolicy();
            policy += "; native validation " + (_nativeServerValidationGuard ? "on" : "off");
            if (_nativeServerValidationGuard && eventName == "StructureHitEvent")
            {
                float radius;
                TryReadFloatMember(evnt, "Radius", out radius);
                return CaptureStructureHitGuardPolicy(radius);
            }
            else if (_nativeServerValidationGuard)
            {
                policy += "; native distance setting " + GetServerValidationDistance(eventName).ToString(CultureInfo.InvariantCulture) + "m";
            }
            return policy;
        }

        private string CaptureStructureHitGuardPolicy(float radius)
        {
            return CaptureItemGuardPolicy() + "; native validation on"
                + "; structure damage limit 250; radius limit 50; native range "
                + StructureHitRange(radius).ToString("0.#", CultureInfo.InvariantCulture) + "m";
        }

        private static string GuardEventCategory(string eventName)
        {
            if (eventName.IndexOf("Structure", StringComparison.OrdinalIgnoreCase) >= 0
                || eventName.StartsWith("Place", StringComparison.OrdinalIgnoreCase) || eventName == "FreeFormPlaceElement")
                return "Construction";
            return IsItemBearingServerEvent(eventName) ? "Items" : "Server";
        }

        private string CaptureMovementPolicy(MovementSignalState state, int strikeThreshold)
        {
            return "Mode " + state.LastContext
                + "; source " + state.LastContextEvidence
                + "; interval " + state.LastEvaluationSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "s"
                + "; speed " + state.LastContextHorizontalLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m/s"
                + "; vertical " + state.LastContextVerticalLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m/s"
                + "; teleport " + state.LastContextTeleportLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m"
                + "; strikes " + strikeThreshold.ToString(CultureInfo.InvariantCulture)
                + "; speed check " + (_speedHackGuard ? "on" : "off")
                + "; teleport check " + (_teleportGuard ? "on" : "off")
                + "; vertical/world bounds " + (_noclipGuard ? _noclipFloorY.ToString("0.#", CultureInfo.InvariantCulture) + " to 1500m" : "off");
        }
    }
}
