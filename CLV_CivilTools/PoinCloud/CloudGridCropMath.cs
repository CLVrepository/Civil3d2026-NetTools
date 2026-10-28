using System;

namespace CLV_CivilTools.PointCloud
{
    internal readonly record struct CropXYZ(double X, double Y, double Z);

    internal static class CloudGridCropMath
    {
        internal static CropXYZ ToNative(CropXYZ world, CropXYZ location, double rotation, double scale)
        {
            Validate(world, location, rotation, scale);
            double x = world.X - location.X, y = world.Y - location.Y;
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            return new((c * x + s * y) / scale, (-s * x + c * y) / scale, (world.Z - location.Z) / scale);
        }

        internal static CropXYZ ToWorld(CropXYZ native, CropXYZ location, double rotation, double scale)
        {
            Validate(native, location, rotation, scale);
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            return new(location.X + scale * (c * native.X - s * native.Y),
                location.Y + scale * (s * native.X + c * native.Y), location.Z + scale * native.Z);
        }

        private static void Validate(CropXYZ point, CropXYZ location, double rotation, double scale)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z) ||
                !double.IsFinite(location.X) || !double.IsFinite(location.Y) || !double.IsFinite(location.Z) ||
                !double.IsFinite(rotation) || !double.IsFinite(scale) || scale <= 0)
                throw new ArgumentException("Cloud crop coordinates and placement must be finite; scale must be positive.");
        }
    }
}
