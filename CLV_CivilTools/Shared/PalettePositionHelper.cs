using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Autodesk.AutoCAD.Windows;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Shared first-open placement helper for CLV WinForms PaletteSet tools.
    /// Places floating palettes relative to the current AutoCAD main window instead
    /// of relying on persisted absolute screen coordinates from another monitor.
    /// </summary>
    internal static class PalettePositionHelper
    {
        private const int DefaultOffsetX = 430;
        private const int DefaultOffsetY = 160;
        private const int ScreenPadding = 12;

        public static void ConfigureSize(
            PaletteSet paletteSet,
            Size size,
            Size minimumSize)
        {
            if (paletteSet == null)
                return;

            paletteSet.Size = size;
            paletteSet.MinimumSize = minimumSize;
        }

        private static readonly HashSet<int> PositionedThisSession = new HashSet<int>();

        public static void ShowNearAutoCadWindow(
            PaletteSet paletteSet,
            Size size,
            Size minimumSize,
            int offsetX = DefaultOffsetX,
            int offsetY = DefaultOffsetY)
        {
            if (paletteSet == null)
                return;

            ConfigureSize(paletteSet, size, minimumSize);

            int sessionKey = RuntimeHelpers.GetHashCode(paletteSet);
            bool shouldApplyStartupPosition = !PositionedThisSession.Contains(sessionKey);

            // AutoCAD/PaletteSet can restore its persisted floating location when
            // Visible is set. On the first show for this AutoCAD session, show the
            // palette first, then apply the CAD-window-relative startup position so
            // Autodesk's saved coordinates from another monitor do not win.
            //
            // After the first show, do not reposition. This lets the user move the
            // palette once during the current Civil 3D session and have subsequent
            // command calls reopen it at that user-selected session location.
            paletteSet.Visible = true;

            if (shouldApplyStartupPosition)
            {
                ApplyCurrentPosition(paletteSet, size, offsetX, offsetY);
                PositionedThisSession.Add(sessionKey);
            }
        }

        public static void ApplyCurrentPosition(
            PaletteSet paletteSet,
            Size size,
            int offsetX = DefaultOffsetX,
            int offsetY = DefaultOffsetY)
        {
            if (paletteSet == null)
                return;

            try
            {
                IntPtr acadHandle = GetAutoCadMainWindowHandle();
                Rectangle acadBounds = GetWindowBounds(acadHandle);
                Rectangle workingArea = Screen.FromHandle(acadHandle).WorkingArea;

                int x = acadBounds.Left + offsetX;
                int y = acadBounds.Top + offsetY;

                x = Clamp(x, workingArea.Left + ScreenPadding, workingArea.Right - size.Width - ScreenPadding);
                y = Clamp(y, workingArea.Top + ScreenPadding, workingArea.Bottom - size.Height - ScreenPadding);

                paletteSet.Location = new Point(x, y);
            }
            catch (System.Exception)
            {
                // Palette placement should never block tool startup. If AutoCAD/Windows
                // does not provide a valid host window, keep Autodesk's default placement.
            }
        }

        private static IntPtr GetAutoCadMainWindowHandle()
        {
            try
            {
                object? mainWindow = AcadApp.MainWindow;
                if (mainWindow != null)
                {
                    IntPtr reflectedHandle = ReadHandleProperty(mainWindow, "Handle")
                        ?? ReadHandleProperty(mainWindow, "HWND")
                        ?? IntPtr.Zero;

                    if (reflectedHandle != IntPtr.Zero)
                        return reflectedHandle;
                }
            }
            catch (System.Exception)
            {
                // Fall through to process-window fallback.
            }

            return Process.GetCurrentProcess().MainWindowHandle;
        }

        private static IntPtr? ReadHandleProperty(object source, string propertyName)
        {
            PropertyInfo? property = source.GetType().GetProperty(propertyName);
            object? value = property?.GetValue(source);

            return value switch
            {
                IntPtr intPtr => intPtr,
                int intValue => new IntPtr(intValue),
                long longValue => new IntPtr(longValue),
                _ => null
            };
        }

        private static Rectangle GetWindowBounds(IntPtr handle)
        {
            if (handle != IntPtr.Zero && GetWindowRect(handle, out RECT rect))
            {
                int width = Math.Max(1, rect.Right - rect.Left);
                int height = Math.Max(1, rect.Bottom - rect.Top);
                return new Rectangle(rect.Left, rect.Top, width, height);
            }

            return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min)
                return min;

            if (value < min)
                return min;

            return value > max ? max : value;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
