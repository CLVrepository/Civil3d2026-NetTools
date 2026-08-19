using System;
using System.Drawing;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.PdfViewer
{
    public static class PdfViewerCommands
    {
        private static PaletteSet? _paletteSet;
        private static PdfViewerControl? _control;

        [CommandMethod("PDFVIEW")]
        [CommandMethod("MAPREVIEW")]
        public static void ShowViewer()
        {
            if (_paletteSet == null)
            {
                _control = new PdfViewerControl();
                _paletteSet = new PaletteSet("PDF VIEWER")
                {
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
                };
                _paletteSet.Add("MAP REVIEW", _control);
                PalettePositionHelper.ConfigureSize(_paletteSet, new Size(620, 780), new Size(420, 520));
            }

            _control?.RefreshForActiveDocument();
            PalettePositionHelper.ShowNearAutoCadWindow(
                _paletteSet,
                new Size(620, 780),
                new Size(420, 520),
                offsetX: 720,
                offsetY: 120);
        }
    }
}
