using ProtoBuf;

namespace VsTrashcan.Models.Networking
{
    // Client -> server: the client has created its copy of the trashcan inventory and wants its contents
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class TrashcanReadyMessage
    {
    }

    // Client -> server: the player closed their inventory, so the recently-trashed items are now destroyed
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class TrashcanClosedMessage
    {
    }
}
