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
        internal sealed record Target(ObjectId Family, ObjectId Size, string Description, double? FrameInches, double? PipeInnerInches = null);
        private sealed record SizeInfo(ObjectId Id, string Name, double? PipeId, double? Barrel, double? Frame, string PipeData, string? Material);

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
            var contexts = data.GetSupportedContexts();
            string pipeData = "PipeInnerDiameter unavailable";
            double? pipeId = null;
            if (contexts.Contains(PartContextType.PipeInnerDiameter))
            {
                PartDataField field = data.GetDataFieldBy(PartContextType.PipeInnerDiameter);
                string raw = Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "<null>";
                pipeId = Dimension(data, PartContextType.PipeInnerDiameter);
                pipeData = "PipeInnerDiameter=" + raw + " [" + field.Units + "]" +
                    (pipeId.HasValue ? " = " + SewerInfoModel.Number(pipeId) + " in" : " (invalid value or unsupported units)");
            }
            string? material = contexts.Contains(PartContextType.Material)
                ? Convert.ToString(data.GetDataFieldBy(PartContextType.Material).Value, CultureInfo.InvariantCulture) : null;
            return new SizeInfo(size.ObjectId, size.Name, pipeId,
                Dimension(data, PartContextType.StructInnerDiameter), Dimension(data, PartContextType.StructFrameDiameter),
                pipeData, material);
        }
        private static string PipeEvidence(SizeInfo size, string family)
        {
            double? nominal = SewerInfoPartRules.PipeNominal(family, size.PipeId);
            string? problem = SewerInfoPartRules.MaterialProblem(family, size.Material);
            return size.Name + " [" + size.PipeData +
                "; nominal=" + SewerInfoModel.Number(nominal) + " in" +
                "; Material=" + (string.IsNullOrWhiteSpace(size.Material) ? "<absent; exact family used>" : size.Material) +
                (problem == null ? "" : "; REJECTED: " + problem) + "]";
        }
        private static SizeInfo[] Sizes(PartFamily family, Transaction tr)
        {
            var result = new List<SizeInfo>();
            for (int i = 0; i < family.PartSizeCount; i++) result.Add(Read((PartSize)tr.GetObject(family[i], OpenMode.ForRead)));
            return result.ToArray();
        }
        private static SizeInfo Choose(SizeInfo[] candidates, ObjectId current, string reason, string available)
        {
            int selected = SewerInfoPartRules.SelectPartCandidate(candidates.Select(x => x.Id).ToArray(), current);
            if (selected < 0) throw new InvalidOperationException(reason + ": " + candidates.Length + " matching sizes found. " + (candidates.Length == 0 ? "No compatible numeric definition was resolved. Check the parameters and units below before adding any size; a displayed name is not proof that a definition is missing." : "Multiple compatible definitions remain; resolve duplicate variants before swapping.") + " Available sizes: " + available);
            return candidates[selected];
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
                nominal = SewerInfoPartRules.PipeNominal(p.PartFamilyName, p.InnerHeight * 12);
            if (!nominal.HasValue)
            {
                if (familyId != p.PartFamilyId) throw new InvalidOperationException("Pipe " + p.Handle + ": nominal diameter unknown; cannot choose the new material's size.");
                return new Target(familyId, p.GetPartSizeId(), p.PartFamilyName + " / " + p.PartSizeName + " (plan diameter unknown; existing size retained)", null);
            }
            SizeInfo[] sizes = Sizes(family, tr);
            SizeInfo selected = Choose(sizes.Where(s => Equal(SewerInfoPartRules.PipeNominal(family.Name, s.PipeId), nominal.Value) && SewerInfoPartRules.MaterialProblem(family.Name, s.Material) == null).ToArray(), p.GetPartSizeId(),
                "Pipe " + p.Handle + ": nominal " + SewerInfoModel.Number(nominal) + " in in " + family.Name, string.Join(" | ", sizes.Select(s => PipeEvidence(s, family.Name))));
            return new Target(familyId, selected.Id, family.Name + " / " + PipeEvidence(selected, family.Name) + " (" +
                SewerInfoPartRules.PipeDimensionBasis(family.Name, selected.PipeId!.Value, nominal.Value) + "; " +
                (wantedFamily == null ? "plan material unknown, family retained" : "plan material " + wantedFamily) + ")", null, selected.PipeId);
        }
        internal static void VerifyPipeDimensions(Pipe pipe, Target target)
        {
            if (target.PipeInnerInches.HasValue &&
                !SewerInfoPartRules.SamePipeDimension(pipe.InnerHeight * 12, target.PipeInnerInches.Value))
                throw new InvalidOperationException("Pipe " + pipe.Handle +
                    ": native inside height does not match the resolved numeric part definition. Changes rolled back.");
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
