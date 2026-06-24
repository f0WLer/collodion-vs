using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Photochemistry.Exposure
{
    // GL state is saved and restored around all public operations so VS's render pipeline is unaffected.
    internal sealed class GpuExposureAccumulator : IDisposable
    {
        public int Width  { get; }
        public int Height { get; }
        public int FramesAccumulated => _frameCount;
        // Fixed at construction — callers must recreate the buffer when the target sample count changes.
        public int TargetSampleCount => _targetSampleCount;

        public bool  LinearizeInput          { get; set; } = true;
        public bool  ApplySpectralWeights    { get; set; } = true;
        public bool  ApplyHDCurve            { get; set; } = true;
        public bool  NormalizeByActualFrameCount { get; set; } = false;
        public float ExposureGain            { get; set; } = 1.0f;
        public float RedSensitivity          { get; set; } = 0.12f;
        public float GreenSensitivity        { get; set; } = 0.45f;
        public float BlueSensitivity         { get; set; } = 1.00f;
        public float DevelopmentStrength     { get; set; } = 3.5f;
        public float HDGamma                 { get; set; } = 1.1f;
        public float InertiaPoint            { get; set; } = 0f;
        public bool  UseLogAccumulation      { get; set; } = true;
        public float ReciprocityExponent     { get; set; } = 1f;

        private readonly ICoreClientAPI _capi;
        private readonly ClientPlatformWindows _platform;
        private readonly int _targetSampleCount;
        private int _frameCount;

        private FrameBufferRef _sampleFbo = null!;

        private readonly int[] _accumFboIds = new int[2];
        private readonly int[] _accumTexIds = new int[2];
        private int _readIdx  = 0;
        private int _writeIdx = 1;

        private FrameBufferRef _resolveFbo = null!;

        private int _accumProgram;
        private int _resolveProgram;
        private int _fullscreenVao;

        private int _uAccumSample;
        private int _uAccumAccum;
        private int _uAccumLinearize;
        private int _uAccumLogAccum;
        private int _uAccumDevStrength;

        private int _uResolveAccum;
        private int _uResolveInvRef;
        private int _uResolveSpectral;
        private int _uResolveHdCurve;
        private int _uResolveRedSens;
        private int _uResolveGreenSens;
        private int _uResolveBlueSens;
        private int _uResolveDevStrength;
        private int _uResolveGamma;
        private int _uResolveNorm;
        private int _uResolveInertia;
        private int _uResolveLogAccum;
        private int _uResolveReciprocity;

        private bool _disposed;

        // Large-triangle trick: fullscreen quad from gl_VertexID, no VBO needed.
        private const string VertSrc = @"
            #version 330 core
            out vec2 v_uv;
            void main() {
                float x = float((gl_VertexID & 1) == 0 ? 0 : 2);
                float y = float((gl_VertexID & 2) == 0 ? 0 : 2);
                v_uv        = vec2(x, y);
                gl_Position = vec4(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
            }";



        internal GpuExposureAccumulator(ICoreClientAPI capi, int width, int height, int referenceFrameCount)
        {
            _capi = capi;
            _platform = (ClientPlatformWindows)((ClientMain)capi.World).Platform;
            Width  = width;
            Height = height;
            _targetSampleCount = Math.Max(1, referenceFrameCount);

            AllocateGpuResources();
        }

        internal static void ComputeTargetDimensions(int sourceW, int sourceH, int maxDim, out int width, out int height)
        {
            float scale = (float)maxDim / Math.Max(sourceW, sourceH);
            scale = Math.Min(1f, scale);
            width  = Math.Max(1, (int)(sourceW * scale));
            height = Math.Max(1, (int)(sourceH * scale));
        }

        // Overload for raw GL IDs — used when the source is the default back-buffer (ID 0).
        public void Accumulate(int sourceFboId, int sourceWidth, int sourceHeight)
        {
            if (_disposed) return;

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();
                ExposureBlit.BlitYFlipped(sourceFboId, sourceWidth, sourceHeight, _sampleFbo);
                AccumulateFromSampleFbo();
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        public void Accumulate(FrameBufferRef sourceFbo)
        {
            if (_disposed) return;

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();
                ExposureBlit.BlitYFlipped(sourceFbo, _sampleFbo);
                AccumulateFromSampleFbo();
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        // Precondition: GL state saved and render state disabled by the caller.
        private void AccumulateFromSampleFbo()
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[_writeIdx]);
            GL.Viewport(0, 0, Width, Height);

            GL.UseProgram(_accumProgram);
            GL.Uniform1(_uAccumSample,       0);
            GL.Uniform1(_uAccumAccum,         1);
            GL.Uniform1(_uAccumLinearize,     LinearizeInput      ? 1 : 0);
            GL.Uniform1(_uAccumLogAccum,      UseLogAccumulation  ? 1 : 0);
            GL.Uniform1(_uAccumDevStrength,   DevelopmentStrength);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sampleFbo.ColorTextureIds[0]);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

            GL.BindVertexArray(_fullscreenVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            (_readIdx, _writeIdx) = (_writeIdx, _readIdx);
            _frameCount++;
        }

        public void Reset()
        {
            if (_disposed) return;

            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevFbo);
            float[] zeros = [0f, 0f, 0f, 0f];
            for (int i = 0; i < 2; i++)
            {
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[i]);
                GL.ClearBuffer(ClearBuffer.Color, 0, zeros);
            }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevFbo);

            _readIdx  = 0;
            _writeIdx = 1;
            _frameCount = 0;
        }

        public SKBitmap Resolve()
        {
            if (_disposed || _frameCount == 0)
                return CreateBlackBitmap();

            SaveGlState(out GlState state);
            try
            {
                DisableRenderStateForFullscreenPass();

                // ExposureGain scales invRef without touching u_norm — equivalent to extending the exposure beyond the reference.
                float invRef = ExposureGain * (NormalizeByActualFrameCount
                    ? 1f / _frameCount
                    : 1f / _targetSampleCount);

                // Normalise spectral weights so a grey pixel always maps to the same energy.
                float rw = RedSensitivity, gw = GreenSensitivity, bw = BlueSensitivity;
                float wSum = rw + gw + bw;
                if (wSum > 1e-6f) { rw /= wSum; gw /= wSum; bw /= wSum; }

                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo.FboId);
                GL.Viewport(0, 0, Width, Height);

                GL.UseProgram(_resolveProgram);
                GL.Uniform1(_uResolveAccum,       0);
                GL.Uniform1(_uResolveInvRef,      invRef);
                GL.Uniform1(_uResolveSpectral,    ApplySpectralWeights ? 1 : 0);
                GL.Uniform1(_uResolveHdCurve,     ApplyHDCurve         ? 1 : 0);
                GL.Uniform1(_uResolveRedSens,     rw);
                GL.Uniform1(_uResolveGreenSens,   gw);
                GL.Uniform1(_uResolveBlueSens,    bw);
                GL.Uniform1(_uResolveDevStrength, DevelopmentStrength);
                GL.Uniform1(_uResolveGamma,       HDGamma);

                // Normalise output so a full-white scene at exactly the reference exposure maps to 1.0.
                // In log-space mode each full-white frame contributes log(1 + k); after N_ref frames
                // the normalised E = log(1 + k).  In linear mode eRef is still 1.0.
                float eRef = UseLogAccumulation ? MathF.Log(1f + DevelopmentStrength) : 1f;
                float hdRef = ApplyHDCurve
                    ? (UseLogAccumulation
                        ? MathF.Pow(MathF.Max(eRef, 0f), HDGamma)
                        : MathF.Pow(MathF.Max(MathF.Log10(1f + DevelopmentStrength), 0f), HDGamma))
                    : 1f;
                GL.Uniform1(_uResolveNorm,     hdRef > 1e-6f ? 1f / hdRef : 1f);
                GL.Uniform1(_uResolveInertia,  InertiaPoint * eRef);
                GL.Uniform1(_uResolveLogAccum, UseLogAccumulation ? 1 : 0);

                // Reciprocity failure: long exposures yield less effective E than predicted.
                // Factor = (N/N_ref)^(p-1) where p = Schwarzschild exponent (p<1 → failure).
                float reciprocityFactor = ReciprocityExponent < 1f - 1e-4f
                    ? MathF.Pow(MathF.Max((float)_frameCount / _targetSampleCount, 1e-4f), ReciprocityExponent - 1f)
                    : 1f;
                GL.Uniform1(_uResolveReciprocity, reciprocityFactor);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);

                GL.BindVertexArray(_fullscreenVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

                // Synchronous readback — acceptable since Resolve() is triggered only on preview/shutter-close, not per sample.
                GL.Finish();
                int byteCount = Width * Height * 4;
                byte[] pixels = new byte[byteCount];
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resolveFbo.FboId);
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

                var info   = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var bitmap = new SKBitmap(info);
                Marshal.Copy(pixels, 0, bitmap.GetPixels(), byteCount);
                return bitmap;
            }
            finally
            {
                RestoreGlState(in state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FreeGpuResources();
        }

        private void AllocateGpuResources()
        {
            _sampleFbo = _platform.CreateFramebuffer(
                new FramebufferAttrs("photochemistry-gpu-accu-sample", Width, Height)
                {
                    Attachments =
                    [
                        new FramebufferAttrsAttachment
                        {
                            AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                            Texture = new RawTexture
                            {
                                Width               = Width,
                                Height              = Height,
                                PixelInternalFormat = EnumTextureInternalFormat.Rgba8,
                                PixelFormat         = EnumTexturePixelFormat.Rgba,
                                MinFilter           = EnumTextureFilter.Linear,
                                MagFilter           = EnumTextureFilter.Linear,
                            }
                        }
                    ]
                });

            _resolveFbo = _platform.CreateFramebuffer(
                new FramebufferAttrs("photochemistry-gpu-accu-resolve", Width, Height)
                {
                    Attachments =
                    [
                        new FramebufferAttrsAttachment
                        {
                            AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                            Texture = new RawTexture
                            {
                                Width               = Width,
                                Height              = Height,
                                PixelInternalFormat = EnumTextureInternalFormat.Rgba8,
                                PixelFormat         = EnumTexturePixelFormat.Rgba,
                                MinFilter           = EnumTextureFilter.Nearest,
                                MagFilter           = EnumTextureFilter.Nearest,
                            }
                        }
                    ]
                });

            // Raw GL — EnumTextureInternalFormat may not expose Rgba32f.
            GL.GenTextures(2, _accumTexIds);
            GL.GenFramebuffers(2, _accumFboIds);
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int prevFbo);

            for (int i = 0; i < 2; i++)
            {
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[i]);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f,
                    Width, Height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[i]);
                GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _accumTexIds[i], 0);
                // Clear to zero so the first accumulate reads 0.0.
                GL.ClearBuffer(ClearBuffer.Color, 0, [0f, 0f, 0f, 0f]);
            }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevFbo);

            string accumFrag   = LoadShaderSource("Photochemistry.gpu-exposure-accum.frag.glsl");
            string resolveFrag = LoadShaderSource("Photochemistry.gpu-exposure-develop.frag.glsl");
            _accumProgram   = CompileProgram(VertSrc, accumFrag);
            _resolveProgram = CompileProgram(VertSrc, resolveFrag);

            // Cache uniform locations — queried once here so per-draw-call cost is zero.
            _uAccumSample    = GL.GetUniformLocation(_accumProgram,   "u_sample");
            _uAccumAccum     = GL.GetUniformLocation(_accumProgram,   "u_accum");
            _uAccumLinearize = GL.GetUniformLocation(_accumProgram,   "u_linearize");
            _uAccumLogAccum  = GL.GetUniformLocation(_accumProgram,   "u_log_accum");
            _uAccumDevStrength = GL.GetUniformLocation(_accumProgram, "u_dev_strength");

            _uResolveAccum       = GL.GetUniformLocation(_resolveProgram, "u_accum");
            _uResolveInvRef      = GL.GetUniformLocation(_resolveProgram, "u_inv_ref");
            _uResolveSpectral    = GL.GetUniformLocation(_resolveProgram, "u_spectral");
            _uResolveHdCurve     = GL.GetUniformLocation(_resolveProgram, "u_hd_curve");
            _uResolveRedSens     = GL.GetUniformLocation(_resolveProgram, "u_red_sens");
            _uResolveGreenSens   = GL.GetUniformLocation(_resolveProgram, "u_green_sens");
            _uResolveBlueSens    = GL.GetUniformLocation(_resolveProgram, "u_blue_sens");
            _uResolveDevStrength = GL.GetUniformLocation(_resolveProgram, "u_dev_strength");
            _uResolveGamma       = GL.GetUniformLocation(_resolveProgram, "u_gamma");
            _uResolveNorm        = GL.GetUniformLocation(_resolveProgram, "u_norm");
            _uResolveInertia     = GL.GetUniformLocation(_resolveProgram, "u_inertia");
            _uResolveLogAccum    = GL.GetUniformLocation(_resolveProgram, "u_log_accum");
            _uResolveReciprocity = GL.GetUniformLocation(_resolveProgram, "u_reciprocity");

            // Empty VAO required for vertex-ID-based draws.
            GL.GenVertexArrays(1, out _fullscreenVao);
        }

        private void FreeGpuResources()
        {
            try { _capi.Render.DestroyFrameBuffer(_sampleFbo);  } catch { /* best-effort */ }
            try { _capi.Render.DestroyFrameBuffer(_resolveFbo); } catch { /* best-effort */ }

            for (int i = 0; i < 2; i++)
            {
                int fboId = _accumFboIds[i];
                int texId = _accumTexIds[i];
                try { GL.DeleteFramebuffer(fboId); } catch { /* best-effort */ }
                try { GL.DeleteTexture(texId);     } catch { /* best-effort */ }
            }

            try { GL.DeleteProgram(_accumProgram); } catch { /* best-effort */ }
            try { GL.DeleteProgram(_resolveProgram); } catch { /* best-effort */ }
            try { GL.DeleteVertexArray(_fullscreenVao); } catch { /* best-effort */ }
        }

        private SKBitmap CreateBlackBitmap()
        {
            var info   = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var bitmap = new SKBitmap(info);
            bitmap.Erase(SKColors.Black);
            return bitmap;
        }

        private readonly struct GlState
        {
            public readonly int DrawFbo;
            public readonly int ReadFbo;
            public readonly int Program;
            public readonly int Vao;
            public readonly int ActiveTexUnit;
            public readonly int Tex0;
            public readonly int Tex1;
            public readonly int[] Viewport;
            public readonly bool DepthTestEnabled;
            public readonly bool BlendEnabled;

            public GlState(int drawFbo, int readFbo, int program, int vao,
                int activeTexUnit, int tex0, int tex1, int[] viewport,
                bool depthTest, bool blend)
            {
                DrawFbo          = drawFbo;
                ReadFbo          = readFbo;
                Program          = program;
                Vao              = vao;
                ActiveTexUnit    = activeTexUnit;
                Tex0             = tex0;
                Tex1             = tex1;
                Viewport         = viewport;
                DepthTestEnabled = depthTest;
                BlendEnabled     = blend;
            }
        }

        private void SaveGlState(out GlState state)
        {
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int drawFbo);
            GL.GetInteger(GetPName.ReadFramebufferBinding, out int readFbo);
            GL.GetInteger(GetPName.CurrentProgram,         out int prog);
            GL.GetInteger(GetPName.VertexArrayBinding,     out int vao);
            GL.GetInteger(GetPName.ActiveTexture,          out int activeUnit);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.GetInteger(GetPName.TextureBinding2D, out int tex0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.GetInteger(GetPName.TextureBinding2D, out int tex1);

            var vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);

            state = new GlState(drawFbo, readFbo, prog, vao, activeUnit, tex0, tex1, vp,
                GL.IsEnabled(EnableCap.DepthTest), GL.IsEnabled(EnableCap.Blend));
        }

        private static void RestoreGlState(in GlState s)
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, s.DrawFbo);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, s.ReadFbo);
            GL.UseProgram(s.Program);
            GL.BindVertexArray(s.Vao);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, s.Tex0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, s.Tex1);
            GL.ActiveTexture((TextureUnit)s.ActiveTexUnit);

            GL.Viewport(s.Viewport[0], s.Viewport[1], s.Viewport[2], s.Viewport[3]);

            if (s.DepthTestEnabled) GL.Enable(EnableCap.DepthTest);
            else                    GL.Disable(EnableCap.DepthTest);

            if (s.BlendEnabled) GL.Enable(EnableCap.Blend);
            else                GL.Disable(EnableCap.Blend);
        }

        private static void DisableRenderStateForFullscreenPass()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        public byte[]? SerializeAccumulation()
        {
            if (_frameCount <= 0 || _disposed) return null;

            int pixelCount  = Width * Height;
            int floatCount  = pixelCount * 4;
            byte[] blob = new byte[ExposureAccumulationBlobFormat.GetTotalByteCount(Width, Height, 4)];

            ExposureAccumulationBlobFormat.WriteHeader(blob, Width, Height, 4, _frameCount, ExposureAccumulationBlobFormat.GpuBackend);

            SaveGlState(out GlState state);
            try
            {
                float[] floats = new float[floatCount];
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _accumFboIds[_readIdx]);
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.Float, floats);
                System.Buffer.BlockCopy(floats, 0, blob, ExposureAccumulationBlobFormat.HeaderSize, floatCount * sizeof(float));
            }
            finally
            {
                RestoreGlState(in state);
            }

            return blob;
        }

        public bool DeserializeAccumulation(byte[] data, out int frameCount)
        {
            frameCount = 0;
            if (_disposed) return false;
            if (!ExposureAccumulationBlobFormat.TryReadHeader(data, out ExposureAccumulationBlobHeader header)) return false;
            if (header.Width != Width || header.Height != Height || header.ChannelCount != 4 || header.BackendTag != ExposureAccumulationBlobFormat.GpuBackend) return false;

            int floatCount = header.Width * header.Height * 4;
            int expected   = ExposureAccumulationBlobFormat.GetTotalByteCount(header.Width, header.Height, header.ChannelCount);
            if (data.Length < expected) return false;

            float[] floats = new float[floatCount];
            System.Buffer.BlockCopy(data, ExposureAccumulationBlobFormat.HeaderSize, floats, 0, floatCount * sizeof(float));

            SaveGlState(out GlState state);
            try
            {
                GL.BindTexture(TextureTarget.Texture2D, _accumTexIds[_readIdx]);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height,
                    PixelFormat.Rgba, PixelType.Float, floats);

                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _accumFboIds[_writeIdx]);
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
            finally
            {
                RestoreGlState(in state);
            }

            _frameCount = header.FrameCount;
            frameCount  = header.FrameCount;
            return true;
        }

        private static string LoadShaderSource(string resourceName)
        {
            var asm = typeof(GpuExposureAccumulator).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded shader not found: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static int CompileProgram(string vertSrc, string fragSrc)
        {
            int vert = CompileShader(ShaderType.VertexShader,   vertSrc);
            int frag = CompileShader(ShaderType.FragmentShader, fragSrc);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vert);
            GL.AttachShader(prog, frag);
            GL.LinkProgram(prog);
            GL.DetachShader(prog, vert);
            GL.DetachShader(prog, frag);
            GL.DeleteShader(vert);
            GL.DeleteShader(frag);

            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linkOk);
            if (linkOk == 0)
            {
                string log = GL.GetProgramInfoLog(prog);
                GL.DeleteProgram(prog);
                throw new InvalidOperationException($"Exposure accumulator shader link failed: {log}");
            }
            return prog;
        }

        private static int CompileShader(ShaderType type, string src)
        {
            int id = GL.CreateShader(type);
            GL.ShaderSource(id, src);
            GL.CompileShader(id);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
            {
                string log = GL.GetShaderInfoLog(id);
                GL.DeleteShader(id);
                throw new InvalidOperationException($"Exposure accumulator {type} compilation failed: {log}");
            }
            return id;
        }
    }
}
