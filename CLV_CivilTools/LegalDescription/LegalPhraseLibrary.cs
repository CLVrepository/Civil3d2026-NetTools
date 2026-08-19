using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CLV_CivilTools.LegalDescription
{
    internal sealed class LegalPhraseOption
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string Placement { get; set; } = "PREFIX";
    }

    internal sealed class LegalPhraseLibraryModel
    {
        public List<LegalPhraseOption> SamePointBeginnings { get; set; } = new();
        public List<LegalPhraseOption> Commencements { get; set; } = new();
        public List<LegalPhraseOption> FinalTieCalls { get; set; } = new();
        public List<LegalPhraseOption> ReturnCalls { get; set; } = new();
        public List<LegalPhraseOption> LineRelationships { get; set; } = new();
    }

    internal static class LegalPhraseLibrary
    {
        private static readonly Lazy<LegalPhraseLibraryModel> Library = new(Load);

        internal static IReadOnlyList<LegalPhraseOption> SamePointBeginnings => Library.Value.SamePointBeginnings;
        internal static IReadOnlyList<LegalPhraseOption> Commencements => Library.Value.Commencements;
        internal static IReadOnlyList<LegalPhraseOption> FinalTieCalls => Library.Value.FinalTieCalls;
        internal static IReadOnlyList<LegalPhraseOption> ReturnCalls => Library.Value.ReturnCalls;
        internal static IReadOnlyList<LegalPhraseOption> LineRelationships => Library.Value.LineRelationships;

        internal static LegalPhraseOption? Find(IEnumerable<LegalPhraseOption> source, string key)
        {
            return source.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        internal static string Resolve(IEnumerable<LegalPhraseOption> source, string key, string fallback)
        {
            LegalPhraseOption? option = Find(source, key);
            return option == null || string.IsNullOrWhiteSpace(option.Template) ? fallback : option.Template;
        }

        internal static string DisplayName(IEnumerable<LegalPhraseOption> source, string key)
        {
            LegalPhraseOption? option = Find(source, key);
            return option?.Name ?? key;
        }

        private static LegalPhraseLibraryModel Load()
        {
            try
            {
                using Stream stream = EmbeddedLegalResourceService.OpenPhraseLibrary();
                LegalPhraseLibraryModel? model = JsonSerializer.Deserialize<LegalPhraseLibraryModel>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (model != null)
                    return model;
            }
            catch
            {
                // Use deterministic built-in fallback below.
            }

            return new LegalPhraseLibraryModel
            {
                SamePointBeginnings = new()
                {
                    new() { Key = "BEGINNING_AT", Name = "BEGINNING AT", Template = "BEGINNING AT {POB_DESCRIPTION};" },
                    new() { Key = "BEGINNING_AT_POINT", Name = "BEGINNING AT A POINT", Template = "BEGINNING AT A POINT, SAID POINT BEING {POB_DESCRIPTION};" }
                },
                Commencements = new()
                {
                    new() { Key = "COMMENCING_AT", Name = "COMMENCING AT", Template = "COMMENCING AT {POC_DESCRIPTION};" },
                    new() { Key = "COMMENCING_SAME_BEING", Name = "COMMENCING / SAME BEING", Template = "COMMENCING AT {POC_DESCRIPTION}, SAME BEING {POC_RELATIONSHIP};" },
                    new() { Key = "COMMENCING_ALSO_BEING", Name = "COMMENCING / ALSO BEING", Template = "COMMENCING AT {POC_DESCRIPTION}, ALSO BEING {POC_RELATIONSHIP};" }
                },
                FinalTieCalls = new()
                {
                    new() { Key = "SAID_POINT_POB", Name = "SAID POINT BEING POB", Template = "{COURSE_TEXT} TO A POINT, SAID POINT BEING THE POINT OF BEGINNING;" },
                    new() { Key = "TRUE_POB", Name = "TRUE POINT OF BEGINNING", Template = "{COURSE_TEXT} TO THE TRUE POINT OF BEGINNING;" },
                    new() { Key = "DIRECT_POB", Name = "DIRECT POINT OF BEGINNING", Template = "{COURSE_TEXT} TO THE POINT OF BEGINNING;" },
                    new() { Key = "POINT_ON_FEATURE_POB", Name = "POINT ON FEATURE / POB", Template = "{COURSE_TEXT} TO A POINT ON {POB_DESCRIPTION}, ALSO BEING THE POINT OF BEGINNING;" }
                },
                ReturnCalls = new()
                {
                    new() { Key = "RETURN_POB", Name = "TO POINT OF BEGINNING", Template = "THENCE TO THE POINT OF BEGINNING." },
                    new() { Key = "RETURN_TRUE_POB", Name = "TO TRUE POINT OF BEGINNING", Template = "THENCE TO THE TRUE POINT OF BEGINNING." },
                    new() { Key = "CLOSE_POB", Name = "CLOSING AT POB", Template = "THENCE RETURNING TO THE POINT OF BEGINNING." }
                },
                LineRelationships = new()
                {
                    new() { Key = "NONE", Name = "NONE", Template = "", Placement = "PREFIX" },
                    new() { Key = "ALONG_SAID_LINE", Name = "ALONG SAID LINE", Template = "ALONG SAID LINE", Placement = "PREFIX" },
                    new() { Key = "CONTINUING_ALONG", Name = "CONTINUING ALONG", Template = "CONTINUING ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "DEPARTING_SAID_LINE", Name = "DEPARTING SAID LINE", Template = "DEPARTING SAID LINE", Placement = "PREFIX" },
                    new() { Key = "ALONG_ROW", Name = "ALONG RIGHT-OF-WAY", Template = "ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "ALONG_CENTERLINE", Name = "ALONG CENTERLINE", Template = "ALONG THE CENTERLINE OF {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "ALONG_LOT_LINE", Name = "ALONG LOT LINE", Template = "ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "ALONG_PARCEL_LINE", Name = "ALONG PARCEL LINE", Template = "ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "ALONG_SECTION_LINE", Name = "ALONG SECTION LINE", Template = "ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "ALONG_EASEMENT_LINE", Name = "ALONG EASEMENT LINE", Template = "ALONG {REFERENCE}", Placement = "PREFIX" },
                    new() { Key = "TO_POINT_ON", Name = "TO A POINT ON", Template = "TO A POINT ON {REFERENCE}", Placement = "SUFFIX" },
                    new() { Key = "TO_INTERSECTION", Name = "TO INTERSECTION WITH", Template = "TO THE INTERSECTION WITH {REFERENCE}", Placement = "SUFFIX" },
                    new() { Key = "CUSTOM", Name = "CUSTOM CONTEXT", Template = "{REFERENCE}", Placement = "PREFIX" }
                }
            };
        }
    }
}
