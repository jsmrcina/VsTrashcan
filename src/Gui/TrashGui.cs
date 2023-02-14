using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsTrashcan
{
    public class TrashGui : GuiDialog
    {
        InventoryGeneric trashSlotInventory;
        InventoryGeneric trashSlotFilterInventory;

        public TrashGui(ICoreClientAPI capi, InventoryGeneric trashSlotInventory, InventoryGeneric trashSlotFilterInventory) : base(capi)
        {
            this.trashSlotInventory = trashSlotInventory;
            this.trashSlotFilterInventory = trashSlotFilterInventory;
            ComposeDialog();
        }

        public override string ToggleKeyCombinationCode => null;

        private void ComposeDialog()
        {

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle);

            ElementBounds trashSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.CenterFixed, 0, 30, 1, 1);
            ElementBounds trashSlotFilterBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 120, 2, 2);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(40f);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            SingleComposer = capi.Gui.CreateCompo("trashDialog", dialogBounds)
                    .AddShadedDialogBG(bgBounds)
                    .AddDialogTitleBar("Trash")
                    .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(trashSlotInventory, null, 1, trashSlotBounds)
                    .AddItemSlotGrid(trashSlotFilterInventory, null, 2, trashSlotFilterBounds)
                    .EndChildElements()
                    .Compose();
        }
    }
}