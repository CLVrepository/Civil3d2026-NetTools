using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Globalization;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using WinFlowDirection = System.Windows.Forms.FlowDirection;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Gis
{
    public class GisPalette
    {
        private static PaletteSet? _ps;
        private static ListBox? _lstAerials;
        private static Button? _btnUnload;
        private static TabControl? _tabControl;

        private const int ButtonWidth = 256;
        private const int ButtonHeight = 24;
        private const float PaletteFontSize = 7.0f;

        // ============================================================
        // COMMAND TO SHOW PALETTE  (Q2)
        // ============================================================

        [CommandMethod("Q2")]
        public static void ShowGisPalette()
        {
            if (_ps == null)
            {
                CreatePalette();
            }

            if (_ps != null)
            {
                PalettePositionHelper.ShowNearAutoCadWindow(
                    _ps,
                    new Size(340, 700),
                    new Size(300, 540),
                    offsetX: 470,
                    offsetY: 200);
            }

            RefreshAerialList();
        }

        // ============================================================
        // PALETTE CREATION
        // ============================================================

        private static void CreatePalette()
        {
            _ps = new PaletteSet("GIS Tools")
            {
                Style = PaletteSetStyles.ShowAutoHideButton
                      | PaletteSetStyles.ShowCloseButton
                      | PaletteSetStyles.ShowPropertiesMenu
            };


            // One palette containing a TabControl with AERIAL / GIS tabs.
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // AERIAL tab
            var aerialPage = new TabPage("AERIAL")
            {
                Padding = new Padding(3)
            };
            aerialPage.Controls.Add(CreateAerialTabPanel());

            // GIS tab
            var gisPage = new TabPage("GIS")
            {
                Padding = new Padding(3)
            };
            gisPage.Controls.Add(CreateGisTabPanel());

            _tabControl.TabPages.Add(aerialPage);
            _tabControl.TabPages.Add(gisPage);

            mainPanel.Controls.Add(_tabControl);
            _ps.Add("GIS", mainPanel);

            PalettePositionHelper.ConfigureSize(
                _ps,
                new Size(340, 700),
                new Size(300, 540));
        }

        private static Control CreateAerialTabPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            _btnUnload = new Button
            {
                Text = "Unload Aerials",
                Dock = DockStyle.Top,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            _btnUnload.Click += (s, e) =>
            {
                try
                {
                    Aerials.RemoveAllNearmapLayers();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("Error unloading aerials:\n" + ex.Message,
                        "GIS Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            _lstAerials = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };

            _lstAerials.DoubleClick += (s, e) =>
            {
                if (_lstAerials.SelectedItem is AerialItem item &&
                    !string.IsNullOrEmpty(item.FullPath))
                {
                    try
                    {
                        Aerials.LoadAerialFromFile(item.FullPath);
                    }
                    catch (System.Exception ex)
                    {
                        MessageBox.Show("Error loading aerial:\n" + ex.Message,
                            "GIS Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            panel.Controls.Add(_lstAerials);
            panel.Controls.Add(_btnUnload);

            return panel;
        }

        private static Control CreateGisTabPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = WinFlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            var addressSection = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 8, 0, 2),
                Text = "ADDRESS"
            };

            var btnLocateAddress = new Button
            {
                Text = "LOCATE ADDRESS",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnLocateAddress.Click += (s, e) => GisLocateAddressCommands.ShowLocateDialogFromPalette();

            var btnLocateParcel = new Button
            {
                Text = "LOCATE PARCEL",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnLocateParcel.Click += (s, e) => GisLocateParcelCommands.ShowLocateParcelDialogFromPalette();

            var section = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 12, 0, 2),
                Text = "DATA"
            };

            var btnImport = new Button
            {
                Text = "IMPORT GIS",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnImport.Click += (s, e) => GisImportCommands.RunFromPalette();

            var btnCreateData = new Button
            {
                Text = "CREATE DATA",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnCreateData.Click += (s, e) => GisCreateDataPaletteCommands.ShowCreateDataPalette();

            var toolsSection = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 12, 0, 2),
                Text = "GIS TOOLS"
            };

            var referenceSection = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 12, 0, 2),
                Text = "SECTION/COORDINATE SYSTEM"
            };

            var btnLoadReferenceLayers = new Button
            {
                Text = "DISPLAY COORDINATE ZONES",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnLoadReferenceLayers.Click += (s, e) => GisReferenceLayers.LoadReferenceLayers();

            var btnUnloadReferenceLayers = new Button
            {
                Text = "UNLOAD COORDINATE ZONES",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnUnloadReferenceLayers.Click += (s, e) => GisReferenceLayers.RemoveReferenceLayers();


            var btnDisplaySections = new Button
            {
                Text = "DISPLAY SECTIONS",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnDisplaySections.Click += (s, e) => GisSectionReferenceLayers.DisplaySections();

            var btnUnloadSections = new Button
            {
                Text = "UNLOAD SECTIONS",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnUnloadSections.Click += (s, e) => GisSectionReferenceLayers.UnloadSections();

            var btnSurveyReportHtml = new Button
            {
                Text = "HTML SURVEY REPORT",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnSurveyReportHtml.Click += (s, e) => Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("GISSURVEYREPORTHTML ", true, false, false);

            var xdataSection = new Label
            {
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Bold),
                Padding = new Padding(0, 12, 0, 2),
                Text = "XDATA"
            };

            var btnCacheInspect = new Button
            {
                Text = "OD INSPECT",
                Width = ButtonWidth,
                Height = ButtonHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, PaletteFontSize, FontStyle.Regular)
            };
            btnCacheInspect.Click += (s, e) => Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("GISCACHEINSPECT ", true, false, false);

            panel.Controls.Add(addressSection);
            panel.Controls.Add(btnLocateAddress);
            panel.Controls.Add(btnLocateParcel);
            panel.Controls.Add(section);
            panel.Controls.Add(btnImport);
            panel.Controls.Add(btnCreateData);
            panel.Controls.Add(referenceSection);
            panel.Controls.Add(btnLoadReferenceLayers);
            panel.Controls.Add(btnUnloadReferenceLayers);
            panel.Controls.Add(btnDisplaySections);
            panel.Controls.Add(btnUnloadSections);
            panel.Controls.Add(toolsSection);
            panel.Controls.Add(btnSurveyReportHtml);
            panel.Controls.Add(xdataSection);
            panel.Controls.Add(btnCacheInspect);
            return panel;
        }

        // ============================================================
        // AERIAL LIST HANDLING (NEWEST → OLDEST BY DATE IN NAME)
        // ============================================================

        private static void RefreshAerialList()
        {
            if (_lstAerials == null)
                return;

            _lstAerials.BeginUpdate();
            _lstAerials.Items.Clear();

            try
            {
                string folder = Aerials.NearmapFolder;

                if (!Directory.Exists(folder))
                {
                    _lstAerials.Items.Add(
                        new AerialItem
                        {
                            DisplayName = "(folder not found)",
                            FullPath = ""
                        });
                }
                else
                {
                    var files = Directory
                        .GetFiles(folder, "*.layer")
                        .Select(f => new
                        {
                            FullPath = f,
                            DisplayName = Path.GetFileNameWithoutExtension(f),
                            ParsedDate = ParseDateFromName(
                                Path.GetFileNameWithoutExtension(f))
                        })
                        .OrderByDescending(x => x.ParsedDate)
                        .ToList();

                    if (files.Count == 0)
                    {
                        _lstAerials.Items.Add(
                            new AerialItem
                            {
                                DisplayName = "(no .layer files found)",
                                FullPath = ""
                            });
                    }
                    else
                    {
                        foreach (var file in files)
                        {
                            _lstAerials.Items.Add(new AerialItem
                            {
                                DisplayName = file.DisplayName,
                                FullPath = file.FullPath
                            });
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                _lstAerials.Items.Add(
                    new AerialItem
                    {
                        DisplayName = "(error: " + ex.Message + ")",
                        FullPath = ""
                    });
            }
            finally
            {
                _lstAerials.EndUpdate();
            }
        }

        /// <summary>
        /// Parses filenames like "(2026 - January) LasVegas"
        /// into a DateTime(2026, 1, 1). If parsing fails, returns DateTime.MinValue.
        /// </summary>
        private static DateTime ParseDateFromName(string name)
        {
            try
            {
                // Expect: "(2026 - January) LasVegas"
                int start = name.IndexOf('(');
                int end = name.IndexOf(')');

                if (start < 0 || end <= start)
                    return DateTime.MinValue;

                string inside = name.Substring(start + 1, end - start - 1);
                // inside = "2026 - January"

                var parts = inside.Split('-');
                if (parts.Length != 2)
                    return DateTime.MinValue;

                int year = int.Parse(parts[0].Trim());
                string monthName = parts[1].Trim();

                int month = DateTime.ParseExact(
                    monthName,
                    "MMMM",
                    CultureInfo.InvariantCulture).Month;

                return new DateTime(year, month, 1);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        // ============================================================
        // SUPPORT CLASS FOR LISTBOX ITEMS
        // ============================================================

        private class AerialItem
        {
            public string DisplayName { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;

            public override string ToString() => DisplayName;
        }
    }
}