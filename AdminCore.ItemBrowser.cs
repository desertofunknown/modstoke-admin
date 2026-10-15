using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private const int ItemCatalogPageSize = 24;
        private readonly List<ItemCatalogEntry> _filteredItemCatalog = new List<ItemCatalogEntry>();
        private int _itemCatalogPage;
        private int _itemCatalogFilterRevision = -1;
        private string _itemCatalogFilterQuery;
        private string _itemCatalogFilterCategory;
        private bool _itemCatalogWorldReady;
        private bool _itemCatalogRefreshRequested;
        private int _itemCatalogScrollTarget = -1;

        private void UpdateItemCatalogAvailability()
        {
            if (_itemCatalogWorldReady != _worldReady)
            {
                _itemCatalogWorldReady = _worldReady;
                ResetItemCatalog();
            }
            if (_itemCatalogRefreshRequested)
            {
                _itemCatalogRefreshRequested = false;
                RefreshItemCatalog(true);
            }
            else if (_menuOpen && _activeTab == "Debug" && _debugActiveTab == "Items")
            {
                EnsureItemCatalog();
            }
        }

        private void DrawDebugItemsSubTab()
        {
            GUILayout.Label("Item Catalog", _sectionTitleStyle);
            GUILayout.BeginHorizontal(_rowStyle);
            DrawItemCategoryButton("All", 54f);
            DrawItemCategoryButton("Weapons", 88f);
            DrawItemCategoryButton("Ammo", 68f);
            DrawItemCategoryButton("Food", 64f);
            DrawItemCategoryButton("Clothing", 88f);
            DrawItemCategoryButton("Materials", 94f);
            DrawItemCategoryButton("Blueprints", 104f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            DrawItemCategoryButton("Tools", 66f);
            DrawItemCategoryButton("Story", 66f);
            DrawItemCategoryButton("Vehicles", 88f);
            DrawItemCategoryButton("Other", 70f);
            GUILayout.FlexibleSpace();
            if (DrawModerationIconButton(_iconRaw, "Refresh", _quietButtonStyle, _worldReady, 92f, 30f))
                _itemCatalogRefreshRequested = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(new GUIContent("Search", null, "Search names, browser groups or item IDs."), _contextLabelStyle, GUILayout.Width(56f), GUILayout.Height(32f));
            _debugItemSearchInput = GUILayout.TextField(_debugItemSearchInput ?? "", _inputStyle, GUILayout.Width(310f), GUILayout.Height(32f));
            if (GUILayout.Button("Clear Search", _quietButtonStyle, GUILayout.Width(112f), GUILayout.Height(32f)))
                _debugItemSearchInput = "";
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label(new GUIContent("Selected", null, "Enter an item ID or exact name, or select a catalog row."), _contextLabelStyle, GUILayout.Width(66f), GUILayout.Height(32f));
            _debugItemInput = GUILayout.TextField(_debugItemInput ?? "", _inputStyle, GUILayout.Width(210f), GUILayout.Height(32f));
            GUILayout.Label("Count (1-50)", _contextLabelStyle, GUILayout.Width(104f), GUILayout.Height(32f));
            _debugItemCountInput = GUILayout.TextField(_debugItemCountInput ?? "", _inputStyle, GUILayout.Width(58f), GUILayout.Height(32f));
            ItemCatalogEntry selected = FindSelectedCatalogItem();
            if (DrawModerationButton("Show Selected", _quietButtonStyle, selected != null, GUILayout.Width(132f), GUILayout.Height(32f)))
                ShowSelectedCatalogItem(selected);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(selected == null ? "Selection: " + (String.IsNullOrWhiteSpace(_debugItemInput) ? "none" : _debugItemInput.Trim() + " (not matched to a catalog row)")
                : "Selected: " + selected.Name + " | ID " + selected.Id.ToString(CultureInfo.InvariantCulture) + " | Group: " + selected.Category, _hintStyle);

            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawDebugCommandButton(_iconItems, "Add", _primaryButtonStyle, 78f, 32f, "_additem", "Add the entered quantity to your inventory.")) RunDebugItemAction("_additem");
            if (DrawDebugCommandButton(_iconItems, "Drop", _buttonStyle, 82f, 32f, "_spawnitem", "Send one world-spawn request per item in Count.")) RunDebugItemAction("_spawnitem");
            if (DrawDebugCommandButton(_iconShield, "Equip", _buttonStyle, 82f, 32f, "_equipitem", "Equip once; Count is ignored.")) RunDebugItemAction("_equipitem");
            if (DrawDebugCommandButton(_iconWarning, "Remove", _dangerButtonStyle, 92f, 32f, "_removeitem", "Remove the entered quantity from your inventory.")) RunDebugItemAction("_removeitem");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(_rowStyle);
            GUILayout.Label("Tag", _contextLabelStyle, GUILayout.Width(42f), GUILayout.Height(32f));
            _debugTagInput = GUILayout.TextField(_debugTagInput ?? "", _inputStyle, GUILayout.Width(190f), GUILayout.Height(32f));
            if (DrawDebugCommandButton(_iconItems, "Add Tag", _buttonStyle, 96f, 32f, "_additemswithtag", "Add every item with the given game item tag."))
                RunDebugConsoleAction("Item tag", "_additemswithtag", _debugTagInput, "additemswithtag " + (_debugTagInput ?? ""));
            DrawDebugMethodButton("Add All", "_addAllItems", "", "addallitems", 92f, _dangerButtonStyle);
            DrawDebugMethodButton("Remove All", "_removeAllItems", "", "removeallitems", 112f, _dangerButtonStyle);
            DrawDebugMethodButton("Refill", "_refillContainers", "", "refillcontainers", 82f, _buttonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawServerItemGrant();
            GUILayout.Space(6f);
            List<ItemCatalogEntry> matches = GetFilteredItemCatalog();
            DrawItemCatalogPager(matches.Count);
            GUILayout.BeginHorizontal();
            GUILayout.Label("NAME", _tableHeaderStyle, GUILayout.Width(220f));
            GUILayout.Label("ID", _tableHeaderStyle, GUILayout.Width(62f));
            GUILayout.Label("GROUP", _tableHeaderStyle, GUILayout.Width(94f));
            GUILayout.Label("ACTIONS", _tableHeaderStyle, GUILayout.Width(294f));
            GUILayout.EndHorizontal();

            _itemCatalogScroll = GUILayout.BeginScrollView(_itemCatalogScroll, false, true, GUILayout.Height(260f));
            if (matches.Count == 0)
                GUILayout.Label(_itemCatalog.Count == 0 ? _itemCatalogStatus : "No items match this search and group.", _hintStyle);
            else
            {
                int start = _itemCatalogPage * ItemCatalogPageSize;
                int end = Math.Min(start + ItemCatalogPageSize, matches.Count);
                for (int i = start; i < end; i++) DrawItemCatalogRow(matches[i], selected == null ? -1 : selected.Id);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(_itemCatalogStatus + " | Groups are browsing shortcuts; item actions use the ID.", _hintStyle);
        }

        private void DrawItemCategoryButton(string category, float width)
        {
            GUIStyle style = String.Equals(_debugItemCategory, category, StringComparison.OrdinalIgnoreCase) ? _primaryButtonStyle : _quietButtonStyle;
            if (DrawDebugIconButton(_iconItems, category, style, width, 30f, "Browse " + category + " items."))
                _debugItemCategory = category;
        }

        private List<ItemCatalogEntry> GetFilteredItemCatalog()
        {
            string query = (_debugItemSearchInput ?? "").Trim();
            string category = _debugItemCategory ?? "All";
            bool filterChanged = !String.Equals(query, _itemCatalogFilterQuery, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(category, _itemCatalogFilterCategory, StringComparison.OrdinalIgnoreCase);
            if (!filterChanged && _itemCatalogFilterRevision == _itemCatalogRevision) return _filteredItemCatalog;

            _filteredItemCatalog.Clear();
            foreach (ItemCatalogEntry entry in _itemCatalog)
            {
                if (!String.Equals(category, "All", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                if (query.Length != 0 && entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Id.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.Ordinal) < 0) continue;
                _filteredItemCatalog.Add(entry);
            }
            _itemCatalogFilterQuery = query;
            _itemCatalogFilterCategory = category;
            _itemCatalogFilterRevision = _itemCatalogRevision;
            _itemCatalogScrollTarget = -1;
            if (filterChanged)
            {
                _itemCatalogPage = 0;
                _itemCatalogScroll = Vector2.zero;
            }
            _itemCatalogPage = Math.Min(_itemCatalogPage, Math.Max(0, (_filteredItemCatalog.Count - 1) / ItemCatalogPageSize));
            return _filteredItemCatalog;
        }

        private void DrawItemCatalogPager(int count)
        {
            int lastPage = Math.Max(0, (count - 1) / ItemCatalogPageSize);
            int nextPage = _itemCatalogPage;
            GUILayout.BeginHorizontal(_rowStyle);
            if (DrawModerationButton("First", _quietButtonStyle, _itemCatalogPage > 0, GUILayout.Width(64f), GUILayout.Height(30f))) nextPage = 0;
            if (DrawModerationButton("Previous", _buttonStyle, _itemCatalogPage > 0, GUILayout.Width(88f), GUILayout.Height(30f))) nextPage = _itemCatalogPage - 1;
            GUILayout.Label(count == 0 ? "0 matches" : "Page " + (_itemCatalogPage + 1).ToString(CultureInfo.InvariantCulture)
                + " / " + (lastPage + 1).ToString(CultureInfo.InvariantCulture), _contextValueStyle, GUILayout.Width(142f), GUILayout.Height(30f));
            if (DrawModerationButton("Next", _buttonStyle, _itemCatalogPage < lastPage, GUILayout.Width(72f), GUILayout.Height(30f))) nextPage = _itemCatalogPage + 1;
            if (DrawModerationButton("Last", _quietButtonStyle, _itemCatalogPage < lastPage, GUILayout.Width(64f), GUILayout.Height(30f))) nextPage = lastPage;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (nextPage != _itemCatalogPage)
            {
                _itemCatalogPage = nextPage;
                _itemCatalogScroll = Vector2.zero;
                _itemCatalogScrollTarget = -1;
            }
            if (count > 0)
                GUILayout.Label("Showing " + (_itemCatalogPage * ItemCatalogPageSize + 1).ToString(CultureInfo.InvariantCulture)
                    + "-" + Math.Min((_itemCatalogPage + 1) * ItemCatalogPageSize, count).ToString(CultureInfo.InvariantCulture)
                    + " of " + count.ToString(CultureInfo.InvariantCulture) + " matching items", _hintStyle);
        }

        private ItemCatalogEntry FindSelectedCatalogItem()
        {
            string input = (_debugItemInput ?? "").Trim();
            int id;
            bool numeric = Int32.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
            ItemCatalogEntry selected = null;
            foreach (ItemCatalogEntry entry in _itemCatalog)
            {
                if (numeric ? entry.Id != id : !String.Equals(entry.Name, input, StringComparison.OrdinalIgnoreCase)) continue;
                if (selected != null) return null;
                selected = entry;
            }
            return selected;
        }

        private void ShowSelectedCatalogItem(ItemCatalogEntry selected)
        {
            _debugItemSearchInput = "";
            _debugItemCategory = "All";
            List<ItemCatalogEntry> matches = GetFilteredItemCatalog();
            int index = matches.FindIndex(entry => entry.Id == selected.Id);
            if (index >= 0) _itemCatalogPage = index / ItemCatalogPageSize;
            _itemCatalogScroll = Vector2.zero;
            _itemCatalogScrollTarget = selected.Id;
        }

        private void DrawItemCatalogRow(ItemCatalogEntry entry, int selectedId)
        {
            bool selected = selectedId == entry.Id;
            GUILayout.BeginHorizontal(_rowStyle);
            if (GUILayout.Button(new GUIContent(Shorten(entry.Name, 30), null, entry.Name), selected ? _primaryButtonStyle : _quietButtonStyle, GUILayout.Width(220f), GUILayout.Height(30f)))
                SelectItemCatalogEntry(entry);
            GUILayout.Label(entry.Id.ToString(CultureInfo.InvariantCulture), _monoStyle, GUILayout.Width(62f), GUILayout.Height(30f));
            GUILayout.Label(entry.Category, selected ? _contextValueStyle : _labelStyle, GUILayout.Width(94f), GUILayout.Height(30f));
            if (DrawDebugCommandButton(_iconItems, "Add", _primaryButtonStyle, 64f, 30f, "_additem", "Add Count of " + entry.Name)) { SelectItemCatalogEntry(entry); RunDebugItemAction("_additem"); }
            if (DrawDebugCommandButton(_iconItems, "Drop", _buttonStyle, 68f, 30f, "_spawnitem", "Send Count spawn requests for " + entry.Name)) { SelectItemCatalogEntry(entry); RunDebugItemAction("_spawnitem"); }
            if (DrawDebugCommandButton(_iconShield, "Equip", _buttonStyle, 74f, 30f, "_equipitem", "Equip " + entry.Name + " once")) { SelectItemCatalogEntry(entry); RunDebugItemAction("_equipitem"); }
            if (DrawDebugCommandButton(_iconWarning, "Remove", _dangerButtonStyle, 88f, 30f, "_removeitem", "Remove Count of " + entry.Name)) { SelectItemCatalogEntry(entry); RunDebugItemAction("_removeitem"); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (_itemCatalogScrollTarget == entry.Id && Event.current.type == EventType.Repaint)
            {
                _itemCatalogScroll.y = Math.Max(0f, GUILayoutUtility.GetLastRect().y);
                _itemCatalogScrollTarget = -1;
            }
        }

        private void SelectItemCatalogEntry(ItemCatalogEntry entry)
        {
            _debugItemInput = entry.Id.ToString(CultureInfo.InvariantCulture);
            ActionOk("Selected item " + entry.Name + " (" + _debugItemInput + ")");
        }
    }
}
