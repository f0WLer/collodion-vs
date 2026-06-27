using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Photocore.Configuration;

namespace Photocore.Plates
{
    public abstract class ItemPlateBase : Item
    {
        private const float GroundScale = 2.5f;
        private readonly object _groundMeshLock = new();
        private readonly Dictionary<string, MultiTextureMeshRef> _groundMeshCache = new(StringComparer.OrdinalIgnoreCase);

        private bool TryGetGroundMesh(ICoreClientAPI capi, ItemStack itemstack, out MultiTextureMeshRef? meshRef)
        {
            meshRef = null;
            string code = itemstack?.Collectible?.Code?.ToShortString() ?? Code?.ToShortString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code)) return false;

            lock (_groundMeshLock)
            {
                if (_groundMeshCache.TryGetValue(code, out var cached) && cached != null)
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

                lock (_groundMeshLock)
                {
                    _groundMeshCache[code] = uploaded;
                }

                meshRef = uploaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DisposeGroundMeshCache()
        {
            lock (_groundMeshLock)
            {
                if (_groundMeshCache.Count <= 0) return;

                foreach (MultiTextureMeshRef meshRef in _groundMeshCache.Values)
                {
                    try
                    {
                        meshRef?.Dispose();
                    }
                    catch { }
                }

                _groundMeshCache.Clear();
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            #pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            #pragma warning restore CS0618

            try
            {
                if (target == EnumItemRenderTarget.Ground)
                {
                    if (TryGetGroundMesh(capi, itemstack, out var meshRef) && meshRef != null)
                    {
                        renderinfo.ModelRef = meshRef;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            string fallback = base.GetHeldItemName(itemStack);
            return ResolveDisplayName(itemStack, fallback);
        }

        private static string ResolveDisplayName(ItemStack stack, string fallback)
        {
            if (stack == null) return fallback;

            string? explicitNameKey = stack?.Attributes?.GetString("photochemPlateNameLangCode");
            
            if (!string.IsNullOrWhiteSpace(explicitNameKey))
            {
                return Lang.Get(explicitNameKey);
            }

            PlateStage stage = PlateAttributes.GetStage(stack);
            string codePath = stack?.Collectible?.Code?.Path ?? string.Empty;

            if (codePath.Equals("glassplate", StringComparison.OrdinalIgnoreCase))
            {
                if (stage == PlateStage.Clean) return Lang.Get("photocore:plate-name-glass-clean");
                if (stage == PlateStage.Sensitizing) return Lang.Get("photocore:plate-name-glass-sensitizing");
                return Lang.Get("photocore:plate-name-glass");
            }

            if (codePath.Equals("sensitizedplate", StringComparison.OrdinalIgnoreCase))
            {
                // "Exposed" from the moment the exposure starts — the latent image is committed.
                if (stage == PlateStage.Exposing || stage == PlateStage.ExposurePaused || stage == PlateStage.Exposed)
                    return Lang.Get("photocore:plate-name-exposed");
                return Lang.Get("photocore:plate-name-sensitized");
            }

            if (codePath.Equals("photoplate", StringComparison.OrdinalIgnoreCase))
            {
                if (stage == PlateStage.Developing) return Lang.Get("photocore:plate-name-developing");
                if (stage == PlateStage.Developed) return Lang.Get("photocore:plate-name-developed");
                if (stage == PlateStage.Finished) return Lang.Get("photocore:plate-name-photo-finished");
                return Lang.Get("photocore:plate-name-photo");
            }

            return fallback;
        }

        // Suppress the JSON-defined Dry transition once the plate is marked dried,
        // marked never-drying, or in a stage that doesn't track wetness.
        public override TransitionableProperties[]? GetTransitionableProperties(IWorldAccessor world, ItemStack itemstack, Vintagestory.API.Common.Entities.Entity forEntity)
        {
            if (itemstack?.Attributes == null) return base.GetTransitionableProperties(world, itemstack, forEntity);
            if (itemstack.Attributes.GetBool(PlateDryingTransition.AttrDried)) return null;
            if (itemstack.Attributes.GetBool(PlateDryingTransition.AttrNeverDries)) return null;
            if (!ShouldTrackDryness(itemstack)) return null;
            return base.GetTransitionableProperties(world, itemstack, forEntity);
        }

        // Apply rate multipliers that freeze or slow drying based on plate state.
        public override float GetTransitionRateMul(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
        {
            if (transType == EnumTransitionType.Dry)
            {
                if (inSlot?.Itemstack?.Attributes?.GetBool(PlateDryingTransition.AttrStoredInPlateBox) == true)
                    return PlateDryingTransition.ResolveStorageDryingRateMul(api);

                PlateStage stage = PlateAttributes.GetStage(inSlot?.Itemstack);
                if (stage == PlateStage.Exposing)
                {
                    if (PhotocoreConfigAccess.ResolveConfig(api)?.Viewfinder?.PauseDryingDuringExposure ?? true)
                        return 0f;
                }
            }
            return base.GetTransitionRateMul(world, inSlot, transType);
        }

        // When the Dry transition completes, mark the stack dried in place rather than
        // swapping to a transitioned item — keeps the stack as a dry sensitized/photo plate.
        public override ItemStack OnTransitionNow(ItemSlot slot, TransitionableProperties props)
        {
            if (props?.Type == EnumTransitionType.Dry && slot?.Itemstack != null)
            {
                ItemStack stack = slot.Itemstack.Clone();
                stack.Attributes.SetBool(PlateDryingTransition.AttrDried, true);
                stack.Attributes.RemoveAttribute("transitionstate");
                return stack;
            }
            return base.OnTransitionNow(slot, props);
        }

        // Subclasses opt out of dryness tracking for stages that no longer need it
        // (e.g. a finished photo plate is permanent and doesn't dry).
        protected virtual bool ShouldTrackDryness(ItemStack stack) => true;

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (api.Side == EnumAppSide.Client)
            {
                DisposeGroundMeshCache();
            }
        }
    }
}
