using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    internal static partial class GisSewerPreparation
    {
        // Uses the current production readers and pure clipping planner. No writes,
        // new geometry, layer creation, archive changes or transaction commit.
        internal static void Diagnose(Document document)
        {
            if (document != AcadApp.DocumentManager.MdiActiveDocument) return;
            var report = new StringBuilder("\nCLV-GIS-SSWR-DIAG: read-only endpoint/outer-circle probes; no drawing changes.\n");
            try
            {
                using (document.LockDocument())
                using (Transaction tr = document.Database.TransactionManager.StartTransaction())
                {
                    Database db = document.Database;
                    var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    if (model.Name != BlockTableRecord.ModelSpace)
                        throw new InvalidOperationException("Diagnostic requires model space.");
                    report.AppendLine($"InsertionUnits={db.Insunits}; legacy center XY tolerance=0.10; connection XYZ tolerance=1e-8; null XY tolerance=0.10 / Z tolerance=1e-8; geometry tolerance=1e-8.");
                    var archiveRoot = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    report.AppendLine($"Existing sewer archive={archiveRoot.Contains(ArchiveKey)}. This report examines raw input, not archived output validity.");
                    SewerManholeBatch batch = GisSewerManholePlanner.Plan(db, tr);
                    foreach (string review in batch.Reviews) report.AppendLine("STRUCTURE REVIEW: " + review);
                    foreach (var structure in batch.Structures)
                    {
                        DescribeStructure(structure);
                        if (!structure.BlockId.IsNull && tr.GetObject(structure.BlockId, OpenMode.ForRead) is BlockReference block)
                            report.AppendLine($"  blockInsertionXYZ={P(block.Position)}");
                    }
                    var byName = batch.Structures.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
                    int count = 0;
                    foreach (ObjectId id in model.Cast<ObjectId>().ToArray())
                    {
                        if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased || !IsRawPipeLayer(entity.Layer)) continue;
                        try
                        {
                            if (IsExcludedStormPipe(id, entity.Layer)) continue;
                            count++;
                            report.AppendLine($"PIPE handle={id.Handle}, type={entity.GetType().Name}, layer={entity.Layer}");
                            if (!GisImportCommands.TryReadSewerPipeData(id, out var data, out string detail) || data == null)
                                throw new InvalidOperationException(detail);
                            report.AppendLine($"  OD Name={data.Name}; StructureStart={data.StructureStart}; StructureEnd={data.StructureEnd}; diameterFeet={N(data.DiameterFeet)}; size={SewerPreparationRules.ClassifyDiameter(data.DiameterFeet)}");
                            var sourceLayer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                            report.AppendLine($"  sourceLayerLocked={sourceLayer.IsLocked}");
                            if (entity.GetType() != typeof(Line)) throw new InvalidOperationException("Only an exact LINE is supported by current conversion.");
                            var line = (Line)entity;
                            report.AppendLine($"  WCS Start={P(line.StartPoint)} End={P(line.EndPoint)} Normal={P(new Point3d(line.Normal.X, line.Normal.Y, line.Normal.Z))} Thickness={N(line.Thickness)}");
                            bool planar = true;
                            try { RequirePlanarLine(line); }
                            catch (System.Exception ex) { planar = false; report.AppendLine("  PLANAR REVIEW: " + ex.Message); }
                            if (!byName.TryGetValue(data.StructureStart, out var first) || !byName.TryGetValue(data.StructureEnd, out var last))
                                throw new InvalidOperationException("OD endpoints are not uniquely represented in verified structure plans; see structure reviews.");
                            Pair("Start -> OD first", line.StartPoint, first);
                            Pair("End -> OD last", line.EndPoint, last);
                            Pair("Start -> OD last", line.StartPoint, last);
                            Pair("End -> OD first", line.EndPoint, first);
                            bool forward = LegacyMatchesTerminal(line.StartPoint, first) && LegacyMatchesTerminal(line.EndPoint, last);
                            bool reverse = LegacyMatchesTerminal(line.StartPoint, last) && LegacyMatchesTerminal(line.EndPoint, first);
                            report.AppendLine($"  legacy center gate forward={forward}, reverse={reverse}; {(forward && reverse ? "BOTH match" : !forward && !reverse ? "NEITHER matches" : "unique orientation")}");
                            var current = SewerTerminalOrientation.Plan(Coordinates(line.StartPoint), Coordinates(line.EndPoint), Terminal(first), Terminal(last));
                            report.AppendLine($"  current circle/open-end orientation: forward={current.Forward}, reverse={current.Reverse}, unique={current.Unique}; Forward: {current.ForwardReason}; Reverse: {current.ReverseReason}");
                            if (planar)
                            {
                                Probe("Forward", line, first, last, data.DiameterFeet);
                                Probe("Reverse", line, last, first, data.DiameterFeet);
                            }
                        }
                        catch (System.Exception error) { report.AppendLine($"  PIPE REVIEW {id.Handle}: {error.Message}"); }
                    }
                    report.AppendLine($"Raw candidate pipes reported={count}. All probes are diagnostic only; no association is accepted by these probes.");
                    var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (var target in new[] { (PipeLayer, "HIDDEN2"), (CenterLayer, "CENTER2"), (OuterLayer, "HIDDEN3"), (InnerLayer, "HIDDEN4") })
                    {
                        if (!layers.Has(target.Item1)) { report.AppendLine($"TARGET {target.Item1}: absent; required linetype={target.Item2}"); continue; }
                        var layer = (LayerTableRecord)tr.GetObject(layers[target.Item1], OpenMode.ForRead);
                        string linetype = ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name;
                        report.AppendLine($"TARGET {target.Item1}: locked={layer.IsLocked}, linetype={linetype}, required={target.Item2}");
                    }
                }
            }
            catch (System.Exception error) { report.AppendLine("DIAGNOSTIC STOP: " + error); }
            document.Editor.WriteMessage("\n{0}", report.ToString());

            void DescribeStructure(SewerManholePlan structure)
            {
                report.AppendLine($"STRUCTURE {structure.Name}, pointHandle={structure.SourceId.Handle}, kind={structure.OpenEndKind}, XYZ={P(structure.SourcePosition)}");
                if (structure.Outer != null)
                    report.AppendLine($"  blockHandle={structure.BlockId.Handle}; outerCenter={P(structure.Outer.Center)}, outerRadius={N(structure.Outer.Radius)}, innerRadii={string.Join(", ", structure.Inner.Select(circle => N(circle.Radius)))}");
            }
            void Pair(string label, Point3d endpoint, SewerManholePlan structure)
            {
                Vector3d delta = endpoint - structure.SourcePosition;
                double xy = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                report.AppendLine($"  {label} ({structure.Name}, {structure.OpenEndKind}): dx={N(delta.X)}, dy={N(delta.Y)}, dz={N(delta.Z)}, XYdistance={N(xy)}, legacyCenterMatch={LegacyMatchesTerminal(endpoint, structure)}");
                if (structure.Outer != null)
                {
                    Vector3d d = endpoint - structure.Outer.Center;
                    double distance = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    report.AppendLine($"    to outer center: XY={N(distance)}, dz={N(d.Z)}, signedBoundaryDistance={N(distance - structure.Outer.Radius)} (negative=inside)");
                }
            }
            void Probe(string label, Line line, SewerManholePlan start, SewerManholePlan end, double diameter)
            {
                // Pure XY probe; it never overrides the separately reported XYZ gate.
                SewerCircle? a = start.IsOpenEnd ? null : new SewerCircle(start.Name, start.Outer!.Center.X, start.Outer.Center.Y, start.Outer.Radius);
                SewerCircle? b = end.IsOpenEnd ? null : new SewerCircle(end.Name, end.Outer!.Center.X, end.Outer.Center.Y, end.Outer.Radius);
                report.AppendLine($"  {label} XYZ agreement: sourceStart={Math.Abs(line.StartPoint.Z - start.SourcePosition.Z) <= Tolerance}, sourceEnd={Math.Abs(line.EndPoint.Z - end.SourcePosition.Z) <= Tolerance}, outerStart={start.IsOpenEnd || Math.Abs(line.StartPoint.Z - start.Outer!.Center.Z) <= Tolerance}, outerEnd={end.IsOpenEnd || Math.Abs(line.EndPoint.Z - end.Outer!.Center.Z) <= Tolerance}");
                Side("center", new Vector3d(0, 0, 0));
                if (SewerPreparationRules.ClassifyDiameter(diameter) == SewerPipeSizeKind.CenterAndWalls)
                {
                    Vector3d direction = line.EndPoint - line.StartPoint;
                    double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                    Vector3d shift = new Vector3d(-direction.Y / length, direction.X / length, 0) * (diameter / 2);
                    Side("positive wall", shift); Side("negative wall", -shift);
                }
                void Side(string side, Vector3d shift)
                {
                    Point3d s = line.StartPoint + shift, e = line.EndPoint + shift;
                    var decision = SewerCircleTrim.Plan(new SewerPoint2(s.X, s.Y), new SewerPoint2(e.X, e.Y), a, b);
                    report.AppendLine($"    {label} {side} XY-only: {decision.Kind}, parameters={N(decision.StartParameter)}..{N(decision.EndParameter)}; {decision.Reason}");
                }
            }
        }
        private static string N(double number) => number.ToString("R", CultureInfo.InvariantCulture);
        private static string P(Point3d point) => $"({N(point.X)}, {N(point.Y)}, {N(point.Z)})";
    }
}
