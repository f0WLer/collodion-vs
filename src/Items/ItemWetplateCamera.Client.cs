using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using System;

namespace Collodion
{
    public partial class ItemWetplateCamera
    {
        private static float GetExposureSeconds(CollodionModSystem modSys)
        {
            float seconds = modSys.Config?.Viewfinder?.HoldStillDurationSeconds ?? 0f;
            if (seconds < 0f) seconds = 0f;
            if (seconds > 30f) seconds = 30f;
            return seconds;
        }

        private static void BeginTimedExposure(EntityAgent byEntity, float durationSeconds)
        {
            if (byEntity?.Attributes == null) return;

            ITreeAttribute tree = byEntity.Attributes.GetOrAddTreeAttribute(ExposureTimedAttrKey);

            long nowMs = 0;
            try
            {
                nowMs = byEntity.World?.ElapsedMilliseconds ?? 0;
            }
            catch
            {
                nowMs = 0;
            }

            if (nowMs <= 0) nowMs = Environment.TickCount64;
            tree.SetLong(ExposureTimedStartMsKey, nowMs);

            int durationMs = (int)Math.Round(durationSeconds * 1000f);
            if (durationMs < 1) durationMs = 1;
            tree.SetInt(ExposureTimedDurationMsKey, durationMs);
        }

        private static void ClearTimedExposure(EntityAgent byEntity)
        {
            byEntity?.Attributes?.RemoveAttribute(ExposureTimedAttrKey);
        }

        private static bool IsTimedExposureActive(EntityAgent byEntity, out float durationSeconds)
        {
            durationSeconds = 0f;
            if (byEntity?.Attributes == null) return false;

            ITreeAttribute? tree = byEntity.Attributes.GetTreeAttribute(ExposureTimedAttrKey);
            if (tree == null) return false;

            int durationMs = tree.GetInt(ExposureTimedDurationMsKey, 0);
            if (durationMs <= 0) return false;

            durationSeconds = durationMs / 1000f;
            return durationSeconds > 0f;
        }

        private static float GetTimedExposureElapsedSeconds(EntityAgent byEntity)
        {
            if (byEntity?.Attributes == null) return 0f;

            ITreeAttribute? tree = byEntity.Attributes.GetTreeAttribute(ExposureTimedAttrKey);
            if (tree == null) return 0f;

            long startMs = tree.GetLong(ExposureTimedStartMsKey, 0);
            if (startMs <= 0) return 0f;

            long nowMs = 0;
            try
            {
                nowMs = byEntity.World?.ElapsedMilliseconds ?? 0;
            }
            catch
            {
                nowMs = 0;
            }

            if (nowMs <= 0) nowMs = Environment.TickCount64;
            if (nowMs <= startMs) return 0f;

            return (nowMs - startMs) / 1000f;
        }

        private static bool GetLmbPrev(EntityAgent byEntity)
        {
            if (byEntity?.Attributes == null) return false;

            ITreeAttribute tree = byEntity.Attributes.GetOrAddTreeAttribute(ExposureTimedAttrKey);
            return tree.GetBool(ExposureLmbPrevKey, false);
        }

        private static void SetLmbPrev(EntityAgent byEntity, bool value)
        {
            if (byEntity?.Attributes == null) return;

            ITreeAttribute tree = byEntity.Attributes.GetOrAddTreeAttribute(ExposureTimedAttrKey);
            tree.SetBool(ExposureLmbPrevKey, value);
        }

        private static readonly object GroundMeshLock = new object();
        private static readonly Vec3f GroundMeshScaleCenter = new Vec3f(0.5f, 0.5f, 0.5f);
        private static readonly AssetLocation ExposureStartSound = new AssetLocation("collodion", "sounds/exposure-start");
        private static readonly AssetLocation ExposureFinishSound = new AssetLocation("collodion", "sounds/exposure-end");
        private static MultiTextureMeshRef? groundMeshRef;

        private static float NextRandomPitch(IWorldAccessor? world)
        {
            float basePitch = 0.92f;
            float spread = 0.16f;

            try
            {
                if (world?.Rand != null)
                {
                    return basePitch + (float)world.Rand.NextDouble() * spread;
                }
            }
            catch
            {
                // Fallback below.
            }

            return 1f;
        }

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
                mesh.Scale(GroundMeshScaleCenter, 2.5f, 2.5f, 2.5f);

                var meshRefLocal = capi.Render.UploadMultiTextureMesh(mesh);

                lock (GroundMeshLock)
                {
                    if (groundMeshRef != null)
                    {
                        meshRef = groundMeshRef;
                        meshRefLocal.Dispose();
                        return true;
                    }

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

            // Preserve JSON guiTransform values while keeping GUI preview stationary.
            if (target == EnumItemRenderTarget.Gui)
            {
                var t = renderinfo.Transform == null
                    ? new ModelTransform()
                    : RenderPoseUtil.CloneTransform(renderinfo.Transform);

                if (renderinfo.Transform == null)
                {
                    t.Translation = new FastVec3f(0f, 0f, 0f);
                    t.Rotation = new FastVec3f(0f, 0f, 0f);
                    t.Origin.X = 0.5f;
                    t.Origin.Y = 0.5f;
                    t.Origin.Z = 0.5f;
                    t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                }

                t.Rotate = false;
                renderinfo.Transform = t;
            }

            // IMPORTANT: do NOT mutate renderinfo.Transform in-place.
            // VS may reuse the same ModelTransform instance between frames; adding deltas
            // every frame causes accumulation (item drifts away / disappears) and "reset"
            // won't appear to restore it until restart.
            RenderPoseUtil.ApplyPoseDelta(modSys, poseTarget, ref renderinfo);
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (api.Side != EnumAppSide.Client) return false;

            var modSys = api.ModLoader.GetModSystem<CollodionModSystem>();
            if (!modSys.IsViewfinderActive)
            {
                ClearTimedExposure(byEntity);
                SetLmbPrev(byEntity, false);
                return false;
            }

            bool leftDown = byEntity.Controls?.LeftMouseDown == true;
            bool leftPrev = GetLmbPrev(byEntity);
            bool leftPressed = leftDown && !leftPrev;

            if (leftPressed && !IsTimedExposureActive(byEntity, out _))
            {
                bool started = modSys.RequestPhotoCaptureFromViewfinder(byEntity, silentIfBusy: true);
                if (started)
                {
                    float startExposureSeconds = GetExposureSeconds(modSys);
                    if (startExposureSeconds > 0f)
                    {
                        BeginTimedExposure(byEntity, startExposureSeconds);
                        try
                        {
                            api.World.PlaySoundAt(ExposureStartSound, byEntity, null, true, 16f, NextRandomPitch(api.World));
                        }
                        catch
                        {
                            // Sound should never block exposure flow.
                        }
                    }
                }
            }

            SetLmbPrev(byEntity, leftDown);

            if (!IsTimedExposureActive(byEntity, out float exposureSeconds))
            {
                // Keep the interact chain alive while RMB viewfinder is active.
                return true;
            }

            float exposureElapsedSeconds = GetTimedExposureElapsedSeconds(byEntity);
            if (exposureElapsedSeconds < exposureSeconds)
            {
                // Keep interaction active while timed exposure is running.
                return true;
            }

            try
            {
                api.World.PlaySoundAt(ExposureFinishSound, byEntity, null, true, 16f, NextRandomPitch(api.World));
            }
            catch
            {
                // Sound should never block exposure flow.
            }
            ClearTimedExposure(byEntity);
            return true;
        }

        public override bool OnHeldInteractCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            if (api.Side == EnumAppSide.Client)
            {
                if (IsTimedExposureActive(byEntity, out _))
                {
                    // Keep interact active until timed exposure finishes.
                    return false;
                }

                ClearTimedExposure(byEntity);
                SetLmbPrev(byEntity, false);
            }

            return base.OnHeldInteractCancel(secondsPassed, slot, byEntity, blockSelection, entitySel, cancelReason);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side != EnumAppSide.Client) return;

            // Timed exposure may continue after input release; don't clear it here.
            if (IsTimedExposureActive(byEntity, out _))
            {
                return;
            }

            ClearTimedExposure(byEntity);
            SetLmbPrev(byEntity, false);

            // Viewfinder mode exit is driven by tick polling.
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (api.Side != EnumAppSide.Client) return;

            lock (GroundMeshLock)
            {
                groundMeshRef?.Dispose();
                groundMeshRef = null;
            }
        }
    }
}
