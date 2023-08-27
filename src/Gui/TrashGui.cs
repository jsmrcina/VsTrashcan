using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Cairo;

namespace VsTrashcan
{
    public class TrashGui : GuiDialog
    {
        InventoryGeneric trashSlotInventory;
        InventoryGeneric trashSlotFilterInventory;

        private readonly int _numTrashFilterRows;
        private readonly int _numTrashFilterCols;
        private ICoreClientAPI _clientApi; 

        ElementBounds trashAreaBounds;

        public TrashGui(ICoreClientAPI capi, InventoryGeneric trashSlotInventory, InventoryGeneric trashSlotFilterInventory, int numTrashFilterRows, int numTrashFilterCols) : base(capi)
        {
            _numTrashFilterRows = numTrashFilterRows;
            _numTrashFilterCols = numTrashFilterCols;
            _clientApi = capi;
            this.trashSlotInventory = trashSlotInventory;
            this.trashSlotFilterInventory = trashSlotFilterInventory;
        }

        public override string ToggleKeyCombinationCode => null;

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeDialog();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
        }

        private void ComposeDialog()
        {
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle);
            if (_clientApi.Settings.Bool["immersiveMouseMode"])
            {
                dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightBottom);
            }

            trashAreaBounds = ElementBounds.Fixed(0, 0, 210, 250);

            ElementBounds trashSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.CenterFixed, 0, 30, 1, 1);
            ElementBounds trashSlotFilterBounds = ElementStdBounds.SlotGrid(EnumDialogArea.CenterFixed, 0, 120, _numTrashFilterRows, _numTrashFilterCols);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(40f);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(trashAreaBounds);

            SingleComposer = capi.Gui.CreateCompo("trashDialog", dialogBounds)
                    .AddShadedDialogBG(bgBounds)
                    .AddDialogTitleBar("Trash")
                    .BeginChildElements(bgBounds)
                    .AddDynamicCustomDraw(trashAreaBounds, OnBgDraw, "symbolDrawer")
                    .AddStaticText("Trash", CairoFont.WhiteDetailText(), ElementBounds.Fixed(83, 5, 100, 45), "trashText")
                    .AddItemSlotGrid(trashSlotInventory, null, 1, trashSlotBounds)
                    .AddStaticText("Auto-trash Filters", CairoFont.WhiteDetailText(), ElementBounds.Fixed(50, 90, 120, 45), "trashFilterText")
                    .AddItemSlotGrid(trashSlotFilterInventory, null, _numTrashFilterCols, trashSlotFilterBounds)
                    .EndChildElements()
                    .Compose();
        }

        private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            double top = trashAreaBounds.fixedHeight + trashAreaBounds.fixedY;

            ctx.Save();
            Matrix m = ctx.Matrix;
            m.Translate(GuiElement.scaled(5), GuiElement.scaled(5));
            m.Scale(GuiElement.scaled(0.2), GuiElement.scaled(0.2));
            ctx.Matrix = m;

            var rgba = new double[] { 1, 1, 1, 1 };
            capi.Gui.Icons.DrawIcon(ctx, "game:textures/gui/target", 100, 100, 32, 32, rgba);

            ctx.Restore();
        }
    }
}