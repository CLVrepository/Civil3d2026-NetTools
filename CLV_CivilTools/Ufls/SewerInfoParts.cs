using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    internal static class SewerInfoParts
    {
        internal sealed record Target(ObjectId Family, ObjectId Size, string Description, double? FrameInches);
        private sealed record SizeInfo(ObjectId Id, string Name, double? PipeId, double? Barrel, double? Frame);

        private static double? Dimension(PartDataRecord data, PartContextType context)
        {
            if (!data.GetSupportedContexts().Contains(context)) return null;
            PartDataField field = data.GetDataFieldBy(context);
            if (!double.TryParse(Convert.ToString(field.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return null;
            return SewerInfoPartRules.Inches(value, field.Units);
        }
        private static SizeInfo Read(PartSize size)
        {
            using PartDataRecord data = size.SizeDataRecord;
            return new SizeInfo(size.ObjectId, size.Name, Dimension(data, PartContextType.PipeInnerDiameter),
                Dimension(data, PartContextType.StructInnerDiameter), Dimension(data, PartContextType.StructFrameDiameter));
        }
        private static SizeInfo[] Sizes(PartFamily family, Transaction tr)
        {
            var result = new List<SizeInfo>();
            for (int i = 0; i < family.PartSizeCount; i++) result.Add(Read((PartSize)tr.GetObject(family[i], OpenMode.ForRead)));
            return result.ToArray();
        }
        private static SizeInfo Choose(SizeInfo[] candidates, ObjectId current, string reason, string available)
        {
            SizeInfo? same = candidates.SingleOrDefault(x => x.Id == current);
            if (same != null) return same;
            if (candidates.Length != 1) throw new InvalidOperationException(reason + ": " + candidates.Length + " exact sizes found. Add the required size to the network parts list or resolve duplicate variants. Available sizes: " + available);
            return candidates[0];
        }
        private static bool Equal(double? a, double b) => a.HasValue && Math.Abs(a.Value - b) < 0.0001;
        internal static Target Pipe(Pipe p, Network network, SewerConnection c, Transaction tr)
        {
            string? wantedFamily = SewerInfoPartRules.Family(c);
            ObjectId familyId = p.PartFamilyId;
            if (wantedFamily != null)
            {
                if (network.PartsListId.IsNull) throw new InvalidOperationException("Network has no assigned parts list.");
                var list = (PartsList)tr.GetObject(network.PartsListId, OpenMode.ForRead);
                PartFamily[] families = list.GetPartFamilyIdsByDomain(DomainType.Pipe).Cast<ObjectId>()
                    .Select(id => (PartFamily)tr.GetObject(id, OpenMode.ForRead)).Where(f => string.Equals(f.Name, wantedFamily, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (families.Length != 1) throw new InvalidOperationException("Pipe " + p.Handle + ": required family '" + wantedFamily + "' not uniquely available in network parts list '" + list.Name + "'. Add that family and retry.");
                familyId = families[0].ObjectId;
            }
            var family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
            double? nominal = c.DiameterInches;
            if (!nominal.HasValue && familyId != p.PartFamilyId)
                nominal = SewerInfoPartRules.PipeNominal(p.PartFamilyName, p.PartSizeName, p.InnerHeight * 12);
            if (!nominal.HasValue)
            {
                if (familyId != p.PartFamilyId) throw new InvalidOperationException("Pipe " + p.Handle + ": nominal diameter unknown; cannot choose the new material's size.");
                return new Target(familyId, p.GetPartSizeId(), p.PartFamilyName + " / " + p.PartSizeName + " (plan diameter unknown; existing size retained)", null);
            }
            SizeInfo[] sizes = Sizes(family, tr);
            SizeInfo selected = Choose(sizes.Where(s => Equal(SewerInfoPartRules.PipeNominal(family.Name, s.Name, s.PipeId), nominal.Value)).ToArray(), p.GetPartSizeId(),
                "Pipe " + p.Handle + ": nominal " + SewerInfoModel.Number(nominal) + " in in " + family.Name, string.Join(" | ", sizes.Select(s => s.Name)));
            return new Target(familyId, selected.Id, family.Name + " / " + selected.Name + " (nominal " + SewerInfoModel.Number(nominal) + " in; " + (wantedFamily == null ? "plan material unknown, family retained" : "plan material " + wantedFamily) + ")", null);
        }
        internal static Target Structure(Structure s, SewerManhole m, Transaction tr)
        {
            if (!m.StructureDiameterInches.HasValue) return new Target(s.PartFamilyId, s.GetPartSizeId(), s.PartFamilyName + " / " + s.PartSizeName + " (plan diameter unknown; unchanged)", null);
            string basis = (m.StructureDiameterBasis ?? "").ToLowerInvariant();
            if (basis.Length > 0 && !basis.Contains("base") && !basis.Contains("barrel") && !basis.Contains("inner"))
                throw new InvalidOperationException("Structure diameter basis '" + basis + "' is not a base/barrel/inner diameter. Complete the record before resizing.");
            var family = (PartFamily)tr.GetObject(s.PartFamilyId, OpenMode.ForRead);
            SizeInfo[] sizes = Sizes(family, tr);
            double frame = s.FrameDiameter * 12;
            if (!double.IsFinite(frame) || frame <= 0) throw new InvalidOperationException("Cannot determine current structure frame/opening diameter; structure size was not guessed.");
            SizeInfo selected = Choose(sizes.Where(x => Equal(x.Barrel, m.StructureDiameterInches.Value) && Equal(x.Frame, frame)).ToArray(), s.GetPartSizeId(),
                "Structure " + s.Handle + ": barrel " + SewerInfoModel.Number(m.StructureDiameterInches) + " in; retain frame " + SewerInfoModel.Number(frame) + " in in " + family.Name,
                string.Join(" | ", sizes.Select(x => x.Name + " [barrel=" + SewerInfoModel.Number(x.Barrel) + ", frame=" + SewerInfoModel.Number(x.Frame) + "]")));
            return new Target(family.ObjectId, selected.Id, family.Name + " / " + selected.Name + " (same family; frame " + SewerInfoModel.Number(frame) + " in retained)", frame);
        }
    }
}
