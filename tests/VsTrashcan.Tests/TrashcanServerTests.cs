using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using VsTrashcan.Filters;
using VsTrashcan.Inventory;
using VsTrashcan.Server;
using Xunit;

namespace VsTrashcan.Tests
{
    public class TrashcanServerTests
    {
        private readonly TestWorld world = new TestWorld();
        private readonly TrashcanServer server;
        private readonly FakeServerPlayer alice = new FakeServerPlayer("uid-alice", "Alice");
        private readonly FakeServerPlayer bob = new FakeServerPlayer("uid-bob", "Bob");
        private readonly Item copper, tin, pickaxe;
        private readonly Block bamboo, gravel;

        public TrashcanServerTests()
        {
            server = new TrashcanServer(world.Api.Object);
            copper = world.AddItem("game:ingot-copper", 10);
            tin = world.AddItem("game:ingot-tin", 11);
            pickaxe = world.AddItem("game:pickaxe-steel", 1894, maxStackSize: 1, durability: 1000);
            bamboo = world.AddBlock("game:bambooshoots-brown", 1894);
            gravel = world.AddBlock("game:gravel-granite", 10); // same number as copper
        }

        // Clicks a filter slot on the server's copy of the inventory, as the game does when the client's packet arrives
        private static void SetFilter(TrashcanInventory inv, int index, ItemStack held)
        {
            var mouse = new DummySlot(held);
            var op = new ItemStackMoveOperation(null, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, held?.StackSize ?? 0);
            inv.ActivateSlot(TrashcanInventory.FirstFilterSlotId + index, mouse, ref op);
        }

        private static byte[] LegacyBytes(ItemStack stack)
        {
            using var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);
            stack.ToBytes(writer);
            return ms.GetBuffer();
        }

        // --- Joining ---

        [Fact]
        public void JoiningRegistersTheInventoryWithThePlayer()
        {
            var inv = server.OnPlayerJoin(alice);

            Assert.Same(inv, Assert.Single(alice.OpenedInventories));
            Assert.Equal("vstrashcan-uid-alice", inv.InventoryID);
            Assert.All(inv.GetFilters(), Assert.Null);
        }

        [Fact]
        public void EachPlayerGetsTheirOwnInventory()
        {
            Assert.NotSame(server.OnPlayerJoin(alice), server.OnPlayerJoin(bob));
            Assert.NotSame(server.GetInventory(alice.PlayerUID), server.GetInventory(bob.PlayerUID));
        }

        // --- Auto-trash decision ---

        [Fact]
        public void OnlyMatchingPickupsAreAutoTrashed()
        {
            SetFilter(server.OnPlayerJoin(alice), 0, TestWorld.Stack(copper, 5));

            Assert.True(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(copper, 30)));
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(tin)));
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(gravel))); // same number as copper, but a block
        }

        [Fact]
        public void ABambooShootsFilterNeverTrashesSteelPickaxes()
        {
            SetFilter(server.OnPlayerJoin(alice), 0, TestWorld.Stack(bamboo));

            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(pickaxe)));
            Assert.True(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(bamboo)));
        }

        [Fact]
        public void APlayersFiltersNeverAffectAnotherPlayer()
        {
            SetFilter(server.OnPlayerJoin(alice), 0, TestWorld.Stack(copper));
            server.OnPlayerJoin(bob);

            Assert.False(server.ShouldAutoTrash(bob.PlayerUID, TestWorld.Stack(copper)));
        }

        [Fact]
        public void NoFiltersOrUnknownPlayersTrashNothing()
        {
            server.OnPlayerJoin(alice);

            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(copper)));
            Assert.False(server.ShouldAutoTrash("nobody", TestWorld.Stack(copper)));
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, null));
        }

        [Fact]
        public void ClearingAFilterStopsAutoTrash()
        {
            var inv = server.OnPlayerJoin(alice);
            SetFilter(inv, 0, TestWorld.Stack(copper));
            SetFilter(inv, 0, null);

            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(copper)));
        }

        [Fact]
        public void LeavingStopsAutoTrashAndDropsTheHistory()
        {
            SetFilter(server.OnPlayerJoin(alice), 0, TestWorld.Stack(copper));

            server.OnPlayerLeave(alice);

            Assert.Null(server.GetInventory(alice.PlayerUID));
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(copper)));
        }

        // --- Persistence ---

        [Fact]
        public void FiltersAreSavedWhenChangedAndComeBackInTheSameSlots()
        {
            var inv = server.OnPlayerJoin(alice);
            SetFilter(inv, 2, TestWorld.Stack(copper));
            SetFilter(inv, 9, TestWorld.Stack(bamboo));
            Assert.True(alice.Moddata.ContainsKey(FilterStore.ModdataKey));

            server.OnPlayerLeave(alice);
            var restarted = new TrashcanServer(world.Api.Object);
            var back = restarted.OnPlayerJoin(alice).GetFilters();

            Assert.Same(copper, back[2].Collectible);
            Assert.Same(bamboo, back[9].Collectible);
            Assert.Equal(2, back.Count(f => f != null));
            Assert.False(restarted.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(pickaxe)));
        }

        [Fact]
        public void FiltersThatNoLongerExistAreDroppedWithAWarning()
        {
            var gadget = world.AddItem("othermod:gadget", 99);
            SetFilter(server.OnPlayerJoin(alice), 0, TestWorld.Stack(gadget));
            world.Items.Remove(gadget);

            var back = new TrashcanServer(world.Api.Object).OnPlayerJoin(alice).GetFilters();

            Assert.All(back, Assert.Null);
            world.Logger.Verify(l => l.Warning(It.Is<string>(s => s.Contains("othermod:gadget"))), Times.Once());
        }

        // --- 1.0.x migration ---

        private void StoreLegacy(Dictionary<string, List<byte[]>> filters)
        {
            world.SaveGameData[LegacyFilters.SaveGameKey] = SerializerUtil.Serialize(filters);
        }

        [Fact]
        public void LegacyFiltersAreConvertedOnFirstJoinWithoutMixingItemsAndBlocks()
        {
            StoreLegacy(new Dictionary<string, List<byte[]>>
            {
                [alice.PlayerUID] = new List<byte[]> { LegacyBytes(TestWorld.Stack(bamboo)), LegacyBytes(TestWorld.Stack(copper)) },
                [bob.PlayerUID] = new List<byte[]> { LegacyBytes(TestWorld.Stack(tin)) },
            });

            var filters = server.OnPlayerJoin(alice).GetFilters();

            Assert.Same(bamboo, filters[0].Collectible);
            Assert.Same(copper, filters[1].Collectible);
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(pickaxe)));
            Assert.False(server.ShouldAutoTrash(alice.PlayerUID, TestWorld.Stack(gravel)));
            Assert.True(alice.Moddata.ContainsKey(FilterStore.ModdataKey));

            var remaining = LegacyFilters.Read(world.SaveGameData[LegacyFilters.SaveGameKey]);
            Assert.Equal(new[] { bob.PlayerUID }, remaining.Keys);
        }

        [Fact]
        public void LegacyFiltersAreOnlyConvertedOnce()
        {
            StoreLegacy(new Dictionary<string, List<byte[]>> { [alice.PlayerUID] = new List<byte[]> { LegacyBytes(TestWorld.Stack(copper)) } });
            var inv = server.OnPlayerJoin(alice);
            SetFilter(inv, 0, null); // the player clears the converted filter

            server.OnPlayerLeave(alice);
            var back = new TrashcanServer(world.Api.Object).OnPlayerJoin(alice).GetFilters();

            Assert.All(back, Assert.Null);
        }

        [Fact]
        public void SavedFiltersWinOverLegacyOnes()
        {
            alice.Moddata[FilterStore.ModdataKey] = FilterStore.Serialize(new[] { TestWorld.Stack(tin) });
            StoreLegacy(new Dictionary<string, List<byte[]>> { [alice.PlayerUID] = new List<byte[]> { LegacyBytes(TestWorld.Stack(copper)) } });

            var filters = server.OnPlayerJoin(alice).GetFilters();

            Assert.Same(tin, filters[0].Collectible);
            Assert.Equal(1, filters.Count(f => f != null));
        }

        [Fact]
        public void PlayersWithoutLegacyFiltersStartEmpty()
        {
            StoreLegacy(new Dictionary<string, List<byte[]>> { [bob.PlayerUID] = new List<byte[]> { LegacyBytes(TestWorld.Stack(copper)) } });

            Assert.All(server.OnPlayerJoin(alice).GetFilters(), Assert.Null);
        }

        // --- Client sync and closing ---

        [Fact]
        public void ClientReadySendsEveryslot()
        {
            var inv = server.OnPlayerJoin(alice);
            inv.DirtySlots.Clear();

            server.OnClientReady(alice);

            Assert.Equal(inv.Count, inv.DirtySlots.Count);
        }

        [Fact]
        public void ClosingTheInventoryDestroysTheHistoryButNotFilters()
        {
            var inv = server.OnPlayerJoin(alice);
            var mouse = new DummySlot(TestWorld.Stack(copper, 20));
            var op = new ItemStackMoveOperation(null, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 20);
            inv.ActivateSlot(TrashcanInventory.TrashSlotId, mouse, ref op);
            SetFilter(inv, 0, TestWorld.Stack(tin));

            server.OnInventoryClosed(alice);

            Assert.All(inv.HistorySlots, s => Assert.True(s.Empty));
            Assert.Same(tin, inv.GetFilters()[0].Collectible);
        }

        [Fact]
        public void MessagesForPlayersWhoLeftAreIgnored()
        {
            server.OnClientReady(alice);
            server.OnInventoryClosed(alice);
            server.OnPlayerLeave(alice);
        }
    }
}
