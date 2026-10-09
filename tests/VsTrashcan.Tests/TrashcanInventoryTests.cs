using System.Linq;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTrashcan.Inventory;
using Xunit;

namespace VsTrashcan.Tests
{
    //
    // Slot behavior, run through the inventory's ActivateSlot exactly as the game's slot click packets do (on the client
    // and then again on the server). "mouse" is the item held on the cursor.
    //
    public class TrashcanInventoryTests
    {
        private readonly TestWorld world = new TestWorld();
        private readonly TrashcanInventory inv;
        private readonly Item copper, tin, iron;
        private readonly Item pickaxe;

        public TrashcanInventoryTests()
        {
            inv = new TrashcanInventory("player-1", world.Api.Object);
            copper = world.AddItem("game:ingot-copper", 10);
            tin = world.AddItem("game:ingot-tin", 11);
            iron = world.AddItem("game:ingot-iron", 12);
            pickaxe = world.AddItem("game:pickaxe-copper", 13, maxStackSize: 1, durability: 400);
        }

        private ItemStackMoveOperation Op(EnumMouseButton button, int quantity) =>
            new ItemStackMoveOperation(world.World.Object, button, 0, EnumMergePriority.AutoMerge, quantity);

        private void Click(int slotId, ItemSlot mouse, EnumMouseButton button = EnumMouseButton.Left)
        {
            var op = Op(button, mouse.StackSize);
            inv.ActivateSlot(slotId, mouse, ref op);
        }

        private static DummySlot Hand(ItemStack stack = null) => new DummySlot(stack);
        private ItemSlot History(int i) => inv[TrashcanInventory.FirstHistorySlotId + i];
        private int Filter(int i) => TrashcanInventory.FirstFilterSlotId + i;
        // Codes in the history, newest first, with the trailing empty slots left off
        private string[] HistoryCodes() => inv.HistorySlots.Select(s => s.Itemstack == null ? "-" : $"{s.Itemstack.Collectible.Code.Path}x{s.StackSize}")
            .Reverse().SkipWhile(c => c == "-").Reverse().ToArray();

        // --- Trash slot ---

        [Fact]
        public void LeftClickTrashesTheWholeHeldStack()
        {
            var mouse = Hand(TestWorld.Stack(copper, 20));

            Click(TrashcanInventory.TrashSlotId, mouse);

            Assert.True(mouse.Empty);
            Assert.True(inv.TrashSlot.Empty);
            Assert.Equal(new[] { "ingot-copperx20" }, HistoryCodes());
        }

        [Fact]
        public void RightClickTrashesExactlyOneAndKeepsTheRestInHand()
        {
            // 1.0.x deleted the whole held stack on the server while the client still showed the rest in hand
            var mouse = Hand(TestWorld.Stack(copper, 20));

            Click(TrashcanInventory.TrashSlotId, mouse, EnumMouseButton.Right);

            Assert.Equal(19, mouse.StackSize);
            Assert.Equal(new[] { "ingot-copperx1" }, HistoryCodes());
        }

        [Fact]
        public void RepeatedRightClicksCollectInOneHistoryEntry()
        {
            var mouse = Hand(TestWorld.Stack(copper, 20));

            for (int i = 0; i < 3; i++) Click(TrashcanInventory.TrashSlotId, mouse, EnumMouseButton.Right);

            Assert.Equal(17, mouse.StackSize);
            Assert.Equal(new[] { "ingot-copperx3" }, HistoryCodes());
        }

        [Fact]
        public void DifferentItemsAreKeptNewestFirst()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 2)));
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(tin, 3)));

            Assert.Equal(new[] { "ingot-tinx3", "ingot-copperx2" }, HistoryCodes());
        }

        [Fact]
        public void ItemsThatDoNotStackGetTheirOwnEntries()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(pickaxe)));
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(pickaxe)));

            Assert.Equal(new[] { "pickaxe-copperx1", "pickaxe-copperx1" }, HistoryCodes());
        }

        [Fact]
        public void SameItemWithDifferentAttributesGetsItsOwnEntry()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 2)));
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 2, a => a.SetString("variant", "x"))));

            Assert.Equal(new[] { "ingot-copperx2", "ingot-copperx2" }, HistoryCodes());
        }

        [Fact]
        public void MergingNeverExceedsTheMaxStackSize()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 63)));
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 5)));

            Assert.Equal(new[] { "ingot-copperx5", "ingot-copperx63" }, HistoryCodes());
        }

        [Fact]
        public void TheEleventhTrashedItemPushesTheOldestOut()
        {
            var items = Enumerable.Range(0, 11).Select(i => world.AddItem($"game:junk-{i}", 100 + i)).ToArray();

            foreach (var item in items) Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(item)));

            Assert.Equal(Enumerable.Range(1, 10).Reverse().Select(i => $"junk-{i}x1"), HistoryCodes());
        }

        [Fact]
        public void AnythingCanBeTrashedWhateverItsStorageType()
        {
            var backpack = world.AddItem("game:backpack-normal", 20, maxStackSize: 1);
            backpack.StorageFlags = EnumItemStorageFlags.Backpack;
            var mouse = Hand(TestWorld.Stack(backpack));

            Click(TrashcanInventory.TrashSlotId, mouse);

            Assert.True(mouse.Empty);
            Assert.Equal("backpack-normalx1", HistoryCodes()[0]);
        }

        [Fact]
        public void NothingCanBeTakenOutOfTheTrashSlotItself()
        {
            Assert.False(inv.TrashSlot.CanTake());
        }

        // --- Recently trashed ---

        [Fact]
        public void AHistoryItemCanBeTakenBackIntoAnEmptyHand()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 20)));
            var mouse = Hand();

            Click(TrashcanInventory.FirstHistorySlotId, mouse);

            Assert.Equal(20, mouse.StackSize);
            Assert.Equal(copper, mouse.Itemstack.Collectible);
            Assert.True(History(0).Empty);
        }

        [Fact]
        public void NothingCanBePutIntoOrSwappedWithTheHistory()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 20)));

            var holdingTin = Hand(TestWorld.Stack(tin, 4));
            Click(TrashcanInventory.FirstHistorySlotId, holdingTin);
            var holdingCopper = Hand(TestWorld.Stack(copper, 4));
            Click(TrashcanInventory.FirstHistorySlotId, holdingCopper, EnumMouseButton.Right);
            var holdingIron = Hand(TestWorld.Stack(iron, 4));
            Click(TrashcanInventory.FirstHistorySlotId + 1, holdingIron);

            Assert.Equal(4, holdingTin.StackSize);
            Assert.Equal(4, holdingCopper.StackSize);
            Assert.Equal(4, holdingIron.StackSize);
            Assert.Equal(new[] { "ingot-copperx20" }, HistoryCodes());
        }

        [Fact]
        public void ClearHistoryDestroysTheHistoryButKeepsFilters()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 20)));
            Click(Filter(3), Hand(TestWorld.Stack(tin)));

            inv.ClearHistory();

            Assert.All(inv.HistorySlots, s => Assert.True(s.Empty));
            Assert.True(inv.TrashSlot.Empty);
            Assert.Equal(tin, inv[Filter(3)].Itemstack.Collectible);
        }

        // --- Filter slots ---

        [Fact]
        public void ClickingAFilterSlotRecordsACopyAndTrashesTheHeldStackIntoHistory()
        {
            ItemStack held = TestWorld.Stack(copper, 20, a => a.SetString("variant", "x"));
            var mouse = Hand(held);

            Click(Filter(0), mouse);

            Assert.True(mouse.Empty);
            Assert.Same(held, History(0).Itemstack); // recoverable, not destroyed
            Assert.Equal(20, History(0).StackSize);
            var filter = inv[Filter(0)].Itemstack;
            Assert.NotSame(held, filter);
            Assert.Equal(1, filter.StackSize);
            Assert.Equal(copper, filter.Collectible);
            Assert.Equal("x", filter.Attributes.GetString("variant"));
        }

        [Theory]
        [InlineData(EnumMouseButton.Left)]
        [InlineData(EnumMouseButton.Right)]
        [InlineData(EnumMouseButton.Middle)]
        [InlineData(EnumMouseButton.Wheel)]
        public void EveryMouseButtonSetsTheFilterAndKeepsTheWholeHeldStackRecoverable(EnumMouseButton button)
        {
            var mouse = Hand(TestWorld.Stack(copper, 20));

            Click(Filter(0), mouse, button);

            Assert.True(mouse.Empty);
            Assert.Equal(copper, inv[Filter(0)].Itemstack.Collectible);
            Assert.Equal(new[] { "ingot-copperx20" }, HistoryCodes());
        }

        [Fact]
        public void TheHeldStackCanBeTakenBackAfterSettingAFilter()
        {
            Click(Filter(0), Hand(TestWorld.Stack(copper, 20)));
            var mouse = Hand();

            Click(TrashcanInventory.FirstHistorySlotId, mouse);

            Assert.Equal(20, mouse.StackSize);
            Assert.Equal(copper, inv[Filter(0)].Itemstack.Collectible); // the filter stays
        }

        // Like the game's empty meal bowls and cooked pots, which turn into a different block when put in a slot
        private class ShapeshiftingItem : Item
        {
            public Item TurnsInto;
            public ShapeshiftingItem() : base(0) { }
            public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null)
            {
                slot.Itemstack = new ItemStack(TurnsInto);
            }
        }

        [Fact]
        public void AFilterHoldsExactlyWhatWasClickedEvenIfTheItemWouldTransformInASlot()
        {
            var shapeshifter = new ShapeshiftingItem { Code = new AssetLocation("game:bowl-meal"), ItemId = 50, TurnsInto = tin };
            world.Items.Add(shapeshifter);
            var mouse = Hand(new ItemStack(shapeshifter, 1));

            Click(Filter(0), mouse);

            Assert.Same(shapeshifter, inv[Filter(0)].Itemstack.Collectible);
            Assert.Same(shapeshifter, History(0).Itemstack.Collectible);
        }

        [Fact]
        public void TheFilterCopyIsIndependentOfTheHeldStack()
        {
            Click(Filter(0), Hand(TestWorld.Stack(copper, 20)));

            History(0).Itemstack.Attributes.SetString("variant", "changed"); // the original stack
            History(0).Itemstack.StackSize = 3;

            Assert.Null(inv[Filter(0)].Itemstack.Attributes.GetString("variant"));
            Assert.Equal(1, inv[Filter(0)].StackSize);
        }

        [Fact]
        public void ClickingWithAnotherItemReplacesTheFilter()
        {
            Click(Filter(0), Hand(TestWorld.Stack(copper)));
            var mouse = Hand(TestWorld.Stack(tin, 2));

            Click(Filter(0), mouse);

            Assert.Equal(tin, inv[Filter(0)].Itemstack.Collectible);
            Assert.True(mouse.Empty);
            Assert.Equal(new[] { "ingot-tinx2", "ingot-copperx1" }, HistoryCodes());
        }

        [Fact]
        public void ClickingWithAnEmptyHandClearsTheFilterAndGivesNothing()
        {
            Click(Filter(0), Hand(TestWorld.Stack(copper)));
            var mouse = Hand();

            Click(Filter(0), mouse);

            Assert.True(inv[Filter(0)].Empty);
            Assert.True(mouse.Empty); // the filter sample is not an item you can take
        }

        [Fact]
        public void AFilterSampleCannotBeTakenOrPutIntoAnotherSlot()
        {
            Click(Filter(0), Hand(TestWorld.Stack(copper)));
            ItemSlot filter = inv[Filter(0)];
            var mouse = Hand();
            var op = Op(EnumMouseButton.Left, 1);

            Assert.False(filter.CanTake());
            Assert.Equal(0, filter.TryPutInto(mouse, ref op));
            Assert.True(mouse.Empty);
            Assert.False(filter.Empty);
        }

        [Fact]
        public void FiltersChangedFiresForFilterSlotsOnly()
        {
            int changes = 0;
            inv.FiltersChanged += () => changes++;

            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 5)));
            Click(TrashcanInventory.FirstHistorySlotId, Hand());
            Assert.Equal(0, changes);

            Click(Filter(0), Hand(TestWorld.Stack(copper)));
            Click(Filter(0), Hand());
            Assert.Equal(2, changes);
        }

        [Fact]
        public void SetAndGetFiltersKeepSlotPositions()
        {
            var filters = new ItemStack[TrashcanInventory.FilterCount];
            filters[0] = TestWorld.Stack(copper);
            filters[5] = TestWorld.Stack(tin);
            filters[15] = TestWorld.Stack(iron);

            inv.SetFilters(filters);

            var back = inv.GetFilters();
            Assert.Equal(TrashcanInventory.FilterCount, back.Length);
            for (int i = 0; i < back.Length; i++)
            {
                Assert.Equal(filters[i]?.Collectible, back[i]?.Collectible);
            }
        }

        // --- The inventory as a whole ---

        [Fact]
        public void ShiftClickAndAutomaticTransfersNeverTargetTheTrashcan()
        {
            // Shift-click in the backpack asks every open inventory for its best slot; the trashcan must never offer one
            foreach (var stack in new[] { TestWorld.Stack(copper, 20), TestWorld.Stack(pickaxe) })
            {
                var source = Hand(stack);
                WeightedSlot best = inv.GetBestSuitedSlot(source);

                Assert.Null(best.slot);
                Assert.Equal(0, best.weight);
                for (int i = 0; i < inv.Count; i++)
                {
                    Assert.Equal(0, inv.GetSuitability(source, inv[i], false));
                    Assert.Equal(0, inv.GetSuitability(source, inv[i], true));
                }
            }
        }

        [Theory]
        [InlineData(EnumTransitionType.Perish)]
        [InlineData(EnumTransitionType.Dry)]
        [InlineData(EnumTransitionType.Cure)]
        [InlineData(EnumTransitionType.Ripen)]
        [InlineData(EnumTransitionType.Melt)]
        [InlineData(EnumTransitionType.Harden)]
        public void NothingInTheTrashcanTransitions(EnumTransitionType type)
        {
            // A filter sample must never rot (or ripen, dry, ...) into a different item
            Assert.Equal(0, inv.GetTransitionSpeedMul(type, TestWorld.Stack(copper)));
        }

        [Fact]
        public void DyingDropsNothing()
        {
            Click(TrashcanInventory.TrashSlotId, Hand(TestWorld.Stack(copper, 20)));

            inv.OnOwningEntityDeath(new Vec3d(1, 2, 3));

            world.World.Verify(w => w.SpawnItemEntity(It.IsAny<ItemStack>(), It.IsAny<Vec3d>(), It.IsAny<Vec3d>()), Times.Never());
        }

        [Fact]
        public void InventoryIdIsTheSameOnBothSides()
        {
            Assert.Equal("vstrashcan-player-1", inv.InventoryID);
            Assert.Equal(inv.InventoryID, new TrashcanInventory("player-1", world.Api.Object).InventoryID);
            Assert.NotEqual(inv.InventoryID, new TrashcanInventory("player-2", world.Api.Object).InventoryID);
        }

        [Fact]
        public void LayoutHasOneTrashSlotTenHistorySlotsAndSixteenFilters()
        {
            Assert.Equal(1 + 10 + 16, inv.Count);
            Assert.IsType<ItemSlotTrash>(inv[0]);
            Assert.All(Enumerable.Range(1, 10), i => Assert.IsType<ItemSlotTrashHistory>(inv[i]));
            Assert.All(Enumerable.Range(11, 16), i => Assert.IsType<ItemSlotTrashFilter>(inv[i]));
            Assert.Null(inv[-1]);
            Assert.Null(inv[inv.Count]);
            Assert.Throws<System.NotSupportedException>(() => inv[0] = new DummySlot());
        }

        [Fact]
        public void FilterSlotsLookDifferentFromRealSlots()
        {
            Assert.All(inv.FilterSlots, s => Assert.Equal(ItemSlotTrashFilter.BackgroundColor, s.HexBackgroundColor));
            Assert.Null(inv.TrashSlot.HexBackgroundColor);
            Assert.All(inv.HistorySlots, s => Assert.Null(s.HexBackgroundColor));
        }

        [Fact]
        public void AnEmptyFilterSlotHasNoName()
        {
            // Names of real items need the game's translations, which aren't loaded here; the in-game self-test checks them
            Assert.Null(inv[Filter(0)].GetStackName());
        }

        [Fact]
        public void TheInventoryStaysRegisteredWhenClosed()
        {
            Assert.False(inv.RemoveOnClose);
        }
    }
}
