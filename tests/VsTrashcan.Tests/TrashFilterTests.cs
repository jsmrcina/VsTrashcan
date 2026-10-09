using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using VsTrashcan.Filters;
using Xunit;

namespace VsTrashcan.Tests
{
    //
    // The rule that decides what auto-trash destroys. The main goal: a filter must never match anything that wouldn't
    // stack with its sample. 1.0.x compared numeric ids only and deleted steel pickaxes via a bamboo shoots filter.
    //
    public class TrashFilterTests
    {
        private readonly TestWorld world = new TestWorld();
        private static ItemStack Stack(CollectibleObject c, int size = 1, System.Action<ITreeAttribute> attrs = null) => TestWorld.Stack(c, size, attrs);

        // --- Same item ---

        [Fact]
        public void SameItemMatches()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            Assert.True(TrashFilter.Matches(Stack(copper), Stack(copper)));
        }

        [Fact]
        public void SameBlockMatches()
        {
            var gravel = world.AddBlock("game:gravel-granite", 10);
            Assert.True(TrashFilter.Matches(Stack(gravel), Stack(gravel)));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 64)]
        [InlineData(64, 1)]
        [InlineData(1, 999)]
        public void StackSizeIsIgnored(int sampleSize, int candidateSize)
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            Assert.True(TrashFilter.Matches(Stack(copper, sampleSize), Stack(copper, candidateSize)));
        }

        // --- Items vs blocks: the 1.0.x bug ---

        [Fact]
        public void ItemAndBlockWithTheSameIdNeverMatch()
        {
            // The real report: steel pickaxes and brown bamboo shoots both had id 1894
            var pickaxe = world.AddItem("game:pickaxe-steel", 1894, maxStackSize: 1, durability: 1000);
            var bamboo = world.AddBlock("game:bambooshoots-brown", 1894);

            Assert.Equal(Stack(pickaxe).Id, Stack(bamboo).Id); // the collision is real
            Assert.False(TrashFilter.Matches(Stack(bamboo), Stack(pickaxe)));
            Assert.False(TrashFilter.Matches(Stack(pickaxe), Stack(bamboo)));
        }

        [Fact]
        public void ItemAndBlockWithTheSameCodeNeverMatch()
        {
            var item = world.AddItem("game:clay-blue", 50);
            var block = world.AddBlock("game:clay-blue", 51);

            Assert.False(TrashFilter.Matches(Stack(item), Stack(block)));
            Assert.False(TrashFilter.Matches(Stack(block), Stack(item)));
        }

        [Fact]
        public void ItemAndBlockWithTheSameIdAndCodeNeverMatch()
        {
            var item = world.AddItem("game:clay-blue", 50);
            var block = world.AddBlock("game:clay-blue", 50);

            Assert.False(TrashFilter.Matches(Stack(item), Stack(block)));
            Assert.False(TrashFilter.Matches(Stack(block), Stack(item)));
        }

        [Fact]
        public void FiredCrocksAreNotMatchedByAnItemSharingTheirId()
        {
            // The other real report: "all fired crocks disappear"
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var item = world.AddItem("game:flaxfibers", 300);

            Assert.False(TrashFilter.Matches(Stack(item), Stack(crock)));
            Assert.False(TrashFilter.Matches(Stack(crock), Stack(item)));
        }

        // --- Different items ---

        [Fact]
        public void NeighbouringVariantsDoNotMatch()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var tin = world.AddItem("game:ingot-tin", 11);

            Assert.False(TrashFilter.Matches(Stack(copper), Stack(tin)));
            Assert.False(TrashFilter.Matches(Stack(tin), Stack(copper)));
        }

        [Fact]
        public void SameCodeInDifferentDomainsDoesNotMatch()
        {
            var vanilla = world.AddItem("game:ingot-copper", 10);
            var modded = world.AddItem("othermod:ingot-copper", 11);

            Assert.False(TrashFilter.Matches(Stack(vanilla), Stack(modded)));
        }

        [Fact]
        public void TwoDistinctObjectsSharingAnIdDoNotMatch()
        {
            // Can't happen in a consistent world, but the rule must not depend on that
            var a = world.AddItem("game:ingot-copper", 10);
            var b = world.AddItem("game:ingot-tin", 10);

            Assert.False(TrashFilter.Matches(Stack(a), Stack(b)));
        }

        // --- Attributes ---

        [Fact]
        public void DifferentAttributesDoNotMatch()
        {
            var meal = world.AddItem("game:bowl-meal", 20);
            var stew = Stack(meal, attrs: a => a.SetString("recipeCode", "vegetablestew"));
            var porridge = Stack(meal, attrs: a => a.SetString("recipeCode", "porridge"));

            Assert.False(TrashFilter.Matches(stew, porridge));
            Assert.False(TrashFilter.Matches(porridge, stew));
        }

        [Fact]
        public void AnExtraAttributeOnTheCandidateDoesNotMatch()
        {
            var item = world.AddItem("game:book", 20);
            var plain = Stack(item);
            var written = Stack(item, attrs: a => a.SetString("text", "my notes"));

            Assert.False(TrashFilter.Matches(plain, written));
            Assert.False(TrashFilter.Matches(written, plain));
        }

        [Fact]
        public void SameAttributesMatch()
        {
            var meal = world.AddItem("game:bowl-meal", 20);
            Assert.True(TrashFilter.Matches(
                Stack(meal, attrs: a => a.SetString("recipeCode", "porridge")),
                Stack(meal, attrs: a => a.SetString("recipeCode", "porridge"))));
        }

        [Theory]
        [InlineData("temperature")]
        [InlineData("toolMode")]
        [InlineData("renderVariant")]
        [InlineData("transitionstate")]
        public void AttributesTheGameIgnoresWhenStackingAreIgnored(string attribute)
        {
            var meat = world.AddItem("game:redmeat-raw", 30);
            var plain = Stack(meat);
            var withAttribute = Stack(meat, attrs: a => a.GetOrAddTreeAttribute(attribute).SetFloat("value", 123));

            Assert.True(TrashFilter.Matches(plain, withAttribute));
            Assert.True(TrashFilter.Matches(withAttribute, plain));
        }

        [Fact]
        public void IgnoredAttributesDoNotHideARealDifference()
        {
            var meal = world.AddItem("game:bowl-meal", 20);
            var hotStew = Stack(meal, attrs: a => { a.SetString("recipeCode", "vegetablestew"); a.GetOrAddTreeAttribute("temperature").SetFloat("temperature", 80); });
            var coldPorridge = Stack(meal, attrs: a => a.SetString("recipeCode", "porridge"));

            Assert.False(TrashFilter.Matches(hotStew, coldPorridge));
        }

        [Fact]
        public void ToolsOnlyMatchAtTheSameWear()
        {
            var pickaxe = world.AddItem("game:pickaxe-copper", 40, maxStackSize: 1, durability: 400);
            var fresh = Stack(pickaxe);
            var worn = Stack(pickaxe, attrs: a => a.SetInt("durability", 12));
            var alsoWorn = Stack(pickaxe, attrs: a => a.SetInt("durability", 12));

            Assert.False(TrashFilter.Matches(fresh, worn));
            Assert.False(TrashFilter.Matches(worn, fresh));
            Assert.True(TrashFilter.Matches(worn, alsoWorn));
        }

        // --- Containers (crocks) ---

        [Fact]
        public void AnEmptyCrockDoesNotMatchAFilledOne()
        {
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var carrot = world.AddItem("game:vegetable-carrot", 60);

            var empty = Stack(crock);
            var filled = Stack(crock);
            crock.SetContents(filled, new[] { Stack(carrot, 4) });

            Assert.False(TrashFilter.Matches(empty, filled));
            Assert.False(TrashFilter.Matches(filled, empty));
        }

        [Fact]
        public void CrocksWithDifferentContentsDoNotMatch()
        {
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var carrot = world.AddItem("game:vegetable-carrot", 60);
            var onion = world.AddItem("game:vegetable-onion", 61);

            var carrots = Stack(crock);
            crock.SetContents(carrots, new[] { Stack(carrot, 4) });
            var onions = Stack(crock);
            crock.SetContents(onions, new[] { Stack(onion, 4) });

            Assert.False(TrashFilter.Matches(carrots, onions));
        }

        [Fact]
        public void CrocksWithTheSameContentsMatch()
        {
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var carrot = world.AddItem("game:vegetable-carrot", 60);

            var a = Stack(crock);
            crock.SetContents(a, new[] { Stack(carrot, 4) });
            var b = Stack(crock);
            crock.SetContents(b, new[] { Stack(carrot, 4) });

            Assert.True(TrashFilter.Matches(a, b));
        }

        [Fact]
        public void ASealedCrockDoesNotMatchAnUnsealedOne()
        {
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var unsealed = Stack(crock);
            var sealedCrock = Stack(crock, attrs: a => a.SetBool("sealed", true));

            Assert.False(TrashFilter.Matches(unsealed, sealedCrock));
        }

        // --- Missing or unresolved stacks ---

        [Fact]
        public void NullOrUnresolvedStacksNeverMatch()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var unresolved = new ItemStack { Id = 10, Class = EnumItemClass.Item };

            Assert.False(TrashFilter.Matches(null, Stack(copper)));
            Assert.False(TrashFilter.Matches(Stack(copper), null));
            Assert.False(TrashFilter.Matches(null, null));
            Assert.False(TrashFilter.Matches(unresolved, Stack(copper)));
            Assert.False(TrashFilter.Matches(Stack(copper), unresolved));
        }

        [Fact]
        public void MatchingDoesNotModifyEitherStack()
        {
            var crock = world.AddBlock<BlockCrock>("game:crock-burned", 300);
            var carrot = world.AddItem("game:vegetable-carrot", 60);
            var sample = Stack(crock);
            var candidate = Stack(crock, 3);
            crock.SetContents(candidate, new[] { Stack(carrot, 4) });
            byte[] sampleBefore = sample.ToBytes(), candidateBefore = candidate.ToBytes();

            TrashFilter.Matches(sample, candidate);

            Assert.Equal(sampleBefore, sample.ToBytes());
            Assert.Equal(candidateBefore, candidate.ToBytes());
            Assert.Equal(3, candidate.StackSize);
        }

        // --- Several filters ---

        [Fact]
        public void MatchesAnyNeedsAtLeastOneRealMatch()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            var tin = world.AddItem("game:ingot-tin", 11);
            var gravel = world.AddBlock("game:gravel-granite", 10);
            var filters = new[] { Stack(tin), Stack(gravel) };

            Assert.False(TrashFilter.MatchesAny(filters, Stack(copper)));
            Assert.True(TrashFilter.MatchesAny(filters, Stack(tin)));
            Assert.True(TrashFilter.MatchesAny(filters, Stack(gravel)));
        }

        [Fact]
        public void NoFiltersMatchNothing()
        {
            var copper = world.AddItem("game:ingot-copper", 10);
            Assert.False(TrashFilter.MatchesAny(new ItemStack[0], Stack(copper)));
        }

        /*
            Exhaustive: a world full of items and blocks whose ids and codes deliberately overlap (every item id is also a
            block id, several codes exist as both, some share a domain-less code), each with plain and attribute variants.
            Every stack is tried as a filter against every stack, and a match is only allowed when an independent oracle
            says they are the same thing: same class, same object, same id, same code, same non-ignored attributes.
        */
        [Fact]
        public void ExhaustiveNoFilterMatchesAnythingThatIsNotTheSameThing()
        {
            var stacks = new List<ItemStack>();
            for (int i = 1; i <= 60; i++)
            {
                var item = world.AddItem($"game:item-{i}", i, maxStackSize: i % 3 == 0 ? 1 : 64, durability: i % 3 == 0 ? 100 : 1);
                var block = world.AddBlock(i % 5 == 0 ? $"game:item-{i}" : $"game:block-{i}", i); // id always collides, code sometimes
                foreach (CollectibleObject c in new CollectibleObject[] { item, block })
                {
                    stacks.Add(Stack(c));
                    stacks.Add(Stack(c, 7));
                    stacks.Add(Stack(c, attrs: a => a.SetString("variant", "a")));
                    stacks.Add(Stack(c, attrs: a => a.SetString("variant", "b")));
                    stacks.Add(Stack(c, attrs: a => a.GetOrAddTreeAttribute("temperature").SetFloat("temperature", 500)));
                    stacks.Add(Stack(c, attrs: a => { a.SetString("variant", "a"); a.GetOrAddTreeAttribute("transitionstate").SetFloat("freshHours", 3); }));
                    stacks.Add(Stack(c, attrs: a => a.SetInt("durability", 5)));
                }
            }

            int matches = 0;
            var wrong = new List<string>();
            foreach (ItemStack sample in stacks)
            {
                foreach (ItemStack candidate in stacks)
                {
                    bool matched = TrashFilter.Matches(sample, candidate);
                    if (matched) matches++;
                    if (matched != SameThing(sample, candidate))
                    {
                        wrong.Add($"{Describe(sample)} vs {Describe(candidate)}: matched={matched}");
                    }
                }
            }

            Assert.Empty(wrong);
            Assert.True(matches > stacks.Count); // sanity: the oracle and the rule agree on real matches too
        }

        // Independent of TrashFilter: compares identity directly and attributes after removing the ignored ones
        private static bool SameThing(ItemStack a, ItemStack b)
        {
            if (a.Class != b.Class || a.Id != b.Id || a.Collectible != b.Collectible || !a.Collectible.Code.Equals(b.Collectible.Code))
            {
                return false;
            }
            return Strip(a).Equals(Strip(b));
        }

        private static string Strip(ItemStack s)
        {
            var copy = (TreeAttribute)s.Attributes.Clone();
            foreach (string ignored in Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes)
            {
                copy.RemoveAttribute(ignored);
            }
            return string.Join(";", copy.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value.ToJsonToken()));
        }

        private static string Describe(ItemStack s) => $"{s.Class} {s.Collectible.Code} #{s.Id} [{s.Attributes.ToJsonToken()}]";
    }
}
