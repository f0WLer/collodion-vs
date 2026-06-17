using Collodion.CameraCapture;
using Collodion.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion.FieldCamera
{
    public sealed class BlockEntityMountedCamera : BlockEntity
    {
        private MountedCameraBlockRenderer? _renderer;
        private const string CameraStackAttr   = "collodionMountedCameraStack";
        private const string OwnerUidAttr      = "collodionMountedCameraOwnerUid";
        private const string FacingYawAttr     = "collodionMountedFacingYaw";
        private const string SubOffsetXAttr    = "collodionMountedSubOffsetX";
        private const string SubOffsetZAttr    = "collodionMountedSubOffsetZ";

        private ItemStack? _cameraStack;
        private string _ownerPlayerUid = string.Empty;
        private float _facingYaw;
        private float _subBlockOffsetX;
        private float _subBlockOffsetZ;
        private bool _isExposing;

        public string OwnerPlayerUid => _ownerPlayerUid;
        internal float SubBlockOffsetX => _subBlockOffsetX;
        internal float SubBlockOffsetZ => _subBlockOffsetZ;

        internal bool HasStoredCamera(IWorldAccessor? world)
        {
            return GetStoredCameraStack(world) != null;
        }

        internal ItemStack? GetStoredCameraStack(IWorldAccessor? world)
        {
            _cameraStack?.ResolveBlockOrItem(world);
            return _cameraStack;
        }

        internal void SetStoredCameraStack(ItemStack cameraStack, string ownerPlayerUid, IWorldAccessor? world)
        {
            _cameraStack = cameraStack.Clone();
            _cameraStack.ResolveBlockOrItem(world);
            _ownerPlayerUid = ownerPlayerUid ?? string.Empty;
            RefreshExposingState(world);
            UpdateUpperBlock();
            MarkDirty(true);
        }

        internal ItemStack? TakeStoredCameraStack(IWorldAccessor? world)
        {
            ItemStack? stored = _cameraStack?.Clone();
            stored?.ResolveBlockOrItem(world);
            _cameraStack = null;
            _ownerPlayerUid = string.Empty;
            if (_isExposing)
            {
                _isExposing = false;
                _renderer?.SetExposing(false);
            }
            UpdateUpperBlock();
            MarkDirty(true);
            return stored;
        }

        internal void TransferOwnership(string newOwnerUid)
        {
            _ownerPlayerUid = newOwnerUid ?? string.Empty;
            MarkDirty(true);
        }

        internal void MarkCameraDirty()
        {
            UpdateUpperBlock();
            MarkDirty(true);
        }

        private static readonly AssetLocation _upperBlockCode = new("collodion", "fieldcamera-tripod-upper");

        private void UpdateUpperBlock()
        {
            if (Api?.Side != EnumAppSide.Server || Pos == null) return;

            BlockPos posAbove = Pos.UpCopy();
            Block blockAbove = Api.World.BlockAccessor.GetBlock(posAbove);
            bool hasUpper = blockAbove is BlockMountedCameraUpper;
            bool needsUpper = _cameraStack != null && SelectionTopY > 1.0f;

            if (needsUpper && !hasUpper && blockAbove.Id == 0)
            {
                Block? upper = Api.World.GetBlock(_upperBlockCode);
                if (upper != null && upper.Id > 0)
                    Api.World.BlockAccessor.SetBlock(upper.Id, posAbove);
            }
            else if (!needsUpper && hasUpper)
            {
                Api.World.BlockAccessor.SetBlock(0, posAbove);
            }
        }

        internal void SetFacingYaw(float yaw)
        {
            _facingYaw = yaw;
            _renderer?.SetFacingYaw(yaw);
            MarkDirty(true);
        }

        internal void SetSubBlockOffset(float x, float z)
        {
            _subBlockOffsetX = x;
            _subBlockOffsetZ = z;
            _renderer?.SetSubBlockOffset(x, z);
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api is ICoreClientAPI capi)
            {
                _renderer = new MountedCameraBlockRenderer(capi, Pos, Block, _facingYaw, _subBlockOffsetX, _subBlockOffsetZ, _isExposing);
                capi.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque, "collodion-mounted-camera");
                _renderer.SetHeightOffset(ComputeHeightAboveBlock());
            }
            else if (api.Side == EnumAppSide.Server)
            {
                PauseStaleExposureIfOwnerOffline(api);
            }
        }

        // If this camera loads still marked Exposing but its owning player is offline, the exposure's
        // client-side accumulator is gone — pause the plate so it doesn't render/stay stuck exposing.
        // Covers walk-away-then-chunk-unload disconnects and server crash/restart mid-exposure.
        private void PauseStaleExposureIfOwnerOffline(ICoreAPI api)
        {
            if (_cameraStack == null || string.IsNullOrEmpty(_ownerPlayerUid)) return;

            // Owner online => their client accumulator may still be running; leave the exposure alone.
            foreach (IPlayer p in api.World.AllOnlinePlayers)
                if (string.Equals(p.PlayerUID, _ownerPlayerUid, StringComparison.Ordinal)) return;

            if (CameraItemHelper.TryPauseExposingPlate(api.World, _cameraStack))
            {
                RefreshExposingState(api.World); // recomputes _isExposing from the now-paused plate
                MarkDirty(true);
            }
        }

        private const float HeightVisualBias = 0.25f;
        private const float CameraBodyHeight = 0.35f;

        internal float SelectionTopY => ComputeHeightAboveBlock() + CameraBodyHeight;

        private float ComputeHeightAboveBlock()
        {
            if (CameraItemHelper.TryGetMountedCaptureState(_cameraStack, out VirtualCameraState state))
                return Math.Max(0f, (float)(state.Position.Y - Pos.Y) - HeightVisualBias);
            return 0f;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeRenderer();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeRenderer();
        }

        private void DisposeRenderer()
        {
            if (_renderer == null) return;
            if (Api is ICoreClientAPI capi)
                BestEffort.Try(null, "unregister mounted camera block renderer",
                    () => capi.Event.UnregisterRenderer(_renderer, EnumRenderStage.Opaque));
            _renderer.Dispose();
            _renderer = null;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            // Rendering is handled by MountedCameraBlockRenderer so the block can be
            // excluded from virtual camera captures at runtime. Returning true suppresses
            // the default block shape without adding any geometry to the chunk mesh.
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (_cameraStack != null)
                tree.SetItemstack(CameraStackAttr, _cameraStack);
            else
                tree.RemoveAttribute(CameraStackAttr);

            if (string.IsNullOrWhiteSpace(_ownerPlayerUid))
                tree.RemoveAttribute(OwnerUidAttr);
            else
                tree.SetString(OwnerUidAttr, _ownerPlayerUid);

            tree.SetFloat(FacingYawAttr, _facingYaw);
            tree.SetFloat(SubOffsetXAttr, _subBlockOffsetX);
            tree.SetFloat(SubOffsetZAttr, _subBlockOffsetZ);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            _cameraStack = tree.GetItemstack(CameraStackAttr, null);
            _cameraStack?.ResolveBlockOrItem(worldAccessForResolve);
            _ownerPlayerUid = tree.GetString(OwnerUidAttr, string.Empty);
            _facingYaw = tree.GetFloat(FacingYawAttr, 0f);
            _subBlockOffsetX = tree.GetFloat(SubOffsetXAttr, 0f);
            _subBlockOffsetZ = tree.GetFloat(SubOffsetZAttr, 0f);
            _renderer?.SetFacingYaw(_facingYaw);
            _renderer?.SetSubBlockOffset(_subBlockOffsetX, _subBlockOffsetZ);
            _renderer?.SetHeightOffset(ComputeHeightAboveBlock());
            RefreshExposingState(worldAccessForResolve);
        }

        private void RefreshExposingState(IWorldAccessor? world)
        {
            bool exposing = CameraItemHelper.TryGetLoadedPlateStack(_cameraStack, world, out ItemStack? plate)
                && PlateAttributes.GetStage(plate) == PlateStage.Exposing;
            if (_isExposing == exposing) return;
            _isExposing = exposing;
            _renderer?.SetExposing(exposing);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (_cameraStack?.Collectible?.GetHeldItemName(_cameraStack) is string name && !string.IsNullOrWhiteSpace(name))
                dsc.AppendLine(name);
        }
    }
}