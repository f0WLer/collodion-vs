namespace Photocore.CameraCapture
{
    // Shared with EntityPlayerSelfPortraitPatch for one virtual render pass.
    internal static class VirtualCameraSelfPortraitContext
    {
        [ThreadStatic]
        internal static bool Active;

        [ThreadStatic]
        private static float[]? _tmpTranslate;

        [ThreadStatic]
        private static float[]? _tmpModel;

        internal static float[] TmpTranslate => _tmpTranslate ??= Vintagestory.API.MathTools.Mat4f.Create();
        internal static float[] TmpModel    => _tmpModel    ??= Vintagestory.API.MathTools.Mat4f.Create();
    }
}
