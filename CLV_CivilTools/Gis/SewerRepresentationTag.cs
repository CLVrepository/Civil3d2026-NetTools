using System;
using System.Collections.Generic;

namespace CLV_CivilTools.Gis
{
    // Exact native payload shape and ownership proof, not an application-name allowlist.
    // The 1071 Int32 is opaque native metadata: capture and preserve it, never infer a role.
    internal static class SewerRepresentationTag
    {
        internal const string Application = "AcDbBlockRepETag";
        internal readonly record struct Value(int Code, object Data);

        internal static bool IsExact(IReadOnlyList<Value> values, string selfHandle)
            => values.Count == 4 && values[0].Code == 1001 && values[0].Data is string app && app == Application
            && values[1].Code == 1070 && values[1].Data is short small && small == 1
            && values[2].Code == 1071 && values[2].Data is int
            && values[3].Code == 1005 && values[3].Data is string handle
            && !string.IsNullOrEmpty(selfHandle) && string.Equals(handle, selfHandle, StringComparison.OrdinalIgnoreCase);

        internal static bool AcceptSource(IReadOnlyList<Value> values, string selfHandle,
            bool dynamicCircle, bool ownerVerified, bool selfResolves, bool hasExtension, bool odEmpty)
            => !hasExtension && odEmpty && (values.Count == 0 ||
                dynamicCircle && ownerVerified && selfResolves && IsExact(values, selfHandle));

        // Diagnostics only: acceptance remains the independent predicate above.
        internal static string SourceReviewReason(IReadOnlyList<Value> values, string selfHandle,
            bool dynamicCircle, bool ownerVerified, bool selfResolves, bool hasExtension, bool odEmpty)
        {
            var reasons = new List<string>();
            if (hasExtension) reasons.Add("extension dictionary is present");
            if (!odEmpty) reasons.Add("native OD is present or its read failed");
            if (values.Count != 0)
            {
                if (!dynamicCircle) reasons.Add("tagged leaf is not an exact Circle directly under a dynamic reference");
                if (!ownerVerified) reasons.Add("leaf owner does not match the evaluated block definition");
                if (values.Count != 4) reasons.Add($"representation payload count is {values.Count}; expected 4");
                else
                {
                    int[] expectedCodes = { 1001, 1070, 1071, 1005 };
                    for (int index = 0; index < 4; index++)
                        if (values[index].Code != expectedCodes[index])
                            reasons.Add($"XData[{index}] code is {values[index].Code}; expected {expectedCodes[index]}");
                    if (values[0].Data is not string app || app != Application)
                        reasons.Add("XData[0] application is not exact AcDbBlockRepETag");
                    if (values[1].Data is not short small || small != 1)
                        reasons.Add("XData[1] must be Int16 value 1");
                    if (values[2].Data is not int)
                        reasons.Add("XData[2] must be Int32");
                    if (values[3].Data is not string handle || string.IsNullOrEmpty(selfHandle) ||
                        !string.Equals(handle, selfHandle, StringComparison.OrdinalIgnoreCase))
                        reasons.Add("XData[3] must reference the leaf's own handle");
                }
                // Resolution is only attempted by the host for an exact payload.
                if (IsExact(values, selfHandle) && !selfResolves)
                    reasons.Add("self handle did not resolve to the same native entity");
            }
            return string.Join("; ", reasons);
        }
        internal static string Describe(IReadOnlyList<Value> values)
        {
            var parts = new List<string>();
            for (int index = 0; index < Math.Min(values.Count, 16); index++)
            {
                Value value = values[index];
                string type = value.Data?.GetType().Name ?? "null";
                string content = value.Code is 1001 or 1005 && value.Data is string text ? text :
                    value.Code is 1070 or 1071 && value.Data is short or int ?
                    Convert.ToString(value.Data, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "<not displayed>";
                content = content.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                if (content.Length > 64) content = content.Substring(0, 64) + "...";
                parts.Add($"{value.Code}:{type}={content}");
            }
            if (values.Count > 16) parts.Add($"... {values.Count - 16} additional values");
            return string.Join(", ", parts);
        }
        internal static bool AcceptClone(IReadOnlyList<Value> values, string sourceHandle,
            string destinationHandle, int? sourceTagValue, bool hasExtension)
            => !hasExtension && (values.Count == 0 || sourceTagValue.HasValue &&
                (IsExact(values, sourceHandle) || destinationHandle != "0" && IsExact(values, destinationHandle)) &&
                values[2].Data is int actualTagValue && actualTagValue == sourceTagValue.Value);
    }
    internal static class SewerArchiveVersion
    {
        internal const string Current = "CLV_SEWER_PREP_V2_S4_METADATA";
        internal static void Require(string version)
        {
            if (version == "CLV_SEWER_PREP_V1")
                throw new InvalidOperationException("This drawing contains an earlier S1-S3 sewer archive, which predates S4 metadata signatures. Use an untouched input copy for S4; do not delete the archive or reprocess these outputs. No migration or cleanup was applied.");
            if (version != Current)
                throw new InvalidOperationException("Sewer archive metadata-signature version is unsupported; use an untouched input copy. No migration or cleanup was applied.");
        }
    }
}