using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class CameraLoadPlatePacket
    {
        [ProtoMember(1)]
        public bool Load { get; set; }
    }
}
