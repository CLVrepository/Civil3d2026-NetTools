using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalTextStyleService
    {
        private static IReadOnlyList<LegalTextStyle>? _cached;

        internal static IReadOnlyList<LegalTextStyle> GetStyles()
        {
            if (_cached != null)
                return _cached;

            var styles = new List<LegalTextStyle>
            {
                new()
                {
                    Name = "CLV Old Standard",
                    AllCaps = true,
                    BeginningSame = "BEGINNING AT {POB_DESCRIPTION};",
                    Commencing = "COMMENCING AT {POC_DESCRIPTION};",
                    TieCoursePrefix = "THENCE ",
                    LastTieTemplate = "{COURSE_TEXT} TO A POINT, SAID POINT BEING THE POINT OF BEGINNING;",
                    BoundaryCoursePrefix = "THENCE ",
                    ReturnToBeginning = "THENCE TO THE POINT OF BEGINNING.",
                    AreaTemplate = "SAID PARCEL CONTAINS {AREA_SF} SQUARE FEET, MORE OR LESS."
                },
                new()
                {
                    Name = "True POB",
                    AllCaps = true,
                    BeginningSame = "BEGINNING AT {POB_DESCRIPTION};",
                    Commencing = "COMMENCING AT {POC_DESCRIPTION};",
                    TieCoursePrefix = "THENCE ",
                    LastTieTemplate = "{COURSE_TEXT} TO THE TRUE POINT OF BEGINNING;",
                    BoundaryCoursePrefix = "THENCE ",
                    ReturnToBeginning = "THENCE TO THE TRUE POINT OF BEGINNING.",
                    AreaTemplate = "CONTAINING {AREA_SF} SQUARE FEET, MORE OR LESS."
                },
                new()
                {
                    Name = "Direct POB",
                    AllCaps = true,
                    BeginningSame = "BEGINNING AT {POB_DESCRIPTION};",
                    Commencing = "COMMENCING AT {POC_DESCRIPTION};",
                    TieCoursePrefix = "THENCE ",
                    LastTieTemplate = "{COURSE_TEXT} TO THE POINT OF BEGINNING;",
                    BoundaryCoursePrefix = "THENCE ",
                    ReturnToBeginning = "THENCE TO THE POINT OF BEGINNING.",
                    AreaTemplate = "SAID PARCEL CONTAINS {AREA_SF} SQUARE FEET, MORE OR LESS."
                }
            };

            try
            {
                using Stream stream = EmbeddedLegalResourceService.OpenTextStyles();
                List<LegalTextStyle>? custom = JsonSerializer.Deserialize<List<LegalTextStyle>>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (custom != null)
                {
                    foreach (LegalTextStyle style in custom.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
                    {
                        int existing = styles.FindIndex(s => string.Equals(s.Name, style.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing >= 0)
                            styles[existing] = style;
                        else
                            styles.Add(style);
                    }
                }
            }
            catch (System.Exception)
            {
                // Built-in styles remain available if an embedded office style file is invalid.
            }

            _cached = styles;
            return _cached;
        }

        internal static LegalTextStyle GetStyle(string? name)
        {
            IReadOnlyList<LegalTextStyle> styles = GetStyles();
            return styles.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? styles[0];
        }
    }
}
