using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private delegate bool ItemEventPositionReader(out Vector3 position, out string source);

        private bool ShouldBlockItemPolicy(string eventName, string steamId, PlayerEntry entry, int itemId, int count,
            string itemDescription, bool negativeStorageCount, ItemEventPositionReader readPosition, out string reason)
        {
            reason = "";
            if (negativeStorageCount)
            {
                reason = "Negative storage transfer count: " + itemDescription;
                return true;
            }
            bool serverApproved = IsServerApprovedItem(steamId, itemId);
            bool allowedCommonTransportItem = IsAllowedCommonTransportItem(itemId, itemDescription)
                || String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase);
            if (!allowedCommonTransportItem
                && !serverApproved
                && _storyItemLocationGuard
                && IsPositiveItemAcquisitionEvent(eventName)
                && TryStoryItemLocationViolation(itemId, itemDescription, readPosition, out reason))
            {
                return true;
            }
            if (!allowedCommonTransportItem
                && !serverApproved
                && _highValueItemGuard
                && String.Equals(eventName, "PickupGlider", StringComparison.OrdinalIgnoreCase)
                && GetPlayerSessionMinutes(steamId, entry) <= _highValueGuardMinutes)
            {
                reason = "High-value glider pickup too early: " + itemDescription;
                return true;
            }
            if (!allowedCommonTransportItem && count >= Math.Max(2, _itemEventCountThreshold))
            {
                reason = "Item amount over limit: " + itemDescription;
                return true;
            }

            float sessionMinutes = GetPlayerSessionMinutes(steamId, entry);
            if (!allowedCommonTransportItem && !serverApproved && _artifactItemGuard && ItemMatchesTokens(itemId, itemDescription, _artifactItemTokens))
            {
                reason = "Artifact item blocked: " + itemDescription
                    + " after " + sessionMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m online";
                return true;
            }

            if (!allowedCommonTransportItem && !serverApproved && _highValueItemGuard && ItemMatchesTokens(itemId, itemDescription, _highValueItemTokens) && sessionMinutes <= _highValueGuardMinutes)
            {
                reason = "High-value gear too early: " + itemDescription
                    + " after " + sessionMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m online";
                return true;
            }

            if (!allowedCommonTransportItem && !serverApproved && _earlyItemGuard && ItemMatchesTokens(itemId, itemDescription, _restrictedEarlyItemTokens) && sessionMinutes <= _earlyItemGuardMinutes)
            {
                reason = "Restricted early item: " + itemDescription
                    + " after " + sessionMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m online";
                return true;
            }

            if (!allowedCommonTransportItem && _itemBurstGuard && TrackItemBurst(steamId, itemId, count, eventName, itemDescription, out reason))
            {
                reason = "Item burst over limit: " + reason;
                return true;
            }

            return false;
        }

        private bool TryStoryItemLocationViolation(int itemId, string itemDescription, ItemEventPositionReader readPosition, out string reason)
        {
            reason = "";
            if (_storyItemLocationRules.Count == 0)
            {
                return false;
            }

            List<GuardedItemLocationRule> matching = GetStoryItemLocationRules(itemId, itemDescription);
            if (matching.Count == 0)
            {
                return false;
            }

            Vector3 position;
            string source;
            if (!readPosition(out position, out source))
            {
                AppendEventLog("GUARD", "Story item location could not be verified for " + Shorten(itemDescription, 72));
                return false;
            }

            GuardedItemLocationRule nearest = null;
            float nearestDistance = Single.MaxValue;
            for (int i = 0; i < matching.Count; i++)
            {
                GuardedItemLocationRule rule = matching[i];
                float distance = Vector3.Distance(position, rule.Position);
                if (distance <= rule.Radius)
                {
                    return false;
                }
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = rule;
                }
            }

            string label = nearest == null ? "configured story location" : nearest.Label;
            reason = "Story item picked up away from " + label
                + ": " + itemDescription
                + " at " + FormatVector(position)
                + " via " + source
                + ", nearest " + nearestDistance.ToString("0.#", CultureInfo.InvariantCulture)
                + "m outside allowed radius";
            return true;
        }
    }
}
