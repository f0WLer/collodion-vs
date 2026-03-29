using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class CameraSlingTogglePacket
    {
        [ProtoMember(1)]
        public bool TryWallMount { get; set; }

        [ProtoMember(2)]
        public int TargetX { get; set; }

        [ProtoMember(3)]
        public int TargetY { get; set; }

        [ProtoMember(4)]
        public int TargetZ { get; set; }

        [ProtoMember(5)]
        public string TargetFaceCode { get; set; } = string.Empty;
    }
}
