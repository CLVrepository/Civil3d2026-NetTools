using System;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.Colors;

using CLV_CivilTools.Shared;

// Alias
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools
{
    public static class PctPaletteCommands
    {
        private static PaletteSet? _paletteSet;
        private static PctPaletteControl? _paletteControl;

        [CommandMethod("PCT", "Q3", CommandFlags.Modal)]
        public static void ShowPctPalette()
        {
            if (_paletteSet == null)
            {
                _paletteControl = new PctPaletteControl();

                _paletteSet = new PaletteSet("CLV-POINT CLOUD TOOLS")
                {
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
                };


                _paletteSet.Add("TOOLS", _paletteControl);
                PalettePositionHelper.ConfigureSize(
                    _paletteSet,
                    new Size(320, 600),
                    new Size(280, 480));

            }

            PalettePositionHelper.ShowNearAutoCadWindow(
                _paletteSet,
                new Size(320, 600),
                new Size(280, 480),
                offsetX: 510,
                offsetY: 240);
        }
    }

    public class PctPaletteControl : UserControl
    {
        private const int ContentWidth = 256;
        private const int RowHeight = 24;
        private const float PaletteFontSize = 7.0f;

        public PctPaletteControl()
        {
            InitializeUi();
        }

        private void InitializeUi()
        {
            Dock = DockStyle.Fill;

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            var roadwayPage = new TabPage("ROADWAY");
            var uflsPage = new TabPage("UFLS");

            roadwayPage.Padding = new Padding(3);
            uflsPage.Padding = new Padding(3);

            roadwayPage.Controls.Add(BuildRoadwayPanel());
            uflsPage.Controls.Add(BuildUflsPanel());

            tabs.TabPages.Add(roadwayPage);
            tabs.TabPages.Add(uflsPage);

            Controls.Add(tabs);
        }

        // ------------------------------------------------------------
        // ROADWAY TAB
        // ------------------------------------------------------------
        private Control BuildRoadwayPanel()
        {
            var layout = CreateMainFlowPanel();

            // POINT CLOUD DISPLAY
            layout.Controls.Add(CreateSectionLabel("POINT CLOUD DISPLAY"));
            layout.Controls.Add(CreateCommandButtonRow("ATTACH", "PCT11", "INTENSITY", "PCT11I")); // restored row
            layout.Controls.Add(CreateTransparencyControlRow()); // under Attach/Intensity
            layout.Controls.Add(CreateCommandButtonRow("ON", "PCT7", "OFF", "PCT8"));

            // SAMPLE LINES
            layout.Controls.Add(CreateSectionLabel("SAMPLE LINES"));
            layout.Controls.Add(CreateCommandButton("CREATE SAMPLE LINES", "PCT1"));

            // CROP SAMPLE LINES
            layout.Controls.Add(CreateSectionLabel("CROP SAMPLE LINES"));
            layout.Controls.Add(CreateCommandButtonRow("CROP SL", "PCT2", "UNCROP SL", "PCT2R"));

            // QUICK SAMPLE LINE
            layout.Controls.Add(CreateSectionLabel("QUICK SAMPLE LINE"));
            layout.Controls.Add(CreateCommandButtonRow("QUICK SL", "PCT4", "UNCROP SL", "PCT4R")); // text updates

            // GENERAL CROP
            layout.Controls.Add(CreateSectionLabel("GENERAL CROP"));
            layout.Controls.Add(CreateCommandButtonRow("CROP", "PCT14", "UNCROP", "PCT16"));

            // CROSS SECTION
            layout.Controls.Add(CreateSectionLabel("CROSS SECTION"));
            layout.Controls.Add(CreateCommandButtonRow("CROSS SECTION", "PCT3", "RESET CS", "PCT3R"));

            // VIEW CONTROL
            layout.Controls.Add(CreateSectionLabel("VIEW CONTROL"));
            layout.Controls.Add(CreateCommandButtonRow("3D ROTATE", "PCT18", "RESET VIEW", "PCT18R")); // rename Rotate label only

            // MOVE POINTS
            layout.Controls.Add(CreateSectionLabel("MOVE POINTS"));
            layout.Controls.Add(CreateCommandButton("TO SAMPLE LINE", "PCT5"));
            layout.Controls.Add(CreateCommandButton("MOVE POINTS TO VERTEX", "PCT9"));

            // MOVE POINTS + ADD VERTICES
            layout.Controls.Add(CreateSectionLabel("MOVE POINTS + ADD VERTICES"));
            layout.Controls.Add(CreateCommandButtonRow("SINGLE/MULTIPLE", "PCT6", "ADJACENT", "PCT17"));

            // POLYLINE TOOLS
            layout.Controls.Add(CreateSectionLabel("POLYLINE TOOLS"));
            layout.Controls.Add(CreateCommandButton("COPY VERTEX FROM PL TO PL", "PCT17V"));
            layout.Controls.Add(CreateCommandButton("ADD VERTEX @ CROSSING", "PCT20"));
            layout.Controls.Add(CreateCommandButton("IDENTIFY VERTICES", "PCT10"));
            layout.Controls.Add(CreateCommandButton("GENERAL MARKER", "PCT19"));

            return layout;
        }

        // ------------------------------------------------------------
        // UFLS TAB
        // (kept in existing structure; POINT CLOUD DISPLAY updated to match Attach+Intensity row)
        // ------------------------------------------------------------
        private Control BuildUflsPanel()
        {
            var layout = CreateMainFlowPanel();

            // POINT CLOUD DISPLAY
            layout.Controls.Add(CreateSectionLabel("POINT CLOUD DISPLAY"));
            layout.Controls.Add(CreateCommandButtonRow("ATTACH", "PCT11", "INTENSITY", "PCT11I"));
            layout.Controls.Add(CreateTransparencyControlRow());
            layout.Controls.Add(CreateCommandButtonRow("ON", "PCT7", "OFF", "PCT8"));

            // PIPE LOCATOR (unchanged)
            layout.Controls.Add(CreateSectionLabel("PIPE LOCATOR"));
            layout.Controls.Add(CreateCommandButton("STEP 1 - CROP POINT CLOUD", "PCT14"));
            layout.Controls.Add(CreateCommandButton("STEP 2 - ROTATE VIEW", "PCT15"));
            layout.Controls.Add(CreateCommandButton("STEP 3 - SET UCS", "PCT13"));
            layout.Controls.Add(CreateCommandButton("STEP 4 - TRACE PIPE", "PCT12"));
            layout.Controls.Add(CreateCommandButton("STEP 5 - RESET VIEW", "PCT16"));

            // 2D LINEWORK
            layout.Controls.Add(CreateSectionLabel("2D LINEWORK"));
            layout.Controls.Add(CreateCommandButton("STRC-INNER WALL", "UFLS7PC"));
            layout.Controls.Add(CreateCommandButton("LOCATE MANHOLE", "UFLS6PC"));
            layout.Controls.Add(CreateCommandButton("3P CIRCLE", "UFLS-3PCIRCLE"));
            layout.Controls.Add(CreateCommandButton("3P RECTANGLE", "UFLS-3PRECT"));

            // VIEW CONTROL (unchanged except label)
            layout.Controls.Add(CreateSectionLabel("VIEW CONTROL"));
            layout.Controls.Add(CreateCommandButtonRow("3D ROTATE", "PCT18", "RESET VIEW", "PCT18R"));

            // QUICK CROP (unchanged)
            layout.Controls.Add(CreateSectionLabel("QUICK CROP"));
            layout.Controls.Add(CreateCommandButtonRow("CROP", "PCT14", "UNCROP", "PCT16"));

            // TOOLS (unchanged)
            layout.Controls.Add(CreateSectionLabel("TOOLS"));
            layout.Controls.Add(CreateCommandButton("GENERAL MARKER", "PCT19"));

            return layout;
        }

        // ------------------------------------------------------------
        // UI HELPERS
        // ------------------------------------------------------------

        private static FlowLayoutPanel CreateMainFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown, // avoid AutoCAD FlowDirection ambiguity
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8)
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold), // avoid AutoCAD Font ambiguity
                Padding = new Padding(0, 8, 0, 2),
            };
        }

        private static Button CreateCommandButton(string label, string pctLocalName)
        {
            var btn = new Button
            {
                Text = label,
                Width = ContentWidth,
                Height = RowHeight,
                Margin = new Padding(3, 2, 3, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = pctLocalName
            };

            btn.Click += (s, e) =>
            {
                if (btn.Tag is string localName)
                    RunPct(localName);
            };

            return btn;
        }

        /// <summary>
        /// Two buttons in one row (single "slot" in the FlowLayoutPanel).
        /// </summary>
        private static Control CreateCommandButtonRow(
            string leftLabel, string leftCmd,
            string rightLabel, string rightCmd)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Width = ContentWidth,
                Height = RowHeight,
                Margin = new Padding(3, 2, 3, 2),
                Padding = new Padding(0),
            };

            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var btnLeft = new Button
            {
                Text = leftLabel,
                Dock = DockStyle.Fill,
                Height = RowHeight,
                Margin = new Padding(0, 0, 4, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = leftCmd
            };

            var btnRight = new Button
            {
                Text = rightLabel,
                Dock = DockStyle.Fill,
                Height = RowHeight,
                Margin = new Padding(4, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = rightCmd
            };

            btnLeft.Click += (s, e) =>
            {
                if (btnLeft.Tag is string localName)
                    RunPct(localName);
            };

            btnRight.Click += (s, e) =>
            {
                if (btnRight.Tag is string localName)
                    RunPct(localName);
            };

            row.Controls.Add(btnLeft, 0, 0);
            row.Controls.Add(btnRight, 1, 0);

            return row;
        }

        /// <summary>
        /// Transparency control row (NumericUpDown + Apply).
        /// Applies to currently selected object(s) using implied selection.
        /// </summary>
        private static Control CreateTransparencyControlRow()
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Width = ContentWidth,
                Height = RowHeight,
                Margin = new Padding(3, 2, 3, 2),
                Padding = new Padding(0),
            };

            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));   // label
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));   // numeric
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // button
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var lbl = new Label
            {
                Text = "TRANSPARENCY",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                AutoSize = false
            };

            var nud = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 90,
                Value = 0,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 4, 2),
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };

            var btnApply = new Button
            {
                Text = "APPLY",
                Dock = DockStyle.Fill,
                Height = RowHeight,
                Margin = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };

            btnApply.Click += (s, e) =>
            {
                SetSelectedEntitiesTransparencyPercent((int)nud.Value);
            };

            row.Controls.Add(lbl, 0, 0);
            row.Controls.Add(nud, 1, 0);
            row.Controls.Add(btnApply, 2, 0);

            return row;
        }

        private static void RunPct(string localName)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return;

                doc.SendStringToExecute(
                    localName + " ",
                    activate: true,
                    wrapUpInactiveDoc: false,
                    echoCommand: false);
            }
            catch (System.Exception ex)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nError queuing {localName}: {ex.Message}");
            }
        }

        private static void SetSelectedEntitiesTransparencyPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 90) percent = 90;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            // 0 = opaque, higher = more transparent
            byte alpha = (byte)Math.Max(0, 255 - (percent * 255 / 100));

            var psr = ed.SelectImplied();
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nSelect point cloud object(s) first, then click Apply.");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in psr.Value.GetObjectIds())
                {
                    if (!id.IsValid) continue;

                    if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent)
                    {
                        ent.Transparency = new Transparency(alpha);
                    }
                }

                tr.Commit();
            }

            ed.Regen();
        }
    }
}