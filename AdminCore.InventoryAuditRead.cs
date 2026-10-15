using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private bool TryCollectLocalInventoryAuditItems(out Dictionary<int, int> items, out string failure)
        {
            items = null;
            failure = "Local inventory is not ready";
            try
            {
                object inventory = GetLocalPlayerInventory();
                if (!(inventory is UnityEngine.Object liveInventory) || liveInventory == null
                    || !(inventory is Il2CppObjectBase nativeInventory) || nativeInventory.Pointer == IntPtr.Zero)
                    return false;

                IntPtr inventoryPointer = nativeInventory.Pointer;
                object allItems;
                if (!InvokeMethod(inventory, inventory.GetType(), "GetAllItems", new object[0], false, out allItems)
                    || !(allItems is Il2CppObjectBase nativeItems))
                {
                    failure = "Could not read inventory entries";
                    return false;
                }

                Type mapType = allItems.GetType();
                Type[] mapArguments = mapType.GetGenericArguments();
                if (!mapType.IsGenericType
                    || mapType.GetGenericTypeDefinition() != typeof(Il2CppSystem.Collections.Generic.IReadOnlyDictionary<,>)
                    || mapArguments.Length != 2 || mapArguments[0] != typeof(int))
                {
                    failure = "Inventory entry type is unavailable";
                    return false;
                }
                Type pairType = typeof(Il2CppSystem.Collections.Generic.KeyValuePair<,>).MakeGenericType(mapArguments);
                object countView = CastInventoryAuditInterface(nativeItems,
                    typeof(Il2CppSystem.Collections.Generic.IReadOnlyCollection<>).MakeGenericType(pairType));
                object enumerableView = CastInventoryAuditInterface(nativeItems,
                    typeof(Il2CppSystem.Collections.Generic.IEnumerable<>).MakeGenericType(pairType));
                if (countView == null || enumerableView == null)
                {
                    failure = "Inventory collection interfaces are unavailable";
                    return false;
                }

                int beforeCount;
                if (!TryReadInventoryAuditEntryCount(countView, out beforeCount))
                {
                    failure = "Could not read inventory entry count";
                    return false;
                }

                object enumerator;
                if (!InvokeMethod(enumerableView, enumerableView.GetType(), "GetEnumerator", new object[0], false, out enumerator)
                    || !(enumerator is Il2CppObjectBase nativeEnumerator))
                {
                    failure = "Could not enumerate inventory entries";
                    return false;
                }

                HashSet<int> readIds = new HashSet<int>();
                int visitedCount = 0;
                try
                {
                    Il2CppSystem.Collections.IEnumerator movementView = nativeEnumerator.TryCast<Il2CppSystem.Collections.IEnumerator>();
                    if (movementView == null)
                    {
                        failure = "Inventory iterator interface is unavailable";
                        return false;
                    }
                    while (true)
                    {
                        object moved;
                        if (!InvokeMethod(movementView, movementView.GetType(), "MoveNext", new object[0], false, out moved)
                            || !(moved is bool))
                        {
                            failure = "Could not finish reading inventory entries";
                            return false;
                        }
                        if (!(bool)moved)
                            break;

                        if (visitedCount >= beforeCount)
                        {
                            failure = "Inventory entries changed while reading";
                            return false;
                        }
                        object current;
                        object key;
                        if (!InvokeMethod(enumerator, enumerator.GetType(), "get_Current", new object[0], false, out current)
                            || current == null
                            || !InvokeMethod(current, current.GetType(), "get_Key", new object[0], false, out key)
                            || !(key is int itemId) || itemId <= 0 || !readIds.Add(itemId))
                        {
                            failure = "Could not read a unique inventory item ID";
                            return false;
                        }
                        visitedCount++;
                    }
                }
                finally
                {
                    Il2CppSystem.IDisposable disposable = nativeEnumerator.TryCast<Il2CppSystem.IDisposable>();
                    if (disposable != null)
                        disposable.Dispose();
                }

                int afterCount;
                if (visitedCount != beforeCount
                    || !TryReadInventoryAuditEntryCount(countView, out afterCount) || afterCount != beforeCount)
                {
                    failure = "Inventory entries changed while reading";
                    return false;
                }

                List<int> watchedIds = GetAuditItemIds();
                for (int i = 0; i < watchedIds.Count; i++)
                {
                    if (watchedIds[i] <= 0)
                    {
                        failure = "An inventory watch item ID is invalid";
                        return false;
                    }
                    readIds.Add(watchedIds[i]);
                }

                Dictionary<int, int> collected = new Dictionary<int, int>();
                foreach (int itemId in readIds)
                {
                    int count;
#if NET11_0_OR_GREATER
                    string status;
                    if (!OpenACAI.Sons.OpenACAISonsInventory.TryGetItemCount(itemId, out count, out status, true, false))
                    {
                        failure = "Could not read inventory item count";
                        return false;
                    }
#else
                    object amount;
                    if (!InvokeMethod(inventory, inventory.GetType(), "AmountOf", new object[] { itemId, true, false }, false, out amount)
                        || !(amount is int))
                    {
                        failure = "Could not read inventory item count";
                        return false;
                    }
                    count = (int)amount;
#endif
                    if (count < 0)
                    {
                        failure = "Inventory item count is invalid";
                        return false;
                    }
                    if (count > 0)
                        collected.Add(itemId, count);
                }

                if (!TryReadInventoryAuditEntryCount(countView, out afterCount) || afterCount != beforeCount)
                {
                    failure = "Inventory entries changed while reading counts";
                    return false;
                }

                object currentInventory = GetLocalPlayerInventory();
                if (liveInventory == null
                    || !(currentInventory is UnityEngine.Object currentLiveInventory) || currentLiveInventory == null
                    || !(currentInventory is Il2CppObjectBase currentNativeInventory)
                    || currentNativeInventory.Pointer != inventoryPointer)
                {
                    failure = "Local inventory changed while reading";
                    return false;
                }

                items = collected;
                failure = "";
                return true;
            }
            catch (Exception)
            {
                failure = "Could not finish reading local inventory";
                return false;
            }
        }

        private static object CastInventoryAuditInterface(Il2CppObjectBase instance, Type interfaceType)
        {
            MethodInfo cast = typeof(Il2CppObjectBase).GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (cast == null || !cast.IsGenericMethodDefinition)
                return null;
            return cast.MakeGenericMethod(interfaceType).Invoke(instance, null);
        }

        private bool TryReadInventoryAuditEntryCount(object entries, out int count)
        {
            count = 0;
            object result;
            if (!InvokeMethod(entries, entries.GetType(), "get_Count", new object[0], false, out result)
                || !(result is int) || (int)result < 0)
                return false;
            count = (int)result;
            return true;
        }
    }
}
