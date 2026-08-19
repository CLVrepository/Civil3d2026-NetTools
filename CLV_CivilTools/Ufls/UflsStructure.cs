using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;

using Autodesk.AutoCAD.ApplicationServices;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// 2D structure footprint creation and related plan / section linework helpers.
    /// UFLS7   = trace INNER wall, auto-create OUTER wall.
    /// UFLS8   = trace OUTER wall, auto-create INNER wall.
    /// UFLS7PC = trace INNER wall using point-cloud snap settings.
    /// UFLS-3PCIRCLE = create 3-point circle on layer 0 with pick markers.
    /// UFLS-3PRECT   = create 3-point orthogonal rectangle on layer 0 with pick markers.
    /// </summary>
    public static class UflsStructureCommands
    {
        private const string LayerOuter = "V-SURV-STRC-OUTR-2D~~";
        private const string LayerInner = "V-SURV-STRC-INNR-2D~~";
        private const string LayerPickMarker = "V-TEMP-PICKMARK";
        private const string LayerZero = "0";

        private const short StructureColorIndex = 141;
        private const string OuterLinetype = "CONTINUOUS";
        private const string InnerLinetype = "HIDDEN4";
        private const string PlotStyleName = "M";

        private static double _lastWallThickness = 0.8333; // matches legacy Type II (USD 405)

        [CommandMethod("UFLS", "UFLS7", CommandFlags.Modal)]
        public static void Ufls7_TraceInnerWall()
        {
            RunStructureFootprintWorkflow(traceInnerWall: true, usePointCloudSnaps: false, commandLabel: "UFLS7");
        }

        [CommandMethod("UFLS", "UFLS8", CommandFlags.Modal)]
        public static void Ufls8_TraceOuterWall()
        {
            RunStructureFootprintWorkflow(traceInnerWall: false, usePointCloudSnaps: false, commandLabel: "UFLS8");
        }

        [CommandMethod("UFLS", "UFLS7PC", CommandFlags.Modal)]
        public static void Ufls7Pc_TraceInnerWall_PointCloud()
        {
            RunStructureFootprintWorkflow(traceInnerWall: true, usePointCloudSnaps: true, commandLabel: "UFLS7PC");
        }

        [CommandMethod("UFLS", "UFLS-3PCIRCLE", CommandFlags.Modal)]
        public static void Ufls3PointCircle()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var markerIds = new List<ObjectId>();

            try
            {
                using (doc.LockDocument())
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode: 0, osnapZ: 0, osMode3d: 0);
                    EnsurePickMarkerLayer(db);

                    List<Point3d> pts = CollectFixedPointCount(ed, db, markerIds, 3, "3P circle", "Snaps are OFF.");
                    if (pts.Count != 3)
                    {
                        ed.WriteMessage("\nUFLS-3PCIRCLE: Command cancelled.");
                        return;
                    }

                    if (!GeometryUtils.TryFitCircle2DFrom3Points(pts[0], pts[1], pts[2], out Point3d centerUcs, out double radius))
                    {
                        ed.WriteMessage("\nUFLS-3PCIRCLE: Points are colinear or invalid for a circle.");
                        return;
                    }

                    Point3d centerWcs = new Point3d(centerUcs.X, centerUcs.Y, 0.0);

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        var circle = new Circle(centerWcs, Vector3d.ZAxis, radius)
                        {
                            Layer = LayerZero
                        };

                        ms.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);

                        EraseMarkers(tr, markerIds);
                        tr.Commit();
                    }

                    ed.WriteMessage("\nUFLS-3PCIRCLE: Circle created on layer 0.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-3PCIRCLE error: {ex.Message}");
                TryCleanupMarkers(db, markerIds);
            }
        }

        [CommandMethod("UFLS", "UFLS-3PRECT", CommandFlags.Modal)]
        public static void Ufls3PointRectangle()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var markerIds = new List<ObjectId>();

            try
            {
                using (doc.LockDocument())
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode: 0, osnapZ: 0, osMode3d: 0);
                    EnsurePickMarkerLayer(db);

                    List<Point3d> pts = CollectFixedPointCount(
                        ed,
                        db,
                        markerIds,
                        3,
                        "3P rectangle",
                        "Snaps are OFF. Pick 2 points for one side, then a 3rd point for width / side direction.");

                    if (pts.Count != 3)
                    {
                        ed.WriteMessage("\nUFLS-3PRECT: Command cancelled.");
                        return;
                    }

                    if (!TryBuildRectangleCornersInUcs(pts[0], pts[1], pts[2], out Point3d r1, out Point3d r2, out Point3d r3, out Point3d r4))
                    {
                        ed.WriteMessage("\nUFLS-3PRECT: Unable to create a square 90-degree rectangle from those points.");
                        return;
                    }

                    Point3d w1 = new Point3d(r1.X, r1.Y, 0.0);
                    Point3d w2 = new Point3d(r2.X, r2.Y, 0.0);
                    Point3d w3 = new Point3d(r3.X, r3.Y, 0.0);
                    Point3d w4 = new Point3d(r4.X, r4.Y, 0.0);

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        Polyline rect = BuildClosedPolyline(new[] { w1, w2, w3, w4 });
                        rect.Layer = LayerZero;
                        ms.AppendEntity(rect);
                        tr.AddNewlyCreatedDBObject(rect, true);

                        EraseMarkers(tr, markerIds);
                        tr.Commit();
                    }

                    ed.WriteMessage("\nUFLS-3PRECT: Rectangle created on layer 0.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-3PRECT error: {ex.Message}");
                TryCleanupMarkers(db, markerIds);
            }
        }

        private static void RunStructureFootprintWorkflow(bool traceInnerWall, bool usePointCloudSnaps, string commandLabel)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            string tracedLabel = traceInnerWall ? "INSIDE" : "OUTSIDE";
            var markerIds = new List<ObjectId>();

            try
            {
                using (doc.LockDocument())
                {
                    if (usePointCloudSnaps)
                    {
                        using var snap = SnapState.Capture();
                        snap.Set(osMode: 0, osnapZ: 0, osMode3d: 128);
                        ExecuteStructureTrace(db, ed, markerIds, traceInnerWall, tracedLabel, commandLabel, "Point-cloud 3D osnaps are ON.");
                    }
                    else
                    {
                        short originalOsMode = Convert.ToInt16(AcadApp.GetSystemVariable("OSMODE"), CultureInfo.InvariantCulture);
                        try
                        {
                            AcadApp.SetSystemVariable("OSMODE", 8); // NODE only, same intent as legacy LISP
                            ExecuteStructureTrace(db, ed, markerIds, traceInnerWall, tracedLabel, commandLabel, "Node snap is ON.");
                        }
                        finally
                        {
                            try
                            {
                                AcadApp.SetSystemVariable("OSMODE", originalOsMode);
                            }
                            catch
                            {
                                // ignore restore failure
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{commandLabel} error: {ex.Message}");
                TryCleanupMarkers(db, markerIds);
            }
        }

        private static void ExecuteStructureTrace(
            Database db,
            Editor ed,
            List<ObjectId> markerIds,
            bool traceInnerWall,
            string tracedLabel,
            string commandLabel,
            string snapModeMessage)
        {
            EnsureSupportLayers(db);

            List<Point3d> pickedPoints = CollectFootprintPoints(ed, db, markerIds, tracedLabel, snapModeMessage);
            if (pickedPoints.Count < 3)
            {
                ed.WriteMessage($"\n{commandLabel}: Need at least 3 points to form a closed shape.");
                return;
            }

            double? wallThickness = StructureWallTypeForm.PromptForThickness(_lastWallThickness);
            if (!wallThickness.HasValue)
            {
                ed.WriteMessage($"\n{commandLabel}: Cancelled.");
                return;
            }

            _lastWallThickness = wallThickness.Value;

            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            EnsureStructureLayers(db, tr);

            Polyline tracedPolyline = BuildClosedPolyline(pickedPoints);
            tracedPolyline.Layer = traceInnerWall ? LayerInner : LayerOuter;
            ms.AppendEntity(tracedPolyline);
            tr.AddNewlyCreatedDBObject(tracedPolyline, true);

            Polyline? offsetPolyline = CreateOffsetMate(tracedPolyline, wallThickness.Value, traceInnerWall);
            if (offsetPolyline == null)
            {
                throw new InvalidOperationException("Unable to create offset structure wall from the picked footprint.");
            }

            offsetPolyline.Layer = traceInnerWall ? LayerOuter : LayerInner;
            ms.AppendEntity(offsetPolyline);
            tr.AddNewlyCreatedDBObject(offsetPolyline, true);

            EraseMarkers(tr, markerIds);
            tr.Commit();

            ed.WriteMessage(
                $"\n{commandLabel}: {tracedLabel} wall traced and matching {(traceInnerWall ? "OUTSIDE" : "INSIDE")} wall created. Wall thickness = {wallThickness.Value:0.####} ft.");
        }

        private static List<Point3d> CollectFootprintPoints(Editor ed, Database db, List<ObjectId> markerIds, string tracedLabel, string snapModeMessage)
        {
            var points = new List<Point3d>();

            ed.WriteMessage($"\nPick points to define the {tracedLabel} wall footprint. {snapModeMessage} Press Enter when done.");

            double markerRadius = GetMarkerRadius();

            while (true)
            {
                PromptPointOptions ppo = points.Count == 0
                    ? new PromptPointOptions("\nPick first point <Enter to cancel>: ")
                    : new PromptPointOptions("\nPick next point <Enter to finish>: ");

                ppo.AllowNone = true;

                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status == PromptStatus.Cancel)
                {
                    points.Clear();
                    break;
                }

                if (ppr.Status == PromptStatus.None)
                {
                    break;
                }

                if (ppr.Status != PromptStatus.OK)
                {
                    break;
                }

                Point3d flat = new Point3d(ppr.Value.X, ppr.Value.Y, 0.0);
                points.Add(flat);
                CreateMarkerCircle(db, flat, markerRadius, markerIds);
            }

            return points;
        }

        private static List<Point3d> CollectFixedPointCount(
            Editor ed,
            Database db,
            List<ObjectId> markerIds,
            int pointCount,
            string toolLabel,
            string introMessage)
        {
            var points = new List<Point3d>();
            ed.WriteMessage($"\n{toolLabel}: {introMessage}");

            double markerRadius = GetMarkerRadius();

            for (int i = 1; i <= pointCount; i++)
            {
                PromptPointResult ppr = ed.GetPoint($"\nPick point {i} of {pointCount}: ");
                if (ppr.Status != PromptStatus.OK)
                {
                    points.Clear();
                    break;
                }

                Point3d flat = new Point3d(ppr.Value.X, ppr.Value.Y, 0.0);
                points.Add(flat);
                CreateMarkerCircle(db, flat, markerRadius, markerIds);
            }

            return points;
        }

        private static double GetMarkerRadius()
        {
            try
            {
                double viewSize = Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"), CultureInfo.InvariantCulture);
                return Math.Max(viewSize * 0.01, 0.05);
            }
            catch
            {
                return 1.0;
            }
        }

        private static void CreateMarkerCircle(Database db, Point3d center, double radius, List<ObjectId> markerIds)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var circle = new Circle(center, Vector3d.ZAxis, radius)
            {
                Layer = LayerPickMarker
            };

            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
            markerIds.Add(circle.ObjectId);
            tr.Commit();
        }

        private static Polyline BuildClosedPolyline(IReadOnlyList<Point3d> points)
        {
            var pline = new Polyline();

            for (int i = 0; i < points.Count; i++)
            {
                pline.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0.0, 0.0, 0.0);
            }

            pline.Closed = true;
            return pline;
        }

        private static Polyline? CreateOffsetMate(Polyline source, double distance, bool tracedInnerWall)
        {
            Polyline? positive = CloneOffsetPolyline(source, distance);
            Polyline? negative = CloneOffsetPolyline(source, -distance);

            double sourceArea = GetAbsArea(source);

            if (tracedInnerWall)
            {
                return ChooseByArea(sourceArea, wantLarger: true, positive, negative);
            }

            return ChooseByArea(sourceArea, wantLarger: false, positive, negative);
        }

        private static Polyline? ChooseByArea(double sourceArea, bool wantLarger, Polyline? a, Polyline? b)
        {
            var candidates = new List<Polyline>();
            if (a != null) candidates.Add(a);
            if (b != null) candidates.Add(b);

            if (candidates.Count == 0)
                return null;

            Polyline? preferred = null;
            double bestDelta = double.MaxValue;

            foreach (Polyline candidate in candidates)
            {
                double area = GetAbsArea(candidate);
                bool matches = wantLarger ? area > sourceArea : area < sourceArea;
                if (!matches)
                    continue;

                double delta = Math.Abs(area - sourceArea);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    preferred = candidate;
                }
            }

            if (preferred != null)
            {
                foreach (Polyline candidate in candidates)
                {
                    if (!ReferenceEquals(candidate, preferred))
                        candidate.Dispose();
                }

                return preferred;
            }

            Polyline fallback = candidates
                .OrderByDescending(c => wantLarger ? GetAbsArea(c) : -GetAbsArea(c))
                .First();

            foreach (Polyline candidate in candidates)
            {
                if (!ReferenceEquals(candidate, fallback))
                    candidate.Dispose();
            }

            return fallback;
        }

        private static Polyline? CloneOffsetPolyline(Polyline source, double distance)
        {
            try
            {
                DBObjectCollection curves = source.GetOffsetCurves(distance);
                foreach (DBObject dbo in curves)
                {
                    if (dbo is Polyline pline)
                    {
                        pline.Closed = true;
                        return pline;
                    }

                    dbo.Dispose();
                }
            }
            catch (AcException)
            {
                // geometric offset occasionally fails on irregular input; caller handles null
            }

            return null;
        }

        private static double GetAbsArea(Polyline pline)
        {
            try
            {
                return Math.Abs(pline.Area);
            }
            catch
            {
                return 0.0;
            }
        }

        private static bool TryBuildRectangleCornersInUcs(
            Point3d p1,
            Point3d p2,
            Point3d p3,
            out Point3d r1,
            out Point3d r2,
            out Point3d r3,
            out Point3d r4)
        {
            r1 = Point3d.Origin;
            r2 = Point3d.Origin;
            r3 = Point3d.Origin;
            r4 = Point3d.Origin;

            Vector3d baseVec = new Vector3d(p2.X - p1.X, p2.Y - p1.Y, 0.0);
            if (baseVec.Length < 1e-9)
                return false;

            Vector3d baseDir = baseVec.GetNormal();
            Vector3d perpDir = new Vector3d(-baseDir.Y, baseDir.X, 0.0);
            Vector3d toThird = new Vector3d(p3.X - p1.X, p3.Y - p1.Y, 0.0);
            double width = toThird.DotProduct(perpDir);
            if (Math.Abs(width) < 1e-9)
                return false;

            const double z = 0.0;

            r1 = new Point3d(p1.X, p1.Y, z);
            r2 = new Point3d(p2.X, p2.Y, z);
            r3 = new Point3d(p2.X + perpDir.X * width, p2.Y + perpDir.Y * width, z);
            r4 = new Point3d(p1.X + perpDir.X * width, p1.Y + perpDir.Y * width, z);
            return true;
        }

        private static void AppendLine(BlockTableRecord ms, Transaction tr, Point3d start, Point3d end, string layer)
        {
            var line = new Line(start, end)
            {
                Layer = layer
            };

            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void EnsureSupportLayers(Database db)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            EnsureLayer(db, tr, LayerPickMarker, 6, "CONTINUOUS", null, true);
            EnsureStructureLayers(db, tr);
            tr.Commit();
        }

        private static void EnsurePickMarkerLayer(Database db)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            EnsureLayer(db, tr, LayerPickMarker, 6, "CONTINUOUS", null, true);
            tr.Commit();
        }

        private static void EnsureStructureLayers(Database db, Transaction tr)
        {
            EnsureLayer(db, tr, LayerOuter, StructureColorIndex, OuterLinetype, PlotStyleName, false);
            EnsureLayer(db, tr, LayerInner, StructureColorIndex, InnerLinetype, PlotStyleName, false);
        }

        private static void EnsureLayer(
            Database db,
            Transaction tr,
            string layerName,
            short colorIndex,
            string linetypeName,
            string? plotStyleName,
            bool forceMagenta)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord ltr;

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = layerName };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            }

            ltr.Color = forceMagenta
                ? AcColor.FromRgb(255, 0, 255)
                : AcColor.FromColorIndex(ColorMethod.ByAci, colorIndex);

            ObjectId linetypeId = GetOrLoadLinetypeId(db, tr, linetypeName);
            if (!linetypeId.IsNull)
            {
                ltr.LinetypeObjectId = linetypeId;
            }

            if (!string.IsNullOrWhiteSpace(plotStyleName))
            {
                TryAssignNamedPlotStyle(db, tr, ltr, plotStyleName);
            }
        }

        private static ObjectId GetOrLoadLinetypeId(Database db, Transaction tr, string linetypeName)
        {
            LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                return ltt[linetypeName];

            try
            {
                db.LoadLineTypeFile(linetypeName, "acad.lin");
            }
            catch
            {
                // ignore load failures, will fall back below
            }

            ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                return ltt[linetypeName];

            return ltt.Has("Continuous") ? ltt["Continuous"] : ObjectId.Null;
        }

        private static void TryAssignNamedPlotStyle(Database db, Transaction tr, LayerTableRecord ltr, string plotStyleName)
        {
            try
            {
                DBDictionary psDict = (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
                if (psDict.Contains(plotStyleName))
                {
                    ltr.PlotStyleNameId = psDict.GetAt(plotStyleName);
                }
            }
            catch
            {
                // drawing may not support named plot styles or dictionary access may vary; safe to skip
            }
        }

        private static void EraseMarkers(Transaction tr, IEnumerable<ObjectId> markerIds)
        {
            foreach (ObjectId id in markerIds)
            {
                if (id.IsNull || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForWrite, false) is AcEntity ent && !ent.IsErased)
                {
                    ent.Erase();
                }
            }
        }

        private static void TryCleanupMarkers(Database db, IEnumerable<ObjectId> markerIds)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EraseMarkers(tr, markerIds);
                tr.Commit();
            }
            catch
            {
                // ignore cleanup failure
            }
        }
    }

    internal sealed class StructureWallTypeForm : Form
    {
        private readonly TextBox _txtWallThickness;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        private double? _selectedThickness;

        private StructureWallTypeForm(double defaultThickness)
        {
            Text = "UFLS Structure Type";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 260);

            var group = new GroupBox
            {
                Text = "Select or Enter Wall Thickness",
                Location = new Point(12, 12),
                Size = new Size(376, 185)
            };

            Button btnType2A = CreatePresetButton("Type II (USD 405)", 0.8333, 16, 28, 340);
            Button btnType2B = CreatePresetButton("Type II (USD 405.2)", 1.0, 16, 62, 340);
            Button btnType3 = CreatePresetButton("Type III (USD 406)", 0.9167, 16, 96, 340);

            var lblManual = new Label
            {
                Text = "Manual wall thickness (ft):",
                AutoSize = true,
                Location = new Point(16, 140)
            };

            _txtWallThickness = new TextBox
            {
                Location = new Point(190, 136),
                Width = 150,
                Text = defaultThickness > 0
                    ? defaultThickness.ToString("0.####", CultureInfo.InvariantCulture)
                    : string.Empty
            };

            group.Controls.Add(btnType2A);
            group.Controls.Add(btnType2B);
            group.Controls.Add(btnType3);
            group.Controls.Add(lblManual);
            group.Controls.Add(_txtWallThickness);

            _btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.None,
                Location = new Point(228, 212),
                Width = 75
            };
            _btnOk.Click += OnOkClick;

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(313, 212),
                Width = 75
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.Add(group);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);
        }

        private Button CreatePresetButton(string text, double thickness, int x, int y, int width)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 28)
            };

            btn.Click += (_, _) =>
            {
                _txtWallThickness.Text = thickness.ToString("0.####", CultureInfo.InvariantCulture);
                _txtWallThickness.Focus();
                _txtWallThickness.SelectAll();
            };

            return btn;
        }

        private void OnOkClick(object? sender, EventArgs e)
        {
            if (!double.TryParse(_txtWallThickness.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            {
                MessageBox.Show(this, "Enter a valid positive wall thickness in feet.", "Invalid Wall Thickness", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _selectedThickness = value;
            DialogResult = DialogResult.OK;
            Close();
        }

        public static double? PromptForThickness(double defaultThickness)
        {
            using var form = new StructureWallTypeForm(defaultThickness);
            DialogResult result = AcadApp.ShowModalDialog(form);
            return result == DialogResult.OK ? form._selectedThickness : null;
        }
    }
}
