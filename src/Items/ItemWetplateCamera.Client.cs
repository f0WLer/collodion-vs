using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public partial class ItemWetplateCamera
    {
        private static readonly object GroundMeshLock = new object();
        private static MultiTextureMeshRef? groundMeshRef;

        private bool TryGetGroundMesh(ICoreClientAPI capi, out MultiTextureMeshRef? meshRef)
        {
            lock (GroundMeshLock)
            {
                if (groundMeshRef != null)
                {
                    meshRef = groundMeshRef;
                    return true;
                }
            }

            try
            {
                capi.Tesselator.TesselateItem(this, out MeshData mesh);

                // Scale around center
                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), 2.5f, 2.5f, 2.5f);

                var meshRefLocal = capi.Render.UploadMultiTextureMesh(mesh);

                lock (GroundMeshLock)
                {
                    groundMeshRef = meshRefLocal;
                }

                meshRef = meshRefLocal;
                return true;
            }
            catch
            {
                meshRef = null;
                return false;
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();

            if (target == EnumItemRenderTarget.Ground)
            {
                if (TryGetGroundMesh(capi, out var meshRef) && meshRef != null)
                {
                    renderinfo.ModelRef = meshRef;
                    return;
                }
            }

            string poseTarget = target switch
            {
                EnumItemRenderTarget.HandFp => "fp",
                EnumItemRenderTarget.HandTp => "tp",
                EnumItemRenderTarget.Gui => "gui",
                _ => string.Empty
            };
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(poseTarget)) return;

            // Inventory/hover preview uses the GUI render target and applies its own animated rotation.
            // Provide a centered, fully-specified baseline transform so the preview spins in place.
            if (target == EnumItemRenderTarget.Gui)
            {
                var t = new ModelTransform();
                t.Translation = new FastVec3f(0f, 0f, 0f);
                t.Rotation = new FastVec3f(0f, 0f, 0f);
                t.Origin.X = 0.5f;
                t.Origin.Y = 0.5f;
                t.Origin.Z = 0.5f;
                t.Rotate = true;
                t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                renderinfo.Transform = t;
            }


            // IMPORTANT: do NOT mutate renderinfo.Transform in-place.
            // VS may reuse the same ModelTransform instance between frames; adding deltas
            // every frame causes accumulation (item drifts away / disappears) and "reset"
            // won't appear to restore it until restart.
            RenderPoseUtil.ApplyPoseDelta(modSys, poseTarget, ref renderinfo);
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);

            if (api.Side != EnumAppSide.Client) return;

            var modSys = api.ModLoader.GetModSystem<CollodionModSystem>();
            if (!modSys.IsViewfinderActive)
            {
                // Not in viewfinder mode: do not take a photo; allow default left click behavior.
                return;
            }

            // In viewfinder: left click acts as shutter.
            handling = EnumHandHandling.PreventDefault;
            modSys.RequestPhotoCaptureFromViewfinder(byEntity, silentIfBusy: true);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side != EnumAppSide.Client) return;

            // Viewfinder mode exit is driven by tick polling.
        }
    }
}
