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
