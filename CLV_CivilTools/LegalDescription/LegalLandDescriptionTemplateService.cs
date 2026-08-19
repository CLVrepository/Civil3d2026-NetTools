using System;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalLandDescriptionTemplateService
    {
        internal static string Build(LegalDescriptionSession session)
        {
            if (!session.UseStandardLandDescriptionTemplate && !string.IsNullOrWhiteSpace(session.IntroductoryText))
                return Normalize(session.IntroductoryText);

            if (!string.IsNullOrWhiteSpace(session.IntroductoryText))
                return Normalize(session.IntroductoryText);

            return $"BEING A PORTION OF THE {Value(session.LandPrimaryQuarterName, "XXXXX")} QUARTER " +
                   $"({Value(session.LandPrimaryQuarterCode, "XX")} 1/4) OF THE " +
                   $"{Value(session.LandSecondaryQuarterName, "XXXXX")} QUARTER " +
                   $"({Value(session.LandSecondaryQuarterCode, "XX")} 1/4) OF SECTION " +
                   $"{Value(session.LandSection, "XX")}, TOWNSHIP {Value(session.LandTownship, "XX")} SOUTH, " +
                   $"RANGE {Value(session.LandRange, "XX")} EAST, M.D.M., CITY OF LAS VEGAS, " +
                   "CLARK COUNTY, NEVADA, MORE PARTICULARLY DESCRIBED AS FOLLOWS:";
        }

        private static string Value(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    }
}
