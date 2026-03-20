using Vintagestory.API.MathTools;

namespace Collodion
{
    internal static class PhotoCropMath
    {
        public static void ComputeCenterCrop(float sourceAspect, float targetAspect, out float keepU, out float keepV, float keepBias = 1f)
        {
            keepU = 1f;
            keepV = 1f;

            if (sourceAspect <= 0f || targetAspect <= 0f)
            {
                return;
            }

            if (sourceAspect > targetAspect)
            {
                keepU = GameMath.Clamp((targetAspect / sourceAspect) * keepBias, 0f, 1f);
            }
            else
            {
                keepV = GameMath.Clamp((sourceAspect / targetAspect) * keepBias, 0f, 1f);
            }
        }
    }
}
