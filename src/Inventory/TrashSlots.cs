using Vintagestory.API.Common;

namespace VsTrashcan.Inventory
{
    // Accepts anything dropped on it (any storage type), using the game's normal click rules, so a right click trashes
    // exactly one item. The inventory then moves it into the history straight away.
    public class ItemSlotTrash : ItemSlot
    {
        public ItemSlotTrash(InventoryBase inventory) : base(inventory)
        {
            StorageType = (EnumItemStorageFlags)~0;
        }

        // Nothing is ever taken back out of the trash slot itself; recovering happens from the history
        public override bool CanTake() => false;
    }

    // Recently trashed: items can be taken back out (into an empty hand) but never put in by the player
    public class ItemSlotTrashHistory : ItemSlot
    {
        public ItemSlotTrashHistory(InventoryBase inventory) : base(inventory)
        {
        }

        public override bool CanHold(ItemSlot sourceSlot) => false;

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;

        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            // Only "pick up into an empty hand"; anything else (swapping, dropping onto it) does nothing
            if (sourceSlot.Empty && !Empty)
            {
                base.ActivateSlot(sourceSlot, ref op);
            }
        }
    }

    /*
        An auto-trash filter: clicking with an item records a one-item copy and trashes the held stack (into the history,
        so it can be taken back). Clicking with an empty hand clears the filter. Nothing can be taken out or shift-clicked in.
    */
    public class ItemSlotTrashFilter : ItemSlot
    {
        // Tinted so filters read as "copies", not real slots holding an item
        public const string BackgroundColor = "#6b3a35";
        public const string NamePrefix = "Auto-trash filter (copy): ";

        public ItemSlotTrashFilter(InventoryBase inventory) : base(inventory)
        {
            MaxSlotStackSize = 1;
            HexBackgroundColor = BackgroundColor;
        }

        public override string GetStackName()
        {
            string name = base.GetStackName();
            return name == null ? null : NamePrefix + name;
        }

        public override bool CanHold(ItemSlot sourceSlot) => false;

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;

        public override bool CanTake() => false;

        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (sourceSlot.Empty)
            {
                if (Empty) return;
                Itemstack = null;
            }
            else
            {
                ItemStack sample = sourceSlot.Itemstack.Clone();
                sample.StackSize = 1;
                Itemstack = sample;
            }

            // Not MarkDirty(): that runs the collectible's OnModifiedInInventorySlot, and some (empty meal bowls and
            // cooked pots) turn themselves into a different block there. A filter must hold exactly what was clicked.
            var trashcan = (TrashcanInventory)inventory;
            trashcan.OnFilterSlotChanged(this);

            if (Itemstack != null)
            {
                // The held stack goes first (both sides, like the trash slot), then the server sweeps the inventory
                trashcan.TrashHeldStack(sourceSlot);
                trashcan.TrashMatchingStacks(Itemstack, op.ActingPlayer);
            }
        }
    }
}
