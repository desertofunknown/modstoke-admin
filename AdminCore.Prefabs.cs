using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private readonly Dictionary<int, string> _boltPrefabNameById = new Dictionary<int, string>();
        private readonly Dictionary<int, object> _boltPrefabValues = new Dictionary<int, object>();
        private readonly Dictionary<int, int> _prefabItemIds = new Dictionary<int, int>();
        private readonly Dictionary<int, DateTime> _prefabItemRetryUtc = new Dictionary<int, DateTime>();
        private bool _boltPrefabIndexBuilt;
        private DateTime _nextBoltPrefabRetryUtc;
        private MethodInfo _pickupComponentsMethod;

        private void ResetPrefabCatalog()
        {
            _boltPrefabNameById.Clear();
            _boltPrefabValues.Clear();
            _prefabItemIds.Clear();
            _prefabItemRetryUtc.Clear();
            _boltPrefabIndexBuilt = false;
            _nextBoltPrefabRetryUtc = DateTime.MinValue;
        }

        private static bool TryExtractPrefabId(object value, out int prefabId)
        {
            prefabId = 0;
            if (value is int number)
            {
                prefabId = number;
                return number > 0;
            }
            Type prefabType = ReflectionTools.FindType("Bolt.PrefabId");
            if (value == null || prefabType == null || value.GetType() != prefabType) return false;
            object id = ReflectionTools.GetMemberValue(value, "Value");
            if (!(id is int nativeId) || nativeId <= 0) return false;
            prefabId = nativeId;
            return true;
        }

        private static bool TryReadBoltPrefabId(string memberName, out int prefabId)
        {
            prefabId = 0;
            Type type = ReflectionTools.FindType("BoltPrefabs");
            Type prefabType = ReflectionTools.FindType("Bolt.PrefabId");
            if (type == null || prefabType == null) return false;
            try
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static);
                return property != null && property.PropertyType == prefabType && property.CanRead
                    && TryExtractPrefabId(property.GetValue(null), out prefabId);
            }
            catch { return false; }
        }

        private string GetBoltPrefabName(int prefabId)
        {
            if (prefabId <= 0) return "";
            EnsureBoltPrefabIndex();
            string name;
            return _boltPrefabNameById.TryGetValue(prefabId, out name) ? name : "";
        }

        private void EnsureBoltPrefabIndex()
        {
            if (_boltPrefabIndexBuilt || DateTime.UtcNow < _nextBoltPrefabRetryUtc) return;
            _nextBoltPrefabRetryUtc = DateTime.UtcNow.AddSeconds(2);
            Type type = ReflectionTools.FindType("BoltPrefabs");
            Type prefabType = ReflectionTools.FindType("Bolt.PrefabId");
            if (type == null || prefabType == null) return;

            Dictionary<int, string> names = new Dictionary<int, string>();
            Dictionary<int, object> values = new Dictionary<int, object>();
            bool incomplete = false;
            try
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!property.CanRead || property.PropertyType != prefabType || property.GetIndexParameters().Length != 0) continue;
                    object value;
                    int id;
                    try { value = property.GetValue(null); }
                    catch { incomplete = true; continue; }
                    if (!TryExtractPrefabId(value, out id)) { incomplete = true; continue; }
                    string previous;
                    names[id] = names.TryGetValue(id, out previous) ? previous + "/" + property.Name : property.Name;
                    values[id] = value;
                }
            }
            catch { return; }

            _boltPrefabNameById.Clear();
            _boltPrefabValues.Clear();
            foreach (KeyValuePair<int, string> entry in names) _boltPrefabNameById.Add(entry.Key, entry.Value);
            foreach (KeyValuePair<int, object> entry in values) _boltPrefabValues.Add(entry.Key, entry.Value);
            _boltPrefabIndexBuilt = names.Count > 0 && !incomplete;
        }

        private int MapKnownPrefabIdToItemId(int prefabId)
        {
            if (prefabId <= 0 || !_worldReady) return 0;
            int cached;
            if (_prefabItemIds.TryGetValue(prefabId, out cached)) return cached;
            EnsureBoltPrefabIndex();
            object prefabValue;
            if (!_boltPrefabValues.TryGetValue(prefabId, out prefabValue)) return 0;
            DateTime retry;
            if (_prefabItemRetryUtc.TryGetValue(prefabId, out retry) && DateTime.UtcNow < retry) return 0;
            _prefabItemRetryUtc[prefabId] = DateTime.UtcNow.AddSeconds(5);

            Type registry = ReflectionTools.FindType("Bolt.PrefabDatabase");
            object result;
            if (!InvokeMethod(null, registry, "Find", new object[] { prefabValue }, true, out result)) return 0;
            GameObject prefab = result as GameObject;
            if (prefab == null) return 0;

            int itemId;
            if (!TryReadPickupItem(prefab, out itemId)) return 0;
            Type database = ReflectionTools.FindType("Sons.Items.Core.ItemDatabaseManager");
            object definition;
            if (!InvokeMethod(null, database, "ItemById", new object[] { itemId }, true, out definition)
                || !(definition is UnityEngine.Object nativeDefinition) || nativeDefinition == null
                || !(ReflectionTools.GetMemberValue(definition, "Id") is int actualId) || actualId != itemId) return 0;

            _prefabItemIds[prefabId] = itemId;
            _prefabItemRetryUtc.Remove(prefabId);
            return itemId;
        }

        private bool TryReadPickupItem(GameObject root, out int itemId)
        {
            itemId = 0;
            if (root == null) return false;
            Type pickupType = ReflectionTools.FindType("Sons.Gameplay.PickUp");
            if (pickupType == null) return false;
            try
            {
                if (_pickupComponentsMethod == null)
                {
                    foreach (MethodInfo method in typeof(GameObject).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        if (method.Name == "GetComponentsInChildren" && method.IsGenericMethodDefinition
                            && method.GetGenericArguments().Length == 1 && parameters.Length == 1
                            && parameters[0].ParameterType == typeof(bool))
                        {
                            _pickupComponentsMethod = method.MakeGenericMethod(pickupType);
                            break;
                        }
                    }
                }
                IEnumerable components = _pickupComponentsMethod?.Invoke(root, new object[] { true }) as IEnumerable;
                if (components == null) return false;
                foreach (object component in components)
                {
                    if (!(component is UnityEngine.Object nativeComponent) || nativeComponent == null
                        || !(ReflectionTools.GetMemberValue(component, "_itemId") is int id) || id <= 0) return false;
                    // A hierarchy containing different pickup items cannot identify one event item.
                    if (itemId != 0 && itemId != id) return false;
                    itemId = id;
                }
                return itemId > 0;
            }
            catch { itemId = 0; return false; }
        }
    }
}
