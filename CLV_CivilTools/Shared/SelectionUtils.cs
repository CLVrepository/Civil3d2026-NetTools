using System;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Selection and isolate/unisolate helpers.
    /// Replaces selection-related pieces of CommonUtils.
    /// </summary>
    internal static class SelectionUtils
    {
        /// <summary>
        /// Isolate the given objects using ISOLATEOBJECTS, then clear implied selection.
        /// </summary>
        public static void IsolateSelection(Editor ed, ObjectId[] ids)
        {
            if (ed == null || ids == null || ids.Length == 0)
                return;

            ed.SetImpliedSelection(ids);
            ed.Command("_.ISOLATEOBJECTS");
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
        }

        /// <summary>
        /// Wrapper for UNISOLATEOBJECTS.
        /// </summary>
        public static void Unisolate(Editor ed)
        {
            if (ed == null) return;
            ed.Command("_.UNISOLATEOBJECTS");
        }
    }
}