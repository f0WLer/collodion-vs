using System;
using System.Collections.Generic;
using System.Reflection;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private string? hudHideMechanism;
        private readonly List<HudGuiToggle> hudGuiToggles = new List<HudGuiToggle>();

        private class HudGuiToggle
        {
            public readonly object Dialog;
            public readonly string MemberName;
            public readonly bool OldValue;
            public readonly Action<bool> Setter;

            public HudGuiToggle(object dialog, string memberName, bool oldValue, Action<bool> setter)
            {
                Dialog = dialog;
                MemberName = memberName;
                OldValue = oldValue;
                Setter = setter;
            }
        }

        private void ApplyHudHidden(bool hidden)
        {
            if (ClientApi == null) return;

            // Try Render API first.
            try
            {
                object render = ClientApi.Render;
                if (TrySetHudHiddenViaReflection(render, hidden, out string mechanism))
                {
                    hudHideMechanism = mechanism;
                    return;
                }
            }
            catch
            {
                // ignore and try GUI
            }

            // Try GUI API as fallback.
            try
            {
                object gui = ClientApi.Gui;
                if (TrySetHudHiddenViaReflection(gui, hidden, out string mechanism))
                {
                    hudHideMechanism = mechanism;
                    return;
                }
            }
            catch
            {
                // ignore
            }

            // Final fallback: enumerate loaded GUIs and toggle HUD-like dialogs.
            if (TrySetHudHiddenViaLoadedGuis(hidden, out string viaLoadedGuis))
            {
                hudHideMechanism = viaLoadedGuis;
                return;
            }
        }

        private bool TrySetHudHiddenViaLoadedGuis(bool hidden, out string mechanism)
        {
            mechanism = string.Empty;
            if (ClientApi == null) return false;

            // Build the toggle list once when we first try to hide.
            if (hidden && hudGuiToggles.Count == 0)
            {
                foreach (object dlg in GetLoadedGuisSafe())
                {
                    if (dlg == null) continue;

                    // Only target HUD-like dialogs. (We avoid hiding inventory/menus.)
                    if (!IsHudLikeDialog(dlg)) continue;

                    // Find a writable bool member we can toggle.
                    if (TryCreateBoolToggle(dlg, out HudGuiToggle toggle))
                    {
                        hudGuiToggles.Add(toggle);
                    }
                }
            }

            if (hudGuiToggles.Count == 0)
            {
                return false;
            }

            if (hidden)
            {
                foreach (var toggle in hudGuiToggles)
                {
                    try { toggle.Setter(false); }
                    catch { /* intentional: best-effort non-critical path */ }
                }
                mechanism = $"LoadedGuis toggles={hudGuiToggles.Count} -> false";
                return true;
            }

            // Restore
            foreach (var toggle in hudGuiToggles)
            {
                try { toggle.Setter(toggle.OldValue); }
                catch { /* intentional: best-effort non-critical path */ }
            }
            hudGuiToggles.Clear();
            mechanism = "LoadedGuis restore";
            return true;
        }

        private static bool IsHudLikeDialogTypeName(string typeName)
        {
            string n = typeName.ToLowerInvariant();

            // Positive signals
            bool mentionsHud = n.Contains("hud");
            bool mentionsHotbar = n.Contains("hotbar");
            bool mentionsVitals = n.Contains("vitals") || n.Contains("health") || n.Contains("hunger") || n.Contains("satiety") || n.Contains("stamina");
            bool mentionsStatus = n.Contains("status") || n.Contains("statbar") || n.Contains("statusbar") || n.Contains("bar") || n.Contains("stat");

            if (!(mentionsHud || mentionsHotbar || mentionsVitals || mentionsStatus)) return false;

            // Negative signals (avoid hiding actual menus)
            if (n.Contains("inventory") || n.Contains("craft") || (n.Contains("dialog") && (n.Contains("menu") || n.Contains("settings") || n.Contains("worldmap") || n.Contains("char"))))
            {
                return false;
            }

            // Don't hide chat-related dialogs.
            if (n.Contains("chat")) return false;

            return true;
        }

        private static bool IsHudLikeDialog(object dlg)
        {
            if (dlg == null) return false;

            Type t;
            try { t = dlg.GetType(); }
            catch { return false; }

            // Strong signal: type hierarchy includes HudElement.
            try
            {
                Type? cur = t;
                while (cur != null)
                {
                    string cn = (cur.FullName ?? cur.Name) ?? string.Empty;
                    if (cn.IndexOf("HudElement", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    cur = cur.BaseType;
                }
            }
            catch
            {
                // ignore
            }

            string tn = (t.FullName ?? t.Name) ?? string.Empty;
            return IsHudLikeDialogTypeName(tn);
        }

        private static bool TryCreateBoolToggle(object dialog, out HudGuiToggle toggle)
        {
            toggle = null!;

            var t = dialog.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // Prefer specific member names (most likely to mean visible/render state).
            string[] preferred = { "IsVisible", "Visible", "IsOpened", "Opened", "Render", "ShouldRender", "DrawHud", "Draw" };

            foreach (string name in preferred)
            {
                var prop = t.GetProperty(name, Flags);
                if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
                {
                    bool oldVal = false;
                    try { oldVal = (bool)(prop.GetValue(dialog) ?? false); } catch { /* intentional: best-effort non-critical path */ }
                    Action<bool> setter = v => prop.SetValue(dialog, v);
                    toggle = new HudGuiToggle(dialog, $"{t.Name}.{name}", oldVal, setter);
                    return true;
                }

                var field = t.GetField(name, Flags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    bool oldVal = false;
                    try { oldVal = (bool)(field.GetValue(dialog) ?? false); } catch { /* intentional: best-effort non-critical path */ }
                    Action<bool> setter = v => field.SetValue(dialog, v);
                    toggle = new HudGuiToggle(dialog, $"{t.Name}.{name}", oldVal, setter);
                    return true;
                }
            }

            // Heuristic: any writable bool property containing 'vis', 'open', or 'render'.
            foreach (var prop in t.GetProperties(Flags))
            {
                if (prop.PropertyType != typeof(bool) || !prop.CanWrite) continue;
                string pn = prop.Name.ToLowerInvariant();
                if (!pn.Contains("vis") && !pn.Contains("open") && !pn.Contains("render") && !pn.Contains("draw")) continue;

                bool oldVal = false;
                try { oldVal = (bool)(prop.GetValue(dialog) ?? false); } catch { /* intentional: best-effort non-critical path */ }
                Action<bool> setter = v => prop.SetValue(dialog, v);
                toggle = new HudGuiToggle(dialog, $"{t.Name}.{prop.Name}", oldVal, setter);
                return true;
            }

            // Heuristic: any bool field containing 'vis', 'open', or 'render'.
            foreach (var field in t.GetFields(Flags))
            {
                if (field.FieldType != typeof(bool)) continue;
                string fn = field.Name.ToLowerInvariant();
                if (!fn.Contains("vis") && !fn.Contains("open") && !fn.Contains("render") && !fn.Contains("draw")) continue;

                bool oldVal = false;
                try { oldVal = (bool)(field.GetValue(dialog) ?? false); } catch { /* intentional: best-effort non-critical path */ }
                Action<bool> setter = v => field.SetValue(dialog, v);
                toggle = new HudGuiToggle(dialog, $"{t.Name}.{field.Name}", oldVal, setter);
                return true;
            }

            // Method fallback: TryOpen/TryClose/Open/Close (0 args). Some HUD elements are dialogs with open/close lifecycle.
            try
            {
                var close = t.GetMethod("TryClose", Flags) ?? t.GetMethod("Close", Flags);
                var open = t.GetMethod("TryOpen", Flags) ?? t.GetMethod("Open", Flags);

                if (close != null && close.GetParameters().Length == 0 && open != null && open.GetParameters().Length == 0)
                {
                    Action<bool> setter = v =>
                    {
                        if (v) open.Invoke(dialog, Array.Empty<object>());
                        else close.Invoke(dialog, Array.Empty<object>());
                    };

                    // We don't know current open state without a property; assume open.
                    toggle = new HudGuiToggle(dialog, $"{t.Name}.{open.Name}/{close.Name}", true, setter);
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private IEnumerable<object> GetLoadedGuisSafe()
        {
            if (ClientApi == null) yield break;

            object gui = ClientApi.Gui;
            if (gui == null) yield break;

            List<object> items = new List<object>();

            try
            {
                var t = gui.GetType();
                var getter = t.GetMethod("get_LoadedGuis");
                if (getter == null) yield break;

                object? result = getter.Invoke(gui, Array.Empty<object>());
                if (result is System.Collections.IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item != null) items.Add(item);
                    }
                }
            }
            catch
            {
                yield break;
            }

            foreach (object item in items)
            {
                yield return item;
            }
        }

        private static bool TrySetHudHiddenViaReflection(object target, bool hidden, out string mechanism)
        {
            mechanism = string.Empty;
            if (target == null) return false;

            var t = target.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) Common bool properties.
            // Try several likely names; we prefer explicit Set/Hide calls but these are cheap.
            string[] propNames = { "HudHidden", "HUDHidden", "HideHud", "HideHUD", "ShowHud", "ShowHUD" };
            foreach (string name in propNames)
            {
                var p = t.GetProperty(name, Flags);
                if (p == null || p.PropertyType != typeof(bool) || !p.CanWrite) continue;

                // For ShowHud-style properties we invert.
                bool valueToSet = name.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0 ? !hidden : hidden;
                p.SetValue(target, valueToSet);
                mechanism = $"{t.Name}.{name}={(valueToSet ? "true" : "false")}";
                return true;
            }

            // 2) Common methods.
            // We try: HideHud/ShowHud, SetHudVisible(bool), SetHudHidden(bool), SetShowHud(bool), ToggleHud().
            foreach (var m in t.GetMethods(Flags))
            {
                string mn = m.Name;
                if (mn.IndexOf("hud", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var pars = m.GetParameters();

                // Hide/Show methods with no args.
                if ((mn.Equals("HideHud", StringComparison.OrdinalIgnoreCase) || mn.Equals("HideHUD", StringComparison.OrdinalIgnoreCase)) && pars.Length == 0)
                {
                    if (!hidden) continue;
                    m.Invoke(target, Array.Empty<object>());
                    mechanism = $"{t.Name}.{mn}()";
                    return true;
                }

                if ((mn.Equals("ShowHud", StringComparison.OrdinalIgnoreCase) || mn.Equals("ShowHUD", StringComparison.OrdinalIgnoreCase)) && pars.Length == 0)
                {
                    if (hidden) continue;
                    m.Invoke(target, Array.Empty<object>());
                    mechanism = $"{t.Name}.{mn}()";
                    return true;
                }

                // Methods with a single bool.
                if (pars.Length == 1 && pars[0].ParameterType == typeof(bool))
                {
                    if (!mn.Equals("SetHudVisible", StringComparison.OrdinalIgnoreCase) &&
                        !mn.Equals("SetHudHidden", StringComparison.OrdinalIgnoreCase) &&
                        !mn.Equals("SetShowHud", StringComparison.OrdinalIgnoreCase) &&
                        !mn.Equals("SetHUDVisible", StringComparison.OrdinalIgnoreCase) &&
                        !mn.Equals("SetHUDHidden", StringComparison.OrdinalIgnoreCase) &&
                        !mn.Equals("SetShowHUD", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool arg = mn.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0 ? !hidden : hidden;
                    m.Invoke(target, new object[] { arg });
                    mechanism = $"{t.Name}.{mn}({arg.ToString().ToLowerInvariant()})";
                    return true;
                }
            }

            return false;
        }

    }
}

