using System;
using Vintagestory.API.Client;
using Vintagestory.API.Util;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
        private static void StampUvByRotationCropped(MeshData mesh, TextureAtlasPosition texPos, int rotationDeg, float sourceAspect, float targetAspect)
        {
            if (mesh.Uv == null || mesh.VerticesCount < 4) return;

            GetCroppedTexRect(texPos, sourceAspect, targetAspect, rotationDeg, out float x1, out float x2, out float y1, out float y2);

            int rot = ((rotationDeg % 360) + 360) % 360;
            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                // Expected vertex order per quad: BL, BR, TR, TL
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                switch (rot)
                {
                    case 90:
                        // Rotate 90° clockwise.
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y2;
                        break;

                    case 180:
                        // Rotate 180°.
                        mesh.Uv[v0] = x2; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y1;
                        mesh.Uv[v2] = x1; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y2;
                        break;

                    case 270:
                        // Rotate 90° counter-clockwise.
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y1;
                        mesh.Uv[v1] = x1; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y2;
                        mesh.Uv[v3] = x2; mesh.Uv[v3 + 1] = y1;
                        break;

                    case 0:
                    default:
                        mesh.Uv[v0] = x1; mesh.Uv[v0 + 1] = y2;
                        mesh.Uv[v1] = x2; mesh.Uv[v1 + 1] = y2;
                        mesh.Uv[v2] = x2; mesh.Uv[v2 + 1] = y1;
                        mesh.Uv[v3] = x1; mesh.Uv[v3 + 1] = y1;
                        break;
                }
            }

            EnsureOpaqueVertexColors(mesh);
        }

        private static void GetCroppedTexRect(TextureAtlasPosition texPos, float sourceAspect, float targetAspect, int rotationDeg, out float x1, out float x2, out float y1, out float y2)
        {
            x1 = texPos.x1;
            x2 = texPos.x2;
            y1 = texPos.y1;
            y2 = texPos.y2;

            if (sourceAspect <= 0 || targetAspect <= 0) return;

            int rot = ((rotationDeg % 360) + 360) % 360;
            bool rot90 = rot == 90 || rot == 270;

            float effectiveSourceAspect = rot90 ? (1f / sourceAspect) : sourceAspect;
            if (effectiveSourceAspect <= 0) return;

            PhotoCropMath.ComputeCenterCrop(effectiveSourceAspect, targetAspect, out float keepU, out float keepV);

            if (keepU < 1f)
            {
                float trim = (1f - keepU) * 0.5f;

                if (!rot90)
                {
                    float xr = x2 - x1;
                    x1 += xr * trim;
                    x2 -= xr * trim;
                }
                else
                {
                    float yr = y2 - y1;
                    y1 += yr * trim;
                    y2 -= yr * trim;
                }

                return;
            }

            if (keepV < 1f)
            {
                float trim = (1f - keepV) * 0.5f;

                if (!rot90)
                {
                    float yr = y2 - y1;
                    y1 += yr * trim;
                    y2 -= yr * trim;
                }
                else
                {
                    float xr = x2 - x1;
                    x1 += xr * trim;
                    x2 -= xr * trim;
                }
            }
        }

        private static void StampUvRotate180(MeshData mesh, TextureAtlasPosition texPos)
        {
            if (mesh.Uv == null || mesh.VerticesCount < 4) return;

            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                // Expected vertex order per quad: BL, BR, TR, TL
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                // Rotate 180°.
                mesh.Uv[v0] = texPos.x2;
                mesh.Uv[v0 + 1] = texPos.y1;

                mesh.Uv[v1] = texPos.x1;
                mesh.Uv[v1 + 1] = texPos.y1;

                mesh.Uv[v2] = texPos.x1;
                mesh.Uv[v2 + 1] = texPos.y2;

                mesh.Uv[v3] = texPos.x2;
                mesh.Uv[v3 + 1] = texPos.y2;
            }

            EnsureOpaqueVertexColors(mesh);
        }

        private static void StampUvRotate90Ccw(MeshData mesh, TextureAtlasPosition texPos)
        {
            if (mesh.Uv == null || mesh.VerticesCount < 4) return;

            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                // Expected vertex order per quad: BL, BR, TR, TL
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                // Rotate 90° counter-clockwise.
                mesh.Uv[v0] = texPos.x1;
                mesh.Uv[v0 + 1] = texPos.y1;

                mesh.Uv[v1] = texPos.x1;
                mesh.Uv[v1 + 1] = texPos.y2;

                mesh.Uv[v2] = texPos.x2;
                mesh.Uv[v2 + 1] = texPos.y2;

                mesh.Uv[v3] = texPos.x2;
                mesh.Uv[v3 + 1] = texPos.y1;
            }

            EnsureOpaqueVertexColors(mesh);
        }

        private static void StampUvByRotation(MeshData mesh, TextureAtlasPosition texPos, int rotationDeg)
        {
            int rot = ((rotationDeg % 360) + 360) % 360;
            switch (rot)
            {
                case 0:
                    StampUvNoRotate(mesh, texPos);
                    break;
                case 90:
                    StampUvRotate90Cw(mesh, texPos);
                    break;
                case 180:
                    StampUvRotate180(mesh, texPos);
                    break;
                case 270:
                    StampUvRotate90Ccw(mesh, texPos);
                    break;
                default:
                    StampUvNoRotate(mesh, texPos);
                    break;
            }
        }

        private static void StampUvNoRotate(MeshData mesh, TextureAtlasPosition texPos)
        {
            if (mesh.Uv == null || mesh.VerticesCount < 4) return;

            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                // Expected vertex order per quad: BL, BR, TR, TL
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                mesh.Uv[v0] = texPos.x1;
                mesh.Uv[v0 + 1] = texPos.y2;

                mesh.Uv[v1] = texPos.x2;
                mesh.Uv[v1 + 1] = texPos.y2;

                mesh.Uv[v2] = texPos.x2;
                mesh.Uv[v2 + 1] = texPos.y1;

                mesh.Uv[v3] = texPos.x1;
                mesh.Uv[v3 + 1] = texPos.y1;
            }

            EnsureOpaqueVertexColors(mesh);
        }

        private static void StampUvRotate90Cw(MeshData mesh, TextureAtlasPosition texPos)
        {
            if (mesh.Uv == null || mesh.VerticesCount < 4) return;

            int quadCount = mesh.VerticesCount / 4;
            for (int q = 0; q < quadCount; q++)
            {
                // Expected vertex order per quad: BL, BR, TR, TL
                int v0 = (q * 4 + 0) * 2;
                int v1 = (q * 4 + 1) * 2;
                int v2 = (q * 4 + 2) * 2;
                int v3 = (q * 4 + 3) * 2;
                if (v3 + 1 >= mesh.Uv.Length) break;

                // Rotate 90° clockwise.
                mesh.Uv[v0] = texPos.x2;
                mesh.Uv[v0 + 1] = texPos.y2;

                mesh.Uv[v1] = texPos.x2;
                mesh.Uv[v1 + 1] = texPos.y1;

                mesh.Uv[v2] = texPos.x1;
                mesh.Uv[v2 + 1] = texPos.y1;

                mesh.Uv[v3] = texPos.x1;
                mesh.Uv[v3 + 1] = texPos.y2;
            }

            EnsureOpaqueVertexColors(mesh);
        }

        private static void EnsureOpaqueVertexColors(MeshData mesh)
        {
            int expectedRgbaLen = mesh.VerticesCount * 4;
            if (mesh.Rgba == null || mesh.Rgba.Length != expectedRgbaLen)
            {
                mesh.Rgba = new byte[expectedRgbaLen];
            }
            mesh.Rgba.Fill((byte)255);
        }
    }
}
