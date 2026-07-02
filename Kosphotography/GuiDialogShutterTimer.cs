using System;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Kosphotography
{
    // Tiny CTRL+RMB dialog for setting a timed camera's per-burst duration (seconds). A number field with
    // up/down ±1s steppers, clamped to 1..999. Applying writes the duration to the held camera stack and
    // sends it to the server for authoritative persistence.
    internal sealed class GuiDialogShutterTimer : GuiDialog
    {
        private const string FieldKey = "kos-shutter-duration";

        private readonly KosPhotographyMod _mod;
        private ItemSlot? _cameraSlot;
        private int _duration = KosCameraAttrs.DefaultShutterDurationSeconds;

        internal GuiDialogShutterTimer(ICoreClientAPI capi, KosPhotographyMod mod) : base(capi)
        {
            _mod = mod;
        }

        // Opened manually from the CTRL+RMB hook — no auto-toggle key.
        public override string ToggleKeyCombinationCode => string.Empty;

        internal void OpenForSlot(ItemSlot cameraSlot)
        {
            _cameraSlot = cameraSlot;
            _duration = Clamp(cameraSlot?.Itemstack?.Attributes?.GetInt(
                KosCameraAttrs.ShutterDurationAttr, KosCameraAttrs.DefaultShutterDurationSeconds)
                ?? KosCameraAttrs.DefaultShutterDurationSeconds);
            Compose();
            TryOpen();
        }

        private static int Clamp(int v)
            => Math.Clamp(v, KosCameraAttrs.MinShutterDurationSeconds, KosCameraAttrs.MaxShutterDurationSeconds);

        private void Compose()
        {
            ElementBounds minusBtn = ElementBounds.Fixed(0, 30, 36, 30);
            ElementBounds field    = ElementBounds.Fixed(44, 30, 96, 30);
            ElementBounds plusBtn  = ElementBounds.Fixed(148, 30, 36, 30);
            ElementBounds applyBtn = ElementBounds.Fixed(0, 72, 184, 30);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(minusBtn, field, plusBtn, applyBtn);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("kos-shutter-timer", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("kosphotography:shutter-timer-title"), () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddSmallButton("-", OnMinus, minusBtn)
                    .AddNumberInput(field, OnFieldChanged, CairoFont.WhiteDetailText(), FieldKey)
                    .AddSmallButton("+", OnPlus, plusBtn)
                    .AddSmallButton(Lang.Get("kosphotography:shutter-timer-apply"), OnApply, applyBtn)
                .EndChildElements()
                .Compose();

            SyncField();
        }

        private void OnFieldChanged(string val)
        {
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                _duration = Clamp(v);
        }

        private bool OnMinus() { _duration = Clamp(_duration - 1); SyncField(); return true; }
        private bool OnPlus()  { _duration = Clamp(_duration + 1); SyncField(); return true; }

        private void SyncField()
            => SingleComposer?.GetNumberInput(FieldKey)?.SetValue(_duration.ToString(CultureInfo.InvariantCulture));

        private bool OnApply()
        {
            _duration = Clamp(_duration);

            if (_cameraSlot?.Itemstack != null)
            {
                // Local set for immediate feedback; the server packet makes it authoritative.
                _cameraSlot.Itemstack.Attributes.SetInt(KosCameraAttrs.ShutterDurationAttr, _duration);
                _cameraSlot.MarkDirty();
            }

            _mod.SendShutterDuration(_duration);
            TryClose();
            return true;
        }
    }
}
