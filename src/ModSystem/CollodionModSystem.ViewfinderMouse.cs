using System;
using System.Reflection;

namespace Collodion
{
    // Viewfinder input polling.
    //
    // Responsibilities:
    // - Read mouse button state using reflection across VS versions.
    //
    // Intentionally does NOT:
    // - Contain the viewfinder state machine (see CollodionModSystem.Viewfinder.cs)
    public partial class CollodionModSystem
    {
        private bool GetLeftMouseDown()
        {
            if (ClientApi == null) return false;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            static bool ValueMeansDown(object? v)
            {
                if (v == null) return false;
                if (v is bool b) return b;

                Type vt = v.GetType();
                if (vt.IsEnum)
                {
                    string name = v.ToString() ?? string.Empty;
                    if (name.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("pressed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("held", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("up", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    try { return Convert.ToInt64(v) != 0; } catch { return false; }
                }

                if (v is sbyte || v is byte || v is short || v is ushort || v is int || v is uint || v is long || v is ulong)
                {
                    try { return Convert.ToInt64(v) != 0; } catch { return false; }
                }

                return false;
            }

            bool TryReadLeft(object? mouseState)
            {
                if (mouseState == null) return false;

                var t = mouseState.GetType();

                string[] exactNames = { "Left", "left", "ButtonLeft", "LMB", "MouseLeft" };
                foreach (string name in exactNames)
                {
                    var prop = t.GetProperty(name, Flags);
                    if (prop != null)
                    {
                        try
                        {
                            object? v = prop.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }

                    var field = t.GetField(name, Flags);
                    if (field != null)
                    {
                        try
                        {
                            object? v = field.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }

                try
                {
                    foreach (var prop in t.GetProperties(Flags))
                    {
                        if (prop.Name.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            object? v = prop.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }
                catch { }

                try
                {
                    foreach (var field in t.GetFields(Flags))
                    {
                        if (field.Name.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            object? v = field.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }
                catch { }

                // Indexer: Item[EnumMouseButton]
                try
                {
                    var idx = t.GetProperty("Item", Flags);
                    if (idx != null)
                    {
                        var pars = idx.GetIndexParameters();
                        if (pars.Length == 1 && pars[0].ParameterType.IsEnum)
                        {
                            object? enumVal = null;
                            try { enumVal = Enum.Parse(pars[0].ParameterType, "Left", true); } catch { }
                            if (enumVal != null)
                            {
                                object? v = idx.GetValue(mouseState, new object[] { enumVal });
                                if (ValueMeansDown(v)) return true;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var method in t.GetMethods(Flags))
                    {
                        if (method.ReturnType != typeof(bool)) continue;
                        string ln = method.Name.ToLowerInvariant();
                        if (!(ln.Contains("down") || ln.Contains("pressed") || ln.Contains("held"))) continue;

                        var pars = method.GetParameters();
                        if (pars.Length != 1) continue;
                        var parType = pars[0].ParameterType;
                        if (!parType.IsEnum) continue;

                        object? enumVal = null;
                        try { enumVal = Enum.Parse(parType, "Left", true); }
                        catch { }
                        if (enumVal == null) continue;

                        return (bool)(method.Invoke(mouseState, new object[] { enumVal }) ?? false);
                    }
                }
                catch { }

                return false;
            }

            try
            {
                if (TryReadLeft(ClientApi.Input.InWorldMouseButton)) return true;
            }
            catch { }

            try
            {
                if (TryReadLeft(ClientApi.Input.MouseButton)) return true;
            }
            catch { }

            return false;
        }

        private bool GetRightMouseDown()
        {
            if (ClientApi == null) return false;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            static bool ValueMeansDown(object? v)
            {
                if (v == null) return false;
                if (v is bool b) return b;

                Type vt = v.GetType();
                if (vt.IsEnum)
                {
                    string name = v.ToString() ?? string.Empty;
                    if (name.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("pressed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("held", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (name.IndexOf("up", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                    try { return Convert.ToInt64(v) != 0; } catch { return false; }
                }

                if (v is sbyte || v is byte || v is short || v is ushort || v is int || v is uint || v is long || v is ulong)
                {
                    try { return Convert.ToInt64(v) != 0; } catch { return false; }
                }

                return false;
            }

            // We intentionally use reflection here because the exact mouse-state type differs between versions.
            // Some versions expose fields instead of properties, so we check both.
            bool TryReadRight(object? mouseState)
            {
                if (mouseState == null) return false;

                var t = mouseState.GetType();

                // Exact-name lookup first (fast + deterministic).
                string[] exactNames = { "Right", "right", "ButtonRight", "RMB", "MouseRight" };

                foreach (string name in exactNames)
                {
                    var prop = t.GetProperty(name, Flags);
                    if (prop != null)
                    {
                        try
                        {
                            object? v = prop.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }

                    var field = t.GetField(name, Flags);
                    if (field != null)
                    {
                        try
                        {
                            object? v = field.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }

                // Heuristic: any public bool member containing "right".
                try
                {
                    foreach (var prop in t.GetProperties(Flags))
                    {
                        if (prop.Name.IndexOf("right", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            object? v = prop.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }
                catch { }

                try
                {
                    foreach (var field in t.GetFields(Flags))
                    {
                        if (field.Name.IndexOf("right", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            object? v = field.GetValue(mouseState);
                            if (ValueMeansDown(v)) return true;
                        }
                        catch { }
                    }
                }
                catch { }

                // Indexer: Item[EnumMouseButton]
                try
                {
                    var idx = t.GetProperty("Item", Flags);
                    if (idx != null)
                    {
                        var pars = idx.GetIndexParameters();
                        if (pars.Length == 1 && pars[0].ParameterType.IsEnum)
                        {
                            object? enumVal = null;
                            try { enumVal = Enum.Parse(pars[0].ParameterType, "Right", true); } catch { }
                            if (enumVal != null)
                            {
                                object? v = idx.GetValue(mouseState, new object[] { enumVal });
                                if (ValueMeansDown(v)) return true;
                            }
                        }
                    }
                }
                catch { }

                // Heuristic: look for an IsDown(enum Right) style method.
                try
                {
                    foreach (var method in t.GetMethods(Flags))
                    {
                        if (method.ReturnType != typeof(bool)) continue;
                        string ln = method.Name.ToLowerInvariant();
                        if (!(ln.Contains("down") || ln.Contains("pressed") || ln.Contains("held"))) continue;

                        var pars = method.GetParameters();
                        if (pars.Length != 1) continue;
                        var parType = pars[0].ParameterType;
                        if (!parType.IsEnum) continue;

                        object? enumVal = null;
                        try { enumVal = Enum.Parse(parType, "Right", true); }
                        catch { }
                        if (enumVal == null) continue;

                        return (bool)(method.Invoke(mouseState, new object[] { enumVal }) ?? false);
                    }
                }
                catch { }

                return false;
            }

            try
            {
                if (TryReadRight(ClientApi.Input.InWorldMouseButton)) return true;
            }
            catch { }

            try
            {
                if (TryReadRight(ClientApi.Input.MouseButton)) return true;
            }
            catch { }

            return false;
        }
    }
}
