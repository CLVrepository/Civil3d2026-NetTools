using System;

namespace CLV_CivilTools.Shared
{
    internal enum PipeMaterial
    {
        Pvc,
        C900,
        Rcp
    }

    internal sealed class PipeSize
    {
        public int NominalInches { get; }
        public double InnerDiameterInches { get; }
        public double WallInches { get; }

        public PipeSize(int nominalInches, double innerDiameterInches, double wallInches)
        {
            NominalInches = nominalInches;
            InnerDiameterInches = innerDiameterInches;
            WallInches = wallInches;
        }
    }

    /// <summary>
    /// Catalog of pipe sizes and inside diameters for each material.
    /// Values pulled from your PDF (ASTM D3034 PVC, C900, and RCP).
    /// </summary>
    internal static class PipeCatalog
    {
        // D3034 – Westlake ASTM D3034, SDR-35, etc. (sizes >= 4")
        internal static readonly PipeSize[] PvcSizes = new[]
        {
            new PipeSize( 4,  3.975 , 0.120),
            new PipeSize( 6,  5.861 , 0.180),
            new PipeSize( 8,  7.739 , 0.240),
            new PipeSize(10,  9.679 , 0.300),
            new PipeSize(12, 11.559 , 0.360),
            new PipeSize(15, 14.488 , 0.437),
            new PipeSize(18, 17.703 , 0.499),
            new PipeSize(21, 20.871 , 0.588),
            new PipeSize(24, 23.024 , 0.662),
            new PipeSize(27, 25.867 , 0.737),
            new PipeSize(30, 28.717 , 0.812),
            new PipeSize(36, 34.381 , 0.962),
            new PipeSize(42, 40.071 , 1.112),
            new PipeSize(48, 45.761 , 1.262),
        };

        // AWWA C900 (from your table)
        internal static readonly PipeSize[] C900Sizes = new[]
        {
            new PipeSize( 4,  4.416 , 0.192),
            new PipeSize( 6,  6.348 , 0.276),
            new PipeSize( 8,  8.326 , 0.362),
            new PipeSize(10, 10.212 , 0.444),
            new PipeSize(12, 12.144 , 0.528),
            new PipeSize(14, 14.358 , 0.471),
            new PipeSize(16, 16.008 , 0.696),
            new PipeSize(18, 17.940 , 0.780),
            new PipeSize(20, 19.872 , 0.864),
            new PipeSize(24, 23.736 , 1.032),
            new PipeSize(30, 29.440 , 1.280),
            new PipeSize(36, 35.236 , 1.532),
        };

        // RCP – nominal sizes equal to ID in inches
        internal static readonly PipeSize[] RcpSizes = new[]
        {
            new PipeSize(12, 12.0 , 2.0),
            new PipeSize(15, 15.0 , 2.25),
            new PipeSize(18, 18.0 , 2.5),
            new PipeSize(21, 21.0 , 2.75),
            new PipeSize(24, 24.0 , 3.0),
            new PipeSize(30, 30.0 , 3.5),
            new PipeSize(36, 36.0 , 4.0),
            new PipeSize(42, 42.0 , 4.5),
            new PipeSize(48, 48.0 , 5.0),
            new PipeSize(54, 54.0 , 5.5),
            new PipeSize(60, 60.0 , 6.0),
            new PipeSize(66, 66.0 , 6.5),
            new PipeSize(72, 72.0 , 7.0),
            new PipeSize(78, 78.0 , 7.5),
            new PipeSize(84, 84.0 , 8.0),
            new PipeSize(90, 90.0 , 8.5),
            new PipeSize(96, 96.0 , 9.0),
        };

        private static PipeSize[] GetSizes(PipeMaterial mat) =>
            mat switch
            {
                PipeMaterial.Pvc => PvcSizes,
                PipeMaterial.C900 => C900Sizes,
                PipeMaterial.Rcp => RcpSizes,
                _ => PvcSizes
            };

        /// <summary>
        /// Return the catalog size whose inside diameter is closest to a measured ID.
        /// </summary>
        internal static PipeSize FindClosest(PipeMaterial mat, double measuredIdInches)
        {
            var sizes = GetSizes(mat);
            if (sizes.Length == 0)
                throw new InvalidOperationException("PipeCatalog has no sizes for specified material.");

            // Avoid nullable warnings by not using a null sentinel
            PipeSize best = sizes[0];
            double bestDiff = Math.Abs(best.InnerDiameterInches - measuredIdInches);

            for (int i = 1; i < sizes.Length; i++)
            {
                var s = sizes[i];
                double diff = Math.Abs(s.InnerDiameterInches - measuredIdInches);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// Build the dynamic block visibility state name, e.g. "12 INCH".
        /// </summary>
        internal static string GetVisibilityName(PipeSize s)
            => $"{s.NominalInches} INCH";

        /// <summary>
        /// Outer radius in feet = (ID/2 + wall) in inches, converted to feet.
        /// </summary>
        internal static double GetOuterRadiusFeet(PipeSize s)
        {
            double rInches = (s.InnerDiameterInches * 0.5) + s.WallInches;
            return rInches / 12.0;
        }
    }
}