using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoBlobRequestPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }
}
