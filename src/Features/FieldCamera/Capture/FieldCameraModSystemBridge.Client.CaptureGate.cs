using Collodion.AdminTooling;
using Collodion.CameraCapture;
using Collodion.Plates;
using Vintagestory.API.Common;

namespace Collodion.FieldCamera
{
    internal sealed partial class FieldCameraModSystemBridge
    {
        internal static class CaptureGateService
        {
            internal static bool TryValidateCaptureRequest(FieldCameraModSystemBridge owner, bool silentIfBusy, bool isMounted, out ItemStack? loadedPlateStack)
            {
                loadedPlateStack = null;

                if (owner.ClientApi == null || owner.ClientChannel == null) return false;
                // Handheld capture needs the client renderers wired at startup; the viewport
                // accumulator pushes frames to the preview sink, so gate on that sink's readiness.
                if (!isMounted && owner.Capture._virtualCameraPreviewRenderer == null) return false;

                // Prevent "late shutter" after RMB release.
                if (!owner.CaptureClientRuntime.GetRightMouseDown()) return false;

                // Shutter gating: You can only take a photo when a sensitized plate is loaded.
                // You should always be able to zoom, so we gate only capture (not BeginViewfinderMode).
                try
                {
                    ItemStack? camStack = CameraItemHelper.GetActiveCameraStack(owner.ClientApi);
                    if (camStack == null) return false;

                    if (!CameraEligibility.IsLoadedCodeSensitized(camStack.Attributes.GetString(ItemFieldcamera.AttrLoadedPlate, string.Empty)))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Collodion: load a sensitized plate to take a photo.");
                        return false;
                    }

                    CameraItemHelper.TryGetLoadedPlateStack(
                        camStack,
                        owner.ClientApi.World,
                        out loadedPlateStack,
                        ex => Log.Debug(owner.ClientApi.Logger, "viewfinder loaded plate resolve failed: {0}", ex.Message));

                    // Keep capture gate permissive when only the lightweight loaded-code attribute exists.
                    if (loadedPlateStack != null && !CameraEligibility.IsPlateExposable(loadedPlateStack))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Collodion: only fresh or paused exposure plates can be exposed.");
                        return false;
                    }

                    if (loadedPlateStack != null && PlateDryingTransition.IsDry(owner.ClientApi.World, loadedPlateStack))
                    {
                        owner.CaptureClientRuntime.ShowShutterGateMessageThrottled("Collodion: the plate has dried and can no longer be used.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (owner.IsBestEffortDebugLoggingEnabled) Log.Warn(owner.ClientApi.Logger, "capture request validation failed: {0}", ex.Message);
                    return false;
                }

                return true;
            }
        }
    }
}
