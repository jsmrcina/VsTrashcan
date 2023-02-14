using ProtoBuf;
using Vintagestory.API.Common;

namespace VsTrashcan.Models.Networking
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClearMouseSlotMessage
    {

    }

    public enum TrashFilterMessageType
    {
        Register,
        Unregister
    };

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ModifyTrashFilterMessage
    {
        public TrashFilterMessageType Type;
        public int InventorySlotId;
        public byte[] Itemstack;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class InitialTrashFilterSyncMessage
    {
        public int InventorySlotId;
        public byte[] Itemstack;
    }
}