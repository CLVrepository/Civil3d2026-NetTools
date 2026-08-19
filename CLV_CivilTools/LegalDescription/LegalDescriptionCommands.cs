using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace CLV_CivilTools.LegalDescription
{
    public static class LegalDescriptionCommands
    {
        [CommandMethod("LEGALDESC", CommandFlags.Modal)]
        [CommandMethod("CLV-LEGAL-DESCRIPTION", CommandFlags.Modal)]
        public static void CreateLegalDescription()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            Editor ed = doc.Editor;

            try
            {
                LegalDescriptionSession? savedSession = LegalSessionStorage.Load(doc.Database);
                if (savedSession != null && savedSession.Courses.Count > 0)
                {
                    var startupOptions = new PromptKeywordOptions(
                        "\nA saved legal-description session exists in this drawing. Open it or start a new session?")
                    {
                        AllowNone = true
                    };
                    startupOptions.Keywords.Add("Open");
                    startupOptions.Keywords.Add("New");
                    startupOptions.Keywords.Default = "Open";

                    PromptResult startupChoice = ed.GetKeywords(startupOptions);
                    if (startupChoice.Status == PromptStatus.Cancel)
                        return;

                    string selectedAction = string.IsNullOrWhiteSpace(startupChoice.StringResult)
                        ? "Open"
                        : startupChoice.StringResult;

                    if (string.Equals(selectedAction, "Open", StringComparison.OrdinalIgnoreCase))
                    {
                        LegalDescriptionPalette.Show(savedSession);
                        ed.WriteMessage(
                            $"\nLEGALDESC: Opened the saved session containing {savedSession.TieCourses.Count} tie course(s) " +
                            $"and {savedSession.Courses.Count} boundary course(s). Use REFRESH SOURCE if the source geometry changed.");
                        return;
                    }
                }

                SelectionFilter filter = CreateLineArcFilter();
                var boundaryOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect connected BOUNDARY LINE and ARC entities: "
                };
                PromptSelectionResult selectedBoundary = ed.GetSelection(boundaryOptions, filter);
                if (selectedBoundary.Status != PromptStatus.OK)
                    return;

                PromptPointResult start = ed.GetPoint("\nSelect the desired Point of Beginning endpoint: ");
                if (start.Status != PromptStatus.OK)
                    return;

                var pocOptions = new PromptKeywordOptions("\nIs the Point of Commencement the same as the Point of Beginning?")
                {
                    AllowNone = true
                };
                pocOptions.Keywords.Add("Same");
                pocOptions.Keywords.Add("seParate");
                pocOptions.Keywords.Default = "Same";
                PromptResult pocChoice = ed.GetKeywords(pocOptions);
                if (pocChoice.Status == PromptStatus.Cancel)
                    return;

                Point3d? poc = null;
                IReadOnlyCollection<ObjectId>? tieIds = null;
                if (string.Equals(pocChoice.StringResult, "seParate", StringComparison.OrdinalIgnoreCase))
                {
                    PromptPointResult pocPoint = ed.GetPoint("\nSelect the Point of Commencement endpoint: ");
                    if (pocPoint.Status != PromptStatus.OK)
                        return;
                    poc = pocPoint.Value;

                    var tieOptions = new PromptSelectionOptions
                    {
                        MessageForAdding = "\nSelect connected TIE LINE and ARC entities from the POC to the POB: "
                    };
                    PromptSelectionResult selectedTie = ed.GetSelection(tieOptions, filter);
                    if (selectedTie.Status != PromptStatus.OK)
                        return;
                    tieIds = selectedTie.Value.GetObjectIds().ToList();
                }

                IReadOnlyCollection<ObjectId> boundaryIds = selectedBoundary.Value.GetObjectIds().ToList();
                LegalDescriptionSession session = LegalGeometryService.BuildSession(doc.Database, boundaryIds, start.Value, poc, tieIds);
                LegalGeometrySummary summary = LegalGeometryService.Summarize(session);
                LegalSessionStorage.Save(doc.Database, session);
                LegalDescriptionPalette.Show(session);

                ed.WriteMessage($"\nLEGALDESC: Loaded {session.TieCourses.Count} tie course(s) and {session.Courses.Count} boundary course(s). Forward closure {summary.ForwardMisclosure:F4} ft; reverse build {summary.ReverseMisclosure:F4} ft.");
                if (!string.IsNullOrWhiteSpace(summary.Warning))
                    ed.WriteMessage("\nLEGALDESC warning: " + summary.Warning);
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nLEGALDESC AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nLEGALDESC error: " + ex.Message);
            }
        }

        [CommandMethod("LEGALDESC-OPEN", CommandFlags.Modal)]
        public static void OpenSavedLegalDescription()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            LegalDescriptionSession? session = LegalSessionStorage.Load(doc.Database);
            if (session == null || session.Courses.Count == 0)
            {
                doc.Editor.WriteMessage("\nLEGALDESC-OPEN: No saved legal-description session was found in this drawing.");
                return;
            }
            LegalDescriptionPalette.Show(session);
        }

        private static SelectionFilter CreateLineArcFilter()
        {
            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "ARC"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
        }
    }
}
