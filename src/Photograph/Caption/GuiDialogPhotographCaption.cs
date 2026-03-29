using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public sealed class GuiDialogPhotographCaption : GuiDialogGeneric
    {
        private readonly ICoreClientAPI clientApi;
        private readonly int x;
        private readonly int y;
        private readonly int z;

        private string currentText;

        private int CaptionMaxLength
        {
            get
            {
                try
                {
                    var modSys = clientApi?.ModLoader?.GetModSystem<CollodionModSystem>();
                    return modSys?.Config?.Photograph?.CaptionMaxLength ?? 200;
                }
                catch
                {
                    return 200;
                }
            }
        }

        public GuiDialogPhotographCaption(ICoreClientAPI capi, int x, int y, int z, string initialText) : base("collodion-photograph-caption", capi)
        {
            clientApi = capi;
            this.x = x;
            this.y = y;
            this.z = z;
            currentText = initialText ?? string.Empty;

            DialogTitle = "Photograph caption";
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            ComposeDialog();
        }

        private void ComposeDialog()
        {
            ClearComposers();

            const double pad = 10;
            const double innerWidth = 420;
            const double textHeight = 220;
            const double titleHeight = 30;
            const double gapTitleText = 5;
            const double gapTextButtons = 10;
            const double buttonHeight = 30;

            double dialogWidth = innerWidth + pad * 2;
            double dialogHeight = titleHeight + gapTitleText + textHeight + gapTextButtons + buttonHeight + pad * 2;

            // Fixed-size dialog is the simplest robust option here.
            ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, dialogWidth, dialogHeight);
            ElementBounds bgBounds = ElementBounds.Fill;

            ElementBounds titleBounds = ElementBounds.Fixed(pad, pad, innerWidth, titleHeight);
            ElementBounds textBounds = ElementBounds.Fixed(pad, pad + titleHeight + gapTitleText, innerWidth, textHeight);

            double buttonsY = pad + titleHeight + gapTitleText + textHeight + gapTextButtons;
            double halfButtonWidth = (innerWidth / 2) - 5;
            ElementBounds cancelBounds = ElementBounds.Fixed(pad, buttonsY, halfButtonWidth, buttonHeight);
            ElementBounds saveBounds = ElementBounds.Fixed(pad + halfButtonWidth + 10, buttonsY, halfButtonWidth, buttonHeight);

            SingleComposer = clientApi.Gui
                .CreateCompo("collodion-photograph-caption-" + x + "-" + y + "-" + z, dialogBounds)
                .AddShadedDialogBG(bgBounds, true, 0, 0.6f)
                .AddDialogTitleBar(DialogTitle, OnTitleBarClose, CairoFont.WhiteSmallText(), titleBounds, "title")
                .AddTextArea(textBounds, t => currentText = t ?? string.Empty, CairoFont.SmallTextInput(), "caption")
                .AddSmallButton("Cancel", new ActionConsumable(() =>
                {
                    TryClose();
                    return true;
                }), cancelBounds, EnumButtonStyle.Normal, "cancel")
                .AddSmallButton("Save", new ActionConsumable(() =>
                {
                    SendCaptionToServer();
                    TryClose();
                    return true;
                }), saveBounds, EnumButtonStyle.Normal, "save")
                .Compose();

            try
            {
                var textArea = SingleComposer.GetTextArea("caption");
                textArea.Autoheight = false;
                textArea.SetMaxLength(CaptionMaxLength);
                textArea.SetValue(currentText ?? string.Empty);
            }
            catch
            {
                // Best-effort fallback in case the element type changes.
                TrySetInitialValueReflection(SingleComposer, currentText);
            }
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }

        private void SendCaptionToServer()
        {
            // Important: don't rely solely on the AddTextArea() callback. In some UI states
            // (focus/IME/etc.), the callback may not have fired yet when Save is clicked.
            // Always pull the current value directly from the text area.
            try
            {
                currentText = SingleComposer?.GetTextArea("caption")?.GetText() ?? currentText;
            }
            catch
            {
                // ignore
            }

            try
            {
                // Match the server sanity limit.
                int maxLength = CaptionMaxLength;
                if (maxLength >= 0 && currentText.Length > maxLength)
                {
                    currentText = currentText.Substring(0, maxLength);
                }

                var channel = clientApi.Network.GetChannel("collodion");
                channel.SendPacket(new PhotoCaptionSetPacket
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Caption = currentText ?? string.Empty
                });
            }
            catch
            {
                // ignore
            }

            // Optimistic local update so reopening the editor immediately shows what you saved,
            // even before the server roundtrip comes back.
            try
            {
                var pos = new BlockPos(x, y, z);
                if (clientApi.World?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityPhotograph be)
                {
                    be.SetCaption(currentText);
                    clientApi.World.BlockAccessor.MarkBlockEntityDirty(pos);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void TrySetInitialValueReflection(GuiComposer composer, string value)
        {
            try
            {
                var textArea = composer.GetTextArea("caption");
                if (textArea == null) return;

                var t = textArea.GetType();

                // Try common setter methods first.
                var setValue = t.GetMethod("SetValue") ?? t.GetMethod("SetText") ?? t.GetMethod("SetTextValue") ?? t.GetMethod("SetNewText");
                if (setValue != null)
                {
                    var pars = setValue.GetParameters();
                    if (pars.Length == 1 && pars[0].ParameterType == typeof(string))
                    {
                        setValue.Invoke(textArea, new object[] { value ?? string.Empty });
                        return;
                    }
                }

                // Fallback: writable string property.
                foreach (var propName in new[] { "Text", "Value", "TextValue" })
                {
                    var prop = t.GetProperty(propName);
                    if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(textArea, value ?? string.Empty);
                        return;
                    }
                }

                // Final fallback: private/protected field.
                foreach (var fieldName in new[] { "text", "Text", "value", "Value" })
                {
                    var field = t.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        field.SetValue(textArea, value ?? string.Empty);
                        return;
                    }
                }
            }
            catch
            {
                // best-effort
            }
        }
    }
}
