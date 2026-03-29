using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
        private static Vec3f FaceNormal(string? face)
        {
            switch ((face ?? string.Empty).ToLowerInvariant())
            {
                case "north": return new Vec3f(0, 0, -1);
                case "south": return new Vec3f(0, 0, 1);
                case "east": return new Vec3f(1, 0, 0);
                case "west": return new Vec3f(-1, 0, 0);
                case "up": return new Vec3f(0, 1, 0);
                case "down": return new Vec3f(0, -1, 0);
                default: return new Vec3f(0, 0, 0);
            }
        }

        private static MeshData CreateQuadMeshFromXyz(float[] xyz, string face)
        {
            // Important: The terrain renderer path expects UV + per-face TextureIndices to exist.
            // WithTexPos() will scale 0..1 UVs into atlas space and fill TextureIndices.
            // IMPORTANT: Generate this mesh double-sided (two quads) so backface culling or an unexpected
            // winding order can never fully hide the photo.

            MeshData m = new MeshData(capacityVertices: 8, capacityIndices: 12, withNormals: false, withUv: true, withRgba: true, withFlags: true);

            float[] xyz2 = new float[3 * 8];
            Array.Copy(xyz, 0, xyz2, 0, 3 * 4);
            Array.Copy(xyz, 0, xyz2, 3 * 4, 3 * 4);
            m.SetXyz(xyz2);

            // Seed UVs in 0..1 range (BL, BR, TR, TL) per quad.
            m.SetUv(new float[]
            {
                0f, 1f,
                1f, 1f,
                1f, 0f,
                0f, 0f,
                0f, 1f,
                1f, 1f,
                1f, 0f,
                0f, 0f
            });

            m.Rgba.Fill((byte)255);
            m.SetVerticesCount(8);

            // Critical: AddMeshDataEtc() only copies TextureIds/TextureIndices for the first TextureIndicesCount faces.
            // WithTexPos() does not update this count, so we must set it up-front.
            m.TextureIndicesCount = 2;

            byte xyzFace = FaceToXyzFaceIndex(face);
            byte xyzFaceOpp = FaceToXyzFaceIndex(OppositeFace(face));
            m.XyzFaces = new byte[] { xyzFace, xyzFaceOpp };
            m.XyzFacesCount = 2;

            // Default render pass (solid).
            m.RenderPassesAndExtraBits = new short[] { 0, 0 };
            m.RenderPassCount = 2;

            // Pack normals into Flags (terrain shading expects packed normals here).
            Vec3f n = FaceNormal(face);
            int packedFront = VertexFlags.PackNormal(n.X, n.Y, n.Z);
            int packedBack = VertexFlags.PackNormal(-n.X, -n.Y, -n.Z);
            for (int i = 0; i < 4; i++) m.Flags[i] = packedFront;
            for (int i = 4; i < 8; i++) m.Flags[i] = packedBack;

            // Front face: standard winding. Back face: reversed winding.
            m.SetIndices(new int[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6
            });
            m.SetIndicesCount(12);
            return m;
        }

        private static byte FaceToXyzFaceIndex(string? face)
        {
            switch ((face ?? string.Empty).ToLowerInvariant())
            {
                case "north": return 1;
                case "east": return 2;
                case "south": return 3;
                case "west": return 4;
                case "up": return 5;
                case "down": return 6;
                default: return 0;
            }
        }

        private static string OppositeFace(string? face)
        {
            switch ((face ?? string.Empty).ToLowerInvariant())
            {
                case "north": return "south";
                case "south": return "north";
                case "east": return "west";
                case "west": return "east";
                case "up": return "down";
                case "down": return "up";
                default: return face ?? string.Empty;
            }
        }
    }
}
