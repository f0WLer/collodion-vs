using Vintagestory.API.Client;
using Collodion.CameraCapture;
using Collodion.ImageEffects;

namespace Collodion.AdminTooling
{
    // Dev-time dialog for live-tuning exposure physics toggles and key effects sliders.
    // Opened via the hotkey binding "collodion-exposuregui" (default: unbound, assignable in game settings).
    // Changes take effect immediately; use .collodion effects save to persist effects.
    internal sealed class GuiDialogExposurePhysics : GuiDialog
    {
        // Opened manually via the hotkey handler — no auto-toggle key needed.
        public override string ToggleKeyCombinationCode => string.Empty;

        private readonly VirtualExposureRenderer _renderer;
        private readonly CollodionModSystem _owner;

        internal GuiDialogExposurePhysics(
            ICoreClientAPI capi,
            VirtualExposureRenderer renderer,
            CollodionModSystem owner)
            : base(capi)
        {
            _renderer = renderer;
            _owner    = owner;
        }

        public override void OnGuiOpened() => ComposeDialog();

        // Always returns the live effects config, initialising it if somehow null.
        private ImageEffectsConfig Effects => _owner.Config.Effects ??= new ImageEffectsConfig();

        private void ComposeDialog()
        {
            const double dialogW  = 360.0;
            const double labelW   = 160.0;
            const double sliderW  = dialogW - labelW - 14.0;
            const double halfW    = dialogW / 2.0;
            const double swSize   = 25.0;
            const double rowH     = 32.0;

            // Helper — absolute child-element bounds (relative to bgBounds content area).
            static ElementBounds B(double x, double y, double w, double h)
                => ElementBounds.Fixed(x, y, w, h);

            double y = 28.0;

            // ── Physics section ──────────────────────────────────────────────────
            var physHeader = B(0, y, dialogW, 20);
            y += 26;

            // Row 1: Linearize  |  Spectral Weights
            var sw1 = B(0,          y,              swSize, swSize);
            var lb1 = B(swSize + 6, y + 3,          halfW - swSize - 12, 20);
            var sw2 = B(halfW,      y,              swSize, swSize);
            var lb2 = B(halfW + swSize + 6, y + 3,  halfW - swSize - 12, 20);
            y += rowH;

            // Row 2: H&D Curve  |  Log Accumulation
            // (sw3/lb3, sw4/lb4)
            var sw3 = B(0,          y,              swSize, swSize);
            var lb3 = B(swSize + 6, y + 3,          halfW - swSize - 12, 20);
            var sw4 = B(halfW,      y,              swSize, swSize);
            var lb4 = B(halfW + swSize + 6, y + 3,  halfW - swSize - 12, 20);
            y += rowH;

            // Row 3: Normalize  |  Apply Finishing
            var sw5 = B(0,          y,              swSize, swSize);
            var lb5 = B(swSize + 6, y + 3,          halfW - swSize - 12, 20);
            var sw6 = B(halfW,      y,              swSize, swSize);
            var lb6 = B(halfW + swSize + 6, y + 3,  halfW - swSize - 12, 20);
            y += rowH + 8;
            // ── Chemistry section ─────────────────────────────────────────────────
            var chemHeader = B(0, y, dialogW, 20);
            y += 26;

            // Slider rows for chemistry params
            var (lDev, sDev) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGam, sGam) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lRed, sRed) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGrn2, sGrn2) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lBlu, sBlu) = SliderRow(y, labelW, sliderW); y += rowH;

            // Inertia: float 0..1 → int 0..100 (÷100 = float)
            var (lIne, sIne) = SliderRow(y, labelW, sliderW); y += rowH;

            // Reciprocity: float 0.5..1.0 → int 50..100 (÷100 = float)
            var (lRec, sRec) = SliderRow(y, labelW, sliderW); y += rowH;

            // Exposure Gain: float 0.25..5.0 → int 25..500 (÷100 = float)
            var (lExp, sExp) = SliderRow(y, labelW, sliderW); y += rowH;

            // Reset chemistry button (right-aligned, same row after the last slider)
            var resetChemBtn = B(dialogW - 130, y, 130, 22);
            y += rowH + 4;

            // * Note: These options don't show up in the exposure preview, only in the final render.
            var astLabel = B(0, y, dialogW, 8);
            y += 26;

            // ── Effects section ───────────────────────────────────────────────────
            var fxHeader = B(0, y, dialogW, 20);
            y += 26;

            // Slider rows: [label][slider]
            var (lCon,  sCon)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lBri,  sBri)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSfl,  sSfl)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lCst,  sCst)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSho,  sSho)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSky,  sSky)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGrn,  sGrn)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lVig,  sVig)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lImp,  sImp)  = SliderRow(y, labelW, sliderW); y += rowH + 8;

            // ── Dev Tools section ───────────────────────────────────────────────
            var devHeader    = B(0, y, dialogW, 20);
            y += 26;

            var swPreview = B(0,          y,         swSize, swSize);
            var lbPreview = B(swSize + 6, y + 3,     200,    20);
            y += rowH;

            var givePlateBtn = B(0, y, 220, 25);
            y += rowH + 4;

            // Close button
            var closeBtn = B(dialogW - 90, y, 90, 25);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("phototesting-expphysics", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Exposure Physics Tuner", () => TryClose())
                .BeginChildElements(bgBounds)

                // Physics header
                .AddStaticText("─── Physics ───", CairoFont.WhiteSmallText(), physHeader)

                // Row 1
                .AddSwitch(v => { _renderer.SetPhysics("linearize", v); _renderer.RequestPreviewRefresh(); }, sw1, "sw-linearize", swSize)
                .AddStaticText("Linearize",       CairoFont.WhiteDetailText(), lb1)
                .AddAutoSizeHoverText("Converts each video frame from gamma-encoded sRGB to linear light before accumulation (`pow((c+0.055)/1.055, 2.4)`). Disabled: raw gamma values are summed, compressing shadows and brightening highlights artificially.", CairoFont.WhiteSmallText(), 400, lb1)

                .AddSwitch(v => { _renderer.SetPhysics("spectral",   v); _renderer.RequestPreviewRefresh(); }, sw2, "sw-spectral",  swSize)
                .AddStaticText("Spectral Weights", CairoFont.WhiteDetailText(), lb2)
                .AddAutoSizeHoverText("Collapses the RGB channels using per-channel sensitivity weights (Red/Green/Blue Sensitivity sliders) to a single luminance value, then renders greyscale. Disabled: uses Rec.601 luma (`0.299R + 0.587G + 0.114B`) for the H&D path instead, ignoring the sensitivity sliders.", CairoFont.WhiteSmallText(), 400, lb2)


                // Row 2
                .AddSwitch(v => { _renderer.SetPhysics("hdcurve",  v); _renderer.RequestPreviewRefresh(); }, sw3, "sw-hdcurve",  swSize)
                .AddStaticText("H&D Curve",       CairoFont.WhiteDetailText(), lb3)
                .AddAutoSizeHoverText("Applies the Hurter-Driffield density function `log(1 + E·k)^γ` during develop, simulating how silver halide grains respond non-linearly to exposure. Disabled: accumulated exposure maps directly to output with no chemical response curve.", CairoFont.WhiteSmallText(), 400, lb3)
                
                .AddSwitch(v => { _renderer.SetPhysics("logaccum", v); _renderer.RequestPreviewRefresh(); }, sw4, "sw-logaccum", swSize)
                .AddStaticText("Log Accumulation*", CairoFont.WhiteDetailText(), lb4)
                .AddAutoSizeHoverText("Bakes `log(1 + s·k)` *per frame during accumulation* rather than once at develop time. Shapes the toe and shoulder continuously as frames pile up, matching how a real emulsion integrates light over time. Disabled: frames are summed linearly and the log is applied only at develop.", CairoFont.WhiteSmallText(), 400, lb4)

                // Row 3
                .AddSwitch(v => { _renderer.SetPhysics("normalize", v); _renderer.RequestPreviewRefresh(); }, sw5, "sw-normalize", swSize)
                .AddStaticText("Normalize*",    CairoFont.WhiteDetailText(), lb5)
                .AddAutoSizeHoverText("Divides accumulated exposure by the actual frame count rather than the target frame count. Keeps apparent brightness stable when pausing mid-exposure. Disabled: exposure brightness scales with how many frames were captured relative to the target.", CairoFont.WhiteSmallText(), 400, lb5)
                
                .AddSwitch(v => { _renderer.ApplyFinishing = v; _renderer.RequestPreviewRefresh(); }, sw6, "sw-finishing", swSize)
                .AddStaticText("Apply Finishing", CairoFont.WhiteDetailText(), lb6)
                .AddAutoSizeHoverText("Previews the post-processing ImageEffects stages applied to the final finished plate.", CairoFont.WhiteSmallText(), 400, lb6)


                // Chemistry header
                .AddStaticText("─── Chemistry ───", CairoFont.WhiteSmallText(), chemHeader)

                // Dev Strength: float 0..20 → int 0..200 (÷10 = float)
                .AddStaticText("Dev Strength",      CairoFont.WhiteDetailText(), lDev)
                .AddSlider(v => { _renderer.SetChemistry("devstrength", v / 10f); _renderer.RequestPreviewRefresh(); return true; }, sDev, "sl-devstrength")
                .AddAutoSizeHoverText("The `k` grain-sensitivity constant. In log-space mode it scales each frame's contribution inside `log(1 + s·k)` before accumulation, shaping the toe of the curve. In linear mode it applies during develop as `log(1 + E·k)`. Higher = faster shadow build-up and compressed highlights.", CairoFont.WhiteSmallText(), 400, lDev)


                // H&D Gamma: float 0.5..2.5 → int 50..250 (÷100 = float)
                .AddStaticText("H&D Gamma",          CairoFont.WhiteDetailText(), lGam)
                .AddSlider(v => { _renderer.SetChemistry("hdgamma", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sGam, "sl-hdgamma")
                .AddAutoSizeHoverText("The exponent `γ` applied to the density value after the log curve: `density^γ`. Controls overall contrast slope of the final image. Values below 1.0 flatten contrast; above 1.0 steepen it.", CairoFont.WhiteSmallText(), 400, lGam)


                // Spectral sensitivities: float 0..2 → int 0..200 (÷100 = float)
                .AddStaticText("Red Sensitivity",    CairoFont.WhiteDetailText(), lRed)
                .AddSlider(v => { _renderer.SetChemistry("redsens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sRed, "sl-redsens")
                .AddAutoSizeHoverText("Relative spectral response weights for the red channel, only active when Spectral Weights is on.", CairoFont.WhiteSmallText(), 400, lRed)


                .AddStaticText("Green Sensitivity",  CairoFont.WhiteDetailText(), lGrn2)
                .AddSlider(v => { _renderer.SetChemistry("greensens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sGrn2, "sl-greensens")
                .AddAutoSizeHoverText("Relative spectral response weights for the green channel, only active when Spectral Weights is on.", CairoFont.WhiteSmallText(), 400, lGrn2)


                .AddStaticText("Blue Sensitivity",   CairoFont.WhiteDetailText(), lBlu)
                .AddSlider(v => { _renderer.SetChemistry("bluesens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sBlu, "sl-bluesens")
                .AddAutoSizeHoverText("Relative spectral response weights for the blue channel, only active when Spectral Weights is on.", CairoFont.WhiteSmallText(), 400, lBlu)


                .AddStaticText("Inertia Point",       CairoFont.WhiteDetailText(), lIne)
                .AddSlider(v => { _renderer.SetChemistry("inertia", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sIne, "sl-inertia")
                .AddAutoSizeHoverText("The minimum exposure threshold below which no density is produced. Models the toe of real film where very low light leaves no trace. Higher values clip dark regions to pure black.", CairoFont.WhiteSmallText(), 400, lIne)


                .AddStaticText("Reciprocity Exp*",     CairoFont.WhiteDetailText(), lRec)
                .AddSlider(v => { _renderer.SetChemistry("reciprocity", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sRec, "sl-reciprocity")
                .AddAutoSizeHoverText("Schwarzschild reciprocity failure factor. Multiplies exposure before the density calculation: `E *= reciprocity`. Values below 1.0 mean long exposures underperform relative to what the frame count predicts — mimicking the real-world failure of collodion to obey the exposure law at low intensities.", CairoFont.WhiteSmallText(), 400, lRec)


                .AddStaticText("Exposure Gain",       CairoFont.WhiteDetailText(), lExp)
                .AddSlider(v => { _renderer.SetChemistry("exposuregain", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sExp, "sl-exposuregain")
                .AddAutoSizeHoverText("Final brightness multiplier applied during develop: `E = sum · gain / frames`. Calibrates mid-tone brightness independently of the full-white white point — game scenes rarely hit s=1.0, so 1.0 leaves them dark. Higher = brighter overall exposure. Affects both the preview and the final image.", CairoFont.WhiteSmallText(), 400, lExp)


                .AddSmallButton("Reset to Config Defaults", OnResetChemistry, resetChemBtn)

                .AddStaticText("* These options don't affect the exposure preview, only the final image.", CairoFont.WhiteDetailText(), astLabel)

                // Effects header
                .AddStaticText("─── Effects ───", CairoFont.WhiteSmallText(), fxHeader)

                .AddStaticText("Contrast",           CairoFont.WhiteDetailText(), lCon)
                .AddSlider(v => { Effects.Contrast          = v / 100f; return true; }, sCon, "sl-contrast")

                .AddStaticText("Brightness",          CairoFont.WhiteDetailText(), lBri)
                .AddSlider(v => { Effects.Brightness         = v / 100f; return true; }, sBri, "sl-brightness")

                .AddStaticText("Shadow Floor",        CairoFont.WhiteDetailText(), lSfl)
                .AddSlider(v => { Effects.ShadowFloor        = v / 100f; return true; }, sSfl, "sl-shadowfloor")

                .AddStaticText("Contrast Start",      CairoFont.WhiteDetailText(), lCst)
                .AddSlider(v => { Effects.ContrastStart      = v / 100f; return true; }, sCst, "sl-contraststart")

                .AddStaticText("Highlight Shoulder",  CairoFont.WhiteDetailText(), lSho)
                .AddSlider(v => { Effects.HighlightShoulder  = v / 100f; return true; }, sSho, "sl-shoulder")

                .AddStaticText("Sky Blowout",         CairoFont.WhiteDetailText(), lSky)
                .AddSlider(v => { Effects.SkyBlowout         = v / 100f; return true; }, sSky, "sl-skyblowout")

                .AddStaticText("Grain",               CairoFont.WhiteDetailText(), lGrn)
                .AddSlider(v => { Effects.Grain               = v / 100f; return true; }, sGrn, "sl-grain")

                .AddStaticText("Vignette",            CairoFont.WhiteDetailText(), lVig)
                .AddSlider(v => { Effects.Vignette            = v / 100f; return true; }, sVig, "sl-vignette")

                .AddStaticText("Imperfection",        CairoFont.WhiteDetailText(), lImp)
                .AddSlider(v => { Effects.Imperfection        = v / 100f; return true; }, sImp, "sl-imperfection")

                .AddStaticText("─── Dev Tools ───", CairoFont.WhiteSmallText(), devHeader)
                .AddSwitch(v => _owner.Config.Viewfinder.DebugPreviewPeak = v, swPreview, "sw-preview-peak", swSize)
                .AddStaticText("Preview Peak", CairoFont.WhiteDetailText(), lbPreview)
                .AddSmallButton("Give Sensitized Plate", OnGiveSensitizedPlate, givePlateBtn)

                .AddSmallButton("Close", TryClose, closeBtn)

                .EndChildElements()
                .Compose();

            // ── Initialise switch states ──────────────────────────────────────────
            var c = SingleComposer;
            c.GetSwitch("sw-linearize").SetValue(_renderer.Physics.Linearize);
            c.GetSwitch("sw-spectral") .SetValue(_renderer.Physics.SpectralWeights);
            c.GetSwitch("sw-hdcurve")  .SetValue(_renderer.Physics.HDCurve);
            c.GetSwitch("sw-logaccum") .SetValue(_renderer.Physics.LogAccumulation);
            c.GetSwitch("sw-normalize").SetValue(_renderer.Physics.Normalize);
            c.GetSwitch("sw-finishing").SetValue(_renderer.ApplyFinishing);
            c.GetSwitch("sw-preview-peak").SetValue(_owner.Config.Viewfinder.DebugPreviewPeak);

            // Sliders: 0-1 floats → int 0-100; Contrast 0-3 → 0-300; Brightness -1..1 → -100..100.
            var fx = Effects;
            c.GetSlider("sl-contrast")    .SetValues((int)(fx.Contrast         * 100), 0,    300, 1);
            c.GetSlider("sl-brightness")  .SetValues((int)(fx.Brightness       * 100), -100, 100, 1);
            c.GetSlider("sl-shadowfloor") .SetValues((int)(fx.ShadowFloor      * 100), 0,    100, 1);
            c.GetSlider("sl-contraststart").SetValues((int)(fx.ContrastStart   * 100), 0,    100, 1);
            c.GetSlider("sl-shoulder")    .SetValues((int)(fx.HighlightShoulder* 100), 0,    100, 1);
            c.GetSlider("sl-skyblowout")  .SetValues((int)(fx.SkyBlowout       * 100), 0,    100, 1);
            c.GetSlider("sl-grain")       .SetValues((int)(fx.Grain            * 100), 0,    100, 1);
            c.GetSlider("sl-vignette")    .SetValues((int)(fx.Vignette         * 100), 0,    100, 1);
            c.GetSlider("sl-imperfection").SetValues((int)(fx.Imperfection     * 100), 0,    100, 1);

            // Chemistry sliders — seeded from effective values (override if active, else process profile).
            // Dev Strength: ×10 → int 0..200  |  H&D Gamma: ×100 → int 50..250  |  Sens: ×100 → int 0..200
            var proc = _renderer.ActiveProcess;
            c.GetSlider("sl-devstrength").SetValues((int)(_renderer.Physics.EffectiveDevStrength(proc) * 10),  0,   200, 1);
            c.GetSlider("sl-hdgamma")    .SetValues((int)(_renderer.Physics.EffectiveHDGamma(proc)     * 100), 50,  250, 1);
            c.GetSlider("sl-redsens")    .SetValues((int)(_renderer.Physics.EffectiveRedSens(proc)     * 100), 0,   200, 1);
            c.GetSlider("sl-greensens")  .SetValues((int)(_renderer.Physics.EffectiveGreenSens(proc)   * 100), 0,   200, 1);
            c.GetSlider("sl-bluesens")    .SetValues((int)(_renderer.Physics.EffectiveBlueSens(proc)    * 100), 0,   200, 1);
            // Inertia: 0..1 ×100 → 0..100  |  Reciprocity: 0.5..1.0 ×100 → 50..100
            c.GetSlider("sl-inertia")     .SetValues((int)(_renderer.Physics.EffectiveInertia(proc)      * 100), 0,   100, 1);
            c.GetSlider("sl-reciprocity") .SetValues((int)(_renderer.Physics.EffectiveReciprocity(proc)  * 100), 50,  100, 1);
            // Exposure Gain: 0.25..5.0 ×100 → 25..500
            c.GetSlider("sl-exposuregain").SetValues((int)(_renderer.Physics.EffectiveExposureGain(proc) * 100), 25,  500, 1);
        }

        // Returns a (labelBounds, sliderBounds) pair for a standard side-by-side row.
        private static (ElementBounds label, ElementBounds slider) SliderRow(
            double y, double labelW, double sliderW)
        {
            const double h = 22.0;
            return (
                ElementBounds.Fixed(0,           y, labelW,  h),
                ElementBounds.Fixed(labelW + 14, y, sliderW, h)
            );
        }

        private bool OnResetChemistry()
        {
            _renderer.ResetChemistryOverrides();
            // Reopen the dialog to reseed all sliders from the process profile defaults.
            TryClose();
            TryOpen();
            return true;
        }

        private bool OnGiveSensitizedPlate()
        {
            _owner.ClientChannel?.SendPacket(new GiveSensitizedPlatePacket());
            return true;
        }

    }
}
