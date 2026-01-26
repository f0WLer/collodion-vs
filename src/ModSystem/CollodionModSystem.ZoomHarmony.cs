using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private static bool harmonyProjectionPatched;
        private static string? harmonyProjectionMechanism;
        private static string? harmonyProjectionParam0Name;
        private static string? harmonyProjectionParam1Name;

        private static float lastSet3DProjectionArg0;
        private static float lastSet3DProjectionArg1;
        private static float lastSet3DProjectionOut0;
        private static float lastSet3DProjectionOut1;
        private static long lastSet3DProjectionMs;
        private static int lastSet3DProjectionScaledIndex;

        private static float baselineZtar;
        private static float baselineFov;

        private bool TryEnsureHarmonyProjectionZoomPatch()
        {
            if (harmonyProjectionPatched) return true;
            if (ClientApi == null) return false;

            try
            {
                var harmony = new Harmony("collodion.viewfinderzoom");

                // Spyglass patches ClientMain.Set3DProjection(float,float). Patch the same method.
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;
                MethodInfo? set3d = typeof(ClientMain).GetMethod(
                    "Set3DProjection",
                    Flags,
                    binder: null,
                    types: new[] { typeof(float), typeof(float) },
                    modifiers: null
                );

                if (set3d == null) return false;

                try
                {
                    var pars = set3d.GetParameters();
                    if (pars.Length >= 2)
                    {
                        harmonyProjectionParam0Name = pars[0].Name;
                        harmonyProjectionParam1Name = pars[1].Name;
                    }
                }
                catch
                {
                    harmonyProjectionParam0Name = null;
                    harmonyProjectionParam1Name = null;
                }

                MethodInfo? transpilerMi = typeof(CollodionModSystem).GetMethod(
                    nameof(Set3DProjectionTranspiler),
                    BindingFlags.Static | BindingFlags.NonPublic
                );

                if (transpilerMi == null) return false;

                harmony.Patch(set3d, transpiler: new HarmonyMethod(transpilerMi));

                harmonyProjectionPatched = true;
                harmonyProjectionMechanism = $"Harmony: {typeof(ClientMain).FullName}.Set3DProjection(float,float)";

                lastSet3DProjectionMs = 0;
                lastSet3DProjectionScaledIndex = -1;

                if (ClientConfig?.ShowZoomMechanismChat == true || ClientConfig?.ShowDebugLogs == true)
                {
                    ClientApi.Logger.Notification("Wetplate: " + harmonyProjectionMechanism);
                }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    ClientApi.Logger.Warning("Wetplate: Harmony projection zoom patch failed: " + ex);
                }
                catch
                {
                    // ignore
                }

                return false;
            }
        }

        // Spyglass-style: transpiler inserts a call at the beginning of ClientMain.Set3DProjection
        // to rewrite the fov argument.
        private static IEnumerable<CodeInstruction> Set3DProjectionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> output = new List<CodeInstruction>();

            // fov = AdjustFov(ztar, fov);
            output.Add(new CodeInstruction(OpCodes.Ldarg_1));
            output.Add(new CodeInstruction(OpCodes.Ldarg_2));
            output.Add(new CodeInstruction(OpCodes.Call, typeof(CollodionModSystem).GetMethod(
                nameof(AdjustFov),
                BindingFlags.Static | BindingFlags.NonPublic
            )));
            output.Add(new CodeInstruction(OpCodes.Starg_S, 2));

            output.AddRange(instructions);
            return output;
        }

        private static float AdjustFov(float ztar, float fov)
        {
            var inst = ClientInstance;

            float inZtar = ztar;
            float inFov = fov;
            float outZtar = inZtar;
            float outFov = inFov;
            int scaledIndex = -1;

            // Capture baseline when not zooming.
            if ((baselineZtar <= 0f || baselineFov <= 0f) && inZtar > 10f && inZtar < 1000000f && LooksLikeFov(inFov))
            {
                baselineZtar = inZtar;
                baselineFov = inFov;
            }

            bool shouldZoom = inst?.IsViewfinderActive == true;
            if (shouldZoom)
            {
                float mult = ViewfinderZoomMultiplier;
                outFov = ClampZoomedFov(inFov * mult, inFov);
                scaledIndex = 1;
            }

            lastSet3DProjectionArg0 = inZtar;
            lastSet3DProjectionArg1 = inFov;
            lastSet3DProjectionOut0 = outZtar;
            lastSet3DProjectionOut1 = outFov;
            lastSet3DProjectionScaledIndex = scaledIndex;
            lastSet3DProjectionMs = Environment.TickCount64;

            return outFov;
        }

        private static float ClampZoomedFov(float proposed, float oldValue)
        {
            // Mirror ClampFov() logic but keep it static for Harmony patching.
            float basis = oldValue;
            if (basis > 0f && basis < 10f)
            {
                return Math.Max(0.3f, Math.Min(2.5f, proposed));
            }

            return Math.Max(30f, Math.Min(110f, proposed));
        }

        private string? GetZoomMechanismForTip()
        {
            if (harmonyProjectionPatched && !string.IsNullOrEmpty(harmonyProjectionMechanism))
            {
                return harmonyProjectionMechanism;
            }

            return runtimeFovMechanism;
        }

    }
}
