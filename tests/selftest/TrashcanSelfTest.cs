using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VsTrashcan.Filters;
using VsTrashcan.Inventory;

namespace VsTrashcan.SelfTest
{
    /*
        Runs once the server is up, against the real registry (every item and block, their creative inventory variants,
        filled containers and worn tools). Logs "VsTrashcan self-test: PASSED" or "... FAILED" plus the first failures.

        The central check uses an oracle independent of TrashFilter: two stacks are "the same thing" only if they are the
        same item or block object (same class, id and code) and their attributes are identical once the attributes the
        game ignores for stacking are removed. Any filter match the oracle doesn't agree with is a failure: that is a
        filter that would delete something else.
    */
    public class TrashcanSelfTest : ModSystem
    {
        private ICoreServerAPI api;
        private readonly List<string> failures = new List<string>();
        private readonly List<string> notes = new List<string>();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, Run);
        }

        private void Fail(string message)
        {
            failures.Add(message);
        }

        private void Run()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                List<ItemStack> stacks = CollectStacks();
                CheckFilterMatching(stacks);
                CheckIdCollisions();
                CheckFilterStore();
                CheckLegacyConversion();
                CheckSlots(stacks);
            }
            catch (Exception e)
            {
                Fail($"self-test crashed: {e}");
            }

            foreach (string note in notes)
            {
                api.Logger.Notification("VsTrashcan self-test: {0}", note);
            }
            if (failures.Count == 0)
            {
                api.Logger.Notification($"VsTrashcan self-test: PASSED in {watch.Elapsed.TotalSeconds:F1}s");
            }
            else
            {
                foreach (string failure in failures.Take(25))
                {
                    api.Logger.Error("VsTrashcan self-test: {0}", failure); // never as a format string: descriptions contain braces
                }
                api.Logger.Error($"VsTrashcan self-test: FAILED with {failures.Count} failures");
            }
        }

        // --- Test data from the real registry ---

        private IEnumerable<CollectibleObject> Collectibles => api.World.Collectibles.Where(c => c?.Code != null && c.Id != 0);

        private List<ItemStack> CollectStacks()
        {
            var stacks = new List<ItemStack>();
            ItemStack contentA = FirstStackOf("game:vegetable-carrot"), contentB = FirstStackOf("game:vegetable-onion");
            int creative = 0, containers = 0, worn = 0;

            foreach (CollectibleObject c in Collectibles)
            {
                stacks.Add(new ItemStack(c));

                foreach (var tab in c.CreativeInventoryStacks ?? Array.Empty<CreativeTabAndStackList>())
                {
                    foreach (JsonItemStack js in tab.Stacks ?? Array.Empty<JsonItemStack>())
                    {
                        if (js.ResolvedItemstack == null) js.Resolve(api.World, "vstrashcan self-test", false);
                        if (js.ResolvedItemstack != null)
                        {
                            stacks.Add(js.ResolvedItemstack.Clone());
                            creative++;
                        }
                    }
                }

                // Containers (crocks, pots, bowls, ...) holding different things, the 1.0.x "fired crocks disappear" case
                if (c is BlockContainer container && contentA != null && contentB != null)
                {
                    foreach (ItemStack content in new[] { contentA, contentB })
                    {
                        var filled = new ItemStack(c);
                        try
                        {
                            container.SetContents(filled, new[] { content.Clone() });
                            stacks.Add(filled);
                            containers++;
                        }
                        catch (Exception)
                        {
                            // some containers can't hold vegetables; the empty stack is still tested
                        }
                    }
                }

                // Worn tools
                if (c.GetMaxDurability(new ItemStack(c)) > 1)
                {
                    var wornStack = new ItemStack(c);
                    wornStack.Attributes.SetInt("durability", 1);
                    stacks.Add(wornStack);
                    worn++;
                }
            }

            notes.Add($"{Collectibles.Count()} items and blocks, {stacks.Count} stacks ({creative} creative variants, {containers} filled containers, {worn} worn tools)");
            return stacks;
        }

        private ItemStack FirstStackOf(string code)
        {
            CollectibleObject c = (CollectibleObject)api.World.GetItem(new AssetLocation(code)) ?? api.World.GetBlock(new AssetLocation(code));
            return c == null ? null : new ItemStack(c, 4);
        }

        // --- Checks ---

        private void CheckFilterMatching(List<ItemStack> stacks)
        {
            // Every pair that could possibly match (same class and id) is compared in full. Pairs with a different class
            // or id are covered by CheckIdCollisions (all item/block collisions) and a large random sample below.
            var buckets = stacks.GroupBy(s => (s.Class, s.Id)).ToList();
            long pairs = 0, matches = 0;
            int unmatchedSelf = 0;

            foreach (var bucket in buckets)
            {
                ItemStack[] group = bucket.ToArray();
                foreach (ItemStack sample in group)
                {
                    if (!TrashFilter.Matches(sample, sample.Clone()))
                    {
                        unmatchedSelf++; // a filter that can't match its own sample: useless, but never deletes anything
                    }

                    foreach (ItemStack candidate in group)
                    {
                        pairs++;
                        bool matched = TrashFilter.Matches(sample, candidate);
                        if (matched)
                        {
                            matches++;
                            if (!SameThing(sample, candidate))
                            {
                                Fail($"filter {Describe(sample)} would trash {Describe(candidate)}");
                            }
                        }
                    }
                }
            }

            var random = new Random(1234);
            int sampled = Math.Min(2_000_000, stacks.Count * 50);
            for (int i = 0; i < sampled; i++)
            {
                ItemStack a = stacks[random.Next(stacks.Count)], b = stacks[random.Next(stacks.Count)];
                if (TrashFilter.Matches(a, b) && !SameThing(a, b))
                {
                    Fail($"filter {Describe(a)} would trash {Describe(b)} (random pair)");
                }
            }

            notes.Add($"matching: {pairs} same-id pairs compared in full ({matches} matches), {sampled} random pairs, {unmatchedSelf} stacks that don't match themselves");
        }

        private void CheckIdCollisions()
        {
            var blocksById = api.World.Blocks.Where(b => b?.Code != null && b.Id != 0).GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());
            var collisions = new List<(Item, Block)>();
            foreach (Item item in api.World.Items.Where(i => i?.Code != null && i.Id != 0))
            {
                if (blocksById.TryGetValue(item.Id, out Block block))
                {
                    collisions.Add((item, block));
                }
            }

            foreach ((Item item, Block block) in collisions)
            {
                if (TrashFilter.Matches(new ItemStack(item), new ItemStack(block)) || TrashFilter.Matches(new ItemStack(block), new ItemStack(item)))
                {
                    Fail($"item {item.Code} and block {block.Code} share id {item.Id} and match each other");
                }
            }

            string examples = string.Join(", ", collisions.Take(3).Select(c => $"item {c.Item1.Code} / block {c.Item2.Code} (#{c.Item1.Id})"));
            notes.Add($"{collisions.Count} item/block id collisions in this world, none matching (e.g. {examples})");
            if (collisions.Count == 0)
            {
                Fail("expected item/block id collisions in a vanilla world; the collision check did not test anything");
            }
        }

        private void CheckFilterStore()
        {
            // Every item and block, saved and loaded in batches of one filter grid
            CollectibleObject[] all = Collectibles.ToArray();
            int checkedCount = 0;
            for (int start = 0; start < all.Length; start += TrashcanInventory.FilterCount)
            {
                var filters = new ItemStack[TrashcanInventory.FilterCount];
                for (int i = 0; i < filters.Length && start + i < all.Length; i += 2) // every other slot, so gaps are tested too
                {
                    filters[i] = new ItemStack(all[start + i]);
                }

                ItemStack[] back = FilterStore.Deserialize(FilterStore.Serialize(filters), TrashcanInventory.FilterCount, api.World,
                    what => Fail($"filter store could not resolve {what}"));

                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i] == null)
                    {
                        if (back[i] != null) Fail($"filter store filled empty slot {i} with {Describe(back[i])}");
                        continue;
                    }
                    checkedCount++;
                    if (back[i]?.Collectible != filters[i].Collectible || back[i].Class != filters[i].Class)
                    {
                        Fail($"filter store turned {Describe(filters[i])} into {(back[i] == null ? "nothing" : Describe(back[i]))}");
                    }
                }
            }
            notes.Add($"filter store: {checkedCount} filters saved and loaded");
        }

        private void CheckLegacyConversion()
        {
            // 1.0.x saved ItemStack.ToBytes() (class + id); every item and block must convert back to itself
            int checkedCount = 0;
            foreach (CollectibleObject c in Collectibles)
            {
                var original = new ItemStack(c);
                using var ms = new MemoryStream();
                var writer = new BinaryWriter(ms);
                original.ToBytes(writer);

                ItemStack back = LegacyFilters.Convert(new List<byte[]> { ms.GetBuffer() }, 1, api.World, what => Fail($"legacy conversion failed for {Describe(original)}: {what}"))[0];
                checkedCount++;
                if (back?.Collectible != c || back.Class != original.Class)
                {
                    Fail($"legacy conversion turned {Describe(original)} into {(back == null ? "nothing" : Describe(back))}");
                }
            }
            notes.Add($"legacy conversion: {checkedCount} filters converted");
        }

        private void CheckSlots(List<ItemStack> stacks)
        {
            // The real slot code with real items: right-click trashes exactly one, setting a filter keeps the held stack recoverable
            int checkedCount = 0;
            foreach (ItemStack template in stacks.Where((s, i) => i % 7 == 0))
            {
                var inv = new TrashcanInventory("selftest-" + checkedCount, api);
                int size = Math.Max(1, Math.Min(template.Collectible.MaxStackSize, 10));

                var mouse = new DummySlot(template.Clone());
                mouse.Itemstack.StackSize = size;
                var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, 1);
                inv.ActivateSlot(TrashcanInventory.TrashSlotId, mouse, ref op);
                int inHistory = inv.HistorySlots.Sum(s => s.StackSize);
                if (mouse.StackSize + inHistory != size || inHistory != 1)
                {
                    Fail($"right-click trash of {Describe(template)} x{size}: {inHistory} trashed, {mouse.StackSize} left in hand");
                }

                inv.ClearHistory();
                var held = new DummySlot(template.Clone());
                held.Itemstack.StackSize = size;
                byte[] before = held.Itemstack.ToBytes();
                op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, size);
                inv.ActivateSlot(TrashcanInventory.FirstFilterSlotId, held, ref op);
                // The held stack moves into the history unchanged (recoverable), it is not destroyed or altered
                ItemSlot newest = inv[TrashcanInventory.FirstHistorySlotId];
                if (!held.Empty || newest.Itemstack == null || !newest.Itemstack.ToBytes().SequenceEqual(before))
                {
                    Fail($"setting a filter with {Describe(template)} x{size} did not move the held stack into the history unchanged");
                }
                ItemStack recorded = inv[TrashcanInventory.FirstFilterSlotId].Itemstack;
                if (recorded?.Collectible != template.Collectible)
                {
                    Fail($"filter slot did not record {Describe(template)}, it holds {(recorded == null ? "nothing" : Describe(recorded))}");
                }
                string name = inv[TrashcanInventory.FirstFilterSlotId].GetStackName();
                if (name == null || !name.StartsWith(ItemSlotTrashFilter.NamePrefix) || name.Length <= ItemSlotTrashFilter.NamePrefix.Length)
                {
                    Fail($"filter slot tooltip for {Describe(template)} is '{name}'");
                }
                checkedCount++;
            }
            notes.Add($"slots: {checkedCount} real stacks trashed and used as filters");
        }

        // --- Oracle ---

        private static bool SameThing(ItemStack a, ItemStack b)
        {
            return a.Class == b.Class && a.Id == b.Id && a.Collectible == b.Collectible && a.Collectible.Code.Equals(b.Collectible.Code)
                && Strip(a) == Strip(b);
        }

        private static string Strip(ItemStack s)
        {
            var copy = (TreeAttribute)s.Attributes.Clone();
            foreach (string ignored in GlobalConstants.IgnoredStackAttributes)
            {
                copy.RemoveAttribute(ignored);
            }
            return Canonical(copy);
        }

        // Attribute trees with keys sorted, and stored item stacks (container contents) spelled out by code and size
        private static string Canonical(IAttribute attr)
        {
            switch (attr)
            {
                case ITreeAttribute tree:
                    return "{" + string.Join(",", tree.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + Canonical(kv.Value))) + "}";
                case ItemstackAttribute stackAttr:
                    ItemStack st = stackAttr.value;
                    return st == null ? "null" : $"<{st.Class} {st.Collectible?.Code} #{st.Id} x{st.StackSize} {Canonical(st.Attributes)}>";
                default:
                    return attr?.ToJsonToken() ?? "null";
            }
        }

        private static string Describe(ItemStack s) => $"{s.Class.ToString().ToLowerInvariant()} {s.Collectible?.Code} #{s.Id} {Canonical(s.Attributes)}";
    }
}
