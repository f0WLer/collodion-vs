using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

using Collodion.Plates.Rendering;

namespace Collodion.Frame
{
    // Photo-frame block entity.
    // Holds a single photo plate in its inventory (anything with a non-empty PhotoId attribute).
    // The parent block JSON must declare a "photoshape" attribute pointing to the companion photo-plane
    // block code (e.g. "collodion:photooverlaywall"). On the client, Initialize reads that attribute,
    // appends the facing suffix to get the oriented variant, then UV-stamps the plane block's default
    // mesh with the actual photo texture whenever the inventory slot changes.
    public class BlockEntityFrame : BlockEntity
    {
        private const string InventoryTreeKey = "inventory";

        private readonly InventoryGeneric _inventory;
        public InventoryGeneric Inventory => _inventory;

        private readonly object _meshLock = new object();
        private MeshData? _photoMesh;
        private bool _rebuildScheduled;

        private AssetLocation _photoPlaneCode = new AssetLocation("collodion", "photooverlaywall-north");
        private int _photoUvRotation = 90;

        public BlockEntityFrame()
        {
            _inventory = new InventoryGeneric(1, "photographframe-0", null, null);
            _inventory.SlotModified += OnSlotModified;
        }

        private void OnSlotModified(int _)
        {
            lock (_meshLock) { _photoMesh = null; }
            ScheduleMainThreadRebuild();
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            _inventory.LateInitialize(_inventory.InventoryID, api);

            string photoshape = Block?.Attributes?["photoshape"]?.AsString("collodion:photooverlaywall")
                                ?? "collodion:photooverlaywall";
            string facing = Block?.LastCodePart() ?? "north";
            _photoPlaneCode = new AssetLocation(photoshape + "-" + facing);
            _photoUvRotation = Block?.Attributes?["photoUvRotation"]?.AsInt(90) ?? 90;

            if (api.Side == EnumAppSide.Client && !_inventory[0].Empty)
            {
                ScheduleMainThreadRebuild();
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            bool skipDefault = base.OnTesselation(mesher, tessThreadTesselator);

            if (Api?.Side != EnumAppSide.Client || _inventory[0].Empty) return skipDefault;

            MeshData? mesh;
            lock (_meshLock) { mesh = _photoMesh; }

            if (mesh != null)
            {
                mesher.AddMeshData(mesh);
            }
            else
            {
                ScheduleMainThreadRebuild();
            }

            return skipDefault;
        }

        private void ScheduleMainThreadRebuild()
        {
            if (Api is not ICoreClientAPI capi) return;
            if (_rebuildScheduled) return;
            _rebuildScheduled = true;

            capi.Event.EnqueueMainThreadTask(() =>
            {
                _rebuildScheduled = false;
                MeshData? built = TryBuildPhotoMesh(capi);
                lock (_meshLock) { _photoMesh = built; }
                if (built != null) MarkDirty(true);
            }, "collodion-frame-rebuild");
        }

        private MeshData? TryBuildPhotoMesh(ICoreClientAPI capi)
        {
            if (_inventory[0].Empty) return null;

            ItemStack? stack = _inventory[0].Itemstack;
            if (stack == null) return null;

            if (!PhotoPlateRenderUtil.TryGetPhotoBlockTexture(capi, stack, out TextureAtlasPosition texPos, out float photoAspect, Pos))
                return null;

            Block? planeBlock = Api?.World.GetBlock(_photoPlaneCode);
            if (planeBlock == null) return null;

            MeshData? baseMesh = capi.TesselatorManager.GetDefaultBlockMesh(planeBlock);
            if (baseMesh == null) return null;

            MeshData cloned = baseMesh.Clone();
            PhotoMeshUtil.StampUvByRotationCropped(cloned, texPos, _photoUvRotation, photoAspect, PhotoMeshUtil.PhotoTargetAspect);
            // Blend the silver smoothly over the frame's static black backing (the recessed
            // #backing face in framedwall.json) so it reads as a positive ambrotype rather than
            // an alpha-tested hard cutout in the opaque pass.
            PhotoMeshUtil.SetTransparentRenderPass(cloned);
            return cloned;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            ITreeAttribute? invTree = tree.GetTreeAttribute(InventoryTreeKey);
            if (invTree != null) _inventory.FromTreeAttributes(invTree);

            lock (_meshLock) { _photoMesh = null; }

            if (Api?.Side == EnumAppSide.Client && !_inventory[0].Empty)
                ScheduleMainThreadRebuild();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            TreeAttribute invTree = new TreeAttribute();
            _inventory.ToTreeAttributes(invTree);
            tree[InventoryTreeKey] = invTree;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            lock (_meshLock) { _photoMesh = null; }
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            lock (_meshLock) { _photoMesh = null; }
        }
    }
}
