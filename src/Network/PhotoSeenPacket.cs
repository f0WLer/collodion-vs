using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoSeenPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }
}
