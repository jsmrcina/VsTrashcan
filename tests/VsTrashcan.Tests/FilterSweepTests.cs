using System.Linq;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VsTrashcan.Inventory;
using Xunit;

namespace VsTrashcan.Tests
{
    //
    // Setting a filter moves the held stack and matching stacks already in the backpack and hotbar into "Recently
    // trashed" (once). This is the only place the mod takes items out of the player's own inventory, so the tests here
    // are about what must never be taken: look-alikes, bags, other inventories, and anything on the client side.
    //
    public class FilterSweepTests
    {
        private readonly TestWorld world = new TestWorld();
        private readonly FakeServerPlayer alice = new FakeServerPlayer("uid-alice", "Alice");
        private readonly TrashcanInventory trashcan;
        private readonly InventoryGeneric backpack, hotbar, other;
        private readonly Item copper, tin, bag;
        private readonly Block gravel;

        public TrashcanInventoryLayout Layout => new TrashcanInventoryLayout(trashcan);

        public FilterSweepTests()
        {
            copper = world.AddItem("game:ingot-copper", 10);
            tin = world.AddItem("game:ingot-tin", 11);
            bag = world.AddItem("game:backpack-normal", 12, maxStackSize: 1);
            bag.StorageFlags = EnumItemStorageFlags.Backpack;
            gravel = world.AddBlock("game:gravel-granite", 10); // same number as copper

            trashcan = new TrashcanInventory(alice.PlayerUID, world.Api.Object);

            // Like the game's backpack inventory: the first slots hold the equipped bags, the rest are the bags' contents
            backpack = new InventoryGeneric(12, GlobalConstants.backpackInvClassName, alice.PlayerUID, world.Api.Object,
                (id, self) => id < 2 ? new ItemSlotBackpack(self) : new ItemSlot(self));
            hotbar = new InventoryGeneric(10, GlobalConstants.hotBarInvClassName, alice.PlayerUID, world.Api.Object);
            other = new InventoryGeneric(4, GlobalConstants.characterInvClassName, alice.PlayerUID, world.Api.Object);

            alice.InventoryManagerMock.Setup(m => m.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(backpack);
            alice.InventoryManagerMock.Setup(m => m.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(hotbar);
            alice.InventoryManagerMock.Setup(m => m.GetOwnInventory(GlobalConstants.characterInvClassName)).Returns(other);
        }

        public class TrashcanInventoryLayout
        {
            private readonly TrashcanInventory inv;
            public TrashcanInventoryLayout(TrashcanInventory inv) { this.inv = inv; }
            public string[] History => inv.HistorySlots.Where(s => !s.Empty).Select(s => $"{s.Itemstack.Collectible.Code.Path}x{s.StackSize}").ToArray();
        }

        private DummySlot SetFilter(int index, ItemStack held, IPlayer by = null)
        {
            var mouse = new DummySlot(held);
            var op = new ItemStackMoveOperation(world.World.Object, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, held?.StackSize ?? 0)
            {
                ActingPlayer = by ?? alice
            };
            trashcan.ActivateSlot(TrashcanInventory.FirstFilterSlotId + index, mouse, ref op);
            return mouse;
        }

        private static void Put(InventoryGeneric inv, int slot, ItemStack stack) => inv[slot].Itemstack = stack;

        [Fact]
        public void MatchingStacksInTheBackpackAndHotbarMoveToRecentlyTrashed()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 64));
            Put(backpack, 7, TestWorld.Stack(copper, 10));
            Put(hotbar, 3, TestWorld.Stack(copper, 5));

            SetFilter(0, TestWorld.Stack(copper, 1));

            Assert.True(backpack[2].Empty);
            Assert.True(backpack[7].Empty);
            Assert.True(hotbar[3].Empty);
            Assert.Equal(1 + 64 + 10 + 5, trashcan.HistorySlots.Where(s => !s.Empty).Sum(s => s.StackSize)); // held + swept
            Assert.All(trashcan.HistorySlots.Where(s => !s.Empty), s => Assert.Same(copper, s.Itemstack.Collectible));
        }

        [Fact]
        public void LookAlikesAndOtherThingsStayPut()
        {
            Put(backpack, 2, TestWorld.Stack(tin, 32));                                          // neighbouring variant
            Put(backpack, 3, TestWorld.Stack(gravel, 64));                                       // block with copper's number
            Put(backpack, 4, TestWorld.Stack(copper, 8, a => a.SetString("variant", "special"))); // same item, different attributes
            Put(hotbar, 0, TestWorld.Stack(tin, 1));
            Put(hotbar, 1, TestWorld.Stack(copper, 3));                                          // the only real match

            SetFilter(0, TestWorld.Stack(copper));

            Assert.Equal(32, backpack[2].StackSize);
            Assert.Same(gravel, backpack[3].Itemstack.Collectible);
            Assert.Equal(8, backpack[4].StackSize);
            Assert.Equal(1, hotbar[0].StackSize);
            Assert.True(hotbar[1].Empty);
            Assert.Equal(new[] { "ingot-copperx4" }, Layout.History); // the held one plus the 3 from the hotbar
        }

        [Fact]
        public void StacksThatDifferOnlyInIgnoredAttributesAreSwept()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 4, a => a.GetOrAddTreeAttribute("temperature").SetFloat("temperature", 900)));

            SetFilter(0, TestWorld.Stack(copper));

            Assert.True(backpack[2].Empty);
        }

        [Fact]
        public void EquippedBagsAreNeverTakenEvenWhenTheFilterIsABag()
        {
            Put(backpack, 0, TestWorld.Stack(bag)); // equipped
            Put(backpack, 1, TestWorld.Stack(bag)); // equipped
            Put(backpack, 5, TestWorld.Stack(bag)); // a spare bag carried in the backpack

            SetFilter(0, TestWorld.Stack(bag));

            Assert.False(backpack[0].Empty);
            Assert.False(backpack[1].Empty);
            Assert.True(backpack[5].Empty);
            Assert.Equal(new[] { "backpack-normalx1", "backpack-normalx1" }, Layout.History); // the spare and the held one
        }

        [Fact]
        public void TheHeldStackIsTrashedAlongWithTheSweptOnes()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 10));

            DummySlot mouse = SetFilter(0, TestWorld.Stack(copper, 20));

            Assert.True(mouse.Empty);
            Assert.Equal(new[] { "ingot-copperx30" }, Layout.History);
        }

        [Fact]
        public void OtherInventoriesAreNeverSwept()
        {
            Put(other, 0, TestWorld.Stack(copper, 1)); // e.g. worn gear

            SetFilter(0, TestWorld.Stack(copper));

            Assert.False(other[0].Empty);
            alice.InventoryManagerMock.Verify(m => m.GetOwnInventory(GlobalConstants.characterInvClassName), Times.Never());
        }

        [Fact]
        public void SweptStacksCanBeTakenBack()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 10));
            SetFilter(0, TestWorld.Stack(copper));
            var hand = new DummySlot();
            var op = new ItemStackMoveOperation(world.World.Object, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 0);

            trashcan.ActivateSlot(TrashcanInventory.FirstHistorySlotId, hand, ref op);

            Assert.Equal(11, hand.StackSize); // the held one merged with the swept 10
            Assert.Same(copper, hand.Itemstack.Collectible);
        }

        [Fact]
        public void MoreMatchingStacksThanHistorySlotsKeepsTheLastTen()
        {
            var many = new InventoryGeneric(20, GlobalConstants.backpackInvClassName, alice.PlayerUID, world.Api.Object);
            alice.InventoryManagerMock.Setup(m => m.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(many);
            for (int i = 0; i < 12; i++) Put(many, i, TestWorld.Stack(copper, 64));

            SetFilter(0, TestWorld.Stack(copper));

            Assert.All(Enumerable.Range(0, 12), i => Assert.True(many[i].Empty));
            Assert.Equal(TrashcanInventory.HistorySize, Layout.History.Length);
        }

        [Fact]
        public void SweptStacksMergeIntoOneEntryWhenTheyFit()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 10));
            Put(backpack, 3, TestWorld.Stack(copper, 20));

            SetFilter(0, TestWorld.Stack(copper));

            Assert.Equal(new[] { "ingot-copperx31" }, Layout.History); // held 1 + 10 + 20
        }

        [Fact]
        public void ClearingOrReplacingAFilterOnlySweepsTheNewSample()
        {
            SetFilter(0, TestWorld.Stack(copper));
            Put(backpack, 2, TestWorld.Stack(copper, 10)); // picked back up after the filter was set
            Put(backpack, 3, TestWorld.Stack(tin, 5));

            SetFilter(0, null);                            // clearing sweeps nothing
            Assert.Equal(10, backpack[2].StackSize);

            SetFilter(0, TestWorld.Stack(tin));            // the new sample only
            Assert.Equal(10, backpack[2].StackSize);
            Assert.True(backpack[3].Empty);
        }

        [Fact]
        public void SettingAFilterAgainForTheSameItemSweepsAgain()
        {
            SetFilter(0, TestWorld.Stack(copper));
            Put(backpack, 2, TestWorld.Stack(copper, 10));

            SetFilter(0, TestWorld.Stack(copper));

            Assert.True(backpack[2].Empty);
        }

        [Fact]
        public void TheClientNeverSweeps()
        {
            // The server does the moving and the client gets the result through slot updates
            world.Api.Setup(a => a.Side).Returns(EnumAppSide.Client);
            var clientTrashcan = new TrashcanInventory(alice.PlayerUID, world.Api.Object);
            Put(backpack, 2, TestWorld.Stack(copper, 10));
            var mouse = new DummySlot(TestWorld.Stack(copper));
            var op = new ItemStackMoveOperation(world.World.Object, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1) { ActingPlayer = alice };

            clientTrashcan.ActivateSlot(TrashcanInventory.FirstFilterSlotId, mouse, ref op);

            Assert.Equal(10, backpack[2].StackSize);
            Assert.Same(copper, clientTrashcan[TrashcanInventory.FirstFilterSlotId].Itemstack.Collectible);
        }

        [Fact]
        public void NoActingPlayerMeansNoSweep()
        {
            Put(backpack, 2, TestWorld.Stack(copper, 10));
            var mouse = new DummySlot(TestWorld.Stack(copper));
            var op = new ItemStackMoveOperation(world.World.Object, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);

            trashcan.ActivateSlot(TrashcanInventory.FirstFilterSlotId, mouse, ref op);

            Assert.Equal(10, backpack[2].StackSize);
        }

        [Fact]
        public void LoadingSavedFiltersNeverSweeps()
        {
            // Joining with saved filters must not take anything; the sweep only happens when the player sets a filter
            Put(backpack, 2, TestWorld.Stack(copper, 10));

            trashcan.SetFilters(new[] { TestWorld.Stack(copper) });

            Assert.Equal(10, backpack[2].StackSize);
            Assert.Empty(Layout.History);
        }

        [Fact]
        public void TrashingIntoTheTrashSlotNeverSweeps()
        {
            SetFilter(0, TestWorld.Stack(tin));
            Put(backpack, 2, TestWorld.Stack(copper, 10));
            var mouse = new DummySlot(TestWorld.Stack(copper, 3));
            var op = new ItemStackMoveOperation(world.World.Object, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 3) { ActingPlayer = alice };

            trashcan.ActivateSlot(TrashcanInventory.TrashSlotId, mouse, ref op);

            Assert.Equal(10, backpack[2].StackSize);
            Assert.Equal(new[] { "ingot-copperx3", "ingot-tinx1" }, Layout.History); // the trashed copper, then the tin used for the filter
        }
    }
}
