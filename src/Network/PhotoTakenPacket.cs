using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoTakenPacket
    {
        [ProtoMember(1)]
        public string PhotoId { get; set; } = string.Empty;

        [ProtoMember(2)]
        public float HoldStillSeconds { get; set; }

        [ProtoMember(3)]
        public float HoldStillMovement { get; set; }
    }
}
