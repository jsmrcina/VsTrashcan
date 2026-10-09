using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VsTrashcan.Filters;

namespace VsTrashcan.Inventory
{
    //
    // One per player, created with the same ID on the server and the client and kept open on both, so the game's own
    // slot packets move items here exactly like in any other inventory (the server has the final say).
    //
    // Layout: [0] trash slot, [1..HistorySize] recently trashed (newest first), then the auto-trash filter grid.
    //
    public class TrashcanInventory : InventoryBase
    {
        public const string ClassNameCode = "vstrashcan";
        public const int HistorySize = 10;
        public const int FilterRows = 4;
        public const int FilterCols = 4;
        public const int FilterCount = FilterRows * FilterCols;

        public const int TrashSlotId = 0;
        public const int FirstHistorySlotId = 1;
        public const int FirstFilterSlotId = FirstHistorySlotId + HistorySize;

        private readonly ItemSlot[] slots;
        private bool pushing;

        // Raised after a filter slot changed (the server persists filters on this)
        public event Action FiltersChanged;

        public TrashcanInventory(string playerUid, ICoreAPI api) : base(ClassNameCode, playerUid, api)
        {
            slots = new ItemSlot[FirstFilterSlotId + FilterCount];
            slots[TrashSlotId] = new ItemSlotTrash(this);
            for (int i = 0; i < HistorySize; i++)
            {
                slots[FirstHistorySlotId + i] = new ItemSlotTrashHistory(this);
            }
            for (int i = 0; i < FilterCount; i++)
            {
                slots[FirstFilterSlotId + i] = new ItemSlotTrashFilter(this);
            }
        }

        // Null-safe in case anything enumerates the inventory while the base constructor runs, before the slots exist
        public override int Count => slots?.Length ?? 0;

        public override ItemSlot this[int slotId]
        {
            get => slots == null || slotId < 0 || slotId >= slots.Length ? null : slots[slotId];
            set => throw new NotSupportedException("Trashcan slots are fixed");
        }

        public ItemSlot TrashSlot => slots[TrashSlotId];

        public IEnumerable<ItemSlot> HistorySlots
        {
            get { for (int i = 0; i < HistorySize; i++) yield return slots[FirstHistorySlotId + i]; }
        }

        public IEnumerable<ItemSlot> FilterSlots
        {
            get { for (int i = 0; i < FilterCount; i++) yield return slots[FirstFilterSlotId + i]; }
        }

        public ItemSlot FilterSlot(int index) => slots[FirstFilterSlotId + index];

        // Kept registered with the player's inventory manager for the whole session
        public override bool RemoveOnClose => false;

        // Never a destination for shift-click or any automatic transfer: items only get here by being dropped on purpose.
        // Without this, shift-clicking an item in the backpack could move it into the trash.
        public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op = null, List<ItemSlot> skipSlots = null)
        {
            return new WeightedSlot();
        }

        public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
        {
            return 0;
        }

        // Nothing in here spoils or cools: a filter sample must not rot into a different item
        public override float GetTransitionSpeedMul(EnumTransitionType transType, ItemStack stack)
        {
            return 0;
        }

        // Trashed items are not dropped when the player dies
        public override void OnOwningEntityDeath(Vec3d pos)
        {
        }

        public override void OnItemSlotModified(ItemSlot slot)
        {
            base.OnItemSlotModified(slot);

            if (pushing)
            {
                return;
            }

            if (slot == TrashSlot && !slot.Empty)
            {
                ItemStack incoming = TrashSlot.Itemstack;
                TrashSlot.Itemstack = null;
                PushIntoHistory(incoming);
                MarkSlotDirty(TrashSlotId);
            }
        }

        // Setting a filter trashes the stack used to set it. Runs on both sides with the same result, like the trash slot.
        internal void TrashHeldStack(ItemSlot heldSlot)
        {
            if (heldSlot == null || heldSlot.Empty)
            {
                return;
            }

            ItemStack held = heldSlot.Itemstack;
            heldSlot.Itemstack = null;
            heldSlot.MarkDirty();
            PushIntoHistory(held);
        }

        // The player's own inventories that a new filter sweeps once (not the mouse, crafting grid, worn gear, ...)
        private static readonly string[] SweptInventories = { GlobalConstants.backpackInvClassName, GlobalConstants.hotBarInvClassName };

        /*
            Server only, right after a filter slot got a new sample: moves every stack in the player's backpack and hotbar
            that matches it into the history, so the player can take them back until they close their inventory. This is a
            one-off sweep; afterwards only pickups are auto-trashed. Bag slots (the equipped backpacks themselves) are
            never touched, even when the filter is a backpack. Returns the number of stacks moved.
        */
        internal int TrashMatchingStacks(ItemStack sample, IPlayer player)
        {
            if (Api?.Side != EnumAppSide.Server || player?.InventoryManager == null || sample == null)
            {
                return 0;
            }

            int moved = 0;
            foreach (string className in SweptInventories)
            {
                IInventory playerInv = player.InventoryManager.GetOwnInventory(className);
                if (playerInv == null) continue;

                foreach (ItemSlot slot in playerInv)
                {
                    if (slot == null || slot.Empty || slot is ItemSlotBackpack || !TrashFilter.Matches(sample, slot.Itemstack))
                    {
                        continue;
                    }

                    ItemStack taken = slot.Itemstack;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                    PushIntoHistory(taken);
                    moved++;
                }
            }
            return moved;
        }

        // Filter slots report changes here instead of through DidModifyItemSlot (see ItemSlotTrashFilter.ActivateSlot)
        internal void OnFilterSlotChanged(ItemSlot slot)
        {
            MarkSlotDirty(GetSlotId(slot));
            FiltersChanged?.Invoke();
        }

        /*
            Moves a trashed stack into the history, newest first. For the trash slot this runs on both sides with the same
            result, so the client shows it straight away and the server's slot updates agree:
                - merges into the newest entry when it would stack (trashing coal one at a time fills one entry)
                - otherwise shifts the history along; whatever falls off the end is destroyed
        */
        private void PushIntoHistory(ItemStack incoming)
        {
            pushing = true;
            try
            {
                ItemSlot newest = slots[FirstHistorySlotId];
                if (!newest.Empty && newest.Itemstack.Collectible.GetMergableQuantity(newest.Itemstack, incoming, EnumMergePriority.AutoMerge) >= incoming.StackSize)
                {
                    newest.Itemstack.StackSize += incoming.StackSize;
                }
                else
                {
                    for (int i = FirstHistorySlotId + HistorySize - 1; i > FirstHistorySlotId; i--)
                    {
                        slots[i].Itemstack = slots[i - 1].Itemstack;
                    }
                    newest.Itemstack = incoming;
                }

                for (int i = FirstHistorySlotId; i < FirstFilterSlotId; i++)
                {
                    MarkSlotDirty(i);
                }
            }
            finally
            {
                pushing = false;
            }
        }

        // Destroys the history for good (the player closed their inventory or left)
        public void ClearHistory()
        {
            pushing = true;
            try
            {
                for (int i = TrashSlotId; i < FirstFilterSlotId; i++)
                {
                    if (!slots[i].Empty)
                    {
                        slots[i].Itemstack = null;
                        MarkSlotDirty(i);
                    }
                }
            }
            finally
            {
                pushing = false;
            }
        }

        public void SetFilters(IReadOnlyList<ItemStack> filters)
        {
            pushing = true;
            try
            {
                for (int i = 0; i < FilterCount; i++)
                {
                    FilterSlot(i).Itemstack = i < filters.Count ? filters[i] : null;
                    MarkSlotDirty(FirstFilterSlotId + i);
                }
            }
            finally
            {
                pushing = false;
            }
        }

        public ItemStack[] GetFilters()
        {
            var filters = new ItemStack[FilterCount];
            for (int i = 0; i < FilterCount; i++)
            {
                filters[i] = FilterSlot(i).Itemstack;
            }
            return filters;
        }

        public void MarkAllSlotsDirty()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                MarkSlotDirty(i);
            }
        }

        // Filters are persisted separately (player moddata) and the history never is; nothing goes through the tree
        public override void FromTreeAttributes(ITreeAttribute tree)
        {
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
        }
    }
}
