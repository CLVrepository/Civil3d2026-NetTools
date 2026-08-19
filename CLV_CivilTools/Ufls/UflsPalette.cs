using System;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

// Alias
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

using CLV_CivilTools.Ufls;   // <-- for UflsDropInlet.RunDropInlet()
using CLV_CivilTools.Shared;

namespace CLV_CivilTools
{
    // ============================================================
    // PALETTE HOST / COMMAND
    // ============================================================
    public static class UflsPaletteCommands
    {
        private static PaletteSet? _paletteSet;
        private static UflsPaletteControl? _paletteControl;

        /// <summary>
        /// UFLS palette command.
        /// Group: UFLS
        /// Name : Q1  (Q11 retained as legacy alias during transition)
        /// </summary>
        [CommandMethod("UFLS", "Q1", CommandFlags.Modal)]
        [CommandMethod("UFLS", "Q11", CommandFlags.Modal)]
        public static void ShowUflsPalette()
        {
            if (_paletteSet == null)
            {
                _paletteControl = new UflsPaletteControl();

                _paletteSet = new PaletteSet("UFLS")
                {
                    // Q1 is the primary in-CAD working palette, so keep it compact
                    // for 1920 x 1200 users and let each tab scroll vertically.
                    DockEnabled =
                        DockSides.Left |
                        DockSides.Right |
                        DockSides.Top |
                        DockSides.Bottom
                };


                _paletteSet.Add("UFLS – TOOLS", _paletteControl);
                PalettePositionHelper.ConfigureSize(
                    _paletteSet,
                    new Size(360, 760),
                    new Size(320, 560));

            }

            PalettePositionHelper.ShowNearAutoCadWindow(
                _paletteSet,
                new Size(360, 760),
                new Size(320, 560),
                offsetX: 430,
                offsetY: 160);
        }
    }

    // ============================================================
    // MAIN PALETTE CONTROL (CHECK / ADJUST TABS)
    // ============================================================
    public class UflsPaletteControl : UserControl
    {
        private const int ButtonWidth = 256;
        private const int ButtonHeight = 24;
        private const float PaletteFontSize = 7.0f;

        public UflsPaletteControl()
        {
            InitializeUi();
        }

        private void InitializeUi()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            var checkPage = new TabPage("CHECK");
            var adjustPage = new TabPage("ADJUST");
            var layerPage = new TabPage("LABELS");

            checkPage.Padding = new Padding(3);
            adjustPage.Padding = new Padding(3);
            layerPage.Padding = new Padding(3);

            checkPage.Controls.Add(BuildCheckPanel());
            adjustPage.Controls.Add(BuildAdjustPanel());
            layerPage.Controls.Add(BuildLabelPanel());

            tabs.TabPages.Add(checkPage);
            tabs.TabPages.Add(adjustPage);
            tabs.TabPages.Add(layerPage);

            Controls.Add(tabs);
        }

        // ------------------------------------------------------------
        // CHECK TAB
        // ------------------------------------------------------------
        private Control BuildCheckPanel()
        {
            var layout = CreateMainFlowPanel();

            // VERIFICATION
            layout.Controls.Add(CreateSectionLabel("VERIFICATION"));
            layout.Controls.Add(CreateCommandButton("HIGHLIGHT RED", "HIGHLIGHTRED"));
            layout.Controls.Add(CreateCommandButton("HIGHLIGHT GREEN", "HIGHLIGHTGREEN"));
            layout.Controls.Add(CreateCommandButton("OBJECT HIGHLIGHT RED", "UFLS-OBJECT-HIGHLIGHT-RED"));
            layout.Controls.Add(CreateCommandButton("OBJECT HIGHLIGHT GREEN", "UFLS-OBJECT-HIGHLIGHT-GREEN"));

            // REDLINE
            layout.Controls.Add(CreateSectionLabel("REDLINE"));
            layout.Controls.Add(CreateCommandButton("REVISION CLOUD...", "UFLS-REVCLOUD"));
            layout.Controls.Add(CreateCommandButtonRow("NOTE", "UFLS-REDLINE-NOTE", "LEADER", "UFLS-REDLINE-LEADER"));

            // 2D LINEWORK
            layout.Controls.Add(CreateSectionLabel("2D LINEWORK"));
            layout.Controls.Add(CreateCommandButton("3P MANHOLE - ALL", "UFLS-MH-DIALOG"));
            layout.Controls.Add(CreateCommandButton("3P MANHOLE - SINGLE", "UFLS6"));
            layout.Controls.Add(CreateCommandButton("1P MANHOLE - ALL", "UFLS-MH1P-DIALOG"));
            layout.Controls.Add(CreateCommandButton("1P MANHOLE - SINGLE", "UFLS61P"));
            layout.Controls.Add(CreateCommandButtonRow("STRC-INNER WALL", "UFLS7", "STRC-OUTER WALL", "UFLS8"));
            layout.Controls.Add(CreateCommandButton("STUB MARKER", "UFLS-STUB"));
            // Now live: calls UflsDropInlet.RunDropInlet()
            layout.Controls.Add(CreateCommandButton("DROP INLET", "UFLS-DROP-INLET"));
            layout.Controls.Add(CreateCommandButton("3P CIRCLE", "UFLS-3PCIRCLE"));
            layout.Controls.Add(CreateCommandButton("3P RECTANGLE", "UFLS-3PRECT"));

            // 3D LINEWORK
            layout.Controls.Add(CreateSectionLabel("3D LINEWORK"));
            layout.Controls.Add(CreateCommandButton("TOP OF PIPE", "UFLS1"));
            layout.Controls.Add(CreateCommandButton("TRIM TOP OF PIPE", "UFLS5"));
            layout.Controls.Add(CreateCommandButton("LABEL INVERT", "UFLS-PIPE-LABEL-3D"));

            layout.Controls.Add(CreateSectionLabel("INFO"));
            layout.Controls.Add(CreateCommandButton("PIPE INFO @ POINT", "UFLS-PIPE-INFO"));

            return layout;
        }

        // ------------------------------------------------------------
        // ADJUST TAB (placeholder for now)
        // ------------------------------------------------------------
        private Control BuildAdjustPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("SEWER MAIN - MOVE"));
            layout.Controls.Add(CreateCommandButtonRow("MH - SINGLE", "UFLS-ADJ-MH-SINGLE", "MH - ALL", "UFLS-ADJ-MH-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("PIPE - SINGLE", "UFLS-ADJ-PIPE-SINGLE", "PIPE - ALL", "UFLS-ADJ-PIPE-ALL"));

            layout.Controls.Add(CreateSectionLabel("SEWER LATERAL PVC"));
            layout.Controls.Add(CreateCommandButtonRow("LAT - SINGLE", "UFLS-LATERAL-SINGLE", "LAT - ALL", "UFLS-LATERAL-ALL"));

            layout.Controls.Add(CreateSectionLabel("SEWER LATERAL C900"));
            layout.Controls.Add(CreateCommandButtonRow("LAT - SINGLE", "UFLS-LATERAL-C900-SINGLE", "LAT - ALL", "UFLS-LATERAL-C900-ALL"));

            layout.Controls.Add(CreateSectionLabel("STORM JUNCTION STRUCTURE"));
            layout.Controls.Add(CreateCommandButton("RESIZE JUNCTION", "SD-JUNCTION-SIZE"));

            layout.Controls.Add(CreateSectionLabel("STORM DRAIN - MOVE"));
            layout.Controls.Add(CreateCommandButtonRow("JNCT - SINGLE", "UFLS-ADJ-JNCT-SINGLE", "JNCT - ALL", "UFLS-ADJ-JNCT-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("DI - SINGLE", "UFLS-ADJ-DI-SINGLE", "DI - ALL", "UFLS-ADJ-DI-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("MH - SINGLE", "UFLS-ADJ-MH-SINGLE", "MH - ALL", "UFLS-ADJ-MH-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("PIPE - SINGLE", "UFLS-ADJ-PIPE-SINGLE", "PIPE - ALL", "UFLS-ADJ-PIPE-ALL"));

            layout.Controls.Add(CreateSectionLabel("SWAP MATERIAL"));
            layout.Controls.Add(CreateCommandButtonRow("PVC --> C900", "UFLS-PIPE-PVC-C900", "RCP --> C900", "UFLS-PIPE-RCP-C900"));
            layout.Controls.Add(CreateCommandButtonRow("C900 --> RCP", "UFLS-PIPE-C900-RCP", "C900 --> PVC", "UFLS-PIPE-C900-PVC"));

            layout.Controls.Add(CreateSectionLabel("MODIFY"));
            layout.Controls.Add(CreateCommandButton("REMOVE REF ALIGN", "REMOVE-REF-ALIGN"));

            layout.Controls.Add(CreateSectionLabel("INFO"));
            layout.Controls.Add(CreateCommandButton("PIPE INFO @ POINT", "UFLS-PIPE-INFO"));

            layout.Controls.Add(CreateSectionLabel("FINALIZE"));
            layout.Controls.Add(CreateCommandButton("ASSIGN QUALITY", "UFLS-FINALIZE-QUALITY"));

            return layout;
        }


        // ------------------------------------------------------------
        // LABEL TAB
        // ------------------------------------------------------------
        private Control BuildLabelPanel()
        {
            var layout = CreateMainFlowPanel();

            layout.Controls.Add(CreateSectionLabel("PIPE"));
            layout.Controls.Add(CreateCommandButton("PIPE", "UFLS-LABEL-PIPE-CIRCULAR"));
            layout.Controls.Add(CreateCommandButton("BOX CULVERT", "UFLS-LABEL-PIPE-BOX"));

            layout.Controls.Add(CreateSectionLabel("STRUCTURE"));
            layout.Controls.Add(CreateCommandButton("MANHOLE", "UFLS-LABEL-STRC-MANHOLE"));
            layout.Controls.Add(CreateCommandButton("ACCESS", "UFLS-LABEL-STRC-ACCESS"));
            layout.Controls.Add(CreateCommandButton("STUB", "UFLS-LABEL-STRC-STUB"));
            layout.Controls.Add(CreateCommandButton("JUNCTION", "UFLS-LABEL-STRC-JUNCTION"));
            layout.Controls.Add(CreateCommandButton("DROP INLET", "UFLS-LABEL-STRC-INLET"));

            return layout;
        }

        // ------------------------------------------------------------
        // UI HELPERS  (matched to PctPalette style)
        // ------------------------------------------------------------

        private static FlowLayoutPanel CreateMainFlowPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown, // avoid ambiguity
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8, 6, 8, 8)
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


        private Control CreateCommandButtonRow(
            string leftLabel, string leftCmd,
            string rightLabel, string rightCmd)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Width = ButtonWidth,
                Height = ButtonHeight,
                Margin = new Padding(3, 2, 3, 2),
                Padding = new Padding(0)
            };

            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var btnLeft = new Button
            {
                Text = leftLabel,
                Dock = DockStyle.Fill,
                Height = ButtonHeight,
                Margin = new Padding(0, 0, 4, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = leftCmd
            };

            var btnRight = new Button
            {
                Text = rightLabel,
                Dock = DockStyle.Fill,
                Height = ButtonHeight,
                Margin = new Padding(4, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                Tag = rightCmd
            };

            btnLeft.Click += (s, e) => OnCommandButtonClick(btnLeft, e);
            btnRight.Click += (s, e) => OnCommandButtonClick(btnRight, e);

            row.Controls.Add(btnLeft, 0, 0);
            row.Controls.Add(btnRight, 1, 0);

            return row;
        }

        private Button CreateCommandButton(string label, string tagValue)
        {
            var btn = new Button
            {
                Text = label,
                Width = ButtonWidth,
                Height = ButtonHeight,
                Margin = new Padding(3, 2, 3, 2),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular),
                AutoSize = false,
                Tag = tagValue
            };

            btn.Click += OnCommandButtonClick;

            return btn;
        }

        private void OnCommandButtonClick(object? sender, EventArgs e)
        {
            if (sender is not Button btn)
                return;

            if (btn.Tag is not string tag || string.IsNullOrWhiteSpace(tag))
                return;

            // Special case: Drop Inlet -> call .NET Type A tool directly
            if (string.Equals(tag, "UFLS-DROP-INLET", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    UflsDropInlet.RunDropInlet();
                }
                catch (System.Exception ex)
                {
                    var doc = AcadApp.DocumentManager.MdiActiveDocument;
                    doc?.Editor.WriteMessage($"\nError running UFLS-DROP-INLET: {ex.Message}");
                }
                return;
            }

            // Special case: open UFLS manhole dialogs
            if (tag == "UFLS-MH-DIALOG")
            {
                ShowManholeDialog(useSinglePointCenters: false);
                return;
            }

            if (tag == "UFLS-MH1P-DIALOG")
            {
                ShowManholeDialog(useSinglePointCenters: true);
                return;
            }

            // Default: queue CAD command (like PCT palette does)
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return;

                doc.SendStringToExecute(
                    tag + " ",
                    activate: true,
                    wrapUpInactiveDoc: false,
                    echoCommand: false);
            }
            catch (System.Exception ex)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nError queuing {tag}: {ex.Message}");
            }
        }

        private void ShowManholeDialog(bool useSinglePointCenters)
        {
            using (var dlg = new UflsManholeOptionsForm())
            {
                var result = AcadApp.ShowModalDialog(dlg);
                if (result != DialogResult.OK)
                    return;

                bool isInHouse = dlg.IsInHouse;
                string codes = dlg.RawCodes;

                if (useSinglePointCenters)
                    UflsManholeAutoCreate.RunFromPalette1P(isInHouse, codes);
                else
                    UflsManholeAutoCreate.RunFromPalette(isInHouse, codes);
            }
        }
    }

    // --------------------------------------------------------------------
    // Small dialog for manhole options
    // --------------------------------------------------------------------
    internal class UflsManholeOptionsForm : Form
    {
        private readonly RadioButton _rbInHouse;
        private readonly RadioButton _rbOthers;
        private readonly TextBox _txtCodes;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        public bool IsInHouse => _rbInHouse.Checked;
        public string RawCodes => _txtCodes.Text;

        public UflsManholeOptionsForm()
        {
            Text = "UFLS – MANHOLE OPTIONS";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 180);

            var lblSource = new Label
            {
                Text = "SURVEY SOURCE:",
                AutoSize = true,
                Location = new Point(12, 12)
            };

            _rbInHouse = new RadioButton
            {
                Text = "IN-HOUSE (CLV CODES)",
                AutoSize = true,
                Location = new Point(30, 36),
                Checked = true
            };

            _rbOthers = new RadioButton
            {
                Text = "OTHERS (ENTER RAW CODES)",
                AutoSize = true,
                Location = new Point(30, 60)
            };

            var lblCodes = new Label
            {
                Text = "RAW DESCRIPTION CODE(S):",
                AutoSize = true,
                Location = new Point(12, 90)
            };

            _txtCodes = new TextBox
            {
                Location = new Point(30, 110),
                Width = 360,
                Enabled = false
            };

            _rbInHouse.CheckedChanged += OnSourceChanged;
            _rbOthers.CheckedChanged += OnSourceChanged;

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(190, 140),
                Width = 80
            };

            _btnCancel = new Button
            {
                Text = "CANCEL",
                DialogResult = DialogResult.Cancel,
                Location = new Point(280, 140),
                Width = 80
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.Add(lblSource);
            Controls.Add(_rbInHouse);
            Controls.Add(_rbOthers);
            Controls.Add(lblCodes);
            Controls.Add(_txtCodes);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);
        }

        private void OnSourceChanged(object? sender, EventArgs e)
        {
            _txtCodes.Enabled = _rbOthers.Checked;
        }
    }

    // ============================================================
    // WRAPPER COMMANDS (REV CLOUD, FUTURE TOOLS)
    // ============================================================
    public static class UflsWrapperCommands
    {
        /// <summary>
        /// Revision Cloud wrapper.
        /// Asks for size: Large / Medium / Small, then calls
        /// REVCLOUDLARGE / REVCLOUDMEDIUM / REVCLOUDSMALL (LISP).
        /// </summary>
        [CommandMethod("UFLS-REVCLOUD")]
        public static void UflsRevcloud()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var pko = new PromptKeywordOptions(
                "\nRevision cloud size [Large/Medium/Small] <Medium>: ")
            {
                AllowNone = true
            };

            pko.Keywords.Add("Large");
            pko.Keywords.Add("Medium");
            pko.Keywords.Add("Small");
            pko.Keywords.Default = "Medium";

            var res = ed.GetKeywords(pko);
            if (res.Status == PromptStatus.Cancel) return;

            string choice = res.StringResult;
            if (string.IsNullOrEmpty(choice))
                choice = "Medium";

            string cmd = choice switch
            {
                "Large" => "REVCLOUDLARGE",
                "Small" => "REVCLOUDSMALL",
                _ => "REVCLOUDMEDIUM"
            };

            doc.SendStringToExecute(cmd + " ", true, false, false);
        }

        // Additional UFLS wrapper commands live in other source files
        // (for example UflsLaterals.cs for lateral workflow commands).
    }
}
