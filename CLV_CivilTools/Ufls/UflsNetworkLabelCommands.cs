using System;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.Ufls
{
    public static class UflsNetworkLabelCommands
    {
        [CommandMethod("UFLS", "UFLS-LABEL-PIPE-CIRCULAR", CommandFlags.Modal)]
        public static void LabelPipeCircular() => LabelSelectedPipes("R26_V-PIPE-CIRCULAR-E");

        [CommandMethod("UFLS", "UFLS-LABEL-PIPE-BOX", CommandFlags.Modal)]
        public static void LabelPipeBox() => LabelSelectedPipes("R26_V-PIPE-BOX-E");

        [CommandMethod("UFLS", "UFLS-LABEL-STRC-MANHOLE", CommandFlags.Modal)]
        public static void LabelStructureManhole() => LabelSelectedStructures("R26_V-MNHL");

        [CommandMethod("UFLS", "UFLS-LABEL-STRC-ACCESS", CommandFlags.Modal)]
        public static void LabelStructureAccess() => LabelSelectedStructures("R26_V-ACCS");

        [CommandMethod("UFLS", "UFLS-LABEL-STRC-STUB", CommandFlags.Modal)]
        public static void LabelStructureStub() => LabelSelectedStructures("R26_V-STUB");

        [CommandMethod("UFLS", "UFLS-LABEL-STRC-JUNCTION", CommandFlags.Modal)]
        public static void LabelStructureJunction() => LabelSelectedStructures("R26_V-JNCT");

        [CommandMethod("UFLS", "UFLS-LABEL-STRC-INLET", CommandFlags.Modal)]
        public static void LabelStructureInlet() => LabelSelectedStructures("R26_V-INLT");

        private static void LabelSelectedPipes(string requestedStyleName)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            try
            {
                PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = $"\nSelect pipe(s) to label with style '{requestedStyleName}': "
                });

                if (psr.Status != PromptStatus.OK || psr.Value == null)
                    return;

                bool styleExists = TryGetPipePlanLabelStyleId(requestedStyleName, out ObjectId requestedStyleId);

                int created = 0;
                int styled = 0;
                int defaulted = 0;
                int skipped = 0;
                int failed = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject sel in psr.Value)
                    {
                        if (sel == null || sel.ObjectId.IsNull)
                            continue;

                        if (tr.GetObject(sel.ObjectId, OpenMode.ForRead, false) is not Pipe pipe)
                        {
                            skipped++;
                            continue;
                        }

                        if (HasExistingPipeLabel(pipe.ObjectId, requestedStyleName, tr))
                        {
                            skipped++;
                            continue;
                        }

                        ObjectId labelId;
                        try
                        {
                            labelId = styleExists
                                ? PipeLabel.Create(pipe.ObjectId, 0.5d, requestedStyleId)
                                : PipeLabel.Create(pipe.ObjectId, 0.5d);
                        }
                        catch (System.Exception ex)
                        {
                            failed++;
                            ed.WriteMessage($"\nUFLS labels: pipe {pipe.Handle} -> failed to create label. {ex.Message}");
                            continue;
                        }

                        if (labelId.IsNull || labelId.IsErased)
                        {
                            failed++;
                            ed.WriteMessage($"\nUFLS labels: pipe {pipe.Handle} -> label create returned null.");
                            continue;
                        }

                        created++;

                        if (tr.GetObject(labelId, OpenMode.ForRead, false) is PipeLabel label)
                        {
                            if (string.Equals(label.StyleName, requestedStyleName, StringComparison.OrdinalIgnoreCase))
                            {
                                styled++;
                            }
                            else
                            {
                                defaulted++;
                                if (styleExists)
                                {
                                    ed.WriteMessage($"\nUFLS labels: pipe {pipe.Handle} -> requested style '{requestedStyleName}' exists but Civil 3D kept '{label.StyleName}'.");
                                }
                                else
                                {
                                    ed.WriteMessage($"\nUFLS labels: pipe {pipe.Handle} -> requested Civil 3D label style '{requestedStyleName}' was not found in PipeLabelStyles.PlanProfileLabelStyles. Kept current/default style '{label.StyleName}'.");
                                }
                            }
                        }
                        else
                        {
                            defaulted++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nUFLS labels: created {created} pipe label(s). Requested style applied {styled} time(s). Default/standard style kept {defaulted} time(s). Skipped {skipped}. Failed {failed}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-LABEL-PIPE error: {ex.Message}");
            }
        }

        private static void LabelSelectedStructures(string requestedStyleName)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            try
            {
                PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = $"\nSelect structure(s) to label with style '{requestedStyleName}': "
                });

                if (psr.Status != PromptStatus.OK || psr.Value == null)
                    return;

                bool styleExists = TryGetStructurePlanLabelStyleId(requestedStyleName, out ObjectId requestedStyleId);

                int created = 0;
                int styled = 0;
                int defaulted = 0;
                int skipped = 0;
                int failed = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject sel in psr.Value)
                    {
                        if (sel == null || sel.ObjectId.IsNull)
                            continue;

                        if (tr.GetObject(sel.ObjectId, OpenMode.ForRead, false) is not Structure structure)
                        {
                            skipped++;
                            continue;
                        }

                        if (HasExistingStructureLabel(structure.ObjectId, requestedStyleName, tr))
                        {
                            skipped++;
                            continue;
                        }

                        Point3d labelLocation = GetStructurePoint(structure);
                        ObjectId labelId;
                        try
                        {
                            labelId = styleExists
                                ? StructureLabel.Create(structure.ObjectId, requestedStyleId, labelLocation)
                                : StructureLabel.Create(structure.ObjectId);
                        }
                        catch (System.Exception ex)
                        {
                            failed++;
                            ed.WriteMessage($"\nUFLS labels: structure {structure.Handle} -> failed to create label. {ex.Message}");
                            continue;
                        }

                        if (labelId.IsNull || labelId.IsErased)
                        {
                            failed++;
                            ed.WriteMessage($"\nUFLS labels: structure {structure.Handle} -> label create returned null.");
                            continue;
                        }

                        created++;

                        if (tr.GetObject(labelId, OpenMode.ForRead, false) is StructureLabel label)
                        {
                            if (string.Equals(label.StyleName, requestedStyleName, StringComparison.OrdinalIgnoreCase))
                            {
                                styled++;
                            }
                            else
                            {
                                defaulted++;
                                if (styleExists)
                                {
                                    ed.WriteMessage($"\nUFLS labels: structure {structure.Handle} -> requested style '{requestedStyleName}' exists but Civil 3D kept '{label.StyleName}'.");
                                }
                                else
                                {
                                    ed.WriteMessage($"\nUFLS labels: structure {structure.Handle} -> requested Civil 3D label style '{requestedStyleName}' was not found in StructureLabelStyles.LabelStyles. Kept current/default style '{label.StyleName}'.");
                                }
                            }
                        }
                        else
                        {
                            defaulted++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nUFLS labels: created {created} structure label(s). Requested style applied {styled} time(s). Default/standard style kept {defaulted} time(s). Skipped {skipped}. Failed {failed}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-LABEL-STRC error: {ex.Message}");
            }
        }

        private static bool TryGetPipePlanLabelStyleId(string styleName, out ObjectId styleId)
        {
            styleId = ObjectId.Null;

            try
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                LabelStyleCollection styles = civDoc.Styles.LabelStyles.PipeLabelStyles.PlanProfileLabelStyles;
                styleId = styles[styleName];
                return !styleId.IsNull;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetStructurePlanLabelStyleId(string styleName, out ObjectId styleId)
        {
            styleId = ObjectId.Null;

            try
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                LabelStyleCollection styles = civDoc.Styles.LabelStyles.StructureLabelStyles.LabelStyles;
                styleId = styles[styleName];
                return !styleId.IsNull;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasExistingPipeLabel(ObjectId pipeId, string styleName, Transaction tr)
        {
            try
            {
                ObjectIdCollection labelIds = PipeLabel.GetAvailableLabelIds(pipeId);
                foreach (ObjectId labelId in labelIds)
                {
                    if (labelId.IsNull || !labelId.IsValid || labelId.IsErased)
                        continue;

                    if (tr.GetObject(labelId, OpenMode.ForRead, false) is not PipeLabel label)
                        continue;

                    if (string.Equals(label.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool HasExistingStructureLabel(ObjectId structureId, string styleName, Transaction tr)
        {
            try
            {
                ObjectIdCollection labelIds = StructureLabel.GetAvailableLabelIds(structureId);
                foreach (ObjectId labelId in labelIds)
                {
                    if (labelId.IsNull || !labelId.IsValid || labelId.IsErased)
                        continue;

                    if (tr.GetObject(labelId, OpenMode.ForRead, false) is not StructureLabel label)
                        continue;

                    if (string.Equals(label.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static Point3d GetStructurePoint(Structure structure)
        {
            try
            {
                return structure.Position;
            }
            catch
            {
                try
                {
                    return structure.Location;
                }
                catch
                {
                    return Point3d.Origin;
                }
            }
        }
    }
}
