using Photocore.AdminTooling;
using Photocore.CameraCapture;
using Photocore.FieldCamera;
using Photocore.PhotoSync.Integration;
using Photocore.PhotoSync.Store;
using Photocore.Tray;
namespace Photocore
{
    // Lazy feature bridge instances used by the mod-system entrypoints.
    // Callsites use modSys.XxxBridge.Method() directly rather than forwarding each method here.
    public partial class PhotocoreModSystem
    {
        private AdminToolingModSystemBridge? _adminToolingBridge;
        private CameraCaptureModSystemBridge? _cameraCaptureBridge;
        private FieldCameraModSystemBridge? _fieldCameraBridge;
        private PhotoSyncModSystemBridge? _photoSyncBridge;
        private TrayClientEvents? _trayClientEvents;

        internal AdminToolingModSystemBridge AdminToolingBridge => _adminToolingBridge ??= new AdminToolingModSystemBridge(this);
        internal CameraCaptureModSystemBridge CameraCaptureBridge => _cameraCaptureBridge ??= new CameraCaptureModSystemBridge(this);
        internal FieldCameraModSystemBridge FieldCameraBridge => _fieldCameraBridge ??= new FieldCameraModSystemBridge(this);
        internal PhotoSyncModSystemBridge PhotoSyncModSystemBridge => _photoSyncBridge ??= new PhotoSyncModSystemBridge(this);
        internal TrayClientEvents TrayClientEvents => _trayClientEvents ??= new TrayClientEvents(this);

        // Public read-only cross-mod entry point onto the photo store. Resolve this type via
        // capi.ModLoader.GetModSystem<PhotocoreModSystem>(withInheritance: true) so a third-party
        // mod works regardless of which head (collodion or kosphotography) is installed.
        // Available only after Start has run (side selection needs ModApi); throws before that.
        public IPhotoStore PhotoStore => PhotoSyncModSystemBridge.PhotoStore;
    }
}
