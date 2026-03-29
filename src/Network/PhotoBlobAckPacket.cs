using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoBlobAckPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;

        [ProtoMember(2)]
        public bool Ok;

        [ProtoMember(3)]
        public string Error = string.Empty;
    }
}
