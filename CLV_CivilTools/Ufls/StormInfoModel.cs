using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Ufls.StormInfo
{
    internal sealed class StormCatalog
    {
        public string SchemaVersion { get; set; } = "clv.storm-structure-catalog/1.0";
        public string ProjectNumber { get; set; } = "";
        public string System { get; set; } = "storm";
        public StormUnits Units { get; set; } = new();
        public List<StormStructure> Structures { get; set; } = new();
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    internal sealed class StormUnits
    {
        public string Elevations { get; set; } = "feet";
        public string Diameters { get; set; } = "inches";
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    internal sealed class StormStructure
    {
        public StormStructureNumber Number { get; set; }
        public string Name { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Type { get; set; } = "SDMH";
        public double? RimElevation { get; set; }
        public double? StructureDiameterInches { get; set; }
        public double? StructureWidthInches { get; set; }
        public double? StructureLengthInches { get; set; }
        public string TopElevationBasis { get; set; } = "unknown";
        public string InletType { get; set; } = "";
        public double? InletOpeningLengthInches { get; set; }
        public string SourceTopLabel { get; set; } = "";
        public double? SourceTopElevation { get; set; }
        public string ApplicationHoldReason { get; set; } = "";
        public string? StructureDiameterBasis { get; set; }
        public string? StructureDiameterSourceLabel { get; set; }
        public string? Station { get; set; }
        public string ReviewStatus { get; set; } = "needs_user_review";
        public string Completeness { get; set; } = "manual";
        public string Notes { get; set; } = "";
        public List<StormConnection> Connections { get; set; } = new();
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    internal sealed class StormConnection
    {
        public string ConnectionId { get; set; } = "";
        public string Flow { get; set; } = "in";
        public string Direction { get; set; } = "";
        public double? InvertElevation { get; set; }
        public double? DiameterInches { get; set; }
        public string Shape { get; set; } = "circular";
        public double? WidthInches { get; set; }
        public double? HeightInches { get; set; }
        public string? MaterialCode { get; set; }
        public string? Material { get; set; }
        public string? PipeSpecification { get; set; }
        public string? MaterialSourceLabel { get; set; }
        public string? ConnectedStructureName { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    internal static class StormInfoModel
    {
        internal sealed record ConnectionAssignment(int PipeIndex, int ConnectionIndex);
        internal sealed record AssignmentPlan(ConnectionAssignment[] Assignments, int[] PendingConnections, int[] UnchangedPipes);
        internal sealed record NativeConnection(bool AtStart, double OutwardDx, double OutwardDy);
        internal static (double Dx, double Dy) CurveOutwardTangent(double radiusX, double radiusY, bool clockwise, bool atArcStart)
        {
            if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || (Math.Abs(radiusX) < 1e-9 && Math.Abs(radiusY) < 1e-9))
                throw new ArgumentException("Cannot determine a tangent from an invalid arc radius vector.");
            double sign = (clockwise ? -1 : 1) * (atArcStart ? 1 : -1);
            return (-radiusY * sign, radiusX * sign);
        }
        internal sealed record AutomaticPlan(AssignmentPlan Plan, string[] Evidence, int[] DirectionFallbackPipes);
        internal const double CompassSectorHalfWidth = 22.5;
        internal const double UniqueDirectionLimit = CompassSectorHalfWidth + 5;
        internal static AutomaticPlan MatchAutomatically(IReadOnlyList<StormConnection> connections, IReadOnlyList<NativeConnection> native)
        {
            var choices = new int?[native.Count]; var evidence = new string[native.Count];
            var directionFallbackPipes = new List<int>();
            for (int i = 0; i < native.Count; i++)
            {
                choices[i] = -1;
                string role = native[i].AtStart ? "out" : "in";
                double? azimuth = Bearing(native[i].OutwardDx, native[i].OutwardDy);
                int[] roleCandidates = Enumerable.Range(0, connections.Count).Where(j => connections[j].Flow == role).ToArray();
                int[] candidates = roleCandidates.Where(j =>
                {
                    double? direction = Compass(connections[j].Direction);
                    return !azimuth.HasValue || !direction.HasValue || AngularDifference(azimuth.Value, direction.Value) < CompassSectorHalfWidth - 1e-8;
                }).ToArray();
                // Only relax a small compass-boundary discrepancy for a globally unique role pair.
                // Count ALL rows of that role, not unmatched leftovers or only rows with known inverts.
                // A missing bearing/direction cannot justify this bounded-angle fallback.
                bool directionFallback = false;
                if (candidates.Length == 0 && roleCandidates.Length == 1 && azimuth.HasValue
                    && native.Count(p => p.AtStart == native[i].AtStart) == 1)
                {
                    double? direction = Compass(connections[roleCandidates[0]].Direction);
                    if (direction.HasValue && AngularDifference(azimuth.Value, direction.Value) <= UniqueDirectionLimit + 1e-8)
                    {
                        candidates = roleCandidates;
                        directionFallback = true;
                    }
                }
                string basis = (native[i].AtStart ? "START -> OUT" : "END -> IN")
                    + (azimuth.HasValue ? "; WCS outward azimuth " + azimuth.Value.ToString("0.###", CultureInfo.InvariantCulture) + " deg" : "; no horizontal bearing");
                if (candidates.Length == 0) evidence[i] = basis + "; no matching plan role/direction. Native pipe unchanged.";
                else if (candidates.Length > 1) evidence[i] = basis + "; multiple matching plan connections. Native pipe unchanged.";
                else if (!connections[candidates[0]].InvertElevation.HasValue) evidence[i] = basis + "; matching plan invert is unknown. Native pipe unchanged.";
                else
                {
                    choices[i] = candidates[0];
                    if (directionFallback)
                    {
                        directionFallbackPipes.Add(i);
                        double difference = AngularDifference(azimuth!.Value, Compass(connections[candidates[0]].Direction)!.Value);
                        evidence[i] = basis + "; WARNING: bounded direction fallback; exactly one native and one plan "
                            + role.ToUpperInvariant() + "; plan " + connections[candidates[0]].Direction
                            + "; angular difference " + difference.ToString("0.###", CultureInfo.InvariantCulture)
                            + " deg (" + Math.Max(0, difference - CompassSectorHalfWidth).ToString("0.###", CultureInfo.InvariantCulture)
                            + " deg beyond normal sector; maximum extra 5 deg). Verify this connection before applying.";
                    }
                    else evidence[i] = basis + (azimuth.HasValue && Compass(connections[candidates[0]].Direction).HasValue ? "; matched plan compass direction." : "; unique compatible role; direction unavailable.");
                }
            }
            // No pipe wins because of enumeration order when two pipes claim one plan connection.
            foreach (var conflict in Enumerable.Range(0, choices.Length).Where(i => choices[i] >= 0).GroupBy(i => choices[i]!.Value).Where(g => g.Count() > 1))
                foreach (int i in conflict) { choices[i] = -1; evidence[i] = "Multiple native pipes match the same plan connection. Native pipe unchanged."; }
            return new AutomaticPlan(PlanAssignments(connections, choices), evidence, directionFallbackPipes.ToArray());
        }
        private static double? Bearing(double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)) return null;
            return (Math.Atan2(dx, dy) * 180 / Math.PI + 360) % 360;
        }
        private static double AngularDifference(double a, double b)
        { double delta = Math.Abs(a - b) % 360; return Math.Min(delta, 360 - delta); }
        private static double? Compass(string? direction)
        {
            string key = (direction ?? "").ToUpperInvariant().Replace(" ", "").Replace(".", "").Replace("-", "").Replace("/", "");
            return key switch
            {
                "N" or "NORTH" => 0, "NE" or "NORTHEAST" => 45, "E" or "EAST" => 90,
                "SE" or "SOUTHEAST" => 135, "S" or "SOUTH" => 180, "SW" or "SOUTHWEST" => 225,
                "W" or "WEST" => 270, "NW" or "NORTHWEST" => 315, _ => null
            };
        }
        internal static AssignmentPlan PlanAssignments(IReadOnlyList<StormConnection> connections, IReadOnlyList<int?> choices)
        {
            var assignments = new List<ConnectionAssignment>(); var used = new HashSet<int>(); var unchanged = new List<int>();
            for (int i = 0; i < choices.Count; i++)
            {
                int choice = choices[i] ?? throw new ArgumentException("Choose a plan connection or explicitly leave pipe row " + (i + 1) + " unchanged.");
                if (choice == -1) { unchanged.Add(i); continue; }
                if (choice < 0 || choice >= connections.Count) throw new ArgumentException("Invalid plan connection choice.");
                if (!used.Add(choice)) throw new ArgumentException("The same plan connection cannot be assigned to multiple existing pipes.");
                if (!connections[choice].InvertElevation.HasValue) throw new ArgumentException("Selected plan connection has an unknown invert. Complete it or explicitly leave that pipe unchanged.");
                assignments.Add(new ConnectionAssignment(i, choice));
            }
            return new AssignmentPlan(assignments.ToArray(), Enumerable.Range(0, connections.Count).Where(i => !used.Contains(i)).ToArray(), unchanged.ToArray());
        }
        internal const int MaxJsonLength = 8 * 1024 * 1024;
        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true, MaxDepth = 48
        };
        internal static string Name(string project, StormStructureNumber number, string type = "SDMH")
        {
            if (!Regex.IsMatch(project, @"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$") || project.Length > 64)
                throw new ArgumentException("Project number must contain letters/numbers separated by dashes.");
            if (type is not ("SDMH" or "SDDI")) throw new ArgumentException("Structure type must be SDMH or SDDI.");
            return project + "-" + type + "-" + number.NameToken;
        }
        internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
        internal static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(Serialize(value), Json)
            ?? throw new ArgumentException("Empty data.");
        internal static StormCatalog Parse(string text)
        {
            if (text.Length > MaxJsonLength) throw new ArgumentException("Catalog exceeds the 8 MiB text limit.");
            // Reject duplicate property keys instead of silently accepting the last value.
            using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 48 });
            CheckKeys(doc.RootElement);
            StormCatalog c = JsonSerializer.Deserialize<StormCatalog>(text, Json)
                ?? throw new ArgumentException("Empty catalog.");
            Validate(c); return c;
        }
        internal static StormCatalog Merge(StormCatalog? existing, StormCatalog incoming)
        {
            Validate(incoming);
            if (existing == null) return Copy(incoming);
            Validate(existing);
            if (existing.ProjectNumber != incoming.ProjectNumber) throw new ArgumentException("Different project catalogs cannot be merged in this starter. Use a separate drawing.");
            string? Datum(StormCatalog c) => c.Extra != null && c.Extra.TryGetValue("verticalDatum", out JsonElement v)
                && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? oldDatum = Datum(existing), newDatum = Datum(incoming);
            if (!string.IsNullOrWhiteSpace(oldDatum) && !string.IsNullOrWhiteSpace(newDatum) && !string.Equals(oldDatum, newDatum, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Catalogs declare different vertical datums. Resolve the datum before importing; no transformation is performed.");
            StormCatalog merged = Copy(existing); merged.Extra ??= new();
            if (oldDatum == null && incoming.Extra != null && incoming.Extra.TryGetValue("verticalDatum", out JsonElement datum)) merged.Extra["verticalDatum"] = datum.Clone();
            StormCatalog metadata = Copy(incoming); metadata.Structures.Clear();
            merged.Extra["lastImportMetadata"] = JsonSerializer.SerializeToElement(metadata, Json);
            foreach (StormStructure m in incoming.Structures) { merged.Structures.RemoveAll(x => x.Name == m.Name); merged.Structures.Add(Copy(m)); }
            Validate(merged); return merged;
        }
        private static void CheckKeys(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty p in e.EnumerateObject())
                { if (!seen.Add(p.Name)) throw new ArgumentException("Duplicate JSON property: " + p.Name); CheckKeys(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (JsonElement v in e.EnumerateArray()) CheckKeys(v);
        }
        internal static void Validate(StormCatalog c)
        {
            if (c.SchemaVersion is not ("clv.storm-structure-catalog/0.1-draft" or "clv.storm-structure-catalog/0.2-draft" or "clv.storm-structure-catalog/1.0"))
                throw new ArgumentException("Unsupported storm catalog schema version.");
            Name(c.ProjectNumber, 1);
            if (!string.Equals(c.System, "storm", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("This routine accepts storm records only.");
            if (c.Units == null || c.Units.Elevations != "feet" || c.Units.Diameters != "inches")
                throw new ArgumentException("This starter requires elevations in feet and diameters in inches.");
            if (c.Structures == null || c.Structures.Count > 10000) throw new ArgumentException("Invalid structure list (maximum 10000).");
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (StormStructure m in c.Structures)
            {
                if (m == null || !m.Number.IsValid || !numbers.Add(m.Type + ":" + m.Number.NameToken)) throw new ArgumentException("Missing, invalid or duplicate structure identifier.");
                if (m.Name != Name(c.ProjectNumber, m.Number, m.Type) || m.Type is not ("SDMH" or "SDDI"))
                    throw new ArgumentException("Name must follow project-SDMH-identifier or project-SDDI-identifier, with at least two digits for the numeric part and uppercase letters.");
                Finite(m.RimElevation, "Rim/top"); Positive(m.StructureDiameterInches, "Structure diameter");
                Finite(m.SourceTopElevation, "Source top"); Positive(m.InletOpeningLengthInches, "Opening length");
                Positive(m.StructureWidthInches, "Structure width"); Positive(m.StructureLengthInches, "Structure length");
                if (m.StructureWidthInches.HasValue != m.StructureLengthInches.HasValue)
                    throw new ArgumentException(m.Name + ": enter both rectangular structure dimensions or leave both blank.");
                if (m.StructureDiameterInches.HasValue && m.StructureWidthInches.HasValue)
                    throw new ArgumentException(m.Name + ": use a round diameter OR rectangular width/length; do not treat inlet length as diameter.");
                if (m.TopElevationBasis is not ("unknown" or "rim" or "grate" or "flowline" or "top"))
                    throw new ArgumentException(m.Name + ": topElevationBasis must be unknown, rim, grate, flowline or top.");
                if (m.Connections == null || m.Connections.Count > 32) throw new ArgumentException(m.Name + ": maximum 32 pipe connections.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (StormConnection p in m.Connections)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.ConnectionId) || !ids.Add(p.ConnectionId))
                        throw new ArgumentException(m.Name + ": null/duplicate/blank connection identity.");
                    if (p.Flow is not ("in" or "out")) throw new ArgumentException(m.Name + ": pipe flow must be in or out.");
                    Finite(p.InvertElevation, "Invert"); Positive(p.DiameterInches, "Pipe diameter");
                    Positive(p.WidthInches, "Pipe width"); Positive(p.HeightInches, "Pipe height");
                    if (p.Shape is not ("circular" or "rectangular")) throw new ArgumentException("Supported pipe shapes: circular, rectangular.");
                    if (p.Shape == "rectangular" && (!p.WidthInches.HasValue || !p.HeightInches.HasValue || p.DiameterInches.HasValue))
                        throw new ArgumentException("A box pipe requires width and height, with no circular diameter.");
                    if (p.Shape == "circular" && (p.WidthInches.HasValue || p.HeightInches.HasValue))
                        throw new ArgumentException("Circular pipe dimensions belong in diameterInches.");
                    if (m.RimElevation.HasValue && p.InvertElevation >= m.RimElevation)
                        throw new ArgumentException(m.Name + ": pipe invert must be below rim.");
                }
            }
        }
        private static void Finite(double? value, string field)
        { if (value.HasValue && (!double.IsFinite(value.Value) || Math.Abs(value.Value) > 1e8)) throw new ArgumentException(field + " must be finite and within +/-100000000."); }
        private static void Positive(double? value, string field)
        { Finite(value, field); if (value.HasValue && value <= 0) throw new ArgumentException(field + " must be positive or blank."); }
        internal static string Number(double? x) => x.HasValue ? x.Value.ToString("0.########", CultureInfo.InvariantCulture) : "";
        internal static string Elevation(double? x) => x.HasValue ? x.Value.ToString("0.00####", CultureInfo.InvariantCulture) : "UNKNOWN";
        internal static double? ReadNumber(string text, string field)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                throw new ArgumentException(field + ": use a decimal number (period separator) or leave blank.");
            Finite(n, field); return n;
        }
        internal static string Code(string? text) => (text ?? "").Trim().ToUpperInvariant().Replace("C-900", "C900").Replace("SDR-35", "SDR35");
        internal static string PipeLabel(StormConnection p)
        {
            string[] materials = new[] { p.MaterialCode, p.Material, p.PipeSpecification }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return p.Flow.ToUpperInvariant() + " " + p.Direction + " | INV " + Elevation(p.InvertElevation)
                + " ft | " + (p.Shape == "rectangular" ? Number(p.WidthInches) + " x " + Number(p.HeightInches) + " RECT" : p.DiameterInches.HasValue ? Number(p.DiameterInches) : "?") + " in | "
                + (materials.Length == 0 ? "MATERIAL ?" : string.Join(" / ", materials));
        }
        internal static string StructureSize(StormStructure m) => m.StructureWidthInches.HasValue
            ? "STRUCTURE = " + Number(m.StructureWidthInches) + " x " + Number(m.StructureLengthInches) + " in RECT"
            : "STRUCTURE = " + (m.StructureDiameterInches.HasValue ? Number(m.StructureDiameterInches) + " in " + (m.StructureDiameterBasis ?? "") : "UNKNOWN")
                + (m.InletOpeningLengthInches.HasValue ? " | OPENING L = " + Number(m.InletOpeningLengthInches) + " in" : "");
        internal static Dictionary<string, string> Attributes(StormCatalog c, StormStructure m)
        {
            var a = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STRUCT_NAME"] = m.Name, ["RIM"] = (m.TopElevationBasis == "unknown" ? "RIM/GRATE" : m.TopElevationBasis.ToUpperInvariant()) + " = " + Elevation(m.RimElevation) + " ft"
                    + (m.SourceTopElevation.HasValue ? " | SOURCE " + m.SourceTopLabel + " = " + Elevation(m.SourceTopElevation) : ""),
                ["STRUCT_SIZE"] = StructureSize(m),
                ["SOURCE"] = "PLAN " + m.SourceId + " | STA " + (m.Station ?? "?") + " | REVIEW VALUES"
            };
            for (int i = 0; i < m.Connections.Count; i++) a["CONN_" + (i + 1).ToString("D2")] = PipeLabel(m.Connections[i]);
            return a;
        }
    }
}



