using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private List<ItemCatalogEntry> _itemCatalog = new List<ItemCatalogEntry>();
        private int _itemCatalogRevision;
        private string _itemCatalogStatus = "Join a world to load the item catalog";
        private bool _itemCatalogLoaded;
        private DateTime _nextItemCatalogRetryUtc = DateTime.MinValue;

        private void ResetItemCatalog()
        {
            ResetPrefabCatalog();
            _itemCatalog.Clear();
            _itemNameIdCache.Clear();
            _itemIdNameCache.Clear();
            _unresolvedItemIds.Clear();
            _itemCatalogRevision++;
            _itemCatalogLoaded = false;
            _nextItemCatalogRetryUtc = DateTime.MinValue;
            _itemCatalogStatus = "Join a world to load the item catalog";
        }

        private void EnsureItemCatalog()
        {
            if (!_worldReady || _itemCatalogLoaded || DateTime.UtcNow < _nextItemCatalogRetryUtc)
            {
                return;
            }
            RefreshItemCatalog(false);
        }

        private void RefreshItemCatalog(bool showAction)
        {
            if (showAction ? !IsLocalPlayerInWorld() : !_worldReady)
            {
                ResetItemCatalog();
                if (showAction)
                {
                    ActionFail(_itemCatalogStatus);
                }
                return;
            }
            _nextItemCatalogRetryUtc = DateTime.UtcNow.AddSeconds(2);

            List<ItemCatalogEntry> catalog = new List<ItemCatalogEntry>();
            HashSet<int> itemIds = new HashSet<int>();
            int skipped = 0;
            object items = GetItemDatabaseItems();
            object countValue = items == null ? null : ReflectionTools.GetMemberValue(items, "Count");
            bool available = countValue is int && (int)countValue >= 0;
            if (available)
            {
                int count = (int)countValue;
                for (int i = 0; i < count; i++)
                {
                    object item;
                    if (!InvokeMethod(items, items.GetType(), "get_Item", new object[] { i }, false, out item)
                        || !AddItemCatalogEntry(item, catalog, itemIds))
                    {
                        skipped++;
                    }
                }
            }

            catalog.Sort(delegate (ItemCatalogEntry a, ItemCatalogEntry b)
            {
                int category = String.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                if (category != 0) return category;
                int name = String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : a.Id.CompareTo(b.Id);
            });
            _itemCatalog = catalog;
            _itemCatalogRevision++;
            _itemCatalogLoaded = catalog.Count > 0;
            _itemCatalogStatus = !available ? "Item catalog unavailable; retrying"
                : !_itemCatalogLoaded ? "Item catalog empty; retrying"
                : "Catalog: " + catalog.Count.ToString(CultureInfo.InvariantCulture) + " items";
            if (skipped > 0) _itemCatalogStatus += "; skipped " + skipped.ToString(CultureInfo.InvariantCulture) + " invalid, unreadable or duplicate entries";

            _itemNameIdCache.Clear();
            _itemIdNameCache.Clear();
            _unresolvedItemIds.Clear();
            HashSet<string> duplicateNames = new HashSet<string>(_itemNameIdCache.Comparer);
            for (int i = 0; i < catalog.Count; i++)
            {
                ItemCatalogEntry entry = catalog[i];
                _itemIdNameCache.Add(entry.Id, entry.Name);
                if (duplicateNames.Contains(entry.Name))
                {
                    continue;
                }
                if (_itemNameIdCache.ContainsKey(entry.Name))
                {
                    _itemNameIdCache.Remove(entry.Name);
                    duplicateNames.Add(entry.Name);
                }
                else
                {
                    _itemNameIdCache.Add(entry.Name, entry.Id);
                }
            }
            if (showAction)
            {
                ActionResult(_itemCatalogLoaded, _itemCatalogStatus, _itemCatalogStatus);
            }
        }

        private object GetItemDatabaseItems()
        {
            Type type = ReflectionTools.FindType("Sons.Items.Core.ItemDatabaseManager");
            return type == null ? null : ReflectionTools.GetStaticMemberValue(type, "Items");
        }

        private bool AddItemCatalogEntry(object item, List<ItemCatalogEntry> catalog, HashSet<int> itemIds)
        {
            if (item == null)
            {
                return false;
            }
            string name = CleanConfigText(ReflectionTools.GetMemberValue(item, "Name") as string, 80);
            object idValue = ReflectionTools.GetMemberValue(item, "Id");
            if (String.IsNullOrEmpty(name) || !(idValue is int))
            {
                return false;
            }
            int id = (int)idValue;
            if (id <= 0 || !itemIds.Add(id))
            {
                return false;
            }
            int typeMask = ExtractItemTypeMask(item);
            catalog.Add(new ItemCatalogEntry
            {
                Id = id,
                Name = name,
                TypeMask = typeMask,
                Category = ClassifyItem(name, id, typeMask)
            });
            return true;
        }

        private int ExtractItemTypeMask(object item)
        {
            object typeValue = ReflectionTools.GetMemberValue(item, "Type");
            int parsed;
            return TryConvertToInt(typeValue, out parsed) ? parsed : 0;
        }

        private static string ClassifyItem(string name, int id, int typeMask)
        {
            string lowered = (name ?? "").ToLowerInvariant();
            if (id == 626 || id == 630 || lowered.IndexOf("glider", StringComparison.Ordinal) >= 0 || lowered.IndexOf("knightv", StringComparison.Ordinal) >= 0 || lowered.IndexOf("golf", StringComparison.Ordinal) >= 0) return "Vehicles";
            if ((typeMask & 0x9200) != 0 || lowered.IndexOf("pistol", StringComparison.Ordinal) >= 0 || lowered.IndexOf("shotgun", StringComparison.Ordinal) >= 0 || lowered.IndexOf("rifle", StringComparison.Ordinal) >= 0 || lowered.IndexOf("bow", StringComparison.Ordinal) >= 0 || lowered.IndexOf("axe", StringComparison.Ordinal) >= 0) return "Weapons";
            if ((typeMask & 0x60) != 0 || lowered.IndexOf("ammo", StringComparison.Ordinal) >= 0 || lowered.IndexOf("arrow", StringComparison.Ordinal) >= 0 || lowered.IndexOf("slug", StringComparison.Ordinal) >= 0) return "Ammo";
            if ((typeMask & 8) != 0 || lowered.IndexOf("berry", StringComparison.Ordinal) >= 0 || lowered.IndexOf("meat", StringComparison.Ordinal) >= 0 || lowered.IndexOf("drink", StringComparison.Ordinal) >= 0 || lowered.IndexOf("mre", StringComparison.Ordinal) >= 0) return "Food";
            if ((typeMask & 0x4002) != 0 || lowered.IndexOf("armor", StringComparison.Ordinal) >= 0 || lowered.IndexOf("armour", StringComparison.Ordinal) >= 0 || lowered.IndexOf("outfit", StringComparison.Ordinal) >= 0) return "Clothing";
            if ((typeMask & 0x4) != 0 || lowered.IndexOf("log", StringComparison.Ordinal) >= 0 || lowered.IndexOf("stone", StringComparison.Ordinal) >= 0 || lowered.IndexOf("stick", StringComparison.Ordinal) >= 0 || lowered.IndexOf("cloth", StringComparison.Ordinal) >= 0) return "Materials";
            if ((typeMask & 0x100) != 0 || lowered.IndexOf("blueprint", StringComparison.Ordinal) >= 0 || lowered.IndexOf("plan", StringComparison.Ordinal) >= 0) return "Blueprints";
            if ((typeMask & 0x480) != 0 || lowered.IndexOf("keycard", StringComparison.Ordinal) >= 0 || lowered.IndexOf("artifact", StringComparison.Ordinal) >= 0 || lowered.IndexOf("shovel", StringComparison.Ordinal) >= 0 || lowered.IndexOf("rebreather", StringComparison.Ordinal) >= 0 || lowered.IndexOf("ropegun", StringComparison.Ordinal) >= 0) return "Story";
            if (lowered.IndexOf("tool", StringComparison.Ordinal) >= 0 || lowered.IndexOf("gps", StringComparison.Ordinal) >= 0 || lowered.IndexOf("flashlight", StringComparison.Ordinal) >= 0 || lowered.IndexOf("binocular", StringComparison.Ordinal) >= 0 || lowered.IndexOf("lighter", StringComparison.Ordinal) >= 0) return "Tools";
            return "Other";
        }

        private sealed class ItemCatalogEntry
        {
            public int Id;
            public string Name;
            public string Category;
            public int TypeMask;
        }
    }
}
