using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoCaptionSetPacket
    {
        [ProtoMember(1)]
        public int X;

        [ProtoMember(2)]
        public int Y;

        [ProtoMember(3)]
        public int Z;

        [ProtoMember(4)]
        public string Caption = string.Empty;
    }
}
