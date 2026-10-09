using System;
using System.Collections.Generic;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VsTrashcan.Tests
{
    //
    // Hand-written IServerPlayer: see the AssemblyName comment in the csproj for why this can't be a mock.
    // Only what the mod and WaypointMapLayer use is implemented; everything else throws so new dependencies are noticed.
    //
    public class FakeServerPlayer : IServerPlayer
    {
        public record ChatMessage(int GroupId, string Message, EnumChatType ChatType);

        public List<ChatMessage> Messages { get; } = new List<ChatMessage>();

        // Player moddata, which is where the mod saves filters (persisted with the world's player data in the real game)
        public Dictionary<string, byte[]> Moddata { get; } = new Dictionary<string, byte[]>();

        // Inventories the server registered for this player through InventoryManager.OpenInventory
        public List<IInventory> OpenedInventories { get; } = new List<IInventory>();
        public Mock<IPlayerInventoryManager> InventoryManagerMock { get; } = new Mock<IPlayerInventoryManager>();

        public FakeServerPlayer(string uid, string name)
        {
            PlayerUID = uid;
            PlayerName = name;
            var data = new Mock<IServerPlayerData>();
            data.Setup(d => d.PlayerGroupMemberships).Returns(new Dictionary<int, PlayerGroupMembership>());
            ServerData = data.Object;
            InventoryManagerMock.Setup(m => m.OpenInventory(It.IsAny<IInventory>()))
                .Returns((IInventory inv) => { OpenedInventories.Add(inv); return null; });
        }

        public string PlayerUID { get; }
        public string PlayerName { get; }
        public IServerPlayerData ServerData { get; }

        public void SendMessage(int groupId, string message, EnumChatType chatType, string data = null)
        {
            Messages.Add(new ChatMessage(groupId, message, chatType));
        }

        public void SetModdata(string key, byte[] data) => Moddata[key] = data;
        public byte[] GetModdata(string key) => Moddata.TryGetValue(key, out var data) ? data : null;
        public void RemoveModdata(string key) => Moddata.Remove(key);
        public IPlayerInventoryManager InventoryManager => InventoryManagerMock.Object;

        public override string ToString() => PlayerName;

        // --- Not used by the mod ---
        private static Exception NotUsed() => new NotImplementedException("Not used by VsTrashcan; extend FakeServerPlayer if that changed");

        public event OnEntityAction InWorldAction { add { } remove { } }
        public int ItemCollectMode { get => throw NotUsed(); set => throw NotUsed(); }
        public int CurrentChunkSentRadius { get => throw NotUsed(); set => throw NotUsed(); }
        public EnumClientState ConnectionState => throw NotUsed();
        public string IpAddress => throw NotUsed();
        public string LanguageCode => throw NotUsed();
        public float Ping => throw NotUsed();
        public void BroadcastPlayerData(bool sendInventory = false) => throw NotUsed();
        public void Disconnect() => throw NotUsed();
        public void Disconnect(string message) => throw NotUsed();
        public void SendIngameError(string code, string message = null, params object[] langparams) => throw NotUsed();
        public void SendLocalisedMessage(int groupId, string message, params object[] args) => throw NotUsed();
        public void SetRole(string roleCode) => throw NotUsed();
        public void SetSpawnPosition(PlayerSpawnPos pos) => throw NotUsed();
        public void ClearSpawnPosition() => throw NotUsed();
        public FuzzyEntityPos GetSpawnPosition(bool consumeSpawnUse) => throw NotUsed();
        public void SetModData<T>(string key, T data) => throw NotUsed();
        public T GetModData<T>(string key, T defaultValue = default) => throw NotUsed();

        public IPlayerRole Role { get => throw NotUsed(); set => throw NotUsed(); }
        public PlayerGroupMembership[] Groups => throw NotUsed();
        public PlayerGroupMembership[] GetGroups() => throw NotUsed();
        public PlayerGroupMembership GetGroup(int groupId) => throw NotUsed();
        public List<Entitlement> Entitlements => throw NotUsed();
        public BlockSelection CurrentBlockSelection => throw NotUsed();
        public EntitySelection CurrentEntitySelection => throw NotUsed();
        public int ClientId => throw NotUsed();
        public EntityPlayer Entity => throw NotUsed();
        public IWorldPlayerData WorldData => throw NotUsed();
        public string[] Privileges => throw NotUsed();
        public bool ImmersiveFpMode => throw NotUsed();
        public bool HasPrivilege(string privilegeCode) => throw NotUsed();
        bool IPlayer.IsInInteractionRangeOf(BlockPos blockPos, float slack) => throw NotUsed();
        public bool IsInInteractionRangeOf(Entity entity, float slack = .25f) => throw NotUsed();
    }
}
