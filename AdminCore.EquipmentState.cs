using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
#if NET11_0_OR_GREATER
using OpenACAI.Sons;
#endif

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private sealed class EquipmentSnapshot
        {
            public readonly SortedDictionary<int, string> Items = new SortedDictionary<int, string>();
            public readonly SortedSet<int> Shortcuts = new SortedSet<int>();
            public string ShortcutStatus = "unavailable";
        }

        private Type _equipmentIntegerArrayType;
        private MethodInfo _equipmentArrayLengthGetter;
        private MethodInfo _equipmentArrayItemGetter;
        private MethodInfo _equipmentItemById;

#if NET11_0_OR_GREATER
        private bool TryReadServerEquipmentSnapshot(PlayerEntry entry, out EquipmentSnapshot snapshot,
            out OpenACAISonsPlayerStateSnapshot state, out string status)
        {
            snapshot = null;
            state = null;
            string steamId = CleanSteamId(entry?.SteamId);
            status = "Unavailable: a resolved remote Steam64 ID is required";
            if (!LooksLikeSteamId(steamId)
                || !UInt64.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)) return false;
            if (!TryResolveServerIdentity(parsed, out var identity, out string failure))
            {
                status = "Unavailable: " + failure;
                return false;
            }
            if (!OpenACAISonsServerWorld.TryReadPlayerState(identity.Session, out state, out var readStatus))
            {
                status = "SDK player state unavailable (" + readStatus + ")";
                return false;
            }

            EquipmentSnapshot candidate = new EquipmentSnapshot();
            AddEquipmentSource(candidate, state.HeldOnlyItemId, "HeldOnlyItemId");
            AddEquipmentSource(candidate, state.RightHandHeldItemId, "RightHandHeldItemId");
            foreach (int id in new SortedSet<int>(state.HeldItemIds)) AddEquipmentSource(candidate, id, "HeldItems");

            // Shortcuts are optional display data and never enter the held-item policy.
            object entity = entry.BoltEntity;
            candidate.ShortcutStatus = "unavailable: no matching current entity for shortcut reads";
            if (MatchesEquipmentShortcutEntity(entity, steamId, state.NetworkId))
            {
                ReadEquipmentShortcuts(GetNetworkedPlayerState(entry), candidate);
                if (!ReferenceEquals(entry.BoltEntity, entity)
                    || !MatchesEquipmentShortcutEntity(entity, steamId, state.NetworkId))
                {
                    candidate.Shortcuts.Clear();
                    candidate.ShortcutStatus = "unavailable: player entity changed during shortcut read";
                }
            }
            snapshot = candidate;
            status = "SDK held-item fields read successfully";
            return true;
        }

        private bool MatchesEquipmentShortcutEntity(object entity, string steamId, ulong networkId)
        {
            if (!(entity is UnityEngine.Object nativeEntity) || nativeEntity == null
                || !HasLiveEntityProof(entity, steamId)) return false;
            object id = ReflectionTools.GetMemberValue(entity, "networkId");
            return ReflectionTools.GetMemberValue(id, "PackedValue") is ulong packed && packed == networkId;
        }
#else
        private bool TryReadEquipmentSnapshot(object playerState, out EquipmentSnapshot snapshot, out string status)
        {
            snapshot = null;
            object heldOnly;
            object rightHand;
            object heldItems;
            if (!TryReadEquipmentMember(playerState, "HeldOnlyItemId", typeof(int), out heldOnly, out status)
                || !TryReadEquipmentMember(playerState, "RightHandHeldItemId", typeof(int), out rightHand, out status))
                return false;

            Type arrayType = _equipmentIntegerArrayType ?? ReflectionTools.FindType("Bolt.NetworkArray_Integer");
            if (arrayType == null)
            {
                status = "HeldItems: Bolt.NetworkArray_Integer unavailable";
                return false;
            }
            if (!TryReadEquipmentMember(playerState, "HeldItems", arrayType, out heldItems, out status)
                || !TryReadEquipmentArray(heldItems, arrayType, "HeldItems", out SortedSet<int> heldIds, out status))
                return false;

            EquipmentSnapshot candidate = new EquipmentSnapshot();
            AddEquipmentSource(candidate, (int)heldOnly, "HeldOnlyItemId");
            AddEquipmentSource(candidate, (int)rightHand, "RightHandHeldItemId");
            foreach (int id in heldIds) AddEquipmentSource(candidate, id, "HeldItems");

            ReadEquipmentShortcuts(playerState, candidate);
            snapshot = candidate;
            status = "Held-item fields read successfully";
            return true;
        }
#endif

        private void ReadEquipmentShortcuts(object playerState, EquipmentSnapshot candidate)
        {
            Type arrayType = _equipmentIntegerArrayType ?? ReflectionTools.FindType("Bolt.NetworkArray_Integer");
            if (arrayType == null)
            {
                candidate.ShortcutStatus = "unavailable: Bolt.NetworkArray_Integer unavailable";
                return;
            }
            object shortcuts;
            if (TryReadEquipmentMember(playerState, "QuickSelectItemIds", arrayType, out shortcuts, out string shortcutStatus)
                && TryReadEquipmentArray(shortcuts, arrayType, "QuickSelectItemIds", out SortedSet<int> shortcutIds, out shortcutStatus))
            {
                candidate.Shortcuts.UnionWith(shortcutIds);
                candidate.ShortcutStatus = shortcutIds.Count == 0 ? "No shortcut assignments" : "Readable shortcut assignments";
            }
            else candidate.ShortcutStatus = "unavailable: " + shortcutStatus;
        }

        private static void AddEquipmentSource(EquipmentSnapshot snapshot, int id, string source)
        {
            if (id <= 0) return;
            snapshot.Items[id] = snapshot.Items.TryGetValue(id, out string previous) ? previous + ", " + source : source;
        }

        private static bool TryReadEquipmentMember(object instance, string name, Type expectedType, out object value, out string status)
        {
            value = null;
            status = name + ": unavailable";
            if (instance == null) return false;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null)
                {
                    if (property.PropertyType != expectedType)
                    {
                        status = name + ": wrong type";
                        return false;
                    }
                    MethodInfo getter = property.GetGetMethod();
                    if (getter == null || property.GetIndexParameters().Length != 0) return false;
                    value = getter.Invoke(instance, null);
                }
                else
                {
                    FieldInfo field = type.GetField(name, flags);
                    if (field == null) return false;
                    if (field.FieldType != expectedType)
                    {
                        status = name + ": wrong type";
                        return false;
                    }
                    value = field.GetValue(instance);
                }
                if (value == null && expectedType != typeof(string)) return false;
                if (value != null && !expectedType.IsInstanceOfType(value))
                {
                    status = name + ": wrong runtime type";
                    return false;
                }
                status = "readable";
                return true;
            }
            catch (Exception ex)
            {
                value = null;
                status = name + ": read error (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private bool TryReadEquipmentArray(object array, Type arrayType, string source, out SortedSet<int> ids, out string status)
        {
            ids = null;
            status = source + ": unavailable";
            if (array == null) return false;
            if (!arrayType.IsInstanceOfType(array))
            {
                status = source + ": wrong runtime type";
                return false;
            }
            string member = "Length";
            try
            {
                if (_equipmentIntegerArrayType != arrayType)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                    PropertyInfo length = arrayType.GetProperty("Length", flags);
                    MethodInfo lengthGetter = length?.GetGetMethod();
                    MethodInfo itemGetter = arrayType.GetMethod("get_Item", flags, null, new Type[] { typeof(int) }, null);
                    if (length == null || length.PropertyType != typeof(int) || length.GetIndexParameters().Length != 0
                        || lengthGetter == null || itemGetter == null || itemGetter.ReturnType != typeof(int)
                        || itemGetter.ContainsGenericParameters || itemGetter.GetParameters().Length != 1
                        || itemGetter.GetParameters()[0].ParameterType != typeof(int))
                    {
                        status = source + ": exact Length/get_Item readers unavailable";
                        return false;
                    }
                    _equipmentArrayLengthGetter = lengthGetter;
                    _equipmentArrayItemGetter = itemGetter;
                    _equipmentIntegerArrayType = arrayType;
                }
                if (!(_equipmentArrayLengthGetter.Invoke(array, null) is int count) || count < 0)
                {
                    status = source + ": invalid Length";
                    return false;
                }
                SortedSet<int> candidate = new SortedSet<int>();
                object[] arguments = new object[1];
                for (int index = 0; index < count; index++)
                {
                    member = "get_Item(" + index + ")";
                    arguments[0] = index;
                    if (!(_equipmentArrayItemGetter.Invoke(array, arguments) is int id))
                    {
                        status = source + ": " + member + " wrong type";
                        return false;
                    }
                    if (id > 0) candidate.Add(id);
                }
                ids = candidate;
                status = "readable";
                return true;
            }
            catch (Exception ex)
            {
                status = source + ": " + member + " read error (" + ex.GetBaseException().GetType().Name + ")";
                return false;
            }
        }

        private bool TryDescribeEquipmentItem(int id, out string description)
        {
            description = null;
            if (id <= 0) return false;
            try
            {
                if (_equipmentItemById == null)
                {
                    Type database = ReflectionTools.FindType("Sons.Items.Core.ItemDatabaseManager");
                    MethodInfo reader = database?.GetMethod("ItemById", BindingFlags.Public | BindingFlags.Static,
                        null, new Type[] { typeof(int) }, null);
                    if (reader == null || reader.ContainsGenericParameters || reader.GetParameters().Length != 1
                        || reader.GetParameters()[0].ParameterType != typeof(int)) return false;
                    _equipmentItemById = reader;
                }
                object item = _equipmentItemById.Invoke(null, new object[] { id });
                if (!(item is ScriptableObject nativeItem) || nativeItem == null
                    || !TryReadEquipmentMember(item, "Id", typeof(int), out object actualId, out _)
                    || (int)actualId != id) return false;
                if (!TryReadEquipmentMember(item, "Name", typeof(string), out object name, out _)) name = null;
                description = "itemId=" + id.ToString(CultureInfo.InvariantCulture);
                if (!String.IsNullOrEmpty((string)name)) description += ", item=" + (string)name;
                return true;
            }
            catch { return false; }
        }
    }
}
