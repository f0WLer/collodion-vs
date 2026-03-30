using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Collodion
{
    internal static class PhotoMeshUtil
    {
        // Plate/frame visible area is 5w x 5.5h => aspect = 10/11.
        internal const float PhotoTargetAspect = 10f / 11f;

        internal static MeshData CreateOverlayQuad(TextureAtlasPosition texPos, MeshData baseMesh, int uvRotationDeg, bool mirrorX, float photoAspect, string face)
        {
            // Derive the photo quad from the current plate model bounds so shape changes
            // (e.g. recent re-centering for GUI spin) don't break placement.
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            float[] xyz = baseMesh.xyz ?? Array.Empty<float>();
            int verts = baseMesh.VerticesCount;
            if (verts <= 0 || xyz.Length < verts * 3)
            {
                // Fallback: centered quad, should never happen for a real item mesh.
                minX = 0.1f;
                minY = 0.1f;
                minZ = 0.49f;
                maxX = 0.9f;
                maxY = 0.9f;
                maxZ = 0.5f;
                verts = 0;
            }

            for (int i = 0; i < verts; i++)
            {
                float x = xyz[i * 3 + 0];
                float y = xyz[i * 3 + 1];
                float z = xyz[i * 3 + 2];

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            // Push the overlay slightly forward to avoid z-fighting.
            const float eps = 0.0005f;
            float x1 = minX;
            float x2 = maxX;
            float y1 = minY;
            float y2 = maxY;
            float z1 = minZ;
            float z2 = maxZ;

            float[] quad;
            int packed;
            int[] indices;

            switch (face)
            {
                case "north":
                    {
                        float z = minZ - eps;
                        quad = new float[]
                        {
                            x1, y1, z,
                            x2, y1, z,
                            x2, y2, z,
                            x1, y2, z
                        };
                        packed = VertexFlags.PackNormal(0, 0, -1);
                        indices = new int[] { 0, 2, 1, 0, 3, 2 };
                        break;
                    }

                case "up":
                    {
                        float y = maxY + eps;
                        quad = new float[]
                        {
                            x1, y, z1,
                            x2, y, z1,
                            x2, y, z2,
                            x1, y, z2
                        };
                        packed = VertexFlags.PackNormal(0, 1, 0);
                        indices = new int[] { 0, 2, 1, 0, 3, 2 };
                        break;
                    }

                case "down":
                    {
                        float y = minY - eps;
                        quad = new float[]
                        {
                            x1, y, z1,
                            x2, y, z1,
                            x2, y, z2,
                            x1, y, z2
                        };
                        packed = VertexFlags.PackNormal(0, -1, 0);
                        indices = new int[] { 0, 1, 2, 0, 2, 3 };
                        break;
                    }

                case "east":
                    {
                        float x = maxX + eps;
                        quad = new float[]
                        {
                            x, y1, z1,
                            x, y1, z2,
                            x, y2, z2,
                            x, y2, z1
                        };
                        packed = VertexFlags.PackNormal(1, 0, 0);
                        indices = new int[] { 0, 2, 1, 0, 3, 2 };
                        break;
                    }

                case "west":
                    {
                        float x = minX - eps;
                        quad = new float[]
                        {
                            x, y1, z1,
                            x, y1, z2,
                            x, y2, z2,
                            x, y2, z1
                        };
                        packed = VertexFlags.PackNormal(-1, 0, 0);
                        indices = new int[] { 0, 1, 2, 0, 2, 3 };
                        break;
                    }

                case "south":
                default:
                    {
                        float z = maxZ + eps;
                        quad = new float[]
                        {
                            x1, y1, z,
                            x2, y1, z,
                            x2, y2, z,
                            x1, y2, z
                        };
                        packed = VertexFlags.PackNormal(0, 0, 1);
                        indices = new int[] { 0, 1, 2, 0, 2, 3 };
                        break;
                    }
            }

            MeshData m = new MeshData(capacityVertices: 4, capacityIndices: 6, withNormals: false, withUv: true, withRgba: true, withFlags: true);

            m.SetXyz(quad);

            // UVs in 0..1 range (BL, BR, TR, TL).
            // Baseline UVs are un-mirrored; any rotation is applied via ApplyUvRotationCw().
            m.SetUv(new float[]
            {
                0f, 0f,
                1f, 0f,
                1f, 1f,
                0f, 1f
            });

            // Important: UV transforms iterate VerticesCount.
            m.SetVerticesCount(4);

            ApplyUvRotationCw(m, uvRotationDeg);

            // Center-crop the photo so it fills the plate without stretching.
            ApplyUvCenterCropToAspect(m, photoAspect, PhotoTargetAspect, uvRotationDeg);

            if (mirrorX)
            {
                ApplyUvMirrorX(m);
            }

            m.Rgba.Fill((byte)255);

            // Required for texture routing.
            m.TextureIndicesCount = 1;
            m.XyzFaces = new byte[] { 3 };
            m.XyzFacesCount = 1;
            m.RenderPassesAndExtraBits = new short[] { 0 };
            m.RenderPassCount = 1;

            for (int i = 0; i < 4; i++) m.Flags[i] = packed;

            m.SetIndices(indices);
            m.SetIndicesCount(6);

            // Scale UVs into atlas space and fill TextureIndices.
            return m.WithTexPos(texPos);
        }

        internal static void ApplyUvCenterCropToAspect(MeshData mesh, float sourceAspect, float targetAspect, int rotationDeg)
        {
            if (mesh.Uv == null || mesh.VerticesCount <= 0) return;
            if (sourceAspect <= 0 || targetAspect <= 0) return;

            int rot = ((rotationDeg % 360) + 360) % 360;
            bool rot90 = rot == 90 || rot == 270;

            float effectiveSourceAspect = rot90 ? (1f / sourceAspect) : sourceAspect;
            if (effectiveSourceAspect <= 0) return;

            PhotoCropMath.ComputeCenterCrop(effectiveSourceAspect, targetAspect, out float keepU, out float keepV);

            float uMin = (1f - keepU) * 0.5f;
            float vMin = (1f - keepV) * 0.5f;

            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                uv[i * 2 + 0] = uMin + uv[i * 2 + 0] * keepU;
                uv[i * 2 + 1] = vMin + uv[i * 2 + 1] * keepV;
            }
        }

        internal static void ApplyUvRotationCw(MeshData mesh, int uvRotationDeg)
        {
            int rot = ((uvRotationDeg % 360) + 360) % 360;
            if (rot == 0) return;

            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                float u = uv[i * 2 + 0];
                float v = uv[i * 2 + 1];

                float u2;
                float v2;
                switch (rot)
                {
                    case 90:
                        u2 = 1f - v;
                        v2 = u;
                        break;
                    case 180:
                        u2 = 1f - u;
                        v2 = 1f - v;
                        break;
                    case 270:
                        u2 = v;
                        v2 = 1f - u;
                        break;
                    default:
                        return;
                }

                uv[i * 2 + 0] = u2;
                uv[i * 2 + 1] = v2;
            }
        }

        internal static void ApplyUvMirrorX(MeshData mesh)
        {
            float[] uv = mesh.Uv;
            for (int i = 0; i < mesh.VerticesCount; i++)
            {
                uv[i * 2 + 0] = 1f - uv[i * 2 + 0];
            }
        }
    }
}
