using System;
using System.Globalization;

namespace OpenACAIAdminTool
{
    internal sealed partial class AdminCore
    {
        private void RunDebugItemAction(string methodName)
        {
            if (!IsLocalPlayerInWorld())
            {
                ActionFail("Item action unavailable: join a world first");
                return;
            }
            object inventory = GetLocalPlayerInventoryForAdmin();
            if (inventory == null)
            {
                ActionFail("Item action unavailable: local inventory is not ready");
                return;
            }

            string item = CleanConfigText(_debugItemInput, 80);
            if (String.IsNullOrEmpty(item))
            {
                ActionFail("Item action failed: enter an item name or ID");
                return;
            }
            int itemId;
            if (!TryResolveInventoryActionItem(item, out itemId))
            {
                return;
            }

            string itemLabel;
            if (!_itemIdNameCache.TryGetValue(itemId, out itemLabel) || String.IsNullOrEmpty(itemLabel))
            {
                itemLabel = itemId.ToString(CultureInfo.InvariantCulture);
            }
            int count = 1;
            if (!String.Equals(methodName, "_equipitem", StringComparison.OrdinalIgnoreCase))
            {
                if (!Int32.TryParse(_debugItemCountInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                    || count < 1 || count > 50)
                {
                    ActionFail("Enter a whole-number Count from 1 to 50");
                    return;
                }
                _debugItemCountInput = count.ToString(CultureInfo.InvariantCulture);
            }

            if (String.Equals(methodName, "_additem", StringComparison.OrdinalIgnoreCase))
            {
                ChangeLocalInventoryAmount(inventory, itemId, itemLabel, count, true);
            }
            else if (String.Equals(methodName, "_removeitem", StringComparison.OrdinalIgnoreCase))
            {
                ChangeLocalInventoryAmount(inventory, itemId, itemLabel, count, false);
            }
            else if (String.Equals(methodName, "_equipitem", StringComparison.OrdinalIgnoreCase))
            {
                EquipLocalInventoryItem(inventory, itemId, itemLabel);
            }
            else if (String.Equals(methodName, "_spawnitem", StringComparison.OrdinalIgnoreCase))
            {
                string argument = itemId.ToString(CultureInfo.InvariantCulture);
                string command = BuildDebugCommand("spawnitem", argument);
                int sent = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!InvokeDebugConsoleMethod("_spawnitem", argument, command))
                    {
                        break;
                    }
                    sent++;
                }
                string outcome = "Spawn requests sent: " + sent.ToString(CultureInfo.InvariantCulture)
                    + " of " + count.ToString(CultureInfo.InvariantCulture) + " for " + itemLabel;
                ActionResult(sent == count, outcome, outcome + "; " + DebugCommandFailure("dispatch"));
            }
            else
            {
                ActionFail("Item action unavailable: unsupported action");
            }
        }

        private bool TryResolveInventoryActionItem(string item, out int itemId)
        {
            itemId = -1;
            Type database = ReflectionTools.FindType("Sons.Items.Core.ItemDatabaseManager");
            if (database == null || GetItemDatabaseItems() == null)
            {
                ActionFail("Item action unavailable: item catalog is not ready");
                return false;
            }

            itemId = ResolveDebugItemId(item);
            if (itemId <= 0)
            {
                ActionFail("Item action failed: unknown item name or invalid ID: " + item);
                return false;
            }
            object definition;
            if (!InvokeMethod(null, database, "ItemById", new object[] { itemId }, true, out definition))
            {
                ActionFail("Item action unavailable: item catalog lookup failed");
                return false;
            }
            if (definition == null)
            {
                ActionFail("Item action failed: unknown item ID " + itemId.ToString(CultureInfo.InvariantCulture));
                return false;
            }
            return true;
        }

        private bool TryReadLocalInventoryAmount(object inventory, int itemId, out int amount)
        {
            amount = 0;
            object result;
            if (!InvokeMethod(inventory, inventory.GetType(), "AmountOf", new object[] { itemId, true, false }, false, out result)
                || !(result is int))
            {
                return false;
            }
            amount = (int)result;
            return true;
        }

        private void ChangeLocalInventoryAmount(object inventory, int itemId, string itemLabel, int count, bool add)
        {
            int before;
            bool hasBefore = TryReadLocalInventoryAmount(inventory, itemId, out before);
            string method = add ? "AddItem" : "RemoveItem";
            object[] args = add
                ? new object[] { itemId, count, false, false, null }
                : new object[] { itemId, count, false, true, true, null, false };
            object result;
            bool invoked = InvokeMethod(inventory, inventory.GetType(), method, args, false, out result);
            bool accepted = invoked && result is bool && (bool)result;
            int after;
            bool hasAfter = TryReadLocalInventoryAmount(inventory, itemId, out after);
            string outcome = (add ? "Add" : "Remove") + " request for " + itemLabel + " ("
                + count.ToString(CultureInfo.InvariantCulture) + "): "
                + (!invoked ? "native method unavailable or failed" : !(result is bool) ? "unexpected native result"
                    : accepted ? "accepted" : "rejected");

            if (hasBefore && hasAfter)
            {
                long delta = (long)after - before;
                long changed = add ? delta : -delta;
                outcome += "; inventory " + before.ToString(CultureInfo.InvariantCulture) + " -> "
                    + after.ToString(CultureInfo.InvariantCulture);
                if (changed > 0)
                {
                    outcome += "; " + (add ? "added " : "removed ") + changed.ToString(CultureInfo.InvariantCulture)
                        + (changed < count ? " (partial)" : "");
                }
                else
                {
                    outcome += changed == 0 ? "; observed quantity unchanged" : "; quantity changed in the opposite direction";
                }
            }
            else
            {
                outcome += "; quantity change unavailable (before "
                    + (hasBefore ? before.ToString(CultureInfo.InvariantCulture) : "unavailable") + ", after "
                    + (hasAfter ? after.ToString(CultureInfo.InvariantCulture) : "unavailable") + ")";
            }
            ActionResult(accepted, outcome, outcome);
        }

        private void EquipLocalInventoryItem(object inventory, int itemId, string itemLabel)
        {
            object result;
            bool invoked = InvokeMethod(inventory, inventory.GetType(), "TryEquip",
                new object[] { itemId, false, true, false }, false, out result);
            bool accepted = invoked && result is bool && (bool)result;
            object equippedResult;
            object[] equippedArgs = new object[] { itemId, null };
            bool hasEquippedState = InvokeMethod(inventory, inventory.GetType(), "TryGetEquipped", equippedArgs, false, out equippedResult)
                && equippedResult is bool;
            bool equipped = hasEquippedState && (bool)equippedResult;

            string outcome = accepted ? "Equip requested: " + itemLabel
                : "Equip request for " + itemLabel + ": " + (!invoked ? "native method unavailable or failed"
                    : result is bool ? "rejected" : "unexpected native result");
            outcome += !hasEquippedState ? "; equipped status unavailable"
                : equipped ? "; currently equipped" : accepted ? "; not yet equipped" : "; currently not equipped";
            ActionResult(accepted, outcome, outcome);
        }
    }
}
