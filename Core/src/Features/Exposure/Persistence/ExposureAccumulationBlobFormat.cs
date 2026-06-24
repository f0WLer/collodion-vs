using System.Buffers.Binary;

namespace Photochemistry.Exposure
{
    internal readonly record struct ExposureAccumulationBlobHeader(int Width, int Height, int ChannelCount, int FrameCount, int BackendTag);

    internal static class ExposureAccumulationBlobFormat
    {
        internal const int Magic = unchecked((int)0x50455853); // "PEXS"
        internal const int Version = 2;
        internal const int HeaderSize = sizeof(int) * 7;

        internal const int GpuBackend = 1;

        internal static int GetTotalByteCount(int width, int height, int channelCount)
        {
            return checked(HeaderSize + checked(width * height * channelCount * sizeof(float)));
        }

        internal static void WriteHeader(byte[] blob, int width, int height, int channelCount, int frameCount, int backendTag)
        {
            WriteInt(blob, 0, Magic);
            WriteInt(blob, 4, Version);
            WriteInt(blob, 8, width);
            WriteInt(blob, 12, height);
            WriteInt(blob, 16, channelCount);
            WriteInt(blob, 20, frameCount);
            WriteInt(blob, 24, backendTag);
        }

        internal static bool TryReadHeader(byte[] data, out ExposureAccumulationBlobHeader header)
        {
            header = default;
            if (data.Length < HeaderSize) return false;

            int magic = ReadInt(data, 0);
            int version = ReadInt(data, 4);
            int width = ReadInt(data, 8);
            int height = ReadInt(data, 12);
            int channelCount = ReadInt(data, 16);
            int frameCount = ReadInt(data, 20);
            int backendTag = ReadInt(data, 24);

            if (magic != Magic || version != Version) return false;
            if (width <= 0 || height <= 0 || channelCount <= 0 || frameCount < 0) return false;

            header = new ExposureAccumulationBlobHeader(width, height, channelCount, frameCount, backendTag);
            return true;
        }

        private static void WriteInt(byte[] blob, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset, sizeof(int)), value);
        }

        private static int ReadInt(byte[] blob, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset, sizeof(int)));
        }
    }
}