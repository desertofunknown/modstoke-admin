#if NET11_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private static bool TryDescribeLoaderConstructionTarget(string eventName, string targetField,
            ulong targetId, int? amount, out string descriptor, out string detail)
        {
            descriptor = "";
            detail = "Construction target description is unavailable";
            try
            {
                // Saved rules use the native target text. A numeric ID would change their matches.
                Type networkIdType = ReflectionTools.FindType("Bolt.NetworkId");
                Type boltNetwork = ReflectionTools.FindType("BoltNetwork");
                if (networkIdType == null || boltNetwork == null) return false;
                var constructor = networkIdType.GetConstructor(new[] { typeof(ulong) });
                var findEntity = boltNetwork.GetMethod("FindEntity", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { networkIdType }, null);
                if (constructor == null || findEntity == null) return false;
                object networkId = constructor.Invoke(new object[] { targetId });
                object target = findEntity.Invoke(null, new[] { networkId });
                var parts = new List<string>();
                if (amount.HasValue) AppendEventField(parts, "Amount", amount.Value);
                AppendEventField(parts, targetField, target);
                descriptor = parts.Count == 0 ? eventName : eventName + " " + String.Join(", ", parts);
                return true;
            }
            catch (Exception ex)
            {
                detail += " (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }
    }
}
#endif
