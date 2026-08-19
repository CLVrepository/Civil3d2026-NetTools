using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalTextGenerator
    {
        internal static string Build(LegalDescriptionSession session)
        {
            LegalTextStyle style = LegalTextStyleService.GetStyle(session.TextStyleName);
            LegalCurveAnalysisService.Analyze(session);
            var text = new StringBuilder();

            string landDescription = LegalLandDescriptionTemplateService.Build(session);
            if (!string.IsNullOrWhiteSpace(landDescription))
            {
                text.AppendLine(landDescription);
                text.AppendLine();
            }

            if (session.PointOfCommencementEqualsBoundary)
            {
                string beginningTemplate = LegalPhraseLibrary.Resolve(LegalPhraseLibrary.SamePointBeginnings, session.SamePointBeginningKey, style.BeginningSame);
                text.AppendLine(ApplyCommonTokens(beginningTemplate, session));
            }
            else
            {
                string commencementTemplate = LegalPhraseLibrary.Resolve(LegalPhraseLibrary.Commencements, session.CommencementKey, style.Commencing);
                text.AppendLine(ApplyCommonTokens(commencementTemplate, session));
                AppendCourses(text, session.TieCourses, session, style, isTie: true);
            }

            AppendCourses(text, session.Courses, session, style, isTie: false);
            string returnTemplate = LegalPhraseLibrary.Resolve(LegalPhraseLibrary.ReturnCalls, session.ReturnCallKey, style.ReturnToBeginning);
            text.AppendLine(ApplyCommonTokens(returnTemplate, session));

            text.AppendLine();
            string areaText = !string.IsNullOrWhiteSpace(session.AreaStatementOverride)
                ? session.AreaStatementOverride.Trim()
                : BuildAreaStatement(session, style);
            if (!string.IsNullOrWhiteSpace(areaText))
                text.AppendLine(areaText);

            string result = text.ToString().TrimEnd();
            return style.AllCaps ? result.ToUpperInvariant() : result;
        }


        private static string BuildAreaStatement(LegalDescriptionSession session, LegalTextStyle style)
        {
            double squareFeet = Math.Abs(LegalGeometryService.Summarize(session).SignedArea);
            double acres = squareFeet / 43560.0;
            string sf = squareFeet.ToString($"N{Math.Max(0, session.AreaSquareFeetPrecision)}", CultureInfo.InvariantCulture);
            string ac = acres.ToString($"F{Math.Max(0, session.AreaAcresPrecision)}", CultureInfo.InvariantCulture);
            string suffix = session.AreaIncludeComputerMethods
                ? ", MORE OR LESS, AS DETERMINED BY COMPUTER METHODS."
                : ", MORE OR LESS.";

            return (session.AreaOutputKey ?? "SQUARE_FEET").ToUpperInvariant() switch
            {
                "ACRES" => $"CONTAINING {ac} ACRES{suffix}",
                "BOTH" => $"CONTAINING {sf} SQUARE FEET ({ac} ACRES){suffix}",
                _ => $"CONTAINING {sf} SQUARE FEET{suffix}"
            };
        }

        private static void AppendCourses(StringBuilder text, System.Collections.Generic.IReadOnlyList<LegalCourse> source,
            LegalDescriptionSession session, LegalTextStyle style, bool isTie)
        {
            LegalCourse[] included = source.Where(c => c.Include).ToArray();
            for (int index = 0; index < included.Length; index++)
                text.AppendLine(BuildCourseLine(included[index], session, style, isTie, isTie && index == included.Length - 1));
        }

        internal static string BuildCourseLine(LegalDescriptionSession session, LegalCourse course)
        {
            LegalTextStyle style = LegalTextStyleService.GetStyle(session.TextStyleName);
            bool isTie = string.Equals(course.Group, "TIE", StringComparison.OrdinalIgnoreCase);
            LegalCourse[] included = (isTie ? session.TieCourses : session.Courses).Where(c => c.Include).ToArray();
            bool isLastTie = isTie && included.Length > 0 && ReferenceEquals(included[^1], course);
            string result = BuildCourseLine(course, session, style, isTie, isLastTie);
            return style.AllCaps ? result.ToUpperInvariant() : result;
        }

        private static string BuildCourseLine(LegalCourse course, LegalDescriptionSession session, LegalTextStyle style, bool isTie, bool isLastTie)
        {
            string body = string.IsNullOrWhiteSpace(course.OverrideText)
                ? BuildGeometryText(course, session)
                : course.OverrideText.Trim().TrimEnd(';', '.');

            string travel = BuildRelationship(course.RelationshipKey, course.RelationshipReference, "PREFIX");
            string destination = BuildRelationship(course.DestinationRelationshipKey, course.DestinationRelationshipReference, "SUFFIX");
            var courseText = new StringBuilder();
            courseText.Append(isTie ? style.TieCoursePrefix : style.BoundaryCoursePrefix);

            if (!string.IsNullOrWhiteSpace(course.Prefix))
                courseText.Append(course.Prefix.Trim()).Append(' ');
            if (!string.IsNullOrWhiteSpace(course.Context))
                courseText.Append(course.Context.Trim()).Append(", ");

            bool afterBearing = string.Equals(course.RelationshipPlacementKey, "AFTER_BEARING", StringComparison.OrdinalIgnoreCase)
                && string.Equals(course.EntityType, "LINE", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(course.OverrideText)
                && !string.IsNullOrWhiteSpace(travel);

            if (afterBearing && TryBuildLineGeometryParts(course, session, out string bearing, out string distance))
            {
                courseText.Append(bearing).Append(", ").Append(travel).Append(", ").Append(distance);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(travel))
                    courseText.Append(travel).Append(", ");
                courseText.Append(body);
            }

            if (!string.IsNullOrWhiteSpace(destination))
                courseText.Append(' ').Append(destination);
            if (!string.IsNullOrWhiteSpace(course.Suffix))
                courseText.Append(' ').Append(course.Suffix.Trim());

            string line;
            if (isLastTie)
            {
                string finalTieTemplate = LegalPhraseLibrary.Resolve(LegalPhraseLibrary.FinalTieCalls, session.FinalTieKey, style.LastTieTemplate);
                line = ApplyCommonTokens(finalTieTemplate.Replace("{COURSE_TEXT}", courseText.ToString(), StringComparison.OrdinalIgnoreCase), session);
            }
            else
            {
                line = courseText.ToString().TrimEnd(';', '.') + ";";
            }
            return line;
        }

        private static bool TryBuildLineGeometryParts(LegalCourse course, LegalDescriptionSession session, out string bearing, out string distance)
        {
            bearing = string.Empty;
            distance = string.Empty;
            if (!string.Equals(course.EntityType, "LINE", StringComparison.OrdinalIgnoreCase))
                return false;
            LegalTextStyle style = LegalTextStyleService.GetStyle(session.TextStyleName);
            double dx = course.EndX - course.StartX;
            double dy = course.EndY - course.StartY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double azimuth = Math.Atan2(dx, dy);
            if (azimuth < 0.0)
                azimuth += Math.PI * 2.0;
            bearing = FormatQuadrantBearing(azimuth, session.BearingSecondsPrecision);
            distance = length.ToString($"F{session.DistancePrecision}", CultureInfo.InvariantCulture) + " " + style.FeetWord;
            return true;
        }

        private static string BuildRelationship(string key, string reference, string placement)
        {
            LegalPhraseOption? option = LegalPhraseLibrary.Find(LegalPhraseLibrary.LineRelationships, key);
            if (option == null || !string.Equals(option.Placement, placement, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return option.Template.Replace("{REFERENCE}", reference?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        }

        private static string ApplyCommonTokens(string template, LegalDescriptionSession session)
        {
            double area = Math.Abs(LegalGeometryService.Summarize(session).SignedArea);
            return (template ?? string.Empty)
                .Replace("{POC_DESCRIPTION}", NormalizeDescription(session.PointOfCommencementDescription, "THE POINT OF COMMENCEMENT"), StringComparison.OrdinalIgnoreCase)
                .Replace("{POB_DESCRIPTION}", NormalizeDescription(session.PointOfBeginningDescription, "THE POINT OF BEGINNING"), StringComparison.OrdinalIgnoreCase)
                .Replace("{POC_RELATIONSHIP}", NormalizeDescription(session.PointOfCommencementRelationship, "THE REFERENCED CONTROL POINT"), StringComparison.OrdinalIgnoreCase)
                .Replace("{AREA_SF}", area.ToString("N0", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{AREA_SF_2}", area.ToString("N2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{AREA_ACRES}", (area / 43560.0).ToString($"F{Math.Max(0, session.AreaAcresPrecision)}", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDescription(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd(';', '.');
        }

        internal static string BuildGeometryText(LegalCourse course, LegalDescriptionSession session)
        {
            LegalTextStyle style = LegalTextStyleService.GetStyle(session.TextStyleName);
            if (course.EntityType == "ARC")
            {
                string direction = course.CurveRight ? "RIGHT" : "LEFT";
                string delta = FormatAngle(course.DeltaRadians, session.BearingSecondsPrecision);
                string template = course.CurveInClassification switch
                {
                    "TANGENT" => style.TangentCurveTemplate,
                    "REVERSE" => style.ReverseCurveTemplate,
                    "COMPOUND" => style.CompoundCurveTemplate,
                    "NON-TANGENT" => style.NonTangentCurveTemplate,
                    _ => style.CurveTemplate
                };
                string curveText = template
                    .Replace("{DIRECTION}", direction, StringComparison.OrdinalIgnoreCase)
                    .Replace("{CONCAVITY}", course.Concavity, StringComparison.OrdinalIgnoreCase)
                    .Replace("{RADIUS}", course.Radius.ToString($"F{session.DistancePrecision}", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("{DELTA}", delta, StringComparison.OrdinalIgnoreCase)
                    .Replace("{LENGTH}", course.ArcLength.ToString($"F{session.DistancePrecision}", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("{RADIAL_BEARING}", FormatQuadrantBearing(course.RadialBearingAtStart, session.BearingSecondsPrecision), StringComparison.OrdinalIgnoreCase)
                    .Replace("{START_RADIAL_BEARING}", FormatQuadrantBearing(course.RadialBearingAtStart, session.BearingSecondsPrecision), StringComparison.OrdinalIgnoreCase)
                    .Replace("{END_RADIAL_BEARING}", FormatQuadrantBearing(course.RadialBearingAtEnd, session.BearingSecondsPrecision), StringComparison.OrdinalIgnoreCase)
                    .Replace("{CHORD_BEARING}", FormatQuadrantBearing(course.ChordBearing, session.BearingSecondsPrecision), StringComparison.OrdinalIgnoreCase)
                    .Replace("{CHORD_LENGTH}", course.ChordLength.ToString($"F{session.DistancePrecision}", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

                if (string.Equals(course.CurveOutClassification, "NON-TANGENT", StringComparison.OrdinalIgnoreCase))
                {
                    string endingRadial = (style.NonTangentEndRadialTemplate ?? string.Empty)
                        .Replace("{END_RADIAL_BEARING}", FormatQuadrantBearing(course.RadialBearingAtEnd, session.BearingSecondsPrecision), StringComparison.OrdinalIgnoreCase);
                    curveText += endingRadial;
                }
                return curveText;
            }

            double dx = course.EndX - course.StartX;
            double dy = course.EndY - course.StartY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double azimuth = Math.Atan2(dx, dy);
            if (azimuth < 0.0)
                azimuth += Math.PI * 2.0;
            return $"{FormatQuadrantBearing(azimuth, session.BearingSecondsPrecision)}{style.LineDistanceSeparator}{length.ToString($"F{session.DistancePrecision}", CultureInfo.InvariantCulture)} {style.FeetWord}";
        }

        private static string FormatQuadrantBearing(double azimuth, int secondsPrecision)
        {
            double degrees = azimuth * 180.0 / Math.PI;
            string ns;
            string ew;
            double angle;
            if (degrees <= 90.0) { ns = "NORTH"; ew = "EAST"; angle = degrees; }
            else if (degrees <= 180.0) { ns = "SOUTH"; ew = "EAST"; angle = 180.0 - degrees; }
            else if (degrees <= 270.0) { ns = "SOUTH"; ew = "WEST"; angle = degrees - 180.0; }
            else { ns = "NORTH"; ew = "WEST"; angle = 360.0 - degrees; }
            return $"{ns} {FormatDegrees(angle, secondsPrecision)} {ew}";
        }

        private static string FormatAngle(double radians, int secondsPrecision)
        {
            return FormatDegrees(Math.Abs(radians) * 180.0 / Math.PI, secondsPrecision);
        }

        private static string FormatDegrees(double degrees, int secondsPrecision)
        {
            int d = (int)Math.Floor(degrees);
            double minutesFull = (degrees - d) * 60.0;
            int m = (int)Math.Floor(minutesFull);
            double seconds = (minutesFull - m) * 60.0;
            string secondsFormat = secondsPrecision <= 0 ? "00" : "00." + new string('0', secondsPrecision);
            return $"{d:00}°{m:00}'{seconds.ToString(secondsFormat, CultureInfo.InvariantCulture)}\"";
        }
    }
}
