using System;
using Vintagestory.API.Client;
using Vintagestory.API.Util;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
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

            PhotoMeshUtil.EnsureOpaqueVertexColors(mesh);
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

            PhotoMeshUtil.EnsureOpaqueVertexColors(mesh);
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

            PhotoMeshUtil.EnsureOpaqueVertexColors(mesh);
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

            PhotoMeshUtil.EnsureOpaqueVertexColors(mesh);
        }
    }
}
