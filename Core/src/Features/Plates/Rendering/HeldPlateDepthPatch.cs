#pragma warning disable IDE1006 // Harmony magic parameters require __ prefix
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Photocore.Plates.Rendering
{
    // Held plate textures are uniformly semi-transparent (glass). The engine renders first-person
    // held items in the opaque stage with alpha-test + depth-write on (EntityShapeRenderer.RenderItem),
    // so a plate's translucent-but-above-cutoff pixels lay down a solid depth wall. Anything drawn
    // afterward at greater depth (other players, dropped-item entities, and Transparent-pass photo
    // frames) then fails the depth test and vanishes behind the plate.
    //
    // Fix: disable depth *writes* (not depth *test*) for the duration of a plate's held-item draw, so
    // the glass still blends over what's already there and still gets occluded by nearer geometry, but
    // no longer culls what's behind it. Keyed on our own item type; a couple of GLDepthMask toggles
    // per frame only while a plate is in hand, no draw-call or per-pixel cost.
    internal static class HeldPlateDepthPatch
    {
        private const string HarmonyId = "photocore.plates.helditemdepth";
        private static Harmony? _harmony;
        private static ICoreClientAPI? _capi;

        // RenderItem is the base held-item draw funnel (EntityPlayerShapeRenderer doesn't override it),
        // so one patch covers every entity and both hands. Degrades to a no-op if the game type moves.
        internal static void Apply(ICoreClientAPI api)
        {
            Type? entityShapeRendererType = AccessTools.TypeByName("Vintagestory.GameContent.EntityShapeRenderer");
            if (entityShapeRendererType == null) return;

            _capi = api;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(
                AccessTools.Method(entityShapeRendererType, "RenderItem"),
                prefix: new HarmonyMethod(typeof(HeldPlateDepthPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(HeldPlateDepthPatch), nameof(Postfix)));
        }

        internal static void Remove()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            _capi = null;
        }

        [HarmonyPrefix]
        internal static void Prefix(ItemStack stack, bool isShadowPass, out bool __state)
        {
            __state = false;
            ICoreClientAPI? capi = _capi;
            if (capi == null || isShadowPass) return;
            if (stack?.Collectible is not ItemPlateBase) return;

            capi.Render.GLDepthMask(false);
            __state = true;
        }

        [HarmonyPostfix]
        internal static void Postfix(bool __state)
        {
            if (!__state) return;
            _capi?.Render.GLDepthMask(true);
        }
    }
}
