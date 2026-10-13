using System;
using System.Collections.Generic;

namespace CLV_CivilTools.Gis
{
    internal enum SewerPointMetadataKind { None, NativeOdOnly, Review }
    internal sealed record SewerPointMetadataEntry(string Key, string DxfName, bool Owned,
        bool HasXData, bool HasExtension, bool HasUnexpectedReactors);
    internal sealed record SewerPointMetadata(bool HasXData, bool HasUnexpectedReactors,
        bool HasDictionary, bool DictionaryIsExactAndOwned, bool DictionaryHasExtraMetadata,
        IReadOnlyList<SewerPointMetadataEntry> Entries);
    internal sealed record SewerPointCleanupProof(string SourceRole, string OutputRole,
        bool ArchiveLinksUnique, bool SourceUnchanged, bool IdentityAndAnchorMatch,
        bool SourceOdPresent, bool OutputOdPresent, string ExpectedSourceOd,
        string ActualSourceOd, string ExpectedOutputOd, string ActualOutputOd,
        SewerPointMetadata SourceMetadata, SewerPointMetadata OutputMetadata);

    // This is deliberately the one observed native Map OD storage layout. A
    // dictionary key by itself, or a matching OD subset, never authorizes erasure.
    internal static class SewerPointCleanup
    {
        internal static bool UniqueLinks(int sourceLinks, int outerLinks, int ownersOfOuter,
            int structuresWithName, int outersWithName, int retainedSourcesWithName)
            => sourceLinks == 1 && outerLinks == 1 && ownersOfOuter == 1 && structuresWithName == 1 &&
               outersWithName == 1 && retainedSourcesWithName == 1;

        internal static SewerPointMetadataKind Classify(SewerPointMetadata metadata)
        {
            if (metadata.HasXData || metadata.HasUnexpectedReactors || metadata.DictionaryHasExtraMetadata)
                return SewerPointMetadataKind.Review;
            if (!metadata.HasDictionary)
                return metadata.Entries.Count == 0 ? SewerPointMetadataKind.None : SewerPointMetadataKind.Review;
            if (!metadata.DictionaryIsExactAndOwned || metadata.Entries.Count != 1)
                return SewerPointMetadataKind.Review;
            var entry = metadata.Entries[0];
            return entry.Key == "*A1" && entry.DxfName == "IRD_OBJ_RECORD" && entry.Owned &&
                !entry.HasXData && !entry.HasExtension && !entry.HasUnexpectedReactors
                ? SewerPointMetadataKind.NativeOdOnly : SewerPointMetadataKind.Review;
        }

        internal static bool CanErase(SewerPointCleanupProof proof, out string reason)
        {
            reason = "";
            if (proof.SourceRole != "RetainedSourcePoint" || proof.OutputRole != "ManholeOuter" ||
                !proof.ArchiveLinksUnique)
                reason = "No unique archived manhole source-to-outer-circle ownership link.";
            else if (!proof.SourceUnchanged || !proof.IdentityAndAnchorMatch)
                reason = "Source geometry, structure identity or archive anchor differs.";
            else if (!proof.SourceOdPresent || !proof.OutputOdPresent ||
                string.IsNullOrEmpty(proof.ExpectedSourceOd) || string.IsNullOrEmpty(proof.ExpectedOutputOd) ||
                proof.ExpectedSourceOd != proof.ActualSourceOd || proof.ExpectedOutputOd != proof.ActualOutputOd ||
                proof.ActualSourceOd != proof.ActualOutputOd)
                reason = "Complete current source/output OD does not match both archive fingerprints.";
            else if (Classify(proof.SourceMetadata) == SewerPointMetadataKind.Review ||
                (Classify(proof.SourceMetadata) == SewerPointMetadataKind.NativeOdOnly &&
                 Classify(proof.OutputMetadata) != SewerPointMetadataKind.NativeOdOnly))
                reason = "Additional or unsupported point metadata is preserved; the OD-only storage pair was not proved.";
            return reason.Length == 0;
        }

        internal static bool ValidStubArchiveLink(string expectedDigest, int savedCount, string actualDigest)
            => expectedDigest.Length == 0 ? savedCount == 0 :
               expectedDigest.Length == 64 && savedCount > 0 && expectedDigest == actualDigest;

        internal static bool CanArchiveStub(string role, bool exactNullStubIdentity,
            bool uniqueUnchangedOwnership, int verifiedPipeLinks, SewerPointMetadata metadata)
            => role == "OpenNullEnd" && exactNullStubIdentity && uniqueUnchangedOwnership &&
               verifiedPipeLinks > 0 && Classify(metadata) != SewerPointMetadataKind.Review;

        internal static bool ArchivedIdentityValueMatches(int fields, string? kind, string? raw,
            string expected, bool allowMissing)
            => fields == 0 ? allowMissing && expected.Length == 0 :
               fields == 1 && kind == "Character" && raw != null && string.Equals(raw.Trim(), expected, StringComparison.Ordinal);

        internal static bool ValidOpenEndKindRole(string kind, string role)
            => kind == "NullStub" && role == "OpenNullEnd" || kind == "Connection" && role == "OpenConnectionEnd";

        internal static bool CanArchiveOpenEnd(string role, string kind, bool exactIdentity,
            bool uniqueUnchangedOwnership, int verifiedPipeLinks, SewerPointMetadata metadata)
            => ValidOpenEndKindRole(kind, role) && exactIdentity && uniqueUnchangedOwnership &&
               verifiedPipeLinks > 0 && Classify(metadata) != SewerPointMetadataKind.Review;

        // The caller owns a real transaction. Exceptions are deliberately allowed
        // to escape: it must not commit a partially rewritten archive or erasure.
        internal static void Apply<T>(IReadOnlyList<T> points, Action<T> acquireWrite,
            Action revalidate, Action rewriteAndReadBack, Action<T> erase, Action verifyFinal)
        {
            if (points.Count == 0) return;
            foreach (T point in points) acquireWrite(point);
            revalidate();
            rewriteAndReadBack();
            foreach (T point in points) erase(point);
            verifyFinal();
        }
    }
}

