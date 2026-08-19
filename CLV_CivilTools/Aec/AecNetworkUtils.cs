using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

using AeccStructure = Autodesk.Civil.DatabaseServices.Structure;
using AeccPipe = Autodesk.Civil.DatabaseServices.Pipe;

namespace CLV_CivilTools.Aec
{
    internal static class AecNetworkUtils
    {
        /// <summary>
        /// Find the nearest AECC structure whose name contains the given substring
        /// (e.g. "SSMH") within a max XY distance of searchRadius.
        /// Returns ObjectId.Null if none found.
        /// </summary>
        internal static ObjectId FindNearestStructureByName(
            Database db,
            Point3d ptWcs,
            string nameContains,
            double searchRadius)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(nameContains)) return ObjectId.Null;

            ObjectId bestId = ObjectId.Null;
            double bestD2 = searchRadius * searchRadius;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        if (tr.GetObject(id, OpenMode.ForRead, false) is AeccStructure st)
                        {
                            string nm = (st.Name ?? string.Empty);
                            if (nm.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            Point3d loc = st.Location;
                            double dx = loc.X - ptWcs.X;
                            double dy = loc.Y - ptWcs.Y;
                            double d2 = dx * dx + dy * dy;

                            if (d2 < bestD2)
                            {
                                bestD2 = d2;
                                bestId = id;
                            }
                        }
                    }
                }

                tr.Commit();
            }

            return bestId;
        }

        /// <summary>
        /// Move a structure in XY only, preserving Z.
        /// </summary>
        internal static void MoveStructureXyKeepZ(
            Transaction tr,
            AeccStructure st,
            Point3d newXy)
        {
            if (st == null) throw new ArgumentNullException(nameof(st));

            Point3d old = st.Location;
            st.Location = new Point3d(newXy.X, newXy.Y, old.Z);
        }

        /// <summary>
        /// Get all pipes whose start or end structure is the given structure.
        /// </summary>
        internal static List<AeccPipe> GetPipesForStructure(
            Transaction tr,
            Database db,
            AeccStructure structure)
        {
            var result = new List<AeccPipe>();
            if (structure == null) return result;

            ObjectId targetId = structure.ObjectId;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!id.IsValid || id.IsErased) continue;

                    if (tr.GetObject(id, OpenMode.ForRead, false) is AeccPipe pipe)
                    {
                        if (pipe.StartStructureId == targetId ||
                            pipe.EndStructureId == targetId)
                        {
                            result.Add(pipe);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Move a pipe end (start or end) horizontally to a new XY, keeping the
        /// original invert elevation.
        /// </summary>
        internal static void MovePipeEndXyKeepZ(
            AeccPipe pipe,
            bool isStartEnd,
            Point3d newXy)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            if (isStartEnd)
            {
                Point3d p = pipe.StartPoint;
                pipe.StartPoint = new Point3d(newXy.X, newXy.Y, p.Z);
            }
            else
            {
                Point3d p = pipe.EndPoint;
                pipe.EndPoint = new Point3d(newXy.X, newXy.Y, p.Z);
            }
        }
    }
}