using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoBlobRequestPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }

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
