using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsTrashcan
{
    public class TrashGui : GuiDialog
    {
        InventoryGeneric inv;

        public TrashGui(ICoreClientAPI capi, InventoryGeneric inv) : base(capi)
        {
            this.inv = inv;
            ComposeDialog();
        }

        public override string ToggleKeyCombinationCode => null;

        private void ComposeDialog()
        {

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle);

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(40f);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            SingleComposer = capi.Gui.CreateCompo("trashDialog", dialogBounds)
                    .AddShadedDialogBG(bgBounds)
                    .AddDialogTitleBar("Trash")
                    .BeginChildElements(bgBounds)
                    .AddItemSlotGrid(inv, null, 1, slotBounds)
                    .EndChildElements()
                    .Compose();
        }
    }
}