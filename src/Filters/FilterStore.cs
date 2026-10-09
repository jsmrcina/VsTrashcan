using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VsTrashcan.Filters
{
    /*
        Saves a player's filters by code, not by numeric Id: each slot stores its class (item/block), code and attributes,
        and empty slots stay empty (1.0.x packed the list and lost the slot positions). Codes are looked up again on load,
        so a filter can never come back as a different item.
    */
    public static class FilterStore
    {
        public const string ModdataKey = "vstrashcan:filters";
        private const int FormatVersion = 1;

        public static byte[] Serialize(IReadOnlyList<ItemStack> filters)
        {
            var root = new TreeAttribute();
            root.SetInt("version", FormatVersion);
            for (int i = 0; i < filters.Count; i++)
            {
                ItemStack stack = filters[i];
                if (stack?.Collectible?.Code == null)
                {
                    continue;
                }

                ITreeAttribute slot = root.GetOrAddTreeAttribute("slot" + i);
                slot.SetInt("class", (int)stack.Class);
                slot.SetString("code", stack.Collectible.Code.ToString());
                slot["attributes"] = stack.Attributes.Clone();
            }
            return root.ToBytes();
        }

        // Unresolvable entries (e.g. from a removed mod) are left empty and reported through onUnresolved
        public static ItemStack[] Deserialize(byte[] data, int count, IWorldAccessor world, Action<string> onUnresolved = null)
        {
            var filters = new ItemStack[count];
            if (data == null || data.Length == 0)
            {
                return filters;
            }

            TreeAttribute root = TreeAttribute.CreateFromBytes(data);
            for (int i = 0; i < count; i++)
            {
                ITreeAttribute slot = root.GetTreeAttribute("slot" + i);
                string code = slot?.GetString("code");
                if (code == null)
                {
                    continue;
                }

                var itemClass = (EnumItemClass)slot.GetInt("class");
                CollectibleObject collectible = itemClass == EnumItemClass.Block
                    ? world.GetBlock(new AssetLocation(code))
                    : world.GetItem(new AssetLocation(code));
                if (collectible == null)
                {
                    onUnresolved?.Invoke($"{itemClass.ToString().ToLowerInvariant()} {code}");
                    continue;
                }

                var stack = new ItemStack(collectible);
                if (slot["attributes"] is ITreeAttribute attributes)
                {
                    stack.Attributes = attributes.Clone();
                }
                filters[i] = stack;
            }
            return filters;
        }
    }

    /*
        1.0.x stored every player's filters in the save game under "trashCanFiltersByPlayerUid" as a
        Dictionary<playerUid, List<ItemStack.ToBytes()>> (class + numeric Id + attributes, packed without slot positions).
        Within the same world those Ids are still valid, and the class is in the bytes, so they resolve to the right
        item or block. Read once per player and then removed.
    */
    public static class LegacyFilters
    {
        public const string SaveGameKey = "trashCanFiltersByPlayerUid";

        public static Dictionary<string, List<byte[]>> Read(byte[] saveGameData)
        {
            if (saveGameData == null)
            {
                return new Dictionary<string, List<byte[]>>();
            }
            return SerializerUtil.Deserialize<Dictionary<string, List<byte[]>>>(saveGameData) ?? new Dictionary<string, List<byte[]>>();
        }

        public static ItemStack[] Convert(List<byte[]> legacyStacks, int count, IWorldAccessor world, Action<string> onUnresolved = null)
        {
            var filters = new ItemStack[count];
            int next = 0;
            foreach (byte[] bytes in legacyStacks ?? new List<byte[]>())
            {
                if (next >= count)
                {
                    break;
                }

                ItemStack stack;
                try
                {
                    stack = new ItemStack();
                    using (var reader = new BinaryReader(new MemoryStream(bytes)))
                    {
                        stack.FromBytes(reader);
                    }
                }
                catch (Exception e)
                {
                    onUnresolved?.Invoke($"unreadable legacy filter ({e.Message})");
                    continue;
                }

                if (!stack.ResolveBlockOrItem(world))
                {
                    onUnresolved?.Invoke($"legacy {stack.Class.ToString().ToLowerInvariant()} id {stack.Id}");
                    continue;
                }

                stack.StackSize = 1;
                filters[next++] = stack;
            }
            return filters;
        }
    }
}
