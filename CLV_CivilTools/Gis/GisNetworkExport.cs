using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisNetworkExportCommands
    {
        [CommandMethod("GIS-CHECK-CURVES", CommandFlags.Modal)]
        public static void CheckCurvedPipes()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ctx = GisWorkflowContext.Create(doc, null, null);
            var report = GisCurveDetector.Analyze(doc.Database);
            WriteCurveSummary(doc.Editor, ctx, report, logToFile: true);
        }

        [CommandMethod("GIS-EXPORT-SHP", CommandFlags.Modal)]
        public static void ExportNetworkToShp()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using var dlg = new GisExportOptionsForm(doc);
            if (AcadApp.ShowModalDialog(dlg) != DialogResult.OK)
                return;

            var ctx = GisWorkflowContext.Create(doc, dlg.SelectedNetworkKind, dlg.SelectedCoordinateSystem);
            if (ctx == null)
            {
                doc.Editor.WriteMessage("\nGIS-EXPORT-SHP: unable to derive project context.");
                return;
            }

            RunWorkflow(doc, ctx, dlg.AttemptBestEffortAutomation);
        }

        internal static void RunWorkflowFromPalette(bool attemptBestEffortAutomation)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using var dlg = new GisExportOptionsForm(doc)
            {
                AttemptBestEffortAutomation = attemptBestEffortAutomation
            };

            if (AcadApp.ShowModalDialog(dlg) != DialogResult.OK)
                return;

            var ctx = GisWorkflowContext.Create(doc, dlg.SelectedNetworkKind, dlg.SelectedCoordinateSystem);
            if (ctx == null)
            {
                doc.Editor.WriteMessage("\nGIS-EXPORT-SHP: unable to derive project context.");
                return;
            }

            RunWorkflow(doc, ctx, dlg.AttemptBestEffortAutomation);
        }

        private static void RunWorkflow(Document doc, GisWorkflowContext ctx, bool attemptBestEffortAutomation)
        {
            var ed = doc.Editor;

            try
            {
                Directory.CreateDirectory(ctx.GisFolder);
                Directory.CreateDirectory(ctx.ShpFolder);

                var curveReport = GisCurveDetector.Analyze(doc.Database);
                string logPath = Path.Combine(ctx.GisFolder, $"{ctx.BaseName}-GIS-EXPORT.log");
                string scriptPath = Path.Combine(ctx.GisFolder, $"{ctx.BaseName}-GIS-EXPORT.scr");
                string readmePath = Path.Combine(ctx.GisFolder, $"{ctx.BaseName}-GIS-EXPORT-README.txt");

                var script = GisScriptBuilder.Build(ctx, curveReport, attemptBestEffortAutomation, out string readmeText);
                File.WriteAllText(scriptPath, script, Encoding.ASCII);
                File.WriteAllText(readmePath, readmeText, Encoding.UTF8);
                File.WriteAllText(logPath, BuildLogText(ctx, curveReport, scriptPath, readmePath, attemptBestEffortAutomation), Encoding.UTF8);

                ed.WriteMessage($"\nGIS export setup complete for {ctx.BaseName}.");
                ed.WriteMessage($"\n  GIS folder : {ctx.GisFolder}");
                ed.WriteMessage($"\n  SDF path   : {ctx.SdfPath}");
                ed.WriteMessage($"\n  Pipes SHP  : {ctx.PipeShpPath}");
                ed.WriteMessage($"\n  Strc SHP   : {ctx.StructureShpPath}");
                ed.WriteMessage($"\n  Curved pipe candidates: {curveReport.CurvedPipeCount} of {curveReport.TotalPipeCount}");
                ed.WriteMessage($"\n  Script file: {scriptPath}");
                ed.WriteMessage($"\n  Readme     : {readmePath}");

                if (curveReport.CurvedPipeCount > 0)
                {
                    ed.WriteMessage("\nCurved pipes were detected. The generated readme/script includes a manual curve-fix hold point before SHP export.");
                }

                if (attemptBestEffortAutomation)
                {
                    ed.WriteMessage("\nAuto-run is ON. EXPORTSDF will open first so you can save the SDF, then the generated SCRIPT will continue with MAPIMPORT / MAPEXPORT.");
                    ed.WriteMessage($"\nWhen the EXPORTSDF dialog opens, save to: {ctx.SdfPath}");
                    string safeScriptPath = scriptPath.Replace("\\", "\\\\");
                    doc.SendStringToExecute($"_.EXPORTSDF _.SCRIPT \"{safeScriptPath}\" ", true, false, false);
                }
                else
                {
                    ed.WriteMessage("\nBest-effort auto-run is OFF. First run EXPORTSDF and save to the SDF path shown above, then run the generated SCRIPT to continue with MAPIMPORT / MAPEXPORT.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nGIS-EXPORT-SHP error: {ex.Message}");
            }
        }

        private static string BuildLogText(GisWorkflowContext ctx, GisCurveReport curveReport, string scriptPath, string readmePath, bool attemptBestEffortAutomation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CLV_CivilTools GIS Export Log");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Drawing: {ctx.DrawingPath}");
            sb.AppendLine($"Project Number: {ctx.ProjectNumber}");
            sb.AppendLine($"Network Type: {ctx.NetworkKind}");
            sb.AppendLine($"Coordinate System: {ctx.CoordinateSystem}");
            sb.AppendLine($"GIS Folder: {ctx.GisFolder}");
            sb.AppendLine($"SHP Folder: {ctx.ShpFolder}");
            sb.AppendLine($"SDF Path: {ctx.SdfPath}");
            sb.AppendLine($"Pipe SHP Path: {ctx.PipeShpPath}");
            sb.AppendLine($"Structure SHP Path: {ctx.StructureShpPath}");
            sb.AppendLine($"Import Profile: {ctx.ImportProfilePath}");
            sb.AppendLine($"Pipe Export Profile: {ctx.PipeExportProfilePath}");
            sb.AppendLine($"Structure Export Profile: {ctx.StructureExportProfilePath}");
            sb.AppendLine($"Auto-run requested: {attemptBestEffortAutomation}");
            sb.AppendLine("EXPORTSDF handling: interactive file dialog first, generated script continues after SDF creation.");
            sb.AppendLine($"Curved Pipe Candidates: {curveReport.CurvedPipeCount} / {curveReport.TotalPipeCount}");
            sb.AppendLine($"Script: {scriptPath}");
            sb.AppendLine($"Readme: {readmePath}");
            sb.AppendLine();
            foreach (string name in curveReport.CurvedPipeNames)
                sb.AppendLine($"Curved pipe candidate: {name}");
            return sb.ToString();
        }

        private static void WriteCurveSummary(Editor ed, GisWorkflowContext? ctx, GisCurveReport report, bool logToFile)
        {
            ed.WriteMessage($"\nGIS-CHECK-CURVES: {report.CurvedPipeCount} curved-pipe candidate(s) found out of {report.TotalPipeCount} pipe(s).");
            foreach (string name in report.CurvedPipeNames.Take(25))
                ed.WriteMessage($"\n  - {name}");

            if (!logToFile || ctx == null)
                return;

            try
            {
                Directory.CreateDirectory(ctx.GisFolder);
                string logPath = Path.Combine(ctx.GisFolder, $"{ctx.BaseName}-CURVE-CHECK.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"Drawing: {ctx.DrawingPath}");
                sb.AppendLine($"Curved Pipe Candidates: {report.CurvedPipeCount} / {report.TotalPipeCount}");
                sb.AppendLine();
                foreach (string name in report.CurvedPipeNames)
                    sb.AppendLine(name);
                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
                ed.WriteMessage($"\nCurve report written to: {logPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to write curve report: {ex.Message}");
            }
        }
    }

    internal sealed class GisWorkflowContext
    {
        public string DrawingPath { get; init; } = string.Empty;
        public string DrawingFolder { get; init; } = string.Empty;
        public string CadFolder { get; init; } = string.Empty;
        public string GisFolder { get; init; } = string.Empty;
        public string ShpFolder { get; init; } = string.Empty;
        public string ProjectNumber { get; init; } = string.Empty;
        public string NetworkKind { get; init; } = string.Empty;
        public string CoordinateSystem { get; init; } = string.Empty;
        public string BaseName { get; init; } = string.Empty;
        public string SdfPath { get; init; } = string.Empty;
        public string PipeShpPath { get; init; } = string.Empty;
        public string StructureShpPath { get; init; } = string.Empty;
        public string ImportProfilePath { get; init; } = string.Empty;
        public string PipeExportProfilePath { get; init; } = string.Empty;
        public string StructureExportProfilePath { get; init; } = string.Empty;

        public static GisWorkflowContext? Create(Document doc, string? requestedNetworkKind, string? requestedCoordinateSystem)
        {
            string drawingPath = doc.Name;
            if (string.IsNullOrWhiteSpace(drawingPath))
                return null;

            string drawingFolder = Path.GetDirectoryName(drawingPath) ?? string.Empty;
            string cadFolder = FindCadFolder(drawingFolder) ?? drawingFolder;
            string projectNumber = new DirectoryInfo(cadFolder).Parent?.Name ?? Path.GetFileNameWithoutExtension(drawingPath);
            string networkKind = string.IsNullOrWhiteSpace(requestedNetworkKind)
                ? InferNetworkKind(drawingPath)
                : requestedNetworkKind.Trim().ToUpperInvariant();
            string coordinateSystem = string.IsNullOrWhiteSpace(requestedCoordinateSystem)
                ? "NV83.NCRS-LVF"
                : requestedCoordinateSystem.Trim();

            string gisFolder = Path.Combine(cadFolder, "GIS");
            string shpFolder = Path.Combine(gisFolder, "SHP");
            string baseName = $"{projectNumber}-{networkKind}";
            string sdfPath = Path.Combine(gisFolder, $"{baseName}-E.SDF");
            string pipeShpPath = Path.Combine(shpFolder, $"{baseName}-PIPE.shp");
            string strcShpPath = Path.Combine(shpFolder, $"{baseName}-STRC.shp");

            return new GisWorkflowContext
            {
                DrawingPath = drawingPath,
                DrawingFolder = drawingFolder,
                CadFolder = cadFolder,
                GisFolder = gisFolder,
                ShpFolder = shpFolder,
                ProjectNumber = projectNumber,
                NetworkKind = networkKind,
                CoordinateSystem = coordinateSystem,
                BaseName = baseName,
                SdfPath = sdfPath,
                PipeShpPath = pipeShpPath,
                StructureShpPath = strcShpPath,
                ImportProfilePath = GisProfileResolver.ResolveImportProfile(coordinateSystem),
                PipeExportProfilePath = GisProfileResolver.ResolvePipeExportProfile(),
                StructureExportProfilePath = GisProfileResolver.ResolveStructureExportProfile()
            };
        }

        private static string? FindCadFolder(string startFolder)
        {
            var dir = new DirectoryInfo(startFolder);
            while (dir != null)
            {
                if (dir.Name.Equals("CAD", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private static string InferNetworkKind(string drawingPath)
        {
            string upper = drawingPath.ToUpperInvariant();
            if (upper.Contains("STRM")) return "STRM";
            if (upper.Contains("SSWR") || upper.Contains("SEWER")) return "SSWR";
            return "SSWR";
        }
    }

    internal static class GisProfileResolver
    {
        public static string ResolveImportProfile(string coordinateSystem)
        {
            string fileName = coordinateSystem.IndexOf("LVHEF", StringComparison.OrdinalIgnoreCase) >= 0
                ? "UFLS-IMPORT-NV83.NCRS.LVHEF.ipf"
                : "UFLS-IMPORT-NV83.NCRS-LVF.ipf";
            return ResolveProfile(fileName);
        }

        public static string ResolvePipeExportProfile() => ResolveProfile("UFLS-PIPE.epf");
        public static string ResolveStructureExportProfile() => ResolveProfile("UFLS-STRC.epf");

        private static string ResolveProfile(string fileName)
        {
            var candidates = new List<string>();

            try
            {
                string asmFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(asmFolder))
                {
                    candidates.Add(Path.Combine(asmFolder, "Reference", "Network GIS", fileName));
                    candidates.Add(Path.Combine(asmFolder, "Network GIS", fileName));
                    candidates.Add(Path.Combine(asmFolder, fileName));
                }
            }
            catch
            {
            }

            candidates.Add(Path.Combine(Environment.CurrentDirectory, "Reference", "Network GIS", fileName));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "NETWORK GIS", fileName));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, fileName));
            candidates.Add(Path.Combine(@"W:\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP", fileName));

            foreach (string candidate in candidates.Where(File.Exists))
                return candidate;

            return candidates.First();
        }
    }

    internal static class GisScriptBuilder
    {
        public static string Build(GisWorkflowContext ctx, GisCurveReport curveReport, bool attemptBestEffortAutomation, out string readmeText)
        {
            string sdf = EscapePathForScript(ctx.SdfPath);
            string pipeShp = EscapePathForScript(ctx.PipeShpPath);
            string strcShp = EscapePathForScript(ctx.StructureShpPath);
            string ipf = EscapePathForScript(ctx.ImportProfilePath);
            string pipeEpf = EscapePathForScript(ctx.PipeExportProfilePath);
            string strcEpf = EscapePathForScript(ctx.StructureExportProfilePath);

            var sb = new StringBuilder();
            sb.AppendLine("FILEDIA 0");
            sb.AppendLine("CMDDIA 0");
            sb.AppendLine("-LAYER");
            sb.AppendLine("T");
            sb.AppendLine("0");
            sb.AppendLine();
            sb.AppendLine("_.EXPORTTOSDF");
            sb.AppendLine($"\"{sdf}\"");
            sb.AppendLine(ctx.CoordinateSystem);
            sb.AppendLine();

            if (curveReport.CurvedPipeCount > 0)
            {
                sb.AppendLine("; ------------------------------------------------------------");
                sb.AppendLine("; CURVED PIPE HOLD POINT");
                sb.AppendLine("; Exported SDF contains straight pipe features. Your project has");
                sb.AppendLine("; curved-pipe candidates, so pause here and perform the same");
                sb.AppendLine("; manual curve-fix workflow you currently use before continuing.");
                sb.AppendLine("; ------------------------------------------------------------");
                sb.AppendLine("PAUSE");
                sb.AppendLine();
            }

            sb.AppendLine("_.MAPIMPORT");
            sb.AppendLine($"\"{sdf}\"");
            sb.AppendLine($"\"{ipf}\"");
            sb.AppendLine();
            sb.AppendLine("_.MAPEXPORT");
            sb.AppendLine($"\"{pipeShp}\"");
            sb.AppendLine($"\"{pipeEpf}\"");
            sb.AppendLine();
            sb.AppendLine("_.MAPEXPORT");
            sb.AppendLine($"\"{strcShp}\"");
            sb.AppendLine($"\"{strcEpf}\"");
            sb.AppendLine();
            sb.AppendLine("FILEDIA 1");
            sb.AppendLine("CMDDIA 1");

            readmeText = BuildReadme(ctx, curveReport, attemptBestEffortAutomation);
            return sb.ToString();
        }

        private static string BuildReadme(GisWorkflowContext ctx, GisCurveReport curveReport, bool attemptBestEffortAutomation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CLV_CivilTools GIS Export Workflow");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();
            sb.AppendLine($"Project Number : {ctx.ProjectNumber}");
            sb.AppendLine($"Network Type   : {ctx.NetworkKind}");
            sb.AppendLine($"Coord. System  : {ctx.CoordinateSystem}");
            sb.AppendLine($"Drawing        : {ctx.DrawingPath}");
            sb.AppendLine($"SDF Output     : {ctx.SdfPath}");
            sb.AppendLine($"Pipe SHP       : {ctx.PipeShpPath}");
            sb.AppendLine($"Structure SHP  : {ctx.StructureShpPath}");
            sb.AppendLine();
            sb.AppendLine("Profiles");
            sb.AppendLine($"  Import : {ctx.ImportProfilePath}");
            sb.AppendLine($"  Pipe   : {ctx.PipeExportProfilePath}");
            sb.AppendLine($"  Strc   : {ctx.StructureExportProfilePath}");
            sb.AppendLine();
            sb.AppendLine($"Curved pipe candidates: {curveReport.CurvedPipeCount} / {curveReport.TotalPipeCount}");
            foreach (string name in curveReport.CurvedPipeNames)
                sb.AppendLine($"  - {name}");
            sb.AppendLine();
            sb.AppendLine("Notes");
            sb.AppendLine("- In your environment, EXPORTSDF opens a file dialog and then runs EXPORTTOSDF after you pick the SDF path.");
            sb.AppendLine("- Because of that, the generated .SCR file now starts AFTER SDF creation and only handles the follow-on MAPIMPORT / MAPEXPORT portion.");
            sb.AppendLine("- If AUTO-RUN is enabled, the command launches EXPORTSDF first and then queues the generated SCRIPT so it continues after the SDF is saved.");
            sb.AppendLine($"- Save the SDF dialog to this exact path: {ctx.SdfPath}");
            sb.AppendLine("- If curved pipes exist, the script inserts a PAUSE before MAPIMPORT / MAPEXPORT so you can run your current attach / convert-to-curve workflow first.");
            sb.AppendLine($"- Auto-run requested when command was launched: {attemptBestEffortAutomation}");
            return sb.ToString();
        }

        private static string EscapePathForScript(string path) => path.Replace("\"", "\"\"");
    }

    internal sealed class GisCurveReport
    {
        public int TotalPipeCount { get; set; }
        public int CurvedPipeCount { get; set; }
        public List<string> CurvedPipeNames { get; } = new List<string>();
    }

    internal static class GisCurveDetector
    {
        public static GisCurveReport Analyze(Database db)
        {
            var result = new GisCurveReport();

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout || btr.IsFromExternalReference)
                    continue;

                foreach (ObjectId id in btr)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);
                    if (!IsPipeObject(obj))
                        continue;

                    result.TotalPipeCount++;
                    if (IsLikelyCurvedPipe(obj))
                    {
                        result.CurvedPipeCount++;
                        result.CurvedPipeNames.Add(GetPipeDisplayName(obj, id));
                    }
                }
            }

            return result;
        }

        private static bool IsPipeObject(object obj)
        {
            string typeName = obj.GetType().FullName ?? obj.GetType().Name;
            return typeName.IndexOf("Civil.DatabaseServices.Pipe", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("AeccPipe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyCurvedPipe(object pipeObj)
        {
            if (TryGetBool(pipeObj, "IsCurve", out bool isCurve)) return isCurve;
            if (TryGetBool(pipeObj, "IsCurved", out bool isCurved)) return isCurved;
            if (TryGetDouble(pipeObj, "Bulge", out double bulge) && Math.Abs(bulge) > 1e-9) return true;
            if (TryGetDouble(pipeObj, "Radius", out double radius) && radius > 0.0) return true;

            if (TryGetPoint(pipeObj, "StartPoint", out Point3d start)
                && TryGetPoint(pipeObj, "EndPoint", out Point3d end)
                && TryGetDouble(pipeObj, "Length3DCenterToCenter", out double length3d)
                && length3d - start.DistanceTo(end) > 0.01)
            {
                return true;
            }

            if (TryGetDouble(pipeObj, "Length2DCenterToCenter", out double length2d)
                && TryGetPoint(pipeObj, "StartPoint", out start)
                && TryGetPoint(pipeObj, "EndPoint", out end)
                && length2d - new Point2d(start.X, start.Y).GetDistanceTo(new Point2d(end.X, end.Y)) > 0.01)
            {
                return true;
            }

            return false;
        }

        private static string GetPipeDisplayName(object pipeObj, ObjectId id)
        {
            if (TryGetString(pipeObj, "Name", out string? name) && !string.IsNullOrWhiteSpace(name))
                return name!;
            return id.Handle.ToString();
        }

        private static bool TryGetBool(object obj, string propertyName, out bool value)
        {
            value = false;
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != typeof(bool))
                return false;
            value = (bool)(prop.GetValue(obj) ?? false);
            return true;
        }

        private static bool TryGetDouble(object obj, string propertyName, out double value)
        {
            value = 0.0;
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                return false;

            object? raw = prop.GetValue(obj);
            if (raw == null)
                return false;

            switch (raw)
            {
                case double d:
                    value = d;
                    return true;
                case float f:
                    value = f;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                default:
                    return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        private static bool TryGetPoint(object obj, string propertyName, out Point3d value)
        {
            value = default;
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != typeof(Point3d))
                return false;
            object? raw = prop.GetValue(obj);
            if (raw is Point3d point)
            {
                value = point;
                return true;
            }
            return false;
        }

        private static bool TryGetString(object obj, string propertyName, out string? value)
        {
            value = null;
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                return false;
            value = prop.GetValue(obj) as string;
            return value != null;
        }
    }

    internal sealed class GisExportOptionsForm : Form
    {
        private readonly RadioButton _rbSewer;
        private readonly RadioButton _rbStorm;
        private readonly RadioButton _rbLvf;
        private readonly RadioButton _rbLvhef;
        private readonly CheckBox _chkAutoRun;

        public string SelectedNetworkKind => _rbStorm.Checked ? "STRM" : "SSWR";
        public string SelectedCoordinateSystem => _rbLvhef.Checked ? "NV83.NCRS-LVHEF" : "NV83.NCRS-LVF";
        public bool AttemptBestEffortAutomation
        {
            get => _chkAutoRun.Checked;
            set => _chkAutoRun.Checked = value;
        }

        public GisExportOptionsForm(Document doc)
        {
            string inferredPath = doc.Name.ToUpperInvariant();

            Text = "GIS EXPORT OPTIONS";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(430, 260);

            var grpNetwork = new GroupBox
            {
                Text = "NETWORK",
                Left = 12,
                Top = 12,
                Width = 190,
                Height = 90
            };
            _rbSewer = new RadioButton { Text = "SEWER (SSWR)", Left = 16, Top = 28, Width = 140, Checked = !inferredPath.Contains("STRM") };
            _rbStorm = new RadioButton { Text = "STORM (STRM)", Left = 16, Top = 52, Width = 140, Checked = inferredPath.Contains("STRM") };
            grpNetwork.Controls.Add(_rbSewer);
            grpNetwork.Controls.Add(_rbStorm);

            var grpCs = new GroupBox
            {
                Text = "COORDINATE SYSTEM",
                Left = 216,
                Top = 12,
                Width = 200,
                Height = 90
            };
            _rbLvf = new RadioButton { Text = "NV83.NCRS-LVF", Left = 16, Top = 28, Width = 150, Checked = !inferredPath.Contains("LVHEF") };
            _rbLvhef = new RadioButton { Text = "NV83.NCRS-LVHEF", Left = 16, Top = 52, Width = 160, Checked = inferredPath.Contains("LVHEF") };
            grpCs.Controls.Add(_rbLvf);
            grpCs.Controls.Add(_rbLvhef);

            var lblInfo = new Label
            {
                Left = 12,
                Top = 116,
                Width = 404,
                Height = 66,
                Text = "This tool derives CAD\\GIS and CAD\\GIS\\SHP paths from the current drawing path, builds the expected SDF / SHP names, checks for curved pipes, and generates the follow-on SCRIPT for MAPIMPORT -> MAPEXPORT after EXPORTSDF creates the SDF file."
            };

            _chkAutoRun = new CheckBox
            {
                Left = 16,
                Top = 190,
                Width = 360,
                Height = 24,
                Text = "AUTO-RUN GENERATED SCRIPT (BEST EFFORT)",
                Checked = true
            };

            var btnOk = new Button { Text = "OK", Left = 250, Top = 222, Width = 80, DialogResult = DialogResult.OK, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            var btnCancel = new Button { Text = "CANCEL", Left = 336, Top = 222, Width = 80, DialogResult = DialogResult.Cancel, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.Add(grpNetwork);
            Controls.Add(grpCs);
            Controls.Add(lblInfo);
            Controls.Add(_chkAutoRun);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }
    }
}
