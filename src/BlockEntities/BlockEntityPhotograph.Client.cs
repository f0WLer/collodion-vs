using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion
{
    public partial class BlockEntityPhotograph
    {
        partial void ClientRequestMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            clientNeedsRebuild = true;
            RequestClientMeshRebuild();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            clientTesselationCount++;

            // If we previously didn't have the photo file, we may have requested a download.
            // When it arrives, WetplatePhotoSync marks this block dirty; on the next tesselation,
            // detect the file now exists and rebuild the mesh.
            if (Api?.Side == EnumAppSide.Client && clientMesh == null && !string.IsNullOrWhiteSpace(PhotoId) && !clientPhotoFileExists)
            {
                try
                {
                    string normalized = WetplatePhotoSync.NormalizePhotoId(PhotoId!);
                    string path = WetplatePhotoSync.GetPhotoPath(normalized);
                    if (File.Exists(path))
                    {
                        lock (clientMeshLock)
                        {
                            clientPhotoFileExists = true;
                        }

                        clientNeedsRebuild = true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (Api?.Side == EnumAppSide.Client && clientNeedsRebuild)
            {
                clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? meshToAdd = null;
            MeshData? frameMeshToAdd = null;
            lock (clientMeshLock)
            {
                meshToAdd = clientMesh;
                frameMeshToAdd = clientFrameMesh;
            }

            if (frameMeshToAdd != null)
            {
                mesher.AddMeshData(frameMeshToAdd.Clone());
            }

            if (meshToAdd != null)
            {
                mesher.AddMeshData(meshToAdd.Clone());
            }

            // When we add a custom frame mesh, skip the default (oak) block mesh to avoid double-render.
            bool skipDefault = frameMeshToAdd != null;
            return skipDefault || base.OnTesselation(mesher, tessThreadTesselator);
        }
    }
}
