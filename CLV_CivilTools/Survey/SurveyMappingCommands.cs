using System;
using System.Globalization;

using CLV_CivilTools.Shared;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Survey
{
    public static class SurveyMappingCommands
    {
        private const double DefaultAreaLabelTextHeight = 8.0;

        [CommandMethod("SURVEY-DRAW-TIE-LINE", CommandFlags.Modal)]
        [CommandMethod("DRAWTIELINE", CommandFlags.Modal)]
        [CommandMethod("Q4TIE", CommandFlags.Modal)]
        public static void RunDrawTieLine()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            string previousLayerName = GetCurrentLayerName(db);

            try
            {
                LayerStandards.EnsureSurveyTieLineLayer(db, ed);
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(LayerStandards.SurveyTieLineLayerName))
                        db.Clayer = lt[LayerStandards.SurveyTieLineLayerName];
                    tr.Commit();
                }

                RegisterPendingTieLineLayerRestore(doc, previousLayerName);
                ed.WriteMessage($"\nDRAW TIE LINE: current layer set to {LayerStandards.SurveyTieLineLayerName}. Starting LINE command. Current layer will restore to '{previousLayerName}' when LINE ends.");
                doc.SendStringToExecute("_.LINE ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ClearPendingTieLineLayerRestore(doc);
                RestoreCurrentLayer(db, ed, previousLayerName);
                ed.WriteMessage($"\nDRAW TIE LINE error: {ex.Message}");
            }
        }

        private sealed class PendingTieLineLayerRestore
        {
            internal PendingTieLineLayerRestore(string previousLayerName)
            {
                PreviousLayerName = previousLayerName;
            }

            internal string PreviousLayerName { get; }
        }

        private static readonly System.Collections.Generic.Dictionary<Document, PendingTieLineLayerRestore> PendingTieLineLayerRestores =
            new System.Collections.Generic.Dictionary<Document, PendingTieLineLayerRestore>();

        private static void RegisterPendingTieLineLayerRestore(Document doc, string previousLayerName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(previousLayerName))
                return;

            ClearPendingTieLineLayerRestore(doc);
            PendingTieLineLayerRestores[doc] = new PendingTieLineLayerRestore(previousLayerName);
            doc.CommandEnded += OnTrackedTieLineCommandFinished;
            doc.CommandCancelled += OnTrackedTieLineCommandFinished;
            doc.CommandFailed += OnTrackedTieLineCommandFinished;
        }

        private static void ClearPendingTieLineLayerRestore(Document? doc)
        {
            if (doc == null)
                return;

            doc.CommandEnded -= OnTrackedTieLineCommandFinished;
            doc.CommandCancelled -= OnTrackedTieLineCommandFinished;
            doc.CommandFailed -= OnTrackedTieLineCommandFinished;
            PendingTieLineLayerRestores.Remove(doc);
        }

        private static void OnTrackedTieLineCommandFinished(object sender, CommandEventArgs e)
        {
            Document? doc = sender as Document;
            if (doc == null)
                return;

            if (!PendingTieLineLayerRestores.TryGetValue(doc, out PendingTieLineLayerRestore? pending))
            {
                ClearPendingTieLineLayerRestore(doc);
                return;
            }

            string finishedCommand = NormalizeCommandName(e.GlobalCommandName);
            if (IsTieLineWrapperCommand(finishedCommand))
                return;

            if (!string.Equals(finishedCommand, "LINE", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                RestoreCurrentLayer(doc.Database, doc.Editor, pending.PreviousLayerName);
                doc.Editor.WriteMessage($"\nDRAW TIE LINE: current layer restored to '{pending.PreviousLayerName}'.");
            }
            finally
            {
                ClearPendingTieLineLayerRestore(doc);
            }
        }

        private static bool IsTieLineWrapperCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            return string.Equals(commandName, "SURVEY-DRAW-TIE-LINE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "DRAWTIELINE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "Q4TIE", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCommandName(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            string normalized = command.Trim();
            while (normalized.Length > 0 && (normalized[0] == '.' || normalized[0] == '_'))
                normalized = normalized.Substring(1);

            int firstSpace = normalized.IndexOf(' ');
            if (firstSpace >= 0)
                normalized = normalized.Substring(0, firstSpace);

            return normalized.ToUpperInvariant();
        }

        private static string GetCurrentLayerName(Database db)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
            string layerName = ltr.Name;
            tr.Commit();
            return layerName;
        }

        private static void RestoreCurrentLayer(Database db, Editor ed, string layerName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            if (string.IsNullOrWhiteSpace(layerName))
                return;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(layerName))
                    db.Clayer = lt[layerName];
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nDRAW TIE LINE: unable to restore current layer to '{layerName}': {ex.Message}");
            }
        }

        [CommandMethod("SURVEY-AREA-SF-LABEL", CommandFlags.Modal)]
        [CommandMethod("AREASFLABEL", CommandFlags.Modal)]
        [CommandMethod("Q4AREASF", CommandFlags.Modal)]
        public static void RunAreaSquareFootLabel()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptPointOptions ppo = new PromptPointOptions("\nPick a point inside a closed boundary to label area SF: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                    return;

                DBObjectCollection traced = ed.TraceBoundary(ppr.Value, false);
                if (traced == null || traced.Count == 0)
                    throw new InvalidOperationException("No closed boundary was found around the picked point.");

                using DBObjectCollectionHolder holder = new DBObjectCollectionHolder(traced);
                Curve? bestCurve = null;
                double bestArea = 0.0;

                foreach (DBObject obj in traced)
                {
                    if (obj is Curve curve && TryGetClosedCurveArea(curve, out double area) && area > bestArea)
                    {
                        bestArea = area;
                        bestCurve = curve;
                    }
                }

                if (bestCurve == null || bestArea <= 0.0)
                    throw new InvalidOperationException("The traced boundary did not produce a usable closed curve area.");

                Point3d labelPoint = GetLabelPoint(bestCurve, ppr.Value);
                string label = $"{FormatSquareFeet(bestArea)} SF";

                LayerStandards.EnsureSurveyAreaLabelLayer(db, ed);
                using DocumentLock docLock = doc.LockDocument();
                using Transaction tr = db.TransactionManager.StartTransaction();

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                MText text = new MText
                {
                    Contents = label,
                    Location = labelPoint,
                    TextHeight = DefaultAreaLabelTextHeight,
                    Attachment = AttachmentPoint.MiddleCenter
                };
                text.SetDatabaseDefaults();
                text.Layer = LayerStandards.SurveyAreaLabelLayerName;

                ms.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);
                tr.Commit();

                ed.WriteMessage($"\nAREA SF LABEL: placed '{label}' on layer {LayerStandards.SurveyAreaLabelLayerName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nAREA SF LABEL error: {ex.Message}");
            }
        }

        private static bool TryGetClosedCurveArea(Curve curve, out double area)
        {
            area = 0.0;
            if (curve == null || !curve.Closed)
                return false;

            switch (curve)
            {
                case Polyline pl:
                    area = Math.Abs(pl.Area);
                    return area > 0.0;
                case Circle circle:
                    area = Math.PI * circle.Radius * circle.Radius;
                    return area > 0.0;
                default:
                    return false;
            }
        }

        private static Point3d GetLabelPoint(Curve curve, Point3d fallback)
        {
            if (curve is Polyline pl && TryGetPolylineCentroid(pl, out Point3d centroid))
                return centroid;

            try
            {
                Extents3d ext = curve.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryGetPolylineCentroid(Polyline pl, out Point3d centroid)
        {
            centroid = Point3d.Origin;
            int count = pl.NumberOfVertices;
            if (count < 3)
                return false;

            double signedArea2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;

            for (int i = 0; i < count; i++)
            {
                Point2d p0 = pl.GetPoint2dAt(i);
                Point2d p1 = pl.GetPoint2dAt((i + 1) % count);
                double cross = (p0.X * p1.Y) - (p1.X * p0.Y);
                signedArea2 += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }

            if (Math.Abs(signedArea2) < 1e-8)
                return false;

            centroid = new Point3d(cx / (3.0 * signedArea2), cy / (3.0 * signedArea2), pl.Elevation);
            return true;
        }

        private static string FormatSquareFeet(double area)
        {
            return Math.Round(area, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
        }

        private sealed class DBObjectCollectionHolder : IDisposable
        {
            private readonly DBObjectCollection _items;

            internal DBObjectCollectionHolder(DBObjectCollection items)
            {
                _items = items;
            }

            public void Dispose()
            {
                foreach (DBObject obj in _items)
                {
                    obj.Dispose();
                }
            }
        }
    }
}
