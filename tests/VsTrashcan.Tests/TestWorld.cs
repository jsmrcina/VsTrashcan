using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VsTrashcan.Tests
{
    //
    // Real game Item and Block objects (and the game's own BlockCrock) registered in a mocked server world, so stack
    // comparison, merging and lookup by id or code all run the game's code. Items and blocks get separate id spaces,
    // like in the game, and tests deliberately create collisions between them.
    //
    public class TestWorld
    {
        public Mock<ICoreServerAPI> Api { get; }
        public Mock<IServerWorldAccessor> World { get; }
        public Mock<ILogger> Logger { get; }
        public Dictionary<string, byte[]> SaveGameData { get; } = new Dictionary<string, byte[]>();
        public List<Item> Items { get; } = new List<Item>();
        public List<Block> Blocks { get; } = new List<Block>();

        private static readonly FieldInfo CollectibleApiField =
            typeof(CollectibleObject).GetField("api", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException("CollectibleObject.api");

        public TestWorld()
        {
            Api = new Mock<ICoreServerAPI> { DefaultValue = DefaultValue.Mock };
            World = new Mock<IServerWorldAccessor> { DefaultValue = DefaultValue.Mock };
            Logger = new Mock<ILogger>();

            Api.Setup(a => a.Side).Returns(EnumAppSide.Server);
            Api.Setup(a => a.World).Returns(World.Object);
            Api.Setup(a => a.Logger).Returns(Logger.Object);
            World.Setup(w => w.Api).Returns(Api.Object);
            World.Setup(w => w.Side).Returns(EnumAppSide.Server);
            World.Setup(w => w.Logger).Returns(Logger.Object);

            World.Setup(w => w.GetItem(It.IsAny<int>())).Returns((int id) => Items.FirstOrDefault(i => i.ItemId == id));
            World.Setup(w => w.GetBlock(It.IsAny<int>())).Returns((int id) => Blocks.FirstOrDefault(b => b.BlockId == id));
            World.Setup(w => w.GetItem(It.IsAny<AssetLocation>())).Returns((AssetLocation code) => Items.FirstOrDefault(i => i.Code.Equals(code)));
            World.Setup(w => w.GetBlock(It.IsAny<AssetLocation>())).Returns((AssetLocation code) => Blocks.FirstOrDefault(b => b.Code.Equals(code)));
            World.Setup(w => w.Items).Returns(() => Items);
            World.Setup(w => w.Blocks).Returns(() => Blocks);

            // Without an explicit setup Moq records this call with the half-constructed inventory as an argument and later
            // compares arguments as sequences, enumerating an inventory whose slots don't exist yet
            Api.Setup(a => a.ClassRegistry.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), It.IsAny<ICoreAPI>()))
                .Returns(() => new Mock<IInventoryNetworkUtil> { DefaultValue = DefaultValue.Mock }.Object);

            Api.Setup(a => a.WorldManager.SaveGame.GetData(It.IsAny<string>()))
                .Returns((string key) => SaveGameData.TryGetValue(key, out var data) ? data : null);
            Api.Setup(a => a.WorldManager.SaveGame.StoreData(It.IsAny<string>(), It.IsAny<byte[]>()))
                .Callback((string key, byte[] data) => SaveGameData[key] = data);
        }

        public Item AddItem(string code, int id, int maxStackSize = 64, int durability = 1)
        {
            var item = new Item(id) { Code = new AssetLocation(code), MaxStackSize = maxStackSize, Durability = durability };
            CollectibleApiField.SetValue(item, Api.Object);
            Items.Add(item);
            return item;
        }

        public T AddBlock<T>(string code, int id, int maxStackSize = 64) where T : Block, new()
        {
            var block = new T { Code = new AssetLocation(code), BlockId = id, MaxStackSize = maxStackSize };
            CollectibleApiField.SetValue(block, Api.Object);
            Blocks.Add(block);
            return block;
        }

        public Block AddBlock(string code, int id, int maxStackSize = 64) => AddBlock<Block>(code, id, maxStackSize);

        public static ItemStack Stack(CollectibleObject collectible, int size = 1, Action<ITreeAttribute> attributes = null)
        {
            ItemStack stack = collectible is Block block ? new ItemStack(block, size) : new ItemStack((Item)collectible, size);
            attributes?.Invoke(stack.Attributes);
            return stack;
        }
    }
}
