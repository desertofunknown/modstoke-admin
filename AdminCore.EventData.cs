using System;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private MethodInfo _storageItemTokenCast;

        private static bool IsStorageItemEvent(string eventName)
        {
            return eventName == "ItemStorageAddItemEvent" || eventName == "ItemStorageRemoveItemEvent";
        }

        private int ExtractEventItemCount(object evnt)
        {
            if (evnt == null) return 0;

            string memberName;
            switch (evnt.GetType().Name)
            {
                case "DropItem":
                    memberName = "ItemCount";
                    break;
                case "PlayerAddItem":
                case "PlayerGiveActorItem":
                case "PlayerRequestActorItem":
                case "StructureCraftingNodeAddIngredient":
                    memberName = "Amount";
                    break;
                case "ItemStorageAddItemEvent":
                case "ItemStorageRemoveItemEvent":
                    memberName = "Count";
                    break;
                default:
                    return 0;
            }

            object value = ReflectionTools.GetMemberValue(evnt, memberName);
            return value is int count && count > 0 ? count : 0;
        }

        private static string FormatEventItemCount(int count)
        {
            return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "unknown";
        }

        private int ExtractEventItemId(object evnt)
        {
            if (evnt == null) return 0;

            string memberName;
            switch (evnt.GetType().Name)
            {
                case "SpawnLootItemEvent":
                case "PlayerAddItem":
                case "PlayerRemoveItem":
                case "ItemRemoveFromPlayer":
                case "PlayerGiveActorItem":
                case "PlayerRequestActorItem":
                case "AttachItemToZiplineEvent":
                case "PlaceTrophy":
                case "DestroyPickUp":
                    memberName = "ItemId";
                    break;
                case "ItemStorageAddItemEvent":
                case "ItemStorageRemoveItemEvent":
                    return ReadStorageInstanceItemId(evnt);
                case "FreeFormPlaceElement":
                    memberName = "ItemID";
                    break;
                case "PickupRequest":
                    return ReadPickupRequestItemId(evnt);
                case "DropItem":
                case "PickupGlider":
                    int prefabId;
                    return TryExtractEventPrefabId(evnt, out prefabId)
                        ? MapKnownPrefabIdToItemId(prefabId) : 0;
                default:
                    return 0;
            }

            object value = ReflectionTools.GetMemberValue(evnt, memberName);
            return value is int itemId && itemId > 0 ? itemId : 0;
        }

        private int ReadStorageInstanceItemId(object evnt)
        {
            // Storage consumes the instance token. The separate ItemId field is not its item identity.
            if (!(ReflectionTools.GetMemberValue(evnt, "ItemInstance") is Il2CppObjectBase token)) return 0;
            try
            {
                if (_storageItemTokenCast == null)
                {
                    Type tokenType = ReflectionTools.FindType("Sons.Inventory.ItemInstanceBoltDataToken");
                    MethodInfo cast = typeof(Il2CppObjectBase).GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                    if (tokenType == null || cast == null || !cast.IsGenericMethodDefinition) return 0;
                    _storageItemTokenCast = cast.MakeGenericMethod(tokenType);
                }
                object instanceToken = _storageItemTokenCast.Invoke(token, null);
                object instance = ReflectionTools.GetMemberValue(instanceToken, "ItemInstance");
                object value = ReflectionTools.GetMemberValue(instance, "_itemID");
                return value is int itemId && itemId > 0 ? itemId : 0;
            }
            catch { return 0; }
        }

        private string DescribeStorageItemEvent(object evnt, int itemId, int count)
        {
            object rawCount = ReflectionTools.GetMemberValue(evnt, "Count");
            object separateId = ReflectionTools.GetMemberValue(evnt, "ItemId");
            return DescribeStorageItemEvent(evnt.GetType().Name, itemId, count,
                rawCount is int requested ? requested : (int?)null,
                separateId is int separate ? separate : (int?)null);
        }

        private string DescribeStorageItemEvent(string eventName, int itemId, int count, int? requestedCount, int? separateItemId)
        {
            string name = itemId > 0 ? ResolveItemNameById(itemId) : "";
            // Keep unrelated payload and target names out of the text used to match item policies.
            return eventName
                + ", instance ID=" + (itemId > 0 ? itemId.ToString(CultureInfo.InvariantCulture) : "unresolved")
                + ", requestedCount=" + (requestedCount.HasValue ? requestedCount.Value.ToString(CultureInfo.InvariantCulture) : "unreadable")
                + ", quantity=" + FormatEventItemCount(count)
                + (String.IsNullOrEmpty(name) ? "" : ", item=" + name)
                + ", identity=instance token"
                + ", unused field=" + (separateItemId.HasValue ? separateItemId.Value.ToString(CultureInfo.InvariantCulture) : "unreadable");
        }

        private int ReadPickupRequestItemId(object evnt)
        {
            object target = ReflectionTools.GetMemberValue(evnt, "Item");
            if (!IsLiveEventEntity(target) || !(target is Component component) || component == null) return 0;
            try
            {
                int prefabId;
                if (!TryExtractPrefabId(ReflectionTools.GetMemberValue(target, "prefabId"), out prefabId)) return 0;
                int catalogItemId = MapKnownPrefabIdToItemId(prefabId);
                if (catalogItemId <= 0) return 0;
                int liveItemId;
                return TryReadPickupItem(component.gameObject, out liveItemId) && liveItemId == catalogItemId
                    ? liveItemId : 0;
            }
            catch { return 0; }
        }

        private bool TryExtractEventPrefabId(object evnt, out int prefabId)
        {
            prefabId = 0;
            if (evnt == null) return false;

            string memberName;
            switch (evnt.GetType().Name)
            {
                case "DropItem":
                case "PlaceConstruction":
                case "PlaceDryingFood":
                case "StructureCraftingNodeCreate":
                    memberName = "PrefabId";
                    break;
                case "PlaceFoundationEx":
                    memberName = "Prefab";
                    break;
                case "PickupGlider":
                    memberName = "gliderId";
                    break;
                default:
                    return false;
            }

            return TryExtractPrefabId(ReflectionTools.GetMemberValue(evnt, memberName), out prefabId);
        }
    }
}
