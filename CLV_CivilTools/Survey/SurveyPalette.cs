using System;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Survey
{
    public static class SurveyPaletteCommands
    {
        private static PaletteSet? _paletteSet;
        private static SurveyPaletteControl? _paletteControl;

        /// <summary>
        /// Survey mapping palette command.
        /// Name : Q4
        /// </summary>
        [CommandMethod("SURVEY", "Q4", CommandFlags.Modal)]
        [CommandMethod("Q4")]
        public static void ShowSurveyPalette()
        {
            if (_paletteSet == null)
            {
                _paletteControl = new SurveyPaletteControl();
                SurveyPhotoReviewCommands.EnsurePalette();

                _paletteSet = new PaletteSet("SURVEY")
                {
                    DockEnabled =
                        DockSides.Left |
                        DockSides.Right |
                        DockSides.Top |
                        DockSides.Bottom
                };


                _paletteSet.Add("SURVEY – MAPPING", _paletteControl);
                PalettePositionHelper.ConfigureSize(
                    _paletteSet,
                    new Size(340, 700),
                    new Size(300, 540));

            }

            PalettePositionHelper.ShowNearAutoCadWindow(
                _paletteSet,
                new Size(340, 700),
                new Size(300, 540),
                offsetX: 550,
                offsetY: 280);
        }
    }

    public class SurveyPaletteControl : UserControl
    {
        private const int ButtonWidth = 256;
        private const int ButtonHeight = 24;
        private const float PaletteFontSize = 7.0f;

        public SurveyPaletteControl()
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

            var dimensionsPage = new TabPage("LABEL")
            {
                Padding = new Padding(3)
            };
            dimensionsPage.Controls.Add(BuildDimensionsPanel());
            tabs.TabPages.Add(dimensionsPage);

            var mappingPage = new TabPage("MAPPING")
            {
                Padding = new Padding(3)
            };
            mappingPage.Controls.Add(BuildMappingPanel());
            tabs.TabPages.Add(mappingPage);

            var gisPage = new TabPage("GIS")
            {
                Padding = new Padding(3)
            };
            gisPage.Controls.Add(BuildGisPanel());
            tabs.TabPages.Add(gisPage);

            Controls.Add(tabs);
        }

        private Control BuildDimensionsPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("ROADWAY"));
            layout.Controls.Add(CreateCommandButton("STREET NAMES", "SURVEY-LABEL-ROADS"));

            layout.Controls.Add(CreateSectionLabel("DIMENSIONS"));
            layout.Controls.Add(CreateCommandButton("DISTANCE", "SURVEY-DIM-DISTANCE"));
            layout.Controls.Add(CreateCommandButton("OFFSET", "SURVEY-DIM-OFFSET"));
            layout.Controls.Add(CreateCommandButton("ANGLE - DEGREES", "SURVEY-DIM-ANGLE-DEGREES"));
            layout.Controls.Add(CreateCommandButton("ANGLE - SECONDS", "SURVEY-DIM-ANGLE-SECONDS"));
            layout.Controls.Add(CreateCommandButton("RADIUS", "SURVEY-DIM-RADIUS"));

            layout.Controls.Add(CreateSectionLabel("LINES AND CURVES"));
            layout.Controls.Add(CreateCommandButton("2-POINT  ||  BEARING AND DIST", "SURVEY-LC-LABEL-2POINT"));
            layout.Controls.Add(CreateCommandButton("2-POINT  ||  DIST", "SURVEY-LC-LABEL-2POINT-DIST"));
            layout.Controls.Add(CreateCommandButton("BEARING AND DIST", "SURVEY-LC-LABEL-BEARING-DISTANCE"));
            layout.Controls.Add(CreateCommandButton("DISTANCE", "SURVEY-LC-LABEL-DISTANCE"));

            layout.Controls.Add(CreateSectionLabel("AREA"));
            layout.Controls.Add(CreateCommandButton("AREA SF LABEL", "SURVEY-AREA-SF-LABEL"));

            layout.Controls.Add(CreateSectionLabel("LEGEND"));
            layout.Controls.Add(CreateCommandButton("CREATE LEGEND", "SURVEY-CREATE-LEGEND"));
            layout.Controls.Add(CreateCommandButton("UPDATE LEGEND", "SURVEY-UPDATE-LEGEND"));

            layout.Controls.Add(CreateSectionLabel("PLSS SECTIONS"));
            layout.Controls.Add(CreateCommandButton("IMPORT LABELS", "SURVEY-PLSS-IMPORT-LABELS"));

            return layout;
        }

        private Control BuildMappingPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("TRANSFORM"));
            layout.Controls.Add(CreateCommandButton("MAP TRANSFORM", "SURVEY-BESTFIT-MAP"));
            layout.Controls.Add(CreateCommandButton("BOUNDARY TRANSFORM", "SURVEY-TRANSFORM-CONTROL"));
            layout.Controls.Add(CreateCommandButton("C3D TRANSFORM", "ADETRANSFORM"));

            layout.Controls.Add(CreateSectionLabel("TOOLS"));
            layout.Controls.Add(CreateCommandButton("DRAW TIE LINE", "SURVEY-DRAW-TIE-LINE"));
            layout.Controls.Add(CreateCommandButton("PDF VIEWER", "PDFVIEW"));
            layout.Controls.Add(CreateCommandButton("PDF CLIP", "PDFC"));
            layout.Controls.Add(CreateCommandButton("LEGAL DESCRIPTION", "LEGALDESC"));
            layout.Controls.Add(CreateCommandButton("XREF COLOR", "SURVEY-XREF-COLOR"));
            layout.Controls.Add(CreateCommandButton("OFFSET TO TEMP LAYER", "SURVEY-OFFSET-TEMP"));
            layout.Controls.Add(CreateCommandButton("LINEWORK REVIEW", "SURVEY-LINEWORK-REVIEW"));

            layout.Controls.Add(CreateSectionLabel("BOUNDARY"));
            layout.Controls.Add(CreateCommandButton("AUTO CLOSURE", "SURVEY-AUTO-CLOSURE"));
            layout.Controls.Add(CreateCommandButton("CONSTRAINTS", "SURVEY-CLOSURE-CONSTRAINTS"));
            layout.Controls.Add(CreateCommandButton("CLOSURE REPORT", "SURVEY-CLOSURE-REPORT"));
            layout.Controls.Add(CreateCommandButton("CLOSURE MARKERS", "SURVEY-CLOSURE-MARKERS"));
            layout.Controls.Add(CreateCommandButton("GOTO SEGMENT", "SURVEY-CLOSURE-GOTO"));
            layout.Controls.Add(CreateCommandButton("CLEAR REVIEW MARKERS", "SURVEY-CLOSURE-CLEAR-REVIEW"));

            layout.Controls.Add(CreateSectionLabel("SUBDIVISION LINEWORK"));
            layout.Controls.Add(CreateCommandButton("SITE SETTINGS", "CLV_SUBDIV_SITE_SETTINGS"));
            layout.Controls.Add(CreateCommandButton("ROADS", "CLV_SUBDIV_ROADS"));
            layout.Controls.Add(CreateCommandButton("CUL-DE-SAC", "CLV_SUBDIV_CULDESAC"));
            layout.Controls.Add(CreateCommandButton("INTERSECTION", "CLV_SUBDIV_INTERSECTION"));
            layout.Controls.Add(CreateCommandButton("LOT LINES", "CLV_SUBDIV_LOT_LINES"));

            return layout;
        }

        private Control BuildGisPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("TOWNSHIP/RANGE"));
            layout.Controls.Add(CreateCommandButton("SECTION CORNER MARKER", "SURVEY-GIS-SECTION-MARKER"));

            return layout;
        }

        private static FlowLayoutPanel CreateMainFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
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
                Padding = new Padding(0, 8, 0, 2),
            };
        }

        private Button CreateCommandButton(string caption, string command)
        {
            var btn = new Button
            {
                Text = caption,
                Width = ButtonWidth,
                Height = ButtonHeight,
                Margin = new Padding(3, 2, 3, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = command
            };

            btn.Click += (_, _) => SendCommand(command);
            return btn;
        }

        private static void SendCommand(string command)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            doc.SendStringToExecute(command + " ", true, false, false);
        }
    }
}
