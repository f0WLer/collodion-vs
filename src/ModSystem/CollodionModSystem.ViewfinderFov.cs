using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace Collodion
{
    // Viewfinder runtime zoom implementation.
    //
    // Responsibilities:
    // - Bind to a runtime camera FOV field/property/method via reflection (preferred zoom path)
    //
    // Intentionally does NOT:
    // - Handle input polling / viewfinder state machine (see CollodionModSystem.Viewfinder.cs)
    public partial class CollodionModSystem
    {
        private float SafeGetFov(Func<float> getter, float fallback)
        {
            try
            {
                float v = getter();
                if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
                return v;
            }
            catch
            {
                return fallback;
            }
        }

        private void SafeSetFov(Action<float> setter, float value)
        {
            try { setter(value); }
            catch { }
        }

        private float ClampFov(float proposed, float oldValue)
        {
            // Heuristic: some internal fields might be stored in radians (values around 1.0).
            // If so, clamp in a radians-like range; otherwise assume degrees.
            float basis = oldValue;
            if (basis > 0f && basis < 10f)
            {
                return Math.Max(0.3f, Math.Min(2.5f, proposed));
            }

            return Math.Max(30f, Math.Min(110f, proposed));
        }

        private bool TryBindRuntimeFovAccessors(out Func<float> getter, out Action<float> setter, out string mechanism)
        {
            getter = null!;
            setter = null!;
            mechanism = string.Empty;

            if (ClientApi == null) return false;

            object? render = SafeGet(() => (object?)ClientApi.Render);
            object? capi = ClientApi;
            object? player = SafeGet(() => (object?)ClientApi.World?.Player);
            object? playerEnt = SafeGet(() => (object?)ClientApi.World?.Player?.Entity);

            // 1) Conservative list of names so we don't accidentally poke unrelated floats.
            string[] directNames =
            {
                "CameraFov",
                "CameraFoV",
                "Fov",
                "FoV",
                "FieldOfView",
                "FieldOfViewDeg",
                "FovY",
                "FoVy",
                "FovDegrees",
                "FovDeg"
            };

            foreach (object? target in new[] { render, capi, player, playerEnt })
            {
                if (target == null) continue;
                if (TryBindFloatMember(target, directNames, out getter, out setter, out string member))
                {
                    if (LooksLikeFov(getter()))
                    {
                        mechanism = $"{target.GetType().Name}.{member}";
                        return true;
                    }
                }
            }

            // 2) Common camera holder objects.
            string[] cameraObjectNames =
            {
                "Camera",
                "MainCamera",
                "PlayerCamera",
                "ActiveCamera",
                "GameCamera"
            };

            foreach (object? parent in new[] { render, capi, player, playerEnt })
            {
                if (parent == null) continue;

                foreach (object camObj in GetNamedChildObjects(parent, cameraObjectNames))
                {
                    if (TryBindFloatMember(camObj, directNames, out getter, out setter, out string member))
                    {
                        if (LooksLikeFov(getter()))
                        {
                            mechanism = $"{camObj.GetType().Name}.{member}";
                            return true;
                        }
                    }
                }
            }

            // 3) Heuristic scan: any writable float/double member with name containing fov/fieldofview.
            foreach (object? target in new[] { render, capi, player, playerEnt })
            {
                if (target == null) continue;
                if (TryBindHeuristicFovMember(target, out getter, out setter, out string member))
                {
                    mechanism = $"{target.GetType().Name}.{member}";
                    return true;
                }
            }

            foreach (object? parent in new[] { render, capi, player, playerEnt })
            {
                if (parent == null) continue;
                foreach (object camObj in GetNamedChildObjects(parent, cameraObjectNames))
                {
                    if (TryBindHeuristicFovMember(camObj, out getter, out setter, out string member))
                    {
                        mechanism = $"{camObj.GetType().Name}.{member}";
                        return true;
                    }
                }
            }

            // 4) Method-based setter (last resort). We still attempt to pair it with a getter if possible.
            foreach (object? target in new[] { render, capi, player, playerEnt })
            {
                if (target == null) continue;
                if (TryBindFovSetterMethod(target, out getter, out setter, out string methodName))
                {
                    mechanism = $"{target.GetType().Name}.{methodName}(…)";
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeFov(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;

            // Degrees-like
            if (value >= 10f && value <= 180f) return true;
            // Radians-like
            if (value >= 0.2f && value <= 3.5f) return true;
            return false;
        }

        private static IEnumerable<object> GetNamedChildObjects(object parent, string[] names)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = parent.GetType();

            foreach (string name in names)
            {
                object? val = null;
                try
                {
                    var p = t.GetProperty(name, Flags);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                    {
                        val = p.GetValue(parent);
                    }
                    else
                    {
                        var f = t.GetField(name, Flags);
                        if (f != null) val = f.GetValue(parent);
                    }
                }
                catch
                {
                    val = null;
                }

                if (val != null) yield return val;
            }
        }

        private static bool TryBindHeuristicFovMember(object target, out Func<float> getter, out Action<float> setter, out string memberName)
        {
            getter = null!;
            setter = null!;
            memberName = string.Empty;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = target.GetType();

            bool NameLooksRelevant(string n)
            {
                string ln = n.ToLowerInvariant();
                if (ln.Contains("fieldofview") || ln == "fov" || ln.Contains("fov"))
                {
                    // Avoid obvious non-camera fov knobs when possible.
                    if (ln.Contains("hands")) return false;
                    return true;
                }
                return false;
            }

            // Properties first
            foreach (var p in t.GetProperties(Flags))
            {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!(p.PropertyType == typeof(float) || p.PropertyType == typeof(double))) continue;
                if (!NameLooksRelevant(p.Name)) continue;

                Func<float> g = () =>
                {
                    object? v = p.GetValue(target);
                    if (v == null) return 0f;
                    return p.PropertyType == typeof(double) ? (float)(double)v : (float)v;
                };

                float cur;
                try { cur = g(); }
                catch { continue; }
                if (!LooksLikeFov(cur)) continue;

                // Setter: prefer the property setter, but if the property is read-only we try
                // an auto-property backing field ("<PropName>k__BackingField").
                FieldInfo? backingField = null;
                if (!p.CanWrite)
                {
                    try
                    {
                        backingField = t.GetField($"<{p.Name}>k__BackingField", Flags);
                    }
                    catch
                    {
                        backingField = null;
                    }

                    if (backingField == null) continue;
                    if (!(backingField.FieldType == typeof(float) || backingField.FieldType == typeof(double))) continue;
                }

                getter = g;
                setter = v =>
                {
                    if (p.CanWrite)
                    {
                        if (p.PropertyType == typeof(double)) p.SetValue(target, (double)v);
                        else p.SetValue(target, v);
                        return;
                    }

                    if (backingField != null)
                    {
                        if (backingField.FieldType == typeof(double)) backingField.SetValue(target, (double)v);
                        else backingField.SetValue(target, v);
                    }
                };
                memberName = p.CanWrite ? p.Name : $"{p.Name}(<backingfield>)";
                return true;
            }

            // Then fields
            foreach (var f in t.GetFields(Flags))
            {
                if (!(f.FieldType == typeof(float) || f.FieldType == typeof(double))) continue;
                if (!NameLooksRelevant(f.Name)) continue;

                Func<float> g = () =>
                {
                    object? v = f.GetValue(target);
                    if (v == null) return 0f;
                    return f.FieldType == typeof(double) ? (float)(double)v : (float)v;
                };

                float cur;
                try { cur = g(); }
                catch { continue; }
                if (!LooksLikeFov(cur)) continue;

                getter = g;
                setter = v =>
                {
                    if (f.FieldType == typeof(double)) f.SetValue(target, (double)v);
                    else f.SetValue(target, v);
                };
                memberName = f.Name;
                return true;
            }

            return false;
        }

        private static bool TryBindFovSetterMethod(object target, out Func<float> getter, out Action<float> setter, out string methodName)
        {
            getter = null!;
            setter = null!;
            methodName = string.Empty;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = target.GetType();

            // Find a plausible setter method.
            foreach (var m in t.GetMethods(Flags))
            {
                var pars = m.GetParameters();
                if (pars.Length != 1) continue;
                if (!(pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(double))) continue;

                string ln = m.Name.ToLowerInvariant();
                if (!ln.Contains("fov") && !ln.Contains("fieldofview")) continue;
                if (!ln.Contains("set") && !ln.StartsWith("set")) continue;

                // Pair with a getter if there is a matching member.
                if (TryBindHeuristicFovMember(target, out getter, out _, out _))
                {
                    // getter already bound.
                }
                else
                {
                    // No getter found; fall back to a stable-ish default.
                    getter = () => 70f;
                }

                setter = v =>
                {
                    object arg = pars[0].ParameterType == typeof(double) ? (object)(double)v : v;
                    m.Invoke(target, new object[] { arg });
                };

                methodName = m.Name;
                return true;
            }

            return false;
        }

        private static T? SafeGet<T>(Func<T> get)
        {
            try { return get(); }
            catch { return default; }
        }

        private static bool TryBindFloatMember(object target, string[] names, out Func<float> getter, out Action<float> setter, out string memberName)
        {
            getter = null!;
            setter = null!;
            memberName = string.Empty;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = target.GetType();

            foreach (string name in names)
            {
                var p = t.GetProperty(name, Flags);
                if (p != null && p.CanRead && p.CanWrite)
                {
                    if (p.PropertyType == typeof(float))
                    {
                        getter = () => (float)(p.GetValue(target) ?? 0f);
                        setter = v => p.SetValue(target, v);
                        memberName = name;
                        return true;
                    }
                    if (p.PropertyType == typeof(double))
                    {
                        getter = () => (float)((double)(p.GetValue(target) ?? 0d));
                        setter = v => p.SetValue(target, (double)v);
                        memberName = name;
                        return true;
                    }
                }

                var f = t.GetField(name, Flags);
                if (f != null)
                {
                    if (f.FieldType == typeof(float))
                    {
                        getter = () => (float)(f.GetValue(target) ?? 0f);
                        setter = v => f.SetValue(target, v);
                        memberName = name;
                        return true;
                    }
                    if (f.FieldType == typeof(double))
                    {
                        getter = () => (float)((double)(f.GetValue(target) ?? 0d));
                        setter = v => f.SetValue(target, (double)v);
                        memberName = name;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
