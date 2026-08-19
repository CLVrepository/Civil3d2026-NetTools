using System;
using System.Collections.Generic;

namespace CLV_CivilTools.LegalDescription
{
    internal sealed class LegalDescriptionSession
    {
        public string DrawingName { get; set; } = string.Empty;
        public DateTime UpdatedLocal { get; set; } = DateTime.Now;
        public double PointOfCommencementX { get; set; }
        public double PointOfCommencementY { get; set; }
        public double PointOfBoundaryX { get; set; }
        public double PointOfBoundaryY { get; set; }
        public bool PointOfCommencementEqualsBoundary { get; set; } = true;
        public int BearingSecondsPrecision { get; set; }
        public int DistancePrecision { get; set; } = 2;
        public string TextStyleName { get; set; } = "CLV Old Standard";
        public string IntroductoryText { get; set; } = string.Empty;
        public bool UseStandardLandDescriptionTemplate { get; set; } = true;
        public string LandPrimaryQuarterName { get; set; } = "XXXXX";
        public string LandPrimaryQuarterCode { get; set; } = "XX";
        public string LandSecondaryQuarterName { get; set; } = "XXXXX";
        public string LandSecondaryQuarterCode { get; set; } = "XX";
        public string LandSection { get; set; } = "XX";
        public string LandTownship { get; set; } = "XX";
        public string LandRange { get; set; } = "XX";
        public string PointOfCommencementDescription { get; set; } = "THE POINT OF COMMENCEMENT";
        public string PointOfBeginningDescription { get; set; } = "THE POINT OF BEGINNING";
        public string PointOfCommencementRelationship { get; set; } = string.Empty;
        public string SamePointBeginningKey { get; set; } = "BEGINNING_AT";
        public string CommencementKey { get; set; } = "COMMENCING_AT";
        public string FinalTieKey { get; set; } = "SAID_POINT_POB";
        public string ReturnCallKey { get; set; } = "RETURN_POB";
        public string AreaOutputKey { get; set; } = "SQUARE_FEET";
        public int AreaSquareFeetPrecision { get; set; }
        public int AreaAcresPrecision { get; set; } = 3;
        public bool AreaIncludeComputerMethods { get; set; } = true;
        public string AreaStatementOverride { get; set; } = string.Empty;
        public string Apn { get; set; } = string.Empty;
        public string PreparationDate { get; set; } = DateTime.Now.ToString("MMMM d, yyyy").ToUpperInvariant();
        public string PreparedBy { get; set; } = string.Empty;
        public string PeerReviewedBy { get; set; } = string.Empty;
        public string ExplanationText { get; set; } = string.Empty;
        public string BasisOfBearingsText { get; set; } = "GRID NORTH AS DEFINED BY THE CENTRAL MERIDIAN OF THE NEVADA COORDINATE REFERENCE SYSTEM (NCRS), LAS VEGAS ZONE, NORTH AMERICAN DATUM OF 1983; SAID MERIDIAN BEING COINCIDENT WITH 114°58’ WEST OF THE GREENWICH MERIDIAN.";
        public string ExhibitStatement { get; set; } = "AS SHOWN ON “EXHIBIT TO ACCOMPANY LAND DESCRIPTION” ATTACHED HERETO AND MADE A PART HEREOF.";
        public List<LegalCourse> TieCourses { get; set; } = new();
        public List<LegalCourse> Courses { get; set; } = new();
        public string ClosingText { get; set; } = "Containing the area shown by the selected boundary, more or less.";
        public string FinalTextOverride { get; set; } = string.Empty;
        public List<string> LinkedMTextHandles { get; set; } = new();
    }

    internal sealed class LegalCourse
    {
        public int Number { get; set; }
        public string Group { get; set; } = "BOUNDARY";
        public string Handle { get; set; } = string.Empty;
        public string EntityType { get; set; } = "LINE";
        public bool Reversed { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public double ArcLength { get; set; }
        public double DeltaRadians { get; set; }
        public bool CurveRight { get; set; }
        public bool Include { get; set; } = true;
        public string Prefix { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public string OverrideText { get; set; } = string.Empty;
        public string RelationshipKey { get; set; } = "NONE";
        public string RelationshipReference { get; set; } = string.Empty;
        public string RelationshipPlacementKey { get; set; } = "BEFORE_GEOMETRY";
        public string DestinationRelationshipKey { get; set; } = "NONE";
        public string DestinationRelationshipReference { get; set; } = string.Empty;
        public string CurveClassification { get; set; } = string.Empty;
        public string CurveInClassification { get; set; } = string.Empty;
        public string CurveOutClassification { get; set; } = string.Empty;
        public bool TangentAtStart { get; set; }
        public bool TangentAtEnd { get; set; }
        public string Concavity { get; set; } = string.Empty;
        public double RadialBearingAtStart { get; set; }
        public double RadialBearingAtEnd { get; set; }
        public double ChordBearing { get; set; }
        public double ChordLength { get; set; }
    }

    internal sealed class LegalTextStyle
    {
        public string Name { get; set; } = string.Empty;
        public bool AllCaps { get; set; } = true;
        public string BeginningSame { get; set; } = "BEGINNING AT {POB_DESCRIPTION};";
        public string Commencing { get; set; } = "COMMENCING AT {POC_DESCRIPTION};";
        public string TieCoursePrefix { get; set; } = "THENCE ";
        public string LastTieTemplate { get; set; } = "{COURSE_TEXT} TO A POINT, SAID POINT BEING THE POINT OF BEGINNING;";
        public string BoundaryCoursePrefix { get; set; } = "THENCE ";
        public string ReturnToBeginning { get; set; } = "THENCE RETURNING TO THE POINT OF BEGINNING.";
        public string AreaTemplate { get; set; } = "SAID PARCEL CONTAINS {AREA_SF} SQUARE FEET, MORE OR LESS.";
        public string LineDistanceSeparator { get; set; } = ", ";
        public string FeetWord { get; set; } = "FEET";
        public string CurveTemplate { get; set; } = "ALONG A CURVE TO THE {DIRECTION}, HAVING A RADIUS OF {RADIUS} FEET, THROUGH A CENTRAL ANGLE OF {DELTA}, AN ARC LENGTH OF {LENGTH} FEET";
        public string TangentCurveTemplate { get; set; } = "TO THE BEGINNING OF A TANGENT CURVE, CONCAVE {CONCAVITY}, HAVING A RADIUS OF {RADIUS} FEET; THENCE ALONG SAID CURVE THROUGH A CENTRAL ANGLE OF {DELTA}, AN ARC LENGTH OF {LENGTH} FEET";
        public string NonTangentCurveTemplate { get; set; } = "TO THE BEGINNING OF A NON-TANGENT CURVE, CONCAVE {CONCAVITY}, HAVING A RADIUS OF {RADIUS} FEET, A RADIAL LINE TO SAID BEGINNING BEARS {RADIAL_BEARING}; THENCE ALONG SAID CURVE THROUGH A CENTRAL ANGLE OF {DELTA}, AN ARC LENGTH OF {LENGTH} FEET";
        public string ReverseCurveTemplate { get; set; } = "TO THE BEGINNING OF A REVERSE CURVE, CONCAVE {CONCAVITY}, HAVING A RADIUS OF {RADIUS} FEET; THENCE ALONG SAID CURVE THROUGH A CENTRAL ANGLE OF {DELTA}, AN ARC LENGTH OF {LENGTH} FEET";
        public string CompoundCurveTemplate { get; set; } = "TO THE BEGINNING OF A COMPOUND CURVE, CONCAVE {CONCAVITY}, HAVING A RADIUS OF {RADIUS} FEET; THENCE ALONG SAID CURVE THROUGH A CENTRAL ANGLE OF {DELTA}, AN ARC LENGTH OF {LENGTH} FEET";
        public string NonTangentEndRadialTemplate { get; set; } = ", A RADIAL LINE TO SAID ENDING BEARS {END_RADIAL_BEARING}";
    }

    internal sealed class LegalGeometrySummary
    {
        public double TraverseLength { get; init; }
        public double ForwardMisclosure { get; init; }
        public double ReverseMisclosure { get; init; }
        public double SignedArea { get; init; }
        public bool IsClosed { get; init; }
        public string Warning { get; init; } = string.Empty;
    }
}
