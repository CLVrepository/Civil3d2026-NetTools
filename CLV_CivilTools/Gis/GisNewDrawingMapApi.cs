using System;
using System.Reflection;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Drawing coordinate-system assignment only. Map application/project objects
    /// are borrowed host objects. This command does not access the importer.
    /// </summary>
    internal static class GisNewDrawingMapApi
    {
        internal static string ReadProjection()
            => Get(Project(), "Projection") as string
                ?? throw new InvalidOperationException("Map project Projection is not a coordinate-system string.");

        internal static void AssignProjection(string coordinateSystem)
        {
            object project = Project();
            GisNewDrawingCoordinateSystem.AssignSameCode(coordinateSystem,
                code => SetTextProperty(project, "Projection", code),
                () => Get(project, "Projection") as string, "New drawing Map Projection");
        }

        internal static void VerifyProjection(string expectedCode)
            => GisNewDrawingCoordinateSystem.VerifySameCode(expectedCode, ReadProjection(), "New drawing Map Projection");

        private static object Project()
        {
            Type type = Assembly.Load("ManagedMapApi").GetType("Autodesk.Gis.Map.HostMapApplicationServices", true)!;
            object application = type.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException("Map application is unavailable.");
            return Get(application, "ActiveProject");
        }

        private static object Get(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target)
            ?? throw new InvalidOperationException($"Native property {target.GetType().FullName}.{name} is unavailable.");

        private static void SetTextProperty(object target, string name, string value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(target.GetType().FullName, name);
            if (!property.CanRead || !property.CanWrite || property.PropertyType != typeof(string))
                throw new InvalidOperationException($"Native {target.GetType().FullName}.{name} must be a readable/writable coordinate-system string.");
            property.SetValue(target, value);
        }
    }
}
