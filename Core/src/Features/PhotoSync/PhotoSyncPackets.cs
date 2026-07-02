using ProtoBuf;

namespace Photocore.PhotoSync
{
    // Keep these classes data-only and preserve ProtoMember ids for compatibility.
    [ProtoContract]
    public class PhotoBlobAckPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;

        [ProtoMember(2)]
        public bool Ok;

        [ProtoMember(3)]
        public string Error = string.Empty;

        // Direction this ack refers to: true = client->server upload result, false = server->client
        // download request result. Lets the client tell "my upload failed" (transient, bytes exist
        // locally) apart from "the photo I asked to download is not available" (show the placeholder).
        [ProtoMember(4)]
        public bool IsUpload;
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

        // Absolute byte offset in the destination buffer for this chunk.
        [ProtoMember(7)]
        public int ChunkOffset;
    }

    [ProtoContract]
    public class PhotoBlobRequestPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }

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

    [ProtoContract]
    public class PhotoSeenPacket
    {
        [ProtoMember(1)]
        public string PhotoId = string.Empty;
    }
}
