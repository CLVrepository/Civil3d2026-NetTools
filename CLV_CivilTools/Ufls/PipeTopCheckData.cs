using System;
using System.Globalization;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Persists structured data on PIPE TOP CHECK MText objects so later reporting
    /// and exhibit-generation tools can work from drawing data rather than parsing text.
    /// </summary>
    internal static class PipeTopCheckData
    {
        private const string RecordName = "CLV_PIPE_TOP_CHECK";
        private const string Marker = "PIPE_TOP_CHECK";
        private const int SchemaVersion = 1;

        internal enum DisplayMode
        {
            Detailed = 0,
            Exhibit = 1
        }

        internal readonly record struct Snapshot(
            Guid Id,
            string PointNumber,
            ObjectId CogoPointId,
            ObjectId PipeId,
            string PipeHandle,
            string PipeName,
            Point3d CheckPointLocation,
            Point3d LabelLocation,
            double PlanTopElevation,
            double SurveyTopElevation,
            double Difference,
            double Station,
            double Offset,
            DisplayMode Mode,
            string ExhibitId,
            DateTime CreatedUtc);

        internal static void Write(DBObject owner, Transaction tr, Snapshot snapshot)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (owner.ExtensionDictionary.IsNull)
                owner.CreateExtensionDictionary();

            DBDictionary extensionDictionary =
                (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForWrite, false);

            Xrecord record = extensionDictionary.Contains(RecordName)
                ? (Xrecord)tr.GetObject(extensionDictionary.GetAt(RecordName), OpenMode.ForWrite, false)
                : new Xrecord();

            record.Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, Marker),
                new TypedValue((int)DxfCode.Int32, SchemaVersion),
                new TypedValue((int)DxfCode.Text, snapshot.Id.ToString("D")),
                new TypedValue((int)DxfCode.Text, snapshot.PointNumber ?? string.Empty),
                new TypedValue((int)DxfCode.SoftPointerId, snapshot.CogoPointId),
                new TypedValue((int)DxfCode.SoftPointerId + 1, snapshot.PipeId),
                new TypedValue((int)DxfCode.Text, snapshot.PipeHandle ?? string.Empty),
                new TypedValue((int)DxfCode.Text, snapshot.PipeName ?? string.Empty),
                new TypedValue((int)DxfCode.XCoordinate, snapshot.CheckPointLocation),
                new TypedValue((int)DxfCode.XCoordinate + 1, snapshot.LabelLocation),
                new TypedValue((int)DxfCode.Real, snapshot.PlanTopElevation),
                new TypedValue((int)DxfCode.Real + 1, snapshot.SurveyTopElevation),
                new TypedValue((int)DxfCode.Real + 2, snapshot.Difference),
                new TypedValue((int)DxfCode.Real + 3, snapshot.Station),
                new TypedValue((int)DxfCode.Real + 4, snapshot.Offset),
                new TypedValue((int)DxfCode.Int16, (short)snapshot.Mode),
                new TypedValue((int)DxfCode.Text, snapshot.ExhibitId ?? string.Empty),
                new TypedValue((int)DxfCode.Text, snapshot.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));

            if (record.ObjectId.IsNull)
            {
                extensionDictionary.SetAt(RecordName, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
        }

        internal static bool TryRead(DBObject owner, Transaction tr, out Snapshot snapshot)
        {
            snapshot = default;

            if (owner == null || owner.ExtensionDictionary.IsNull)
                return false;

            DBDictionary extensionDictionary =
                (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForRead, false);

            if (!extensionDictionary.Contains(RecordName))
                return false;

            Xrecord record = (Xrecord)tr.GetObject(extensionDictionary.GetAt(RecordName), OpenMode.ForRead, false);
            ResultBuffer? data = record.Data;
            if (data == null)
                return false;

            string marker = string.Empty;
            int schema = 0;
            Guid id = Guid.Empty;
            string pointNumber = string.Empty;
            ObjectId cogoPointId = ObjectId.Null;
            ObjectId pipeId = ObjectId.Null;
            string pipeHandle = string.Empty;
            string pipeName = string.Empty;
            Point3d checkPointLocation = Point3d.Origin;
            Point3d labelLocation = Point3d.Origin;
            double planTopElevation = double.NaN;
            double surveyTopElevation = double.NaN;
            double difference = double.NaN;
            double station = double.NaN;
            double offset = double.NaN;
            DisplayMode mode = DisplayMode.Detailed;
            string exhibitId = string.Empty;
            DateTime createdUtc = DateTime.MinValue;

            foreach (TypedValue value in data)
            {
                switch (value.TypeCode)
                {
                    case (int)DxfCode.Text:
                        if (string.IsNullOrEmpty(marker))
                            marker = value.Value?.ToString() ?? string.Empty;
                        else if (id == Guid.Empty)
                        {
                            _ = Guid.TryParse(value.Value?.ToString(), out id);
                        }
                        else if (string.IsNullOrEmpty(pointNumber))
                            pointNumber = value.Value?.ToString() ?? string.Empty;
                        else if (string.IsNullOrEmpty(pipeHandle))
                            pipeHandle = value.Value?.ToString() ?? string.Empty;
                        else if (string.IsNullOrEmpty(pipeName))
                            pipeName = value.Value?.ToString() ?? string.Empty;
                        else if (string.IsNullOrEmpty(exhibitId))
                            exhibitId = value.Value?.ToString() ?? string.Empty;
                        else
                        {
                            _ = DateTime.TryParse(
                                value.Value?.ToString(),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind,
                                out createdUtc);
                        }
                        break;

                    case (int)DxfCode.Int32:
                        schema = Convert.ToInt32(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.SoftPointerId:
                        cogoPointId = value.Value is ObjectId cogoId ? cogoId : ObjectId.Null;
                        break;

                    case (int)DxfCode.SoftPointerId + 1:
                        pipeId = value.Value is ObjectId pipeObjectId ? pipeObjectId : ObjectId.Null;
                        break;

                    case (int)DxfCode.XCoordinate:
                        if (value.Value is Point3d checkPoint)
                            checkPointLocation = checkPoint;
                        break;

                    case (int)DxfCode.XCoordinate + 1:
                        if (value.Value is Point3d labelPoint)
                            labelLocation = labelPoint;
                        break;

                    case (int)DxfCode.Real:
                        planTopElevation = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.Real + 1:
                        surveyTopElevation = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.Real + 2:
                        difference = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.Real + 3:
                        station = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.Real + 4:
                        offset = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                        break;

                    case (int)DxfCode.Int16:
                        mode = (DisplayMode)Convert.ToInt16(value.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }

            if (!string.Equals(marker, Marker, StringComparison.Ordinal) || schema != SchemaVersion || id == Guid.Empty)
                return false;

            snapshot = new Snapshot(
                id,
                pointNumber,
                cogoPointId,
                pipeId,
                pipeHandle,
                pipeName,
                checkPointLocation,
                labelLocation,
                planTopElevation,
                surveyTopElevation,
                difference,
                station,
                offset,
                mode,
                exhibitId,
                createdUtc);

            return true;
        }
    }
}
