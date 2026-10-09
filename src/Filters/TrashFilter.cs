using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsTrashcan.Filters
{
    public static class TrashFilter
    {
        /*
            A filter sample matches exactly the stacks that would merge with it, using the game's own stacking rule
            (CollectibleObject.Equals with GlobalConstants.IgnoredStackAttributes): same item or block, same attributes,
            ignoring stack size, temperature, tool mode, render variant and spoilage. So a filtered empty crock never
            matches a crock with food in it, and tools only match at the same wear.

            1.0.x compared only ItemStack.Id. Items and blocks are numbered separately, so an item and a block can share
            an Id (a steel pickaxe and brown bamboo shoots did), and filtering one deleted the other. The class is
            checked first here for exactly that reason, before handing over to the collectible (containers such as crocks
            override Equals to unpack their contents before comparing).
        */
        public static bool Matches(ItemStack sample, ItemStack candidate)
        {
            if (sample?.Collectible == null || candidate?.Collectible == null)
            {
                return false;
            }

            if (sample.Class != candidate.Class || sample.Id != candidate.Id || sample.Collectible != candidate.Collectible)
            {
                return false;
            }

            return sample.Collectible.Equals(sample, candidate, GlobalConstants.IgnoredStackAttributes);
        }

        public static bool MatchesAny(IEnumerable<ItemStack> samples, ItemStack candidate)
        {
            foreach (ItemStack sample in samples)
            {
                if (Matches(sample, candidate))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
