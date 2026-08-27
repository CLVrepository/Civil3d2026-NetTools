using System;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.Windows;

namespace CLV_CivilTools.Ufls
{
    public static class UflsPalette
    {
        private const string PaletteName = "Q1";

        private static PaletteSet? _paletteSet;

        public static void Show()
        {
            if (_paletteSet == null)
            {
                _paletteSet = new PaletteSet(PaletteName)
                {
                    Style = PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(240, 400),
                    Size = new Size(300, 700)
                };

                Control content = BuildContent();
                _paletteSet.Add("UFLS", content);
            }

            _paletteSet.Visible = true;
        }

        private static Control BuildContent()
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
            layout.Controls.Add(CreateCommandButton("ADJUST TOP ELEV", "UFLS-ADJ-TOP-ELEV"));
            layout.Controls.Add(CreateCommandButton("PLACE ACCESS MANHOLE", "UFLS-PLACE-ACCESS-MH"));

            layout.Controls.Add(CreateSectionLabel("STORM DRAIN - MOVE"));
            layout.Controls.Add(CreateCommandButtonRow("JNCT - SINGLE", "UFLS-ADJ-JNCT-SINGLE", "JNCT - ALL", "UFLS-ADJ-JNCT-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("DI - SINGLE", "UFLS-ADJ-DI-SINGLE", "DI - ALL", "UFLS-ADJ-DI-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("MH - SINGLE", "UFLS-ADJ-MH-SINGLE", "MH - ALL", "UFLS-ADJ-MH-ALL"));
            layout.Controls.Add(CreateCommandButtonRow("PIPE - SINGLE", "UFLS-ADJ-PIPE-SINGLE", "PIPE - ALL", "UFLS-ADJ-PIPE-ALL"));

            layout.Controls.Add(CreateSectionLabel("SWAP MATERIAL"));
            layout.Controls.Add(CreateCommandButtonRow("PVC --> C900", "UFLS-PIPE-PVC-C900", "RCP --> C900", "UFLS-PIPE-RCP-C900"));
            layout.Controls.Add(CreateCommandButtonRow("C900 --> RCP", "UFLS-PIPE-C900-RCP", "C900 --> PVC", "UFLS-PIPE-C900-PVC"));

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
                Padding = new Padding(6)
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(3, 10, 3, 3)
            };
        }

        private static Button CreateCommandButton(string text, string command)
        {
            var button = new Button
            {
                AutoSize = false,
                Width = 260,
                Height = 28,
                Text = text,
                Margin = new Padding(3)
            };

            button.Click += (_, _) => SendCommand(command);
            return button;
        }

        private static Control CreateCommandButtonRow(string leftText, string leftCommand, string rightText, string rightCommand)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };

            panel.Controls.Add(CreateCommandButton(leftText, leftCommand));
            panel.Controls.Add(CreateCommandButton(rightText, rightCommand));
            return panel;
        }

        private static void SendCommand(string command)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?
                .SendStringToExecute(command + " ", true, false, false);
        }
    }
}
