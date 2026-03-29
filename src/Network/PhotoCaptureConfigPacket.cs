using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoCaptureConfigPacket
    {
        [ProtoMember(1)]
        public int MaxDimension { get; set; }
    }
}
