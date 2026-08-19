using System;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

using CLV_CivilTools.Clv;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Gis
{
    public static class GisCreateDataPaletteCommands
    {
        private static PaletteSet? _paletteSet;
        private static GisCreateDataPaletteControl? _paletteControl;

        [CommandMethod("CLV-GIS-CREATE-DATA", CommandFlags.Modal)]
        public static void ShowCreateDataPaletteCommand()
        {
            ShowCreateDataPalette();
        }

        public static void ShowCreateDataPalette()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (!SurveyDatabaseAccessCommands.CanOpenCreateData(doc?.Editor, out _))
            {
                doc?.Editor.WriteMessage("\nCREATE DATA access denied.");
                return;
            }

            if (_paletteSet == null)
            {
                _paletteControl = new GisCreateDataPaletteControl();
                _paletteSet = new PaletteSet("CREATE DATA")
                {
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
                };


                _paletteSet.Add("CREATE DATA", _paletteControl);
                PalettePositionHelper.ConfigureSize(
                    _paletteSet,
                    new Size(340, 700),
                    new Size(300, 540));

            }

            PalettePositionHelper.ShowNearAutoCadWindow(
                _paletteSet,
                new Size(340, 700),
                new Size(300, 540),
                offsetX: 830,
                offsetY: 200);
        }

    }

    public sealed class GisCreateDataPaletteControl : UserControl
    {
        private const int ButtonWidth = 256;
        private const int ButtonHeight = 24;
        private const float PaletteFontSize = 7.0f;

        public GisCreateDataPaletteControl()
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

            var dataPage = new TabPage("CREATE DATA")
            {
                Padding = new Padding(3)
            };

            dataPage.Controls.Add(BuildCreateDataPanel());
            tabs.TabPages.Add(dataPage);
            Controls.Add(tabs);
        }

        private Control BuildCreateDataPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("STORM DRAIN"));
            layout.Controls.Add(CreateCommandButton("GIS PREP - ALL", "CLV-GIS-STORM-GIS"));
            layout.Controls.Add(CreateCommandButton("JUNCTIONS AND INLETS - ALL", "CLV-GIS-STRM-AUTO"));
            layout.Controls.Add(CreateCommandButton("DROP INLET - SINGLE", "CLV-GIS-DI-EXPLODE"));
            layout.Controls.Add(CreateCommandButton("JUNCTION STRUCTURE - SINGLE", "CLV-GIS-JS-FROM-POINT"));
            layout.Controls.Add(CreateCommandButton("PIPE", "CLV-GIS-PIPE-OFFSET-OD"));

            layout.Controls.Add(CreateSectionLabel("SEWER"));
            layout.Controls.Add(CreateCommandButton("GIS PREP - ALL", "CLV-GIS-SSWR-GIS"));
            layout.Controls.Add(CreateCommandButton("MANHOLE", "CLV-GIS-SSWR-MH"));
            layout.Controls.Add(CreateCommandButton("PIPE", "CLV-GIS-SSWR-PIPE"));

            layout.Controls.Add(CreateSectionLabel("OBJECT DATA"));
            layout.Controls.Add(CreateCommandButton("XFER OBJECT DATA", "CLV-GIS-OD-XFER"));

            layout.Controls.Add(CreateSectionLabel("CLEANUP"));
            layout.Controls.Add(CreateCommandButton("ERASE POINTS", "CLV-GIS-ERASE-POINTS"));

            layout.Controls.Add(CreateSectionLabel("EXPORT"));
            layout.Controls.Add(CreateCommandButton("FINALIZE STRUCTURES", "CLV-GIS-FINALIZE-STRC"));
            layout.Controls.Add(CreateCommandButton("FINALIZE PIPES", "CLV-GIS-FINALIZE-PIPES"));

            layout.Controls.Add(CreateSectionLabel("TOOLS"));
            layout.Controls.Add(CreateCommandButton("TRIM INSIDE", "CLV-GIS-TRIM-INSIDE"));
            layout.Controls.Add(CreateCommandButton("COMPARE", "CLV-GIS-COMPARE"));
            layout.Controls.Add(CreateCommandButton("REMOVE COMPARE", "CLV-GIS-REMOVE-COMPARE"));
            layout.Controls.Add(CreateCommandButton("VIEW IMPORT", "CLV-VIEW-IMPORT"));

            return layout;
        }

        private static FlowLayoutPanel CreateMainFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
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
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 8, 0, 2)
            };
        }

        private static Button CreateCommandButton(string label, string commandName)
        {
            var button = new Button
            {
                Text = label,
                Width = ButtonWidth,
                Height = ButtonHeight,
                Margin = new Padding(3, 2, 3, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                AutoSize = false,
                Tag = commandName
            };

            button.Click += OnCommandButtonClick;
            return button;
        }

        private static void OnCommandButtonClick(object? sender, EventArgs e)
        {
            if (sender is not Button button || button.Tag is not string commandName || string.IsNullOrWhiteSpace(commandName))
                return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            doc.SendStringToExecute(commandName + " ", activate: true, wrapUpInactiveDoc: false, echoCommand: false);
        }
    }
}
