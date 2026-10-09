using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using VsTrashcan.Filters;
using Xunit;

namespace VsTrashcan.Tests
{
    public class FilterStoreTests
    {
        private const int Count = 16;
        private readonly TestWorld world = new TestWorld();

        private ItemStack[] RoundTrip(ItemStack[] filters, TestWorld loadIn = null, List<string> unresolved = null)
        {
            byte[] data = FilterStore.Serialize(filters);
            return FilterStore.Deserialize(data, Count, (loadIn ?? world).World.Object, s => unresolved?.Add(s));
        }

        [Fact]
        public void RoundTripKeepsEachFilterInItsSlotAndGapsEmpty()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var gravel = world.AddBlock("game:gravel-granite", 20);
            var filters = new ItemStack[Count];
            filters[0] = TestWorld.Stack(copper);
            filters[7] = TestWorld.Stack(gravel);
            filters[15] = TestWorld.Stack(copper, attributes: a => a.SetString("variant", "x"));

            var back = RoundTrip(filters);

            for (int i = 0; i < Count; i++)
            {
                if (filters[i] == null) Assert.Null(back[i]);
                else Assert.True(TrashFilter.Matches(filters[i], back[i]) && TrashFilter.Matches(back[i], filters[i]), $"slot {i}");
            }
            Assert.Equal("x", back[15].Attributes.GetString("variant"));
            Assert.Null(back[0].Attributes.GetString("variant"));
        }

        [Fact]
        public void AnItemAndABlockWithTheSameCodeComeBackAsTheRightOnes()
        {
            var item = world.AddItem("game:clay-blue", 50);
            var block = world.AddBlock("game:clay-blue", 50);
            var filters = new ItemStack[Count];
            filters[0] = TestWorld.Stack(item);
            filters[1] = TestWorld.Stack(block);

            var back = RoundTrip(filters);

            Assert.Same(item, back[0].Collectible);
            Assert.Equal(EnumItemClass.Item, back[0].Class);
            Assert.Same(block, back[1].Collectible);
            Assert.Equal(EnumItemClass.Block, back[1].Class);
            Assert.False(TrashFilter.Matches(back[1], TestWorld.Stack(item)));
            Assert.False(TrashFilter.Matches(back[0], TestWorld.Stack(block)));
        }

        [Fact]
        public void FiltersAreFoundByCodeEvenIfIdsChanged()
        {
            // Saved in a world where copper is item 10 and gravel block 20; loaded where those numbers mean other things
            var copper = world.AddItem("game:ingot-copper", 10);
            var gravel = world.AddBlock("game:gravel-granite", 20);
            var filters = new ItemStack[Count];
            filters[0] = TestWorld.Stack(copper);
            filters[1] = TestWorld.Stack(gravel);

            var other = new TestWorld();
            var otherDecoy = other.AddItem("game:pickaxe-steel", 10);
            var otherDecoyBlock = other.AddBlock("game:bambooshoots-brown", 20);
            var otherCopper = other.AddItem("game:ingot-copper", 77);
            var otherGravel = other.AddBlock("game:gravel-granite", 88);

            var back = RoundTrip(filters, other);

            Assert.Same(otherCopper, back[0].Collectible);
            Assert.Same(otherGravel, back[1].Collectible);
            Assert.False(TrashFilter.Matches(back[0], TestWorld.Stack(otherDecoy)));
            Assert.False(TrashFilter.Matches(back[1], TestWorld.Stack(otherDecoyBlock)));
        }

        [Fact]
        public void FiltersForThingsThatNoLongerExistAreDroppedAndReported()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var modded = world.AddItem("othermod:gadget", 11);
            var filters = new ItemStack[Count];
            filters[2] = TestWorld.Stack(modded);
            filters[3] = TestWorld.Stack(copper);
            byte[] data = FilterStore.Serialize(filters);
            world.Items.Remove(modded); // the mod was removed
            var unresolved = new List<string>();

            var back = FilterStore.Deserialize(data, Count, world.World.Object, unresolved.Add);

            Assert.Null(back[2]);
            Assert.Same(copper, back[3].Collectible); // the others keep their slots
            Assert.Equal(new[] { "item othermod:gadget" }, unresolved);
        }

        [Fact]
        public void NoDataMeansNoFilters()
        {
            Assert.All(FilterStore.Deserialize(null, Count, world.World.Object), Assert.Null);
            Assert.All(FilterStore.Deserialize(new byte[0], Count, world.World.Object), Assert.Null);
            Assert.All(RoundTrip(new ItemStack[Count]), Assert.Null);
        }

        [Fact]
        public void SamplesAreLoadedAsSingleItems()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var filters = new ItemStack[Count];
            filters[0] = TestWorld.Stack(copper, 40);

            Assert.Equal(1, RoundTrip(filters)[0].StackSize);
        }

        // --- 1.0.x save format ---

        // Exactly what 1.0.x wrote: ItemStack.ToBytes via MemoryStream.GetBuffer() (so with trailing padding), packed per player
        private static byte[] LegacyBytes(ItemStack stack)
        {
            using var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);
            stack.ToBytes(writer);
            return ms.GetBuffer();
        }

        [Fact]
        public void LegacyFiltersKeepTheItemOrBlockTheyWere()
        {
            // The 1.0.x matching bug ignored the class, but the class was saved, so conversion gets it right
            var pickaxe = world.AddItem("game:pickaxe-steel", 1894, maxStackSize: 1);
            var bamboo = world.AddBlock("game:bambooshoots-brown", 1894);
            var legacy = new List<byte[]> { LegacyBytes(TestWorld.Stack(bamboo)), LegacyBytes(TestWorld.Stack(pickaxe)) };

            var filters = LegacyFilters.Convert(legacy, Count, world.World.Object);

            Assert.Same(bamboo, filters[0].Collectible);
            Assert.Same(pickaxe, filters[1].Collectible);
            Assert.False(TrashFilter.Matches(filters[0], TestWorld.Stack(pickaxe)));
            Assert.False(TrashFilter.Matches(filters[1], TestWorld.Stack(bamboo)));
            Assert.All(filters.Skip(2), Assert.Null);
        }

        [Fact]
        public void LegacyAttributesAreKept()
        {
            var meal = world.AddItem("game:bowl-meal", 20);
            var legacy = new List<byte[]> { LegacyBytes(TestWorld.Stack(meal, 5, a => a.SetString("recipeCode", "porridge"))) };

            var filter = LegacyFilters.Convert(legacy, Count, world.World.Object)[0];

            Assert.Equal("porridge", filter.Attributes.GetString("recipeCode"));
            Assert.Equal(1, filter.StackSize);
            Assert.False(TrashFilter.Matches(filter, TestWorld.Stack(meal, attributes: a => a.SetString("recipeCode", "stew"))));
        }

        [Fact]
        public void BadLegacyEntriesAreSkippedAndReported()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var gone = world.AddItem("game:gone", 11);
            var goneBytes = LegacyBytes(TestWorld.Stack(gone));
            world.Items.Remove(gone);
            var legacy = new List<byte[]> { new byte[] { 1, 2 }, goneBytes, LegacyBytes(TestWorld.Stack(copper)) };
            var unresolved = new List<string>();

            var filters = LegacyFilters.Convert(legacy, Count, world.World.Object, unresolved.Add);

            Assert.Same(copper, filters[0].Collectible);
            Assert.All(filters.Skip(1), Assert.Null);
            Assert.Equal(2, unresolved.Count);
        }

        [Fact]
        public void LegacyListsLongerThanTheGridAreCapped()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var legacy = Enumerable.Range(0, 30).Select(_ => LegacyBytes(TestWorld.Stack(copper))).ToList();

            Assert.Equal(Count, LegacyFilters.Convert(legacy, Count, world.World.Object).Count(f => f != null));
        }

        [Fact]
        public void LegacySaveGameDataIsReadPerPlayer()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var dict = new Dictionary<string, List<byte[]>> { ["uid-a"] = new List<byte[]> { LegacyBytes(TestWorld.Stack(copper)) }, ["uid-b"] = new List<byte[]>() };

            var read = LegacyFilters.Read(SerializerUtil.Serialize(dict));

            Assert.Equal(new[] { "uid-a", "uid-b" }, read.Keys.OrderBy(k => k));
            Assert.Empty(LegacyFilters.Read(null));
        }
    }
}
