using Photochemistry.AdminTooling;
using Photochemistry.CameraCapture;
using Photochemistry.FieldCamera;
using Photochemistry.PhotoSync.Integration;
using Photochemistry.Tray;
namespace Photochemistry
{
    // Lazy feature bridge instances used by the mod-system entrypoints.
    // Callsites use modSys.XxxBridge.Method() directly rather than forwarding each method here.
    public partial class PhotochemistryModSystem
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
    }
}
