using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Stable digest of the existing typed OD record-key multiset. Record ordering is
    /// irrelevant, but duplicate multiplicity, field types and exact values are not.
    /// This pure helper does not validate native OD or authorize cleanup by itself.
    /// </summary>
    internal static class StormObjectDataFingerprint
    {
        public const string Prefix = "CLV_OD_V1:SHA256:";
        public const string FormatPrefix = Prefix;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // Syntax/version validation only. A valid digest is not evidence that any
        // source/output exists or contains OD; callers must read and compare it.
        public static bool IsValid(string? fingerprint)
        {
            if (fingerprint == null || fingerprint.Length != Prefix.Length + 64 ||
                !fingerprint.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            for (int i = Prefix.Length; i < fingerprint.Length; i++)
            {
                char value = fingerprint[i];
                if (!((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F')))
                    return false;
            }
            return true;
        }

        public static string Compute(IEnumerable<string> recordKeys)
        {
            ArgumentNullException.ThrowIfNull(recordKeys);
            string[] keys = recordKeys.ToArray();
            if (keys.Any(key => key == null))
                throw new ArgumentException("OD record keys cannot contain null elements.", nameof(recordKeys));

            // Sort a private copy, preserving every occurrence. Never trim, normalize
            // or deduplicate record keys, which already encode typed native values.
            Array.Sort(keys, StringComparer.Ordinal);
            var canonical = new StringBuilder(FormatPrefix);
            canonical.Append(keys.Length.ToString(CultureInfo.InvariantCulture)).Append(':');
            foreach (string key in keys)
            {
                canonical.Append(key.Length.ToString(CultureInfo.InvariantCulture)).Append(':');
                canonical.Append(key);
            }

            // Invalid UTF-16 must fail, rather than silently replacing distinct native
            // strings with the same Unicode replacement character before hashing.
            byte[] digest = SHA256.HashData(StrictUtf8.GetBytes(canonical.ToString()));
            return FormatPrefix + Convert.ToHexString(digest);
        }
    }
}
