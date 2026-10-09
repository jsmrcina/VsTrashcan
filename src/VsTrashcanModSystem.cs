using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VsTrashcan.Inventory;
using VsTrashcan.Models.Networking;
using VsTrashcan.Pickup;
using VsTrashcan.Server;

namespace VsTrashcan
{
    public class VsTrashcan : ModSystem
    {
        // Not the 1.0.x "trashcanChannel", so this never talks to an old client or one of the community forks
        private const string ChannelName = "vstrashcan";
        private const string ClientConfigFile = "vstrashcan-client.json";

        public class ClientConfig
        {
            public bool HideWindow = false;
        }

        // Server
        internal TrashcanServer Server { get; private set; }
        private ICoreServerAPI serverApi;

        // Client
        private ICoreClientAPI clientApi;
        private IClientNetworkChannel clientChannel;
        private TrashcanInventory clientInventory;
        private TrashGui dialog;
        private ClientConfig clientConfig;

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverApi = api ?? throw new ArgumentException("Server API is null");
            Server = new TrashcanServer(api);

            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<TrashcanReadyMessage>()
                .RegisterMessageType<TrashcanClosedMessage>()
                .SetMessageHandler<TrashcanReadyMessage>((player, msg) => Server.OnClientReady(player))
                .SetMessageHandler<TrashcanClosedMessage>((player, msg) => Server.OnInventoryClosed(player));

            api.Event.PlayerJoin += player => Server.OnPlayerJoin(player);
            api.Event.PlayerNowPlaying += InstallPickupHook;
            api.Event.PlayerRespawn += InstallPickupHook;
            api.Event.PlayerLeave += player => Server.OnPlayerLeave(player);
            api.Event.PlayerDisconnect += player => Server.OnPlayerLeave(player);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Logger.Notification("VsTrashcan: ready"));
        }

        // If another mod replaced the pickup behavior with its own, auto-trash can't work; say so instead of failing silently
        private void InstallPickupHook(IServerPlayer player)
        {
            if (player.Entity != null && !EntityBehaviorTrashcanCollect.Install(player.Entity))
            {
                serverApi.Logger.Error($"VsTrashcan: auto-trash filters will not work for {player.PlayerName}: their entity has no vanilla collectitems behavior (another mod replaced it)");
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientApi = api ?? throw new ArgumentException("Client API is null");

            try
            {
                clientConfig = api.LoadModConfig<ClientConfig>(ClientConfigFile);
            }
            catch (Exception e)
            {
                api.Logger.Warning($"VsTrashcan: could not read {ClientConfigFile}, using defaults: {e.Message}");
            }
            clientConfig ??= new ClientConfig();

            clientChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<TrashcanReadyMessage>()
                .RegisterMessageType<TrashcanClosedMessage>();

            api.ChatCommands.Create("trashcan")
                .WithDescription("Show or hide the trash window next to your inventory")
                .HandleWith(OnToggleWindow);

            api.Event.LevelFinalize += OnLevelFinalizeClient;
        }

        private void OnLevelFinalizeClient()
        {
            IPlayer player = clientApi.World.Player;

            // Same ID as the server's copy ("vstrashcan-<playeruid>"), so slot clicks and slot updates find each other
            clientInventory = new TrashcanInventory(player.PlayerUID, clientApi);
            player.InventoryManager.OpenInventory(clientInventory);
            clientChannel.SendPacket(new TrashcanReadyMessage());

            dialog = new TrashGui(clientApi, clientInventory);

            var backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) as InventoryBase;
            backpack.OnInventoryOpened += OnInventoryOpened;
            backpack.OnInventoryClosed += OnInventoryClosed;
        }

        private void OnInventoryOpened(IPlayer player)
        {
            if (!clientConfig.HideWindow)
            {
                dialog.TryOpen();
            }
        }

        private void OnInventoryClosed(IPlayer player)
        {
            dialog.TryClose();
            clientInventory.ClearHistory();
            clientChannel.SendPacket(new TrashcanClosedMessage());
        }

        private TextCommandResult OnToggleWindow(TextCommandCallingArgs args)
        {
            clientConfig.HideWindow = !clientConfig.HideWindow;
            clientApi.StoreModConfig(clientConfig, ClientConfigFile);

            if (clientConfig.HideWindow)
            {
                dialog?.TryClose();
                return TextCommandResult.Success("Trash window hidden (your auto-trash filters still apply). Type .trashcan to show it again.");
            }
            return TextCommandResult.Success("Trash window shown next to your inventory");
        }
    }
}
