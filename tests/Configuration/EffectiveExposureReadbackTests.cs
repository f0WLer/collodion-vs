using Xunit;
using Photocore.Configuration;

namespace Photocore.Tests.Configuration;

// The exposure seal only ever downscales the accumulation buffer, so a readback buffer smaller than the
// requested capture size silently caps the photo. These pin the derivation that keeps the two in step.
public class EffectiveExposureReadbackTests
{
    [Fact]
    public void MatchesReadbackWhenCaptureIsSmaller()
    {
        var cfg = new ViewfinderConfig { ExposureReadbackMaxDimension = 640, PhotoCaptureMaxDimension = 128 };

        // Supersampling: the extra resolution is scaled away, but it costs nothing to keep the default.
        Assert.Equal(640, cfg.EffectiveExposureReadbackMaxDimension);
    }

    [Fact]
    public void RisesToCaptureSizeWhenCaptureIsLarger()
    {
        var cfg = new ViewfinderConfig { ExposureReadbackMaxDimension = 640, PhotoCaptureMaxDimension = 1024 };

        Assert.Equal(1024, cfg.EffectiveExposureReadbackMaxDimension);
    }

    [Fact]
    public void RisesToAServerOverriddenCaptureSize()
    {
        // In multiplayer the server rewrites PhotoCaptureMaxDimension in memory after the config is
        // clamped. Reading the derived value late is what stops a 640 client from quietly capping a
        // server that asked for 1024.
        var cfg = new ViewfinderConfig();
        cfg.ClampInPlace();
        cfg.PhotoCaptureMaxDimension = ViewfinderConfig.MaxPhotoCaptureMaxDimension;

        Assert.Equal(ViewfinderConfig.MaxPhotoCaptureMaxDimension, cfg.EffectiveExposureReadbackMaxDimension);
    }

    [Fact]
    public void NeverExceedsTheReadbackCeiling()
    {
        var cfg = new ViewfinderConfig
        {
            ExposureReadbackMaxDimension = ViewfinderConfig.MaxExposureReadbackMaxDimension,
            PhotoCaptureMaxDimension = ViewfinderConfig.MaxExposureReadbackMaxDimension * 4
        };

        Assert.Equal(ViewfinderConfig.MaxExposureReadbackMaxDimension, cfg.EffectiveExposureReadbackMaxDimension);
    }

    [Fact]
    public void CaptureCeilingIsReachableByTheReadbackBuffer()
    {
        // Guards the constants themselves: if MaxPhotoCaptureMaxDimension ever climbs above the readback
        // ceiling, every photo above that ceiling is silently downscaled instead of honouring the setting.
        Assert.True(ViewfinderConfig.MaxPhotoCaptureMaxDimension <= ViewfinderConfig.MaxExposureReadbackMaxDimension);
    }
}
