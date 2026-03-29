using System;
using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoBlobChunkPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;

        [ProtoMember(2)]
        public int TotalSize;

        [ProtoMember(3)]
        public int ChunkIndex;

        [ProtoMember(4)]
        public int ChunkCount;

        [ProtoMember(5)]
        public byte[] Data = Array.Empty<byte>();

        // true: client->server upload; false: server->client download
        [ProtoMember(6)]
        public bool IsUpload;
    }
}
