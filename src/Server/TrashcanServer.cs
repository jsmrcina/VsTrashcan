using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VsTrashcan.Filters;
using VsTrashcan.Inventory;

namespace VsTrashcan.Server
{
    //
    // Server side of the mod: each player's trashcan inventory, their saved filters, and the auto-trash decision.
    // Kept apart from the ModSystem so it can be driven directly by tests.
    //
    public class TrashcanServer
    {
        private readonly ICoreServerAPI api;
        private readonly Dictionary<string, TrashcanInventory> inventories = new Dictionary<string, TrashcanInventory>();
        private Dictionary<string, List<byte[]>> legacyFilters;

        public TrashcanServer(ICoreServerAPI api)
        {
            this.api = api;
        }

        public TrashcanInventory GetInventory(string playerUid)
        {
            return inventories.TryGetValue(playerUid, out var inv) ? inv : null;
        }

        public TrashcanInventory OnPlayerJoin(IServerPlayer player)
        {
            var inv = new TrashcanInventory(player.PlayerUID, api);
            inv.SetFilters(LoadFilters(player));
            inv.FiltersChanged += () => SaveFilters(player, inv);
            player.InventoryManager.OpenInventory(inv);
            inventories[player.PlayerUID] = inv;
            return inv;
        }

        // The history is never saved, so anything left in it is destroyed here
        public void OnPlayerLeave(IServerPlayer player)
        {
            inventories.Remove(player.PlayerUID);
        }

        // The client has created its copy of the inventory: send it the current contents
        public void OnClientReady(IServerPlayer player)
        {
            GetInventory(player.PlayerUID)?.MarkAllSlotsDirty();
        }

        // Closing the inventory makes trashing permanent
        public void OnInventoryClosed(IServerPlayer player)
        {
            GetInventory(player.PlayerUID)?.ClearHistory();
        }

        // Called for every item the player is about to pick up
        public bool ShouldAutoTrash(string playerUid, ItemStack stack)
        {
            var inv = GetInventory(playerUid);
            return inv != null && TrashFilter.MatchesAny(inv.GetFilters().Where(f => f != null), stack);
        }

        private ItemStack[] LoadFilters(IServerPlayer player)
        {
            byte[] data = player.GetModdata(FilterStore.ModdataKey);
            if (data != null)
            {
                return FilterStore.Deserialize(data, TrashcanInventory.FilterCount, api.World, LogUnresolved);
            }

            // First join since updating from 1.0.x: convert that player's filters from the old save game format, once
            legacyFilters ??= LegacyFilters.Read(api.WorldManager.SaveGame.GetData(LegacyFilters.SaveGameKey));
            if (!legacyFilters.TryGetValue(player.PlayerUID, out var legacy))
            {
                return new ItemStack[TrashcanInventory.FilterCount];
            }

            ItemStack[] filters = LegacyFilters.Convert(legacy, TrashcanInventory.FilterCount, api.World, LogUnresolved);
            player.SetModdata(FilterStore.ModdataKey, FilterStore.Serialize(filters));
            legacyFilters.Remove(player.PlayerUID);
            api.WorldManager.SaveGame.StoreData(LegacyFilters.SaveGameKey, SerializerUtil.Serialize(legacyFilters));
            api.Logger.Notification($"VsTrashcan: converted {filters.Count(f => f != null)} filters from 1.0.x for {player.PlayerName}");
            return filters;
        }

        private void SaveFilters(IServerPlayer player, TrashcanInventory inv)
        {
            player.SetModdata(FilterStore.ModdataKey, FilterStore.Serialize(inv.GetFilters()));
        }

        private void LogUnresolved(string what)
        {
            api.Logger.Warning($"VsTrashcan: dropping a filter that no longer exists in this world: {what}");
        }
    }
}
