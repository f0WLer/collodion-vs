namespace Collodion
{
    public sealed class PhotoCapturePipelineConfig
    {
        public string Comment_BlankDetectSampleDivisor = "Blank-frame detection sample density divisor. Lower = less CPU and fewer samples; higher = more CPU and stronger detection.";
        public int BlankDetectSampleDivisor = 32;

        public string Comment_PngCompressionQuality = "PNG compression quality parameter. Lower = faster encode/larger files; higher = slower encode/smaller files.";
        public int PngCompressionQuality = 90;

        internal void ClampInPlace()
        {
            if (BlankDetectSampleDivisor < 4) BlankDetectSampleDivisor = 4;
            if (BlankDetectSampleDivisor > 4096) BlankDetectSampleDivisor = 4096;

            if (PngCompressionQuality < 0) PngCompressionQuality = 0;
            if (PngCompressionQuality > 100) PngCompressionQuality = 100;
        }
    }
}
