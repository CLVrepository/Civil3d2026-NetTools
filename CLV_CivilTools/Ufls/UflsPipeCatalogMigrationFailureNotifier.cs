using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Post-command notifier for pipe-catalog migration review items.
    /// The migration command already sets unresolved/failed parts to ACI red.
    /// This notifier applies an existing REDLINE structure style when available
    /// and adds a visible red model-space review marker as a fallback/locator.
    /// </summary>
    public sealed class UflsPipeCatalogMigrationFailureNotifier : IExtensionApplication
    {
        private const string MigrationCommand = "UFLS-PIPE-CATALOG-MIGRATE-UI";
        private const string ReviewLayer = "CLV-MIGRATION-REVIEW";

        public void Initialize()
        {
            AcadApp.DocumentManager.DocumentCreated += OnDocumentCreated;
            foreach (Document doc in AcadApp.DocumentManager)
                Hook(doc);
        }

        public void Terminate()
        {
            AcadApp.DocumentManager.DocumentCreated -= OnDocumentCreated;
            foreach (Document doc in AcadApp.DocumentManager)
                Unhook(doc);
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e) => Hook(e.Document);

        private static void Hook(Document doc)
        {
            doc.CommandEnded -= OnCommandEnded;
            doc.CommandEnded += OnCommandEnded;
        }

        private static void Unhook(Document doc)
        {
            doc.CommandEnded -= OnCommandEnded;
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (!string.Equals(e.GlobalCommandName, MigrationCommand, StringComparison.OrdinalIgnoreCase))
                return;

            if (sender is not Document doc) return;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    ObjectId reviewLayerId = EnsureReviewLayer(tr, doc.Database);
                    ObjectId redlineStructureStyleId = FindRedlineStructureStyle(tr);
                    string redlineStyleName = redlineStructureStyleId.IsNull
                        ? string.Empty
                        : GetStyleName(tr, redlineStructureStyleId);

                    var problemParts = new List<(ObjectId Id, string Name, Point3d Location)>();
                    int structuresRestyled = 0;

                    foreach (ObjectId id in GetCurrentSpaceEntityIds(tr, doc.Database))
                    {
                        if (tr.GetObject(id, OpenMode.ForRead, false) is not AcEntity ent) continue;
                        if (ent.ColorIndex != 1) continue;

                        if (ent is Structure structure)
                        {
                            if (!redlineStructureStyleId.IsNull)
                            {
                                try
                                {
                                    structure.UpgradeOpen();
                                    structure.StyleId = redlineStructureStyleId;
                                    structure.RecordGraphicsModified(true);
                                    structuresRestyled++;
                                }
                                catch
                                {
                                    // Keep the entity-color/review-circle fallback if the style cannot be assigned.
                                }
                            }

                            Point3d p = structure.Location;
                            problemParts.Add((id, SafeName(structure), p));
                        }
                        else if (ent is Pipe pipe)
                        {
                            Point3d p = MidPoint(pipe.StartPoint, pipe.EndPoint);
                            problemParts.Add((id, SafeName(pipe), p));
                        }
                    }

                    foreach (var item in problemParts)
                    {
                        if (HasNearbyMarker(tr, doc.Database, item.Location)) continue;
                        AddReviewMarker(tr, doc.Database, reviewLayerId, item.Location);
                    }

                    tr.Commit();

                    if (problemParts.Count > 0)
                    {
                        string detail = string.Join(Environment.NewLine,
                            problemParts.Take(12).Select((p, i) => $"{i + 1}. {p.Name}"));
                        if (problemParts.Count > 12)
                            detail += Environment.NewLine + $"...and {problemParts.Count - 12} more.";

                        string styleText = structuresRestyled > 0
                            ? $"\n{structuresRestyled} structure(s) were changed to REDLINE style '{redlineStyleName}'."
                            : redlineStructureStyleId.IsNull
                                ? "\nNo structure style containing 'REDLINE' was found; red color/review circles were used instead."
                                : string.Empty;

                        MessageBox.Show(
                            $"Migration completed with {problemParts.Count} part(s) requiring manual review.\n\n" +
                            "Those parts are marked RED in model space with a red review circle." +
                            styleText + "\n\n" +
                            detail,
                            "CLV Pipe Catalog Migration - Review Required",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
            catch
            {
                // Never allow the notifier to interfere with command completion.
            }
        }

        private static ObjectId FindRedlineStructureStyle(Transaction tr)
        {
            try
            {
                CivilDocument civilDoc = CivilApplication.ActiveDocument;
                ObjectId fallback = ObjectId.Null;

                foreach (ObjectId styleId in civilDoc.Styles.StructureStyles)
                {
                    if (tr.GetObject(styleId, OpenMode.ForRead, false) is not StructureStyle style) continue;
                    string name = style.Name ?? string.Empty;
                    if (name.Equals("REDLINE", StringComparison.OrdinalIgnoreCase))
                        return styleId;
                    if (fallback.IsNull && name.Contains("REDLINE", StringComparison.OrdinalIgnoreCase))
                        fallback = styleId;
                }

                return fallback;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string GetStyleName(Transaction tr, ObjectId styleId)
        {
            try
            {
                return tr.GetObject(styleId, OpenMode.ForRead, false) is StructureStyle style
                    ? style.Name
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<ObjectId> GetCurrentSpaceEntityIds(Transaction tr, Database db)
        {
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead, false) is not BlockTableRecord btr)
                yield break;
            foreach (ObjectId id in btr) yield return id;
        }

        private static ObjectId EnsureReviewLayer(Transaction tr, Database db)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ReviewLayer)) return lt[ReviewLayer];

            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = ReviewLayer,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 1),
                IsPlottable = false
            };
            ObjectId id = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private static void AddReviewMarker(Transaction tr, Database db, ObjectId layerId, Point3d center)
        {
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var circle = new Circle(center, Vector3d.ZAxis, 3.0)
            {
                LayerId = layerId,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, 1),
                LineWeight = LineWeight.LineWeight050
            };
            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        private static bool HasNearbyMarker(Transaction tr, Database db, Point3d point)
        {
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is Circle c &&
                    string.Equals(c.Layer, ReviewLayer, StringComparison.OrdinalIgnoreCase) &&
                    c.Center.DistanceTo(point) < 0.1)
                    return true;
            }
            return false;
        }

        private static string SafeName(Part part)
        {
            try
            {
                string family = part.PartFamilyName;
                string size = part.PartSizeName;
                return string.IsNullOrWhiteSpace(family) ? size : $"{family} - {size}";
            }
            catch
            {
                try { return part.PartSizeName; }
                catch { return $"Part {part.ObjectId.Handle}"; }
            }
        }

        private static Point3d MidPoint(Point3d a, Point3d b)
            => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);
    }
}
