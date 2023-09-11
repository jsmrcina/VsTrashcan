using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VsTrashcan.Models.Networking;

[assembly: ModInfo("VsTrashcan",
    Description = "A mod that adds a trashcan to your inventory for discarding stacks",
    Authors = new[] { "jsmrcina" })]

namespace VsTrashcan
{
    public class VsTrashcan : ModSystem
    {
        private readonly string _trashcanChannel = "trashcanChannel";

        private readonly int _numTrashFilterRows = 4;
        private readonly int _numTrashFilterCols = 4;
        private readonly int _numTrashFilterSlots;

        // Client
        private InventoryGeneric _trashSlotInv;
        private InventoryGeneric _trashSlotFilterInvClient;
        private ICoreClientAPI _clientApi;
        private IClientNetworkChannel _clientChannel;
        private GuiDialog _dialog;

        // Server
        private Dictionary<string, InventoryGeneric> _trashSlotFilterInvServer;
        private ICoreServerAPI _serverApi;
        private IServerNetworkChannel _serverChannel;
        private readonly int _trashFilterThreadDelay = 5;

        public VsTrashcan()
        {
            _numTrashFilterSlots = _numTrashFilterRows * _numTrashFilterCols;
        }

        // Client
        public override void StartClientSide(ICoreClientAPI api)
        {
            _clientApi = api ?? throw new ArgumentException("Client API is null");

            _clientApi.RegisterItemClass("VsTrashCan", typeof(VsTrashcan));

            _clientChannel = api.Network.RegisterChannel(_trashcanChannel)
                            .RegisterMessageType(typeof(ClearMouseSlotMessage))
                            .RegisterMessageType(typeof(ModifyTrashFilterMessage))
                            .RegisterMessageType(typeof(InitialTrashFilterSyncMessage))
                            .SetMessageHandler<InitialTrashFilterSyncMessage>(OnInitialTrashFilterSyncMessage);

            _trashSlotFilterInvClient = new InventoryGeneric(_numTrashFilterSlots, null, null);
            _clientApi.Event.LevelFinalize += OnLevelFinalizeClient;
        }

        // Server
        public override void StartServerSide(ICoreServerAPI api)
        {
            _serverApi = api ?? throw new ArgumentException("Server API is null");

            _trashSlotFilterInvServer = new Dictionary<string, InventoryGeneric>();

            _serverChannel = api.Network.RegisterChannel(_trashcanChannel)
                .RegisterMessageType(typeof(ClearMouseSlotMessage))
                .RegisterMessageType(typeof(ModifyTrashFilterMessage))
                .RegisterMessageType(typeof(InitialTrashFilterSyncMessage))
                .SetMessageHandler<ModifyTrashFilterMessage>(OnModifyTrashFilterMessage)
                .SetMessageHandler<ClearMouseSlotMessage>(OnItemSlotTrashedMessage);

            _serverApi.Event.Timer(OnPerformFiltering, _trashFilterThreadDelay);

            _serverApi.Event.PlayerJoin += OnPlayerJoin;
            _serverApi.Event.GameWorldSave += OnGameGettingSaved;
            _serverApi.Event.SaveGameLoaded += OnSaveGameLoaded;
        }

        // Server
        private void OnPerformFiltering()
        {
            Action<IInventory, InventoryGeneric> discardItems = (IInventory bag, InventoryGeneric itemsToDiscard) =>
            {
                foreach (ItemSlot playerInvSlot in bag)
                {
                    if (playerInvSlot.Itemstack == null)
                    {
                        continue;
                    }

                    foreach (ItemSlot discardSlot in itemsToDiscard)
                    {
                        if (discardSlot.Itemstack == null)
                        {
                            continue;
                        }

                        if (playerInvSlot.Itemstack.Id == discardSlot.Itemstack.Id)
                        {
                            playerInvSlot.Itemstack = null;
                            playerInvSlot.MarkDirty();
                            
                            // We just discarded this item, bail out
                            break;
                        }
                    }
                }
            };

            foreach (KeyValuePair<string, InventoryGeneric> keyValue in _trashSlotFilterInvServer)
            {
                IServerPlayer player = _serverApi.World.PlayerByUid(keyValue.Key) as IServerPlayer;
                if (player == null)
                {
                    continue;
                }

                if (player.ConnectionState == EnumClientState.Playing)
                {
                    IInventory backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                    discardItems(backpack, keyValue.Value);

                    IInventory hotbar = player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
                    discardItems(hotbar, keyValue.Value);
                }
            }
        }

        // Server
        private void OnPlayerJoin(IServerPlayer player)
        {
            if (_trashSlotFilterInvServer.ContainsKey(player.PlayerUID))
            {
                InventoryGeneric currentPlayerFilterInv = _trashSlotFilterInvServer[player.PlayerUID];

                int index = 0;
                foreach (ItemSlot item in currentPlayerFilterInv)
                {
                    InitialTrashFilterSyncMessage message = new InitialTrashFilterSyncMessage();
                    message.InventorySlotId = index;
                    index++;

                    if (item.Itemstack == null)
                    {
                        continue;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        BinaryWriter writer = new BinaryWriter(ms);
                        item.Itemstack.ToBytes(writer);
                        message.Itemstack = ms.GetBuffer();
                        _serverChannel.SendPacket<InitialTrashFilterSyncMessage>(message, player);
                    }
                }
            }
            else
            {
                _trashSlotFilterInvServer.Add(player.PlayerUID, new InventoryGeneric(_numTrashFilterSlots, "trashSlotFilter", player.PlayerUID, _serverApi));
            }


        }

        // Server
        private void OnGameGettingSaved()
        {
            Dictionary<string, List<byte[]>> serializableInventoryItems = new Dictionary<string, List<byte[]>>();
            foreach (KeyValuePair<string, InventoryGeneric> keyValuePair in _trashSlotFilterInvServer)
            {
                List<byte[]> itemStackList = new List<byte[]>();
                foreach (ItemSlot slot in keyValuePair.Value)
                {
                    if (slot.Itemstack == null)
                    {
                        continue;
                    }

                    _serverApi.Logger.Warning($"Saving trashcan filter id {slot.Itemstack.Id}");

                    using (MemoryStream ms = new MemoryStream())
                    {
                        BinaryWriter writer = new BinaryWriter(ms);
                        slot.Itemstack.ToBytes(writer);
                        itemStackList.Add(ms.GetBuffer());
                    }
                }
                serializableInventoryItems.Add(keyValuePair.Key, itemStackList);
            }

            _serverApi.WorldManager.SaveGame.StoreData("trashCanFiltersByPlayerUid", SerializerUtil.Serialize(serializableInventoryItems));
        }

        // Server
        private void OnSaveGameLoaded()
        {
            byte[] serializableInventoryItemsAsBytes = _serverApi.WorldManager.SaveGame.GetData("trashCanFiltersByPlayerUid");
            if (serializableInventoryItemsAsBytes == null)
            {
                return;
            }

            Dictionary<string, List<byte[]>> serializableInventoryItems = SerializerUtil.Deserialize<Dictionary<string, List<byte[]>>>(serializableInventoryItemsAsBytes);

            // TODO: Won't respect indexes if there were empty ones.
            int index = 0;
            foreach (KeyValuePair<string, List<byte[]>> keyValuePair in serializableInventoryItems)
            {
                InventoryGeneric playerTrashFilterInventory = new InventoryGeneric(_numTrashFilterSlots, "trashSlotFilter", keyValuePair.Key, _serverApi);
                foreach (byte[] itemStackAsBytes in keyValuePair.Value)
                {
                    using (MemoryStream ms = new MemoryStream(itemStackAsBytes))
                    {
                        playerTrashFilterInventory[index].Itemstack = new ItemStack();
                        playerTrashFilterInventory[index].Itemstack.FromBytes(new BinaryReader(ms));
                        _serverApi.Logger.Warning($"Loading trashcan filter id {playerTrashFilterInventory[index].Itemstack.Id}");
                        index++;
                    }
                }

                _trashSlotFilterInvServer.Add(keyValuePair.Key, playerTrashFilterInventory);
            }
        }

        // Server
        private void OnItemSlotTrashedMessage(IServerPlayer player, ClearMouseSlotMessage _)
        {
            //
            // Update the server to delete the item the user is holding in their hand
            // If we don't do this, the player can then click on any ItemSlot and place the
            // item they just trashed back down.
            //
            //
            player.InventoryManager.MouseItemSlot.Itemstack = null;
        }

        // Server
        private void OnModifyTrashFilterMessage(IServerPlayer player, ModifyTrashFilterMessage msg)
        {
            InventoryGeneric currentPlayerFilterInv = _trashSlotFilterInvServer[player.PlayerUID];

            if (msg.Type == TrashFilterMessageType.Register)
            {
                _serverApi.Logger.Warning($"Received register trash filter: {msg.InventorySlotId}");


                player.InventoryManager.MouseItemSlot.Itemstack = null;

                using (MemoryStream ms = new MemoryStream(msg.Itemstack))
                {
                    currentPlayerFilterInv[msg.InventorySlotId].Itemstack = new ItemStack();
                    currentPlayerFilterInv[msg.InventorySlotId].Itemstack.FromBytes(new BinaryReader(ms));
                }
            }
            else if (msg.Type == TrashFilterMessageType.Unregister)
            {
                _serverApi.Logger.Warning($"Received unregister trash filter: {msg.InventorySlotId}");
                currentPlayerFilterInv[msg.InventorySlotId].Itemstack = null;
            }
        }

        // Client
        private void OnLevelFinalizeClient()
        {
            IPlayer player = _clientApi.World.Player;

            _trashSlotInv = new InventoryGeneric(1, "trashSlot-" + player.PlayerUID, player.PlayerUID, _clientApi);
            _trashSlotInv.SlotModified += OnTrashSlotModified;

            _trashSlotFilterInvClient.LateInitialize("trashSlotFilter-" + player.PlayerUID, _clientApi);
            _trashSlotFilterInvClient.SlotModified += OnTrashSlotFilterModified;


            _dialog = new TrashGui(_clientApi, _trashSlotInv, _trashSlotFilterInvClient, _numTrashFilterRows, _numTrashFilterCols);

            InventoryBase backpack = _clientApi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryBase;
            backpack.OnInventoryOpened += OnInventoryOpened;
            backpack.OnInventoryClosed += OnInventoryClosed;
        }

        // Client
        private void OnInventoryOpened(IPlayer player)
        {
            object packet = _trashSlotInv.Open(player);
            _clientApi.Network.SendPacketClient(packet);

            packet = _trashSlotFilterInvClient.Open(player);
            _clientApi.Network.SendPacketClient(packet);

            _dialog.TryOpen();
        }

        // Client
        private void OnInventoryClosed(IPlayer player)
        {
            object packet = _trashSlotInv.Close(player);
            _clientApi.Network.SendPacketClient(packet);

            packet = _trashSlotFilterInvClient.Close(player);
            _clientApi.Network.SendPacketClient(packet);

            _dialog.TryClose();
        }

        // Client
        private void OnInitialTrashFilterSyncMessage(InitialTrashFilterSyncMessage msg)
        {
            using (MemoryStream ms = new MemoryStream(msg.Itemstack))
            {
                _trashSlotFilterInvClient[msg.InventorySlotId].Itemstack = new ItemStack();
                _trashSlotFilterInvClient[msg.InventorySlotId].Itemstack.FromBytes(new BinaryReader(ms));
            }
        }

        // Client
        private void OnTrashSlotModified(int x)
        {
            _trashSlotInv[x].Itemstack = null;
            _clientChannel.SendPacket(new ClearMouseSlotMessage());
        }

        // Client
        private void OnTrashSlotFilterModified(int x)
        {
            if (_trashSlotFilterInvClient[x].Itemstack != null)
            {
                _clientApi.Logger.Warning($"Register trash filter: {x} {_trashSlotFilterInvClient[x].Itemstack.Collectible.Id}");

                ModifyTrashFilterMessage message = new ModifyTrashFilterMessage();
                message.Type = TrashFilterMessageType.Register;
                message.InventorySlotId = x;

                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(ms);
                    _trashSlotFilterInvClient[x].Itemstack.ToBytes(writer);
                    message.Itemstack = ms.GetBuffer();
                }

                _clientChannel.SendPacket(message);
            }
            else
            {
                _clientApi.Logger.Warning($"Unregister trash filter: {x}");

                ModifyTrashFilterMessage message = new ModifyTrashFilterMessage();
                message.Type = TrashFilterMessageType.Unregister;
                message.InventorySlotId = x;
                message.Itemstack = null;
                _clientChannel.SendPacket(message);
            }
        }
    }
}