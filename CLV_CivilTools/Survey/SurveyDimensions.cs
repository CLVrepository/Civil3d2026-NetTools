using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using CLV_CivilTools.Shared;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Survey
{
    public static class SurveyDimensionCommands
    {
        internal const string DimLayerName = "V-ANNO-DIMS";
        internal const string ArrowDimStyleName = "CLV - ARROW";
        internal const string AngleDegreesDimStyleName = "CLV - ANGLE - DEGREES";
        internal const string AngleSecondsDimStyleName = "CLV - ANGLE - SECONDS";
        private const string DimTemplatePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Reference Templates\Settings (2026).dwt";


        [CommandMethod("SURVEY-LC-LABEL-2POINT", CommandFlags.Modal)]
        [CommandMethod("2-POINT", CommandFlags.Modal)]
        [CommandMethod("Q42POINT", CommandFlags.Modal)]
        public static void RunLineCurveLabelTwoPoint()
        {
            RunNativeLineCurveLabelCommand("_.ADDLINEBETWEENPOINTS", "2-POINT  ||  BEARING AND DIST", "Line and Curve / Line Between Two Points", "R26_Bearing + Distance");
        }

        [CommandMethod("SURVEY-LC-LABEL-2POINT-DIST", CommandFlags.Modal)]
        [CommandMethod("Q42POINTDIST", CommandFlags.Modal)]
        public static void RunLineCurveLabelTwoPointDistance()
        {
            RunNativeLineCurveLabelCommand("_.ADDLINEBETWEENPOINTS", "2-POINT  ||  DIST", "Line and Curve / Line Between Two Points", "R26_Distance");
        }

        [CommandMethod("SURVEY-LC-LABEL-BEARING-DISTANCE", CommandFlags.Modal)]
        [CommandMethod("SURVEY-BEARING-DISTANCE", CommandFlags.Modal)]
        [CommandMethod("BEARINGANDDISTANCE", CommandFlags.Modal)]
        [CommandMethod("Q4BDIST", CommandFlags.Modal)]
        public static void RunLineCurveLabelBearingDistance()
        {
            RunNativeLineCurveLabelCommand("_.ADDSEGMENTLABEL", "BEARING AND DIST", "Line and Curve / Single Segment", "R26_Bearing + Distance");
        }

        [CommandMethod("SURVEY-LC-LABEL-DISTANCE", CommandFlags.Modal)]
        [CommandMethod("SURVEY-SEGMENT-DISTANCE", CommandFlags.Modal)]
        [CommandMethod("Q4LCDIST", CommandFlags.Modal)]
        public static void RunLineCurveLabelDistance()
        {
            RunNativeLineCurveLabelCommand("_.ADDSEGMENTLABEL", "DISTANCE", "Line and Curve / Single Segment", "R26_Distance");
        }

        [CommandMethod("SURVEY-DIM-DISTANCE", CommandFlags.Modal)]
        [CommandMethod("Q4DIST", CommandFlags.Modal)]
        public static void RunDistanceDim()
        {
            RunNativeDimensionCommand("_.DIMALIGNED", ArrowDimStyleName, "DISTANCE");
        }

        [CommandMethod("SURVEY-DIM-ANGLE-DEGREES", CommandFlags.Modal)]
        [CommandMethod("Q4ANGDEG", CommandFlags.Modal)]
        public static void RunAngleDegreesDim()
        {
            RunNativeDimensionCommand("_.DIMANGULAR", AngleDegreesDimStyleName, "ANGLE - DEGREES");
        }

        [CommandMethod("SURVEY-DIM-ANGLE-SECONDS", CommandFlags.Modal)]
        [CommandMethod("Q4ANGSEC", CommandFlags.Modal)]
        public static void RunAngleSecondsDim()
        {
            RunNativeDimensionCommand("_.DIMANGULAR", AngleSecondsDimStyleName, "ANGLE - SECONDS");
        }

        [CommandMethod("SURVEY-DIM-RADIUS", CommandFlags.Modal)]
        [CommandMethod("Q4RADIUS", CommandFlags.Modal)]
        public static void RunRadiusDim()
        {
            RunNativeDimensionCommand("_.DIMRADIUS", AngleDegreesDimStyleName, "RADIUS");
        }

        [CommandMethod("SURVEY-DIM-OFFSET", CommandFlags.Modal)]
        [CommandMethod("Q4OFFSET", CommandFlags.Modal)]
        public static void RunOffsetDim()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            string previousLayerName = GetCurrentLayerName(db);

            try
            {
                LayerStandards.EnsureSurveyDimLayer(db, ed);

                using DocumentLock docLock = doc.LockDocument();
                ObjectId dimStyleId = EnsureDimensionStyleAvailable(db, ed, ArrowDimStyleName);
                if (dimStyleId.IsNull)
                    throw new InvalidOperationException($"Dimension style '{ArrowDimStyleName}' was not found and could not be imported.");

                using Transaction tr = db.TransactionManager.StartTransaction();
                PrepareCurrentDimEnvironment(db, ed, tr, ArrowDimStyleName);

                SelectedCurve first = PromptForCurve(ed, "\nSelect first line / polyline for offset label: ");
                if (!first.IsValid)
                    return;

                SelectedCurve second = PromptForCurve(ed, "\nSelect second line / polyline for offset label: ");
                if (!second.IsValid)
                    return;

                Curve curve1 = (Curve)tr.GetObject(first.ObjectId, OpenMode.ForRead);
                Curve curve2 = (Curve)tr.GetObject(second.ObjectId, OpenMode.ForRead);

                if (!TryResolveOffsetPoints(curve1, first.PickPoint, curve2, second.PickPoint, out Point3d x1, out Point3d x2))
                    throw new InvalidOperationException("Unable to determine a valid offset between the selected objects.");

                if (x1.DistanceTo(x2) <= Tolerance.Global.EqualPoint)
                    throw new InvalidOperationException("Selected objects resolve to zero offset at the picked location.");

                PromptPointOptions ppo = new PromptPointOptions("\nPick dimension line location: ")
                {
                    UseBasePoint = true,
                    BasePoint = MidPoint(x1, x2)
                };
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                    return;

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                AlignedDimension dim = new AlignedDimension(x1, x2, ppr.Value, string.Empty, dimStyleId);
                dim.SetDatabaseDefaults();
                dim.Layer = DimLayerName;
                dim.DimensionStyle = dimStyleId;
                dim.Normal = Vector3d.ZAxis;
                dim.Annotative = AnnotativeStates.True;

                ms.AppendEntity(dim);
                tr.AddNewlyCreatedDBObject(dim, true);

                RestoreCurrentLayer(db, tr, previousLayerName);
                tr.Commit();
                ed.WriteMessage($"\nSURVEY-DIM-OFFSET: created offset dimension on layer {DimLayerName} using style '{ArrowDimStyleName}'. Current layer restored to '{previousLayerName}'.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-DIM-OFFSET error: {ex.Message}");
            }
            finally
            {
                RestoreCurrentLayer(db, ed, previousLayerName);
            }
        }

        internal static void PrepareCurrentDimEnvironment(Database db, Editor ed, Transaction tr, string dimStyleName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            ObjectId dimStyleId = EnsureDimensionStyleAvailable(db, ed, dimStyleName);
            if (dimStyleId.IsNull)
                throw new InvalidOperationException($"Dimension style '{dimStyleName}' was not found and could not be imported.");

            EnsureSurveyDimLayer(db, ed, tr);

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            db.Clayer = lt[DimLayerName];
            SetCurrentDimStyle(db, tr, dimStyleId);
        }

        internal static void SetCurrentDimStyle(Database db, Transaction tr, ObjectId dimStyleId)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (dimStyleId.IsNull) throw new ArgumentException("Dimension style id cannot be null.", nameof(dimStyleId));

            db.Dimstyle = dimStyleId;

            DimStyleTableRecord dstr = (DimStyleTableRecord)tr.GetObject(dimStyleId, OpenMode.ForRead);
            db.SetDimstyleData(dstr);
        }

        internal static ObjectId EnsureDimensionStyleAvailable(Database db, Editor ed, string dimStyleName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            if (string.IsNullOrWhiteSpace(dimStyleName)) throw new ArgumentException("Dimension style name is required.", nameof(dimStyleName));

            TryImportDimensionStyle(db, ed, dimStyleName);

            using Transaction tr = db.TransactionManager.StartTransaction();
            DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            ObjectId dimStyleId = dst.Has(dimStyleName) ? dst[dimStyleName] : ObjectId.Null;
            tr.Commit();
            return dimStyleId;
        }


        private static void RunNativeLineCurveLabelCommand(string command, string friendlyName, string civilLabelType, string desiredLabelStyleName)
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            string previousLayerName = GetCurrentLayerName(db);
            string trackedCommandName = NormalizeCommandName(command);

            try
            {
                // Civil 3D line/curve label commands can ignore the current layer and place
                // the label on the drawing's label default layer, commonly C-ANNO.
                // Do not leave the drawing current layer set to V-LABL. Instead, watch
                // for the objects created by the native Civil 3D command, move those
                // objects to V-LABL after placement, and restore the user's original layer.
                LayerStandards.EnsureSurveyLineCurveLabelLayer(db, ed);
                HashSet<string> beforeHandles = CaptureExistingModelSpaceHandles(db);

                RegisterPendingLayerRestore(doc, previousLayerName, trackedCommandName, beforeHandles, LayerStandards.SurveyLineCurveLabelLayerName, friendlyName, desiredLabelStyleName);
                ed.WriteMessage($"\n{friendlyName}: launching Civil 3D {civilLabelType} label command. The drawing current layer will be restored to '{previousLayerName}' after placement. New line/curve label objects will be moved to {LayerStandards.SurveyLineCurveLabelLayerName}. The created Civil 3D label will be assigned style '{desiredLabelStyleName}'.");
                doc.SendStringToExecute(command + " ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ClearPendingLayerRestore(doc);
                RestoreCurrentLayer(db, ed, previousLayerName);
                ed.WriteMessage($"\n{friendlyName} error: {ex.Message}");
            }
        }

        private static void RunNativeDimensionCommand(string command, string dimStyleName, string friendlyName)
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            string previousLayerName = GetCurrentLayerName(db);
            string trackedCommandName = NormalizeCommandName(command);

            try
            {
                LayerStandards.EnsureSurveyDimLayer(db, ed);

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PrepareCurrentDimEnvironment(db, ed, tr, dimStyleName);
                    tr.Commit();
                }

                RegisterPendingLayerRestore(doc, previousLayerName, trackedCommandName);
                ed.WriteMessage($"\n{friendlyName}: current dim style set to '{dimStyleName}' and current layer set to {DimLayerName}. Current layer will restore to '{previousLayerName}' when the command ends.");
                doc.SendStringToExecute(command + " ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ClearPendingLayerRestore(doc);
                RestoreCurrentLayer(db, ed, previousLayerName);
                ed.WriteMessage($"\n{friendlyName} error: {ex.Message}");
            }
        }

        internal static void EnsureSurveyDimLayer(Database db, Editor ed, Transaction tr)
        {
            if (LayerStandards.TryEnsureManagedLayer(db, tr, ed, DimLayerName))
                return;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(DimLayerName))
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord
            {
                Name = DimLayerName
            };

            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void TryImportDimensionStyle(Database targetDb, Editor ed, string dimStyleName)
        {
            if (!File.Exists(DimTemplatePath))
            {
                ed.WriteMessage($"\nSurvey dimensions: template not found for dimstyle import -> {DimTemplatePath}");
                return;
            }

            try
            {
                using Database sourceDb = new Database(false, true);
                sourceDb.ReadDwgFile(DimTemplatePath, FileShare.Read, true, string.Empty);
                sourceDb.CloseInput(true);

                using Transaction sourceTr = sourceDb.TransactionManager.StartTransaction();
                DimStyleTable sourceDst = (DimStyleTable)sourceTr.GetObject(sourceDb.DimStyleTableId, OpenMode.ForRead);
                if (!sourceDst.Has(dimStyleName))
                {
                    ed.WriteMessage($"\nSurvey dimensions: dimstyle '{dimStyleName}' was not found in template '{DimTemplatePath}'.");
                    return;
                }

                ObjectIdCollection ids = new ObjectIdCollection
                {
                    sourceDst[dimStyleName]
                };

                IdMapping mapping = new IdMapping();
                sourceDb.WblockCloneObjects(ids, targetDb.DimStyleTableId, mapping, DuplicateRecordCloning.Replace, false);
                sourceTr.Commit();

                ed.WriteMessage($"\nSurvey dimensions: synchronized dimstyle '{dimStyleName}' from reference template.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSurvey dimensions: unable to import dimstyle '{dimStyleName}' from '{DimTemplatePath}' -> {ex.Message}");
            }
        }

        private sealed class PendingLayerRestore
        {
            internal PendingLayerRestore(
                string previousLayerName,
                string commandName,
                HashSet<string>? beforeHandles = null,
                string? postCommandLayerName = null,
                string? friendlyName = null,
                string? desiredLabelStyleName = null)
            {
                PreviousLayerName = previousLayerName;
                CommandName = commandName;
                BeforeHandles = beforeHandles;
                PostCommandLayerName = postCommandLayerName;
                FriendlyName = friendlyName ?? commandName;
                DesiredLabelStyleName = desiredLabelStyleName;
            }

            internal string PreviousLayerName { get; }
            internal string CommandName { get; }
            internal HashSet<string>? BeforeHandles { get; }
            internal string? PostCommandLayerName { get; }
            internal string FriendlyName { get; }
            internal string? DesiredLabelStyleName { get; }
            internal HashSet<string> AppendedHandles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly System.Collections.Generic.Dictionary<Document, PendingLayerRestore> PendingLayerRestores =
            new System.Collections.Generic.Dictionary<Document, PendingLayerRestore>();

        private static void RegisterPendingLayerRestore(
            Document doc,
            string previousLayerName,
            string commandName,
            HashSet<string>? beforeHandles = null,
            string? postCommandLayerName = null,
            string? friendlyName = null,
            string? desiredLabelStyleName = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(previousLayerName))
                return;

            ClearPendingLayerRestore(doc);
            PendingLayerRestores[doc] = new PendingLayerRestore(previousLayerName, commandName, beforeHandles, postCommandLayerName, friendlyName, desiredLabelStyleName);
            doc.Database.ObjectAppended += OnTrackedLabelObjectAppended;
            doc.CommandEnded += OnTrackedDimensionCommandFinished;
            doc.CommandCancelled += OnTrackedDimensionCommandFinished;
            doc.CommandFailed += OnTrackedDimensionCommandFinished;
        }

        private static void ClearPendingLayerRestore(Document? doc)
        {
            if (doc == null)
                return;

            doc.Database.ObjectAppended -= OnTrackedLabelObjectAppended;
            doc.CommandEnded -= OnTrackedDimensionCommandFinished;
            doc.CommandCancelled -= OnTrackedDimensionCommandFinished;
            doc.CommandFailed -= OnTrackedDimensionCommandFinished;
            PendingLayerRestores.Remove(doc);
        }

        private static void OnTrackedDimensionCommandFinished(object sender, CommandEventArgs e)
        {
            Document? doc = sender as Document;
            if (doc == null)
                return;

            if (!PendingLayerRestores.TryGetValue(doc, out PendingLayerRestore? pending))
            {
                ClearPendingLayerRestore(doc);
                return;
            }

            string finishedCommand = NormalizeCommandName(e.GlobalCommandName);

            if (!string.IsNullOrWhiteSpace(pending.PostCommandLayerName))
            {
                // The managed wrapper command ends before the native Civil 3D label command
                // starts because it is launched with SendStringToExecute. Do not clean up on
                // the wrapper-command end event. Clean up on the next native command end,
                // cancel, or fail event. This catches command-name variations from Civil 3D
                // while still preserving the user's original current layer.
                if (IsLineCurveLabelWrapperCommand(finishedCommand))
                    return;
            }
            else if (!string.Equals(finishedCommand, pending.CommandName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(pending.PostCommandLayerName))
                {
                    if (pending.AppendedHandles.Count > 0)
                        MoveKnownEntitiesToLayer(doc, pending.AppendedHandles, pending.PostCommandLayerName!, pending.FriendlyName);

                    if (pending.BeforeHandles != null)
                        MoveNewModelSpaceEntitiesToLayer(doc, pending.BeforeHandles, pending.PostCommandLayerName!, pending.FriendlyName);

                    if (!string.IsNullOrWhiteSpace(pending.DesiredLabelStyleName))
                    {
                        HashSet<string> styleTargets = new HashSet<string>(pending.AppendedHandles, StringComparer.OrdinalIgnoreCase);
                        if (pending.BeforeHandles != null)
                        {
                            foreach (string handle in CaptureNewModelSpaceHandles(doc.Database, pending.BeforeHandles))
                                styleTargets.Add(handle);
                        }

                        ApplyLabelStyleToKnownEntities(doc, styleTargets, pending.DesiredLabelStyleName!, pending.FriendlyName);
                    }
                }

                RestoreCurrentLayer(doc.Database, doc.Editor, pending.PreviousLayerName);
                doc.Editor.WriteMessage($"\nSurvey dimensions: current layer restored to '{pending.PreviousLayerName}'.");
            }
            finally
            {
                ClearPendingLayerRestore(doc);
            }
        }


        private static void OnTrackedLabelObjectAppended(object sender, ObjectEventArgs e)
        {
            if (e?.DBObject == null)
                return;

            Database? sourceDb = sender as Database;
            if (sourceDb == null)
                return;

            foreach (KeyValuePair<Document, PendingLayerRestore> kvp in PendingLayerRestores)
            {
                if (!ReferenceEquals(kvp.Key.Database, sourceDb))
                    continue;

                try
                {
                    if (e.DBObject is Entity && e.DBObject.ObjectId.IsValid && !e.DBObject.ObjectId.IsErased)
                    {
                        string handle = e.DBObject.Handle.ToString();
                        if (!string.IsNullOrWhiteSpace(handle) && !string.Equals(handle, "0", StringComparison.OrdinalIgnoreCase))
                            kvp.Value.AppendedHandles.Add(handle);
                    }
                }
                catch
                {
                    // Object handles may not be available for every transient/temporary object.
                }

                return;
            }
        }

        private static bool IsLineCurveLabelWrapperCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            return string.Equals(commandName, "SURVEY-LC-LABEL-2POINT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "2-POINT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "Q42POINT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "SURVEY-LC-LABEL-2POINT-DIST", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "Q42POINTDIST", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "SURVEY-LC-LABEL-BEARING-DISTANCE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "SURVEY-BEARING-DISTANCE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "BEARINGANDDISTANCE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "Q4BDIST", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "SURVEY-LC-LABEL-DISTANCE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "SURVEY-SEGMENT-DISTANCE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "Q4LCDIST", StringComparison.OrdinalIgnoreCase);
        }

        private static void MoveKnownEntitiesToLayer(Document doc, HashSet<string> targetHandles, string layerName, string friendlyName)
        {
            if (targetHandles == null || targetHandles.Count == 0)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            int movedCount = 0;

            try
            {
                LayerStandards.EnsureSurveyLineCurveLabelLayer(db, ed);

                using Transaction tr = db.TransactionManager.StartTransaction();
                foreach (string handleText in targetHandles)
                {
                    if (!long.TryParse(handleText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long handleValue))
                        continue;

                    ObjectId id;
                    try
                    {
                        id = db.GetObjectId(false, new Handle(handleValue), 0);
                    }
                    catch
                    {
                        continue;
                    }

                    if (id.IsNull || !id.IsValid || id.IsErased)
                        continue;

                    if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent)
                    {
                        ent.Layer = layerName;
                        ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                        movedCount++;
                    }
                }

                tr.Commit();

                if (movedCount > 0)
                    ed.WriteMessage($"\n{friendlyName}: moved {movedCount} newly appended label object(s) to layer {layerName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{friendlyName}: unable to move appended label object(s) to layer {layerName}: {ex.Message}");
            }
        }

        private static void ApplyLabelStyleToKnownEntities(Document doc, HashSet<string> targetHandles, string styleName, string friendlyName)
        {
            if (targetHandles == null || targetHandles.Count == 0 || string.IsNullOrWhiteSpace(styleName))
                return;

            Editor ed = doc.Editor;
            try
            {
                ObjectId styleId = FindCivilLabelStyleId(styleName);
                if (styleId.IsNull)
                {
                    ed.WriteMessage($"\n{friendlyName}: Civil 3D label style '{styleName}' was not found. The label retained the current/default style.");
                    return;
                }

                int changedCount = 0;
                using Transaction tr = doc.Database.TransactionManager.StartTransaction();
                foreach (string handleText in targetHandles)
                {
                    if (!long.TryParse(handleText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long handleValue))
                        continue;

                    ObjectId id;
                    try { id = doc.Database.GetObjectId(false, new Handle(handleValue), 0); }
                    catch { continue; }
                    if (id.IsNull || !id.IsValid || id.IsErased)
                        continue;

                    DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    PropertyInfo? styleProperty = obj.GetType().GetProperty("StyleId", BindingFlags.Instance | BindingFlags.Public);
                    if (styleProperty == null || !styleProperty.CanWrite || styleProperty.PropertyType != typeof(ObjectId))
                        continue;

                    styleProperty.SetValue(obj, styleId);
                    changedCount++;
                }
                tr.Commit();

                if (changedCount > 0)
                    ed.WriteMessage($"\n{friendlyName}: assigned Civil 3D label style '{styleName}' to {changedCount} new label object(s).");
                else
                    ed.WriteMessage($"\n{friendlyName}: no newly created Civil 3D label object accepted style '{styleName}'.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{friendlyName}: unable to assign Civil 3D label style '{styleName}': {ex.Message}");
            }
        }

        private static ObjectId FindCivilLabelStyleId(string styleName)
        {
            object civilDocument = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            PropertyInfo? stylesProperty = civilDocument.GetType().GetProperty("Styles", BindingFlags.Instance | BindingFlags.Public);
            object? stylesRoot = stylesProperty?.GetValue(civilDocument);
            if (stylesRoot == null)
                return ObjectId.Null;

            return FindNamedObjectId(stylesRoot, styleName, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        }

        private static ObjectId FindNamedObjectId(object source, string styleName, HashSet<object> visited, int depth)
        {
            if (source == null || depth > 8 || !visited.Add(source))
                return ObjectId.Null;

            Type sourceType = source.GetType();
            MethodInfo? containsMethod = sourceType.GetMethod("Contains", new[] { typeof(string) });
            PropertyInfo? stringIndexer = sourceType.GetProperty("Item", new[] { typeof(string) });
            if (containsMethod != null && stringIndexer != null && stringIndexer.PropertyType == typeof(ObjectId))
            {
                try
                {
                    if (containsMethod.Invoke(source, new object[] { styleName }) is bool found && found)
                        return (ObjectId)(stringIndexer.GetValue(source, new object[] { styleName }) ?? ObjectId.Null);
                }
                catch { }
            }

            foreach (PropertyInfo property in sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || property.PropertyType == typeof(string) || property.PropertyType.IsPrimitive || property.PropertyType == typeof(ObjectId))
                    continue;

                object? child;
                try { child = property.GetValue(source); }
                catch { continue; }
                if (child == null)
                    continue;

                ObjectId foundId = FindNamedObjectId(child, styleName, visited, depth + 1);
                if (!foundId.IsNull)
                    return foundId;
            }

            return ObjectId.Null;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private static HashSet<string> CaptureExistingModelSpaceHandles(Database db)
        {
            HashSet<string> handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsValid && !id.IsErased)
                        handles.Add(id.Handle.ToString());
                }

                tr.Commit();
            }
            catch
            {
                // If capture fails, return an empty set. The post-command cleanup will then skip safely.
            }

            return handles;
        }


        private static HashSet<string> CaptureNewModelSpaceHandles(Database db, HashSet<string> beforeHandles)
        {
            HashSet<string> newHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    string handle = id.Handle.ToString();
                    if (!beforeHandles.Contains(handle))
                        newHandles.Add(handle);
                }

                tr.Commit();
            }
            catch
            {
                // If model-space comparison fails, preserve the append-event handle set only.
            }

            return newHandles;
        }

        private static void MoveNewModelSpaceEntitiesToLayer(Document doc, HashSet<string> beforeHandles, string layerName, string friendlyName)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;
            int movedCount = 0;

            try
            {
                LayerStandards.EnsureSurveyLineCurveLabelLayer(db, ed);

                using Transaction tr = db.TransactionManager.StartTransaction();
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    string handle = id.Handle.ToString();
                    if (beforeHandles.Contains(handle))
                        continue;

                    if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent)
                    {
                        ent.Layer = layerName;
                        ent.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                        movedCount++;
                    }
                }

                tr.Commit();

                if (movedCount > 0)
                    ed.WriteMessage($"\n{friendlyName}: moved {movedCount} new label object(s) to layer {layerName}.");
                else
                    ed.WriteMessage($"\n{friendlyName}: no new label objects were detected to move to {layerName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{friendlyName}: unable to move new label object(s) to layer {layerName}: {ex.Message}");
            }
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
                RestoreCurrentLayer(db, tr, layerName);
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSurvey dimensions: unable to restore current layer to '{layerName}': {ex.Message}");
            }
        }

        private static void RestoreCurrentLayer(Database db, Transaction tr, string layerName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(layerName))
                return;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                return;

            db.Clayer = lt[layerName];
        }

        private static SelectedCurve PromptForCurve(Editor ed, string message)
        {
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage("\nOnly line / polyline / arc style curve objects are allowed.");
            peo.AddAllowedClass(typeof(Curve), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK
                ? new SelectedCurve(per.ObjectId, per.PickedPoint)
                : SelectedCurve.Invalid;
        }

        private static bool TryResolveOffsetPoints(Curve curve1, Point3d pick1, Curve curve2, Point3d pick2, out Point3d point1, out Point3d point2)
        {
            point1 = ProjectPointToCurve(curve1, pick1);
            point2 = ProjectPointToCurve(curve2, pick2);

            for (int i = 0; i < 4; i++)
            {
                point2 = ProjectPointToCurve(curve2, point1);
                point1 = ProjectPointToCurve(curve1, point2);
            }

            return true;
        }

        private static Point3d ProjectPointToCurve(Curve curve, Point3d sourcePoint)
        {
            try
            {
                return curve.GetClosestPointTo(sourcePoint, false);
            }
            catch
            {
                Point3d start = curve.StartPoint;
                Point3d end = curve.EndPoint;
                return start.DistanceTo(sourcePoint) <= end.DistanceTo(sourcePoint) ? start : end;
            }
        }

        private static Point3d MidPoint(Point3d a, Point3d b)
        {
            return new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private readonly struct SelectedCurve
        {
            internal static SelectedCurve Invalid => new SelectedCurve(ObjectId.Null, Point3d.Origin);

            internal SelectedCurve(ObjectId objectId, Point3d pickPoint)
            {
                ObjectId = objectId;
                PickPoint = pickPoint;
            }

            internal ObjectId ObjectId { get; }
            internal Point3d PickPoint { get; }
            internal bool IsValid => !ObjectId.IsNull;
        }
    }
}
