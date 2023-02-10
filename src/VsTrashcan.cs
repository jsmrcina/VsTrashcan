using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

[assembly: ModInfo("VsTrashcan",
    Description = "A mod that adds a trashcan to your inventory for discarding stacks",
    Website = "",
    Authors = new[] { "jsmrcina" })]

namespace VsTrashcan
{
    public class VsTrashcan : ModSystem
    {
        ICoreServerAPI ServerApi;
        ICoreClientAPI ClientApi;
        InventoryGeneric trashcanInv;
        GuiDialog dialog;

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");
            ClientApi.Event.LevelFinalize += OnLevelFinalize;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");
        }

        private void OnLevelFinalize()
        {
            IPlayer player = ClientApi.World.Player;
            trashcanInv = new InventoryGeneric(1, "trash", player.PlayerUID, ClientApi);
            trashcanInv.SlotModified += OnSlotModified;
            dialog = new TrashGui(ClientApi, trashcanInv);

            InventoryBase backpack = ClientApi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryBase;
            backpack.OnInventoryOpened += OnInventoryOpened;
            backpack.OnInventoryClosed += OnInventoryClosed;
        }

        private void OnInventoryOpened(IPlayer player)
        {
            dialog.TryOpen();
        }

        private void OnInventoryClosed(IPlayer player)
        {
            dialog.TryClose();
        }

        private void OnSlotModified(int x)
        {
            trashcanInv[x].Itemstack = null;
        }
    }
}