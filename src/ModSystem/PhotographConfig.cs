namespace Collodion
{
    public sealed class PhotographConfig
    {
        public string Comment_CaptionMaxLength = "Maximum caption length accepted/saved for photograph blocks. Set 0 to disable truncation.";
        public int CaptionMaxLength = 200;

        internal void ClampInPlace()
        {
            if (CaptionMaxLength < 0) CaptionMaxLength = 0;
            if (CaptionMaxLength > 5000) CaptionMaxLength = 5000;
        }
    }
}
