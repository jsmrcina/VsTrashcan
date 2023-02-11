using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VsTrashcan.Models.Networking;

[assembly: ModInfo("VsTrashcan",
    Description = "A mod that adds a trashcan to your inventory for discarding stacks",
    Website = "",
    Authors = new[] { "jsmrcina" })]

namespace VsTrashcan
{
    public class VsTrashcan : ModSystem
    {
        private readonly string _trashcanChannel = "trashcanChannel";

        private ICoreServerAPI ServerApi;
        private ICoreClientAPI ClientApi;

        private IServerNetworkChannel ServerChannel;
        private IClientNetworkChannel ClientChannel;

        private InventoryGeneric trashcanInv;
        private GuiDialog dialog;

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");
            ClientApi.Event.LevelFinalize += OnLevelFinalizeClient;

            ClientChannel = api.Network.RegisterChannel(_trashcanChannel)
                            .RegisterMessageType(typeof(ItemSlotTrashedMessage));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");

            ServerChannel = api.Network.RegisterChannel(_trashcanChannel)
                .RegisterMessageType(typeof(ItemSlotTrashedMessage))
                .SetMessageHandler<ItemSlotTrashedMessage>(OnItemSlotTrashedMessage);
        }

        private void OnLevelFinalizeClient()
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

        private void OnItemSlotTrashedMessage(IServerPlayer player, ItemSlotTrashedMessage _)
        {
            //
            // Update the server to delete the item the user is holding in their hand
            // If we don't do this, the player can then click on any ItemSlot and place the
            // item they just trashed back down.
            //
            //
            player.InventoryManager.MouseItemSlot.Itemstack = null;
        }

        private void OnSlotModified(int x)
        {
            trashcanInv[x].Itemstack = null;
            ClientChannel.SendPacket(new ItemSlotTrashedMessage());
        }
    }
}