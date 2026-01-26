using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public abstract class ItemPlateBase : Item
    {
        private const float GroundScale = 2.5f;
        private static readonly object GroundMeshLock = new object();
        private static readonly Dictionary<string, MultiTextureMeshRef> GroundMeshCache = new Dictionary<string, MultiTextureMeshRef>(StringComparer.OrdinalIgnoreCase);

        private bool TryGetGroundMesh(ICoreClientAPI capi, ItemStack itemstack, out MultiTextureMeshRef? meshRef)
        {
            meshRef = null;
            string code = itemstack?.Collectible?.Code?.ToShortString() ?? Code?.ToShortString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return false;

            lock (GroundMeshLock)
            {
                if (GroundMeshCache.TryGetValue(code, out var cached) && cached != null)
                {
                    meshRef = cached;
                    return true;
                }
            }

            try
            {
                capi.Tesselator.TesselateItem(this, out MeshData mesh);
                mesh.Scale(new Vec3f(0.5f, 0.5f, 0.5f), GroundScale, GroundScale, GroundScale);
                var uploaded = capi.Render.UploadMultiTextureMesh(mesh);

                lock (GroundMeshLock)
                {
                    GroundMeshCache[code] = uploaded;
                }

                meshRef = uploaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
#pragma warning restore CS0618

            try
            {
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();

                if (target == EnumItemRenderTarget.Ground)
                {
                    if (TryGetGroundMesh(capi, itemstack, out var meshRef) && meshRef != null)
                    {
                        renderinfo.ModelRef = meshRef;
                    }
                }

                // Inventory/hover preview uses the GUI render target and applies its own animated rotation.
                // For alignment tuning, we want a stable, non-spinning preview.
                if (target == EnumItemRenderTarget.Gui)
                {
                    var t = new ModelTransform();
                    t.Origin.X = 0.5f;
                    t.Origin.Y = 0.5f;
                    t.Origin.Z = 0.5f;
                    t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                    renderinfo.Transform = t;
                }

                // NOTE: EnumItemRenderTarget.HandFp is obsolete in newer API, but still correct on 1.21.6.
#pragma warning disable CS0618
                string poseKey = target switch
                {
                    EnumItemRenderTarget.HandFp => "plate-fp",
                    EnumItemRenderTarget.HandTp => "plate-tp",
                    EnumItemRenderTarget.Gui => "plate-gui",
                    EnumItemRenderTarget.Ground => "plate-ground",
                    _ => string.Empty
                };
#pragma warning restore CS0618

                if (!string.IsNullOrEmpty(poseKey))
                {
                    RenderPoseUtil.ApplyPoseDelta(modSys, poseKey, ref renderinfo);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public sealed class ItemGenericPlate : ItemPlateBase
    {
    }
}
