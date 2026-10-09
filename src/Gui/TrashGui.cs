using System.Linq;
using Vintagestory.API.Client;
using VsTrashcan.Inventory;

namespace VsTrashcan
{
    public class TrashGui : GuiDialog
    {
        private readonly TrashcanInventory inventory;
        private const int HistoryCols = 5;

        // Lets immersive mouse mode keep working while the inventory is open
        public override bool PrefersUngrabbedMouse => false;

        public override string ToggleKeyCombinationCode => null;

        public TrashGui(ICoreClientAPI capi, TrashcanInventory inventory) : base(capi)
        {
            this.inventory = inventory;
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeDialog();
        }

        private void ComposeDialog()
        {
            double gridWidth = HistoryCols * (GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding);

            // The game centres the inventory normally, but moves it to the right edge in immersive mouse mode (where
            // chests and other block windows float over the block instead), so the trash goes to the left edge there
            bool immersive = capi.Settings.Bool["immersiveMouseMode"];
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(immersive ? EnumDialogArea.LeftMiddle : EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(immersive ? GuiStyle.DialogToScreenPadding : 0, 0);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            ElementBounds trashLabel = ElementBounds.Fixed(0, 30, gridWidth, 20);
            ElementBounds trashSlot = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 52, 1, 1);
            ElementBounds historyLabel = ElementBounds.Fixed(0, 112, gridWidth, 20);
            ElementBounds historySlots = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 134, HistoryCols, TrashcanInventory.HistorySize / HistoryCols);
            ElementBounds filterLabel = ElementBounds.Fixed(0, 246, gridWidth, 20);
            ElementBounds filterSlots = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 268, TrashcanInventory.FilterCols, TrashcanInventory.FilterRows);
            bgBounds.WithChildren(trashLabel, trashSlot, historyLabel, historySlots, filterLabel, filterSlots);

            int[] historyIds = Enumerable.Range(TrashcanInventory.FirstHistorySlotId, TrashcanInventory.HistorySize).ToArray();
            int[] filterIds = Enumerable.Range(TrashcanInventory.FirstFilterSlotId, TrashcanInventory.FilterCount).ToArray();
            CairoFont font = CairoFont.WhiteDetailText();

            SingleComposer = capi.Gui.CreateCompo("vstrashcan-dialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Trash", () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText("Drop items here to trash them", font, trashLabel)
                    .AddItemSlotGrid(inventory, SendPacket, 1, new[] { TrashcanInventory.TrashSlotId }, trashSlot, "trashslot")
                    .AddHoverText("Recently trashed: take items back until you close your inventory", font, 300, historyLabel)
                    .AddStaticText("Recently trashed", font, historyLabel)
                    .AddItemSlotGrid(inventory, SendPacket, HistoryCols, historyIds, historySlots, "historyslots")
                    .AddHoverText("Click with an item to make a filter. The held stack and matching items in your inventory move to Recently trashed, and matching pickups are destroyed. Click with an empty hand to clear.", font, 300, filterLabel)
                    .AddStaticText("Auto-trash on pickup", font, filterLabel)
                    .AddItemSlotGrid(inventory, SendPacket, TrashcanInventory.FilterCols, filterIds, filterSlots, "filterslots")
                .EndChildElements()
                .Compose();
        }

        private void SendPacket(object packet)
        {
            capi.Network.SendPacketClient(packet);
        }
    }
}
