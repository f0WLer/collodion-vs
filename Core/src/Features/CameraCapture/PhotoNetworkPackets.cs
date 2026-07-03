using ProtoBuf;

namespace Photocore.CameraCapture
{
    // Protobuf packet DTOs for camera capture messaging.
    // Keep these classes data-only and preserve ProtoMember ids for compatibility.
    [ProtoContract]
    public class CameraLoadPlatePacket
    {
        [ProtoMember(1)]
        public bool Load { get; set; }
    }

    [ProtoContract]
    public class ServerConfigOverridePacket
    {
        [ProtoMember(1)]
        public int MaxDimension { get; set; }
        // Whether finishing effects (grain, vignette, halation, etc.) get baked into developed photos.
        // Photos are permanent and visible to every player, so this is server-authoritative in
        // multiplayer rather than left to whichever client happens to capture — see
        // CameraCaptureModSystemBridge.Server.cs / .Client.cs.
        [ProtoMember(2)]
        public bool ApplyFinishingEffects { get; set; }
        // How often the client should ping the server that it has seen a photo. A malicious/modified
        // client could ignore its own local value and spam pings, so the server dictates this rather
        // than trusting whatever the client claims — see CameraCaptureModSystemBridge.Server.cs / .Client.cs.
        [ProtoMember(3)]
        public int PhotoSeenPingIntervalSeconds { get; set; }
    }

    [ProtoContract]
    public class ServerConfigOverrideRequestPacket { }

    [ProtoContract]
    public class CameraTripodPacket
    {
        [ProtoMember(1)]
        public bool Mount { get; set; }
    }

    [ProtoContract]
    public class CameraMountRequestPacket
    {
        [ProtoMember(1)] public double CameraPosX { get; set; }
        [ProtoMember(2)] public double CameraPosY { get; set; }
        [ProtoMember(3)] public double CameraPosZ { get; set; }
        [ProtoMember(4)] public float CameraYaw { get; set; }
        [ProtoMember(5)] public float CameraPitch { get; set; }
        [ProtoMember(6)] public float CameraFov { get; set; }
        [ProtoMember(7)] public int CameraDimension { get; set; }
    }

    [ProtoContract]
    public class MountedCameraControlPacket
    {
        [ProtoMember(1)] public bool IsExposing { get; set; }
        [ProtoMember(2)] public string ExposureId { get; set; } = string.Empty;
        [ProtoMember(4)] public bool HasCameraState { get; set; }
        [ProtoMember(5)] public double CameraPosX { get; set; }
        [ProtoMember(6)] public double CameraPosY { get; set; }
        [ProtoMember(7)] public double CameraPosZ { get; set; }
        [ProtoMember(8)] public float CameraYaw { get; set; }
        [ProtoMember(9)] public float CameraPitch { get; set; }
        [ProtoMember(10)] public float CameraFov { get; set; }
        [ProtoMember(11)] public int CameraDimension { get; set; }
        [ProtoMember(15)] public bool PrepareIdlePreview { get; set; }
        // Block position of the mounted camera the recipient is shooting through, so the client
        // can hide exactly that camera (and no others) from its virtual capture.
        [ProtoMember(12)] public bool HasMountBlock { get; set; }
        [ProtoMember(13)] public int MountBlockX { get; set; }
        [ProtoMember(14)] public int MountBlockY { get; set; }
        [ProtoMember(16)] public int MountBlockZ { get; set; }
        // Chemistry of the loaded plate, so the client resolves the correct per-process exposure timing
        // and emulsion response instead of defaulting to iodide. Empty when no plate is loaded.
        [ProtoMember(17)] public string Chemistry { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class ExposureStatePacket
    {
        [ProtoMember(1)] public bool IsExposing { get; set; }
        [ProtoMember(2)] public string ExposureId { get; set; } = string.Empty;
        [ProtoMember(3)] public int ExposedFrames { get; set; }
        [ProtoMember(4)] public int TargetFrames { get; set; }
    }

    [ProtoContract]
    public class CameraRestPacket { }

    [ProtoContract]
    internal class SealAndInsertIntoTrayPacket
    {
        [ProtoMember(1)] public string ExposureId { get; set; } = string.Empty;
        [ProtoMember(2)] public string PhotoId    { get; set; } = string.Empty;
        [ProtoMember(3)] public int TrayPosX      { get; set; }
        [ProtoMember(4)] public int TrayPosY      { get; set; }
        [ProtoMember(5)] public int TrayPosZ      { get; set; }
        [ProtoMember(6)] public int TrayPosDim    { get; set; }
    }
}
