using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using DrawingFont = System.Drawing.Font;
using WinFlowDirection = System.Windows.Forms.FlowDirection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisCacheInspectCommands
    {
        private const string CacheTrackingRegAppName = "CLV_GIS_CACHE";

        [CommandMethod("GISCACHEINSPECT", CommandFlags.Modal)]
        public static void InspectCacheObject()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect object to inspect: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    return;
                }

                ObjectInspectionData inspection;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (!(tr.GetObject(per.ObjectId, OpenMode.ForRead) is Entity ent))
                    {
                        ed.WriteMessage("\nSelected object is not a readable entity.");
                        return;
                    }

                    inspection = BuildInspectionData(ent);
                    tr.Commit();
                }

                using (CacheInspectForm form = new CacheInspectForm(inspection))
                {
                    AcadApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nGISCACHEINSPECT failed: " + ex.Message);
            }
        }

        private static ObjectInspectionData BuildInspectionData(Entity ent)
        {
            Dictionary<string, string> fields = ReadCacheFields(ent.XData);

            return new ObjectInspectionData
            {
                Handle = ent.Handle.ToString(),
                ObjectType = ent.GetType().Name,
                Layer = ent.Layer,
                ImportId = GetField(fields, "IMPORT_ID"),
                ProjectNumber = GetField(fields, "PROJECT_NUMBER"),
                UserName = GetField(fields, "USER_NAME"),
                ImportDate = FirstNonEmpty(GetField(fields, "IMPORT_DATE"), GetField(fields, "IMPORT_DATE_UTC")),
                DataSetType = GetField(fields, "DATASET_TYPE"),
                SourceDwg = GetField(fields, "SOURCE_DWG"),
                CacheName = GetField(fields, "CACHE_NAME"),
                HasCacheXData = fields.Count > 0,
            };
        }

        private static Dictionary<string, string> ReadCacheFields(ResultBuffer? xdata)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (xdata == null)
            {
                return fields;
            }

            bool inCacheRegApp = false;
            foreach (TypedValue tv in xdata)
            {
                if (tv.TypeCode == 1001)
                {
                    string regApp = Convert.ToString(tv.Value) ?? string.Empty;
                    inCacheRegApp = string.Equals(regApp, CacheTrackingRegAppName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inCacheRegApp || tv.TypeCode != 1000)
                {
                    continue;
                }

                string text = Convert.ToString(tv.Value) ?? string.Empty;
                int split = text.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                string key = text.Substring(0, split).Trim();
                string value = split < text.Length - 1 ? text.Substring(split + 1).Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    fields[key] = value;
                }
            }

            return fields;
        }

        private static string GetField(Dictionary<string, string> fields, string key)
        {
            return fields.TryGetValue(key, out string? value) ? value : string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        private sealed class ObjectInspectionData
        {
            public string Handle { get; set; } = string.Empty;
            public string ObjectType { get; set; } = string.Empty;
            public string Layer { get; set; } = string.Empty;
            public string ImportId { get; set; } = string.Empty;
            public string ProjectNumber { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string ImportDate { get; set; } = string.Empty;
            public string DataSetType { get; set; } = string.Empty;
            public string SourceDwg { get; set; } = string.Empty;
            public string CacheName { get; set; } = string.Empty;
            public bool HasCacheXData { get; set; }
        }

        private sealed class CacheInspectForm : Form
        {
            public CacheInspectForm(ObjectInspectionData data)
            {
                Text = "CLV Cache Inspector";
                StartPosition = FormStartPosition.CenterScreen;
                MinimumSize = new Size(640, 420);
                Size = new Size(760, 500);
                FormBorderStyle = FormBorderStyle.Sizable;

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    Padding = new Padding(10),
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Controls.Add(layout);

                TabControl tabs = new TabControl { Dock = DockStyle.Fill };
                tabs.TabPages.Add(BuildSummaryTab(data));
                layout.Controls.Add(tabs, 0, 0);

                FlowLayoutPanel buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = WinFlowDirection.RightToLeft,
                    AutoSize = true,
                    WrapContents = false,
                };
                Button closeButton = new Button
                {
                    Text = "Close",
                    AutoSize = true,
                    DialogResult = DialogResult.OK,
                    Padding = new Padding(10, 4, 10, 4),
                };
                buttons.Controls.Add(closeButton);
                layout.Controls.Add(buttons, 0, 1);
                AcceptButton = closeButton;
            }

            private static TabPage BuildSummaryTab(ObjectInspectionData data)
            {
                TabPage page = new TabPage("Summary");
                TableLayoutPanel table = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    AutoScroll = true,
                    Padding = new Padding(12),
                };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                page.Controls.Add(table);

                AddRow(table, "Import ID", data.ImportId);
                AddRow(table, "Project Number", data.ProjectNumber);
                AddRow(table, "User Name", data.UserName);
                AddRow(table, "Date", data.ImportDate);
                AddRow(table, "Data Set Type", data.DataSetType);
                AddRow(table, "Source DWG", data.SourceDwg);
                AddRow(table, "Cache Name", data.CacheName);
                AddRow(table, "Handle", data.Handle);
                AddRow(table, "Object Type", data.ObjectType);
                AddRow(table, "Layer", data.Layer);
                AddRow(table, "Cache XData", data.HasCacheXData ? "Yes" : "No");

                return page;
            }

            private static void AddRow(TableLayoutPanel table, string labelText, string valueText)
            {
                int row = table.RowCount;
                table.RowCount += 1;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Label label = new Label
                {
                    Text = labelText,
                    AutoSize = true,
                    Font = new DrawingFont(SystemFonts.DefaultFont, FontStyle.Bold),
                    Margin = new Padding(0, 6, 12, 6),
                };

                TextBox value = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(valueText) ? "(none)" : valueText,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    Dock = DockStyle.Top,
                    Margin = new Padding(0, 3, 0, 3),
                };

                table.Controls.Add(label, 0, row);
                table.Controls.Add(value, 1, row);
            }
        }
    }
}
