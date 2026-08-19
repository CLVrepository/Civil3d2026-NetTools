using System;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Captures and restores core snap-related system variables in a safe,
    /// deterministic way. Intended usage:
    ///
    /// using (var snap = SnapState.Capture())
    /// {
    ///     snap.Set(osMode: 0, osnapZ: 0, osMode3d: 0);
    ///     // ... do command work ...
    /// } // snaps restored automatically
    /// </summary>
    public sealed class SnapState : IDisposable
    {
        // Captured values
        private readonly int _osMode;
        private readonly int _osnapZ;
        private readonly int _osMode3d;

        // Flags to indicate whether capture succeeded for each var
        private readonly bool _hasOsMode;
        private readonly bool _hasOsnapZ;
        private readonly bool _hasOsMode3d;

        private bool _disposed;

        private SnapState(
            int osMode, bool hasOsMode,
            int osnapZ, bool hasOsnapZ,
            int osMode3d, bool hasOsMode3d)
        {
            _osMode = osMode;
            _hasOsMode = hasOsMode;

            _osnapZ = osnapZ;
            _hasOsnapZ = hasOsnapZ;

            _osMode3d = osMode3d;
            _hasOsMode3d = hasOsMode3d;
        }

        /// <summary>
        /// Capture current snap-related system variable values.
        /// Safe even if some vars aren't defined (flags will be false).
        /// </summary>
        public static SnapState Capture()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            (int value, bool ok) GetIntVar(string name)
            {
                try
                {
                    object? obj = AcadApp.GetSystemVariable(name);
                    if (obj == null)
                        return (0, false);

                    return (Convert.ToInt32(obj), true);
                }
                catch (Exception ex)
                {
                    ed?.WriteMessage(
                        $"\n[SnapState] Warning: Failed to read {name}: {ex.Message}");
                    return (0, false);
                }
            }

            var (osMode, hasOsMode) = GetIntVar("OSMODE");
            var (osnapZ, hasOsnapZ) = GetIntVar("OSNAPZ");
            var (osMode3d, hasOsMode3d) = GetIntVar("3DOSMODE");

            return new SnapState(
                osMode, hasOsMode,
                osnapZ, hasOsnapZ,
                osMode3d, hasOsMode3d);
        }

        /// <summary>
        /// Convenience helper to set any combination of snap variables in one call.
        /// Only non-null arguments are applied.
        /// </summary>
        public void Set(int? osMode = null, int? osnapZ = null, int? osMode3d = null)
        {
            if (osMode.HasValue)
                SetVar("OSMODE", osMode.Value);

            if (osnapZ.HasValue)
                SetVar("OSNAPZ", osnapZ.Value);

            if (osMode3d.HasValue)
                SetVar("3DOSMODE", osMode3d.Value);
        }

        /// <summary>
        /// Set OSMODE only.
        /// </summary>
        public void SetOsMode(int osMode) => Set(osMode: osMode);

        /// <summary>
        /// Set OSNAPZ only.
        /// </summary>
        public void SetOsnapZ(int osnapZ) => Set(osnapZ: osnapZ);

        /// <summary>
        /// Set 3DOSMODE only.
        /// </summary>
        public void Set3dOsMode(int osMode3d) => Set(osMode3d: osMode3d);

        private static void SetVar(string name, int value)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            try
            {
                AcadApp.SetSystemVariable(name, value);
            }
            catch (Exception ex)
            {
                ed?.WriteMessage(
                    $"\n[SnapState] Warning: Failed to set {name} to {value}: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore captured values. Called automatically when used in a 'using' block.
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Restore only the vars that were successfully captured
            if (_hasOsMode)
                SetVar("OSMODE", _osMode);

            if (_hasOsnapZ)
                SetVar("OSNAPZ", _osnapZ);

            if (_hasOsMode3d)
                SetVar("3DOSMODE", _osMode3d);
        }
    }
}