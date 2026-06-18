using Collodion.CameraCapture;
using Collodion.CameraCapture.Contracts;
using Collodion.Plates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Collodion.FieldCamera
{
    // Kept off the bridge so the bridge stays pure wiring and this holds the mutable per-tick state.
    internal sealed partial class FieldCameraClientRuntime
    {
        private const float RmbReleaseGraceSeconds = 0.04f;

        private readonly FieldCameraModSystemBridge _owner;

        private bool _suppressViewfinderUntilRmbReleased;
        private float _rmbUpSeconds;
        private bool _lastRmbDown;
        private bool _lastLmbDown;
        private bool _rightMouseDown;
        private MouseEventDelegate? _mouseDownHandler;
        private MouseEventDelegate? _mouseUpHandler;
        private MouseWheelEventDelegate? _mouseWheelHandler;

        internal FieldCameraClientRuntime(FieldCameraModSystemBridge owner)
        {
            _owner = owner;
        }

        // Subscribes to MouseDown/MouseUp so RMB state is event-driven (no Input polling / no try-catch).
        // Also subscribes scroll-wheel and +/- keys for live viewfinder zoom.
        internal void SubscribeMouseEvents(ICoreClientAPI api)
        {
            _mouseDownHandler = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = true; };
            _mouseUpHandler   = (MouseEvent e) => { if (e.Button == EnumMouseButton.Right) _rightMouseDown = false; };
            api.Event.MouseDown += _mouseDownHandler;
            api.Event.MouseUp   += _mouseUpHandler;

            _mouseWheelHandler = (MouseWheelEventArgs e) =>
            {
                if (!_owner.Capture.IsViewfinderActive) return;
                float delta = -e.deltaPrecise * CameraCaptureModSystemBridge.ZoomFovStepRad;
                _owner.Capture.AdjustViewfinderZoom(delta);
                e.SetHandled();
            };
            api.Event.MouseWheelMove += _mouseWheelHandler;
        }

        internal void UnsubscribeMouseEvents(ICoreClientAPI api)
        {
            if (_mouseDownHandler != null)
            {
                var d = _mouseDownHandler;
                BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mousedown", () => api.Event.MouseDown -= d);
                _mouseDownHandler = null;
            }
            if (_mouseUpHandler != null)
            {
                var u = _mouseUpHandler;
                BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mouseup", () => api.Event.MouseUp -= u);
                _mouseUpHandler = null;
            }
            if (_mouseWheelHandler != null)
            {
                var w = _mouseWheelHandler;
                BestEffort.Try(_owner.BestEffortLogger, "unsubscribe viewfinder mousewheel", () => api.Event.MouseWheelMove -= w);
                _mouseWheelHandler = null;
            }
            _rightMouseDown = false;
        }

        internal void OnClientViewfinderTick(float dt)
        {
            if (_owner.ClientApi == null) return;

            if (!string.IsNullOrEmpty(_mountedExposureId) && _owner.Capture._virtualExposureRenderer?.State == ExposureState.Done)
                PersistPartialMountedExposure();

            ItemSlot? activeCameraSlot = CameraItemHelper.GetActiveCameraSlot(_owner.ClientApi);
            bool holdingCamera = activeCameraSlot != null;

            // Track LMB edge unconditionally so the transition from RMB-held to free-running
            // exposure does not generate a spurious LMB press on the first tick.
            bool leftDown = _owner.ClientApi.World.Player?.Entity?.Controls?.LeftMouseDown == true;
            bool leftPressed = leftDown && !_lastLmbDown;
            _lastLmbDown = leftDown;

            bool rightDown = GetRightMouseDown();

            bool rightPressed = rightDown && !_lastRmbDown;
            _lastRmbDown = rightDown;

            // Shift+RMB is reserved for loading a plate into the camera (no zoom/viewfinder).
            bool shiftDown = _owner.ClientApi.World.Player?.Entity?.Controls?.ShiftKey == true || _owner.ClientApi.World.Player?.Entity?.Controls?.Sneak == true;
            bool ctrlDown = _owner.ClientApi.World.Player?.Entity?.Controls?.CtrlKey == true;

            // Shift+Ctrl+RMB: set down the tripod camera as a resting block.
            if (holdingCamera && shiftDown && ctrlDown && rightPressed
                && !_owner.Capture.IsViewfinderActive
                && CameraItemHelper.HasMountedTripod(activeCameraSlot?.Itemstack)
                && _owner.ClientChannel != null)
            {
                _suppressViewfinderUntilRmbReleased = true;
                _owner.ClientChannel.SendPacket(new CameraRestPacket());
                return;
            }

            // Ctrl+RMB (no shift): open the camera's shutter-config UI, if a head installed one
            // (e.g. kosphotograph's timed-shutter duration dialog). Baseline installs none ⇒ no-op.
            if (holdingCamera && ctrlDown && !shiftDown && rightPressed
                && !_owner.Capture.IsViewfinderActive
                && ShutterSeam.ConfigUi != null && activeCameraSlot != null
                && ShutterSeam.ConfigUi.TryOpenFor(activeCameraSlot))
            {
                _suppressViewfinderUntilRmbReleased = true;
                return;
            }

            if (holdingCamera && shiftDown && !ctrlDown && rightDown && !_owner.Capture.IsViewfinderActive)
            {
                // Prevent viewfinder from starting if the player releases shift while still holding RMB.
                _suppressViewfinderUntilRmbReleased = true;

                // Load/unload triggers only on edge press and only when networking is available.
                if (!rightPressed || _owner.ClientChannel == null)
                {
                    return;
                }

                ItemSlot? offhand = _owner.ClientApi.World.Player?.InventoryManager?.OffhandHotbarSlot;
                ItemStack? offstack = offhand?.Itemstack;
                ItemStack? cameraStack = activeCameraSlot?.Itemstack;

                bool cameraLoaded = FieldCameraModSystemBridge.CameraHasLoadedPlate(cameraStack);

                // Tripod mount works regardless of whether a plate is loaded.
                if (CameraItemHelper.IsTripodItemStack(offstack) && !CameraItemHelper.HasMountedTripod(cameraStack))
                {
                    _owner.ClientChannel.SendPacket(new CameraTripodPacket { Mount = true });
                    return;
                }

                if (!cameraLoaded)
                {
                    if (offhand != null && offhand.Empty && CameraItemHelper.HasMountedTripod(cameraStack))
                    {
                        _owner.ClientChannel.SendPacket(new CameraTripodPacket { Mount = false });
                        return;
                    }

                    if (CameraEligibility.CanLoadIntoCamera(offstack)) _owner.ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = true });
                    return;
                }

                if (offhand == null || !offhand.Empty) return;

                _owner.ClientChannel.SendPacket(new CameraLoadPlatePacket { Load = false });

                return;
            }

            if (_owner.Capture.IsViewfinderActive) _owner.Capture.EnsureViewfinderZoomApplied();

            if (!holdingCamera)
            {
                _suppressViewfinderUntilRmbReleased = false;
                _rmbUpSeconds = 0f;
                if (_owner.Capture.IsViewfinderActive) _owner.Capture.EndViewfinderMode();
                return;
            }

            if (!rightDown)
            {
                // While a capture is actively accumulating, keep the viewfinder alive even
                // without RMB held, and listen for LMB to pause.
                if (_owner.Capture.IsExposureCapturing)
                {
                    _rmbUpSeconds = 0f;
                    if (!_owner.Capture.IsViewfinderActive) _owner.Capture.BeginViewfinderMode();
                    if (leftPressed)
                    {
                        var playerEntity = _owner.ClientApi.World.Player?.Entity;
                        if (playerEntity != null)
                            TryToggleViewfinderExposure(playerEntity, silentIfBusy: true);
                    }
                    return;
                }

                _rmbUpSeconds += dt;
                if (_rmbUpSeconds > RmbReleaseGraceSeconds)
                {
                    _suppressViewfinderUntilRmbReleased = false;
                    if (_owner.Capture.IsViewfinderActive) _owner.Capture.EndViewfinderMode();
                }
                return;
            }

            _rmbUpSeconds = 0f;

            // RMB is down and camera is held.
            if (_suppressViewfinderUntilRmbReleased) return;
            if (!_owner.Capture.IsViewfinderActive) _owner.Capture.BeginViewfinderMode();

            // Shutter capture is driven by ItemFieldcamera held-interact callbacks while RMB
            // viewfinder is active so we can use the engine's standard timed interaction meter.
        }

        internal bool GetRightMouseDown()
        {
            return _rightMouseDown;
        }
    }
}
