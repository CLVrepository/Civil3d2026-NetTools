using System;
using System.Collections.Generic;

namespace CLV_CivilTools.Gis
{
    // Exact observed native representation payload, not an application-name allowlist.
    internal static class SewerRepresentationTag
    {
        internal const string Application = "AcDbBlockRepETag";
        internal readonly record struct Value(int Code, object Data);

        internal static bool IsExact(IReadOnlyList<Value> values, string selfHandle)
            => values.Count == 4 && values[0].Code == 1001 && values[0].Data is string app && app == Application
            && values[1].Code == 1070 && values[1].Data is short small && small == 1
            && values[2].Code == 1071 && values[2].Data is int large && large == 0
            && values[3].Code == 1005 && values[3].Data is string handle
            && !string.IsNullOrEmpty(selfHandle) && string.Equals(handle, selfHandle, StringComparison.OrdinalIgnoreCase);

        internal static bool AcceptSource(IReadOnlyList<Value> values, string selfHandle,
            bool dynamicCircle, bool ownerVerified, bool selfResolves, bool hasExtension, bool odEmpty)
            => !hasExtension && odEmpty && (values.Count == 0 ||
                dynamicCircle && ownerVerified && selfResolves && IsExact(values, selfHandle));

        internal static bool AcceptClone(IReadOnlyList<Value> values, string sourceHandle,
            string destinationHandle, bool sourceHadTag, bool hasExtension)
            => !hasExtension && (values.Count == 0 || sourceHadTag &&
                (IsExact(values, sourceHandle) || destinationHandle != "0" && IsExact(values, destinationHandle)));
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