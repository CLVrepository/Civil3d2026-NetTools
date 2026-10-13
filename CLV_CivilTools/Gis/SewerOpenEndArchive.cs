using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CLV_CivilTools.Gis
{
    // Keep the S9 constructor/property order: its canonical JSON is digest-linked.
    internal sealed record StubPipeLink(string Handle, string Name, bool AtStart, double X, double Y, double Z);
    internal sealed record StubArchiveEntry(string Handle, string Name, string Part, double X, double Y, double Z,
        string SourceShape, string OdFingerprint, string NativeOd, StubPipeLink[] Pipes);
    internal sealed record StubArchiveData(int Version, StubArchiveEntry[] Points);
    internal sealed record OpenEndArchiveEntry(string Kind, string SourceRole, StubArchiveEntry Source);
    internal sealed record OpenEndArchiveData(int Version, OpenEndArchiveEntry[] Points);
    internal sealed record OpenEndArchiveState(int Version, OpenEndArchiveEntry[] Points, string Json);

    /// <summary>Pure, fail-closed codec for legacy stub and upgraded open-end archives.</summary>
    internal static class SewerOpenEndArchive
    {
        public static OpenEndArchiveState Read(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
                throw new FormatException("Open-end archive version is missing or invalid.");

            OpenEndArchiveEntry[] points;
            string canonical;
            if (number == 1)
            {
                StubArchiveData legacy = JsonSerializer.Deserialize<StubArchiveData>(json)
                    ?? throw new FormatException("Stub archive is unreadable.");
                if (legacy.Points == null) throw new FormatException("Null stub archive points.");
                foreach (StubArchiveEntry source in legacy.Points) ValidateSource(source);
                canonical = JsonSerializer.Serialize(legacy);
                points = legacy.Points.Select(source => new OpenEndArchiveEntry("NullStub", "OpenNullEnd", source)).ToArray();
            }
            else if (number == 2)
            {
                OpenEndArchiveData upgraded = JsonSerializer.Deserialize<OpenEndArchiveData>(json)
                    ?? throw new FormatException("Open-end archive is unreadable.");
                Validate(upgraded.Points);
                canonical = JsonSerializer.Serialize(upgraded);
                points = upgraded.Points;
            }
            else throw new FormatException("Unsupported open-end archive version.");

            // Reject whitespace, alternate encodings, order, unknown/missing/duplicate properties.
            if (!string.Equals(json, canonical, StringComparison.Ordinal))
                throw new FormatException("Open-end archive JSON is not canonical.");
            return new OpenEndArchiveState(number, points, json);
        }

        public static OpenEndArchiveState Create(OpenEndArchiveEntry[] points, bool upgraded)
        {
            Validate(points);
            if (!upgraded && points.Any(point => point.Kind != "NullStub"))
                throw new FormatException("Connections require open-end archive version 2.");
            string json = upgraded
                ? JsonSerializer.Serialize(new OpenEndArchiveData(2, points))
                : JsonSerializer.Serialize(new StubArchiveData(1, points.Select(point => point.Source).ToArray()));
            return Read(json);
        }

        public static string Digest(OpenEndArchiveState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Json)));
        }

        private static void Validate(OpenEndArchiveEntry[] points)
        {
            if (points == null) throw new FormatException("Null open-end archive points.");
            foreach (OpenEndArchiveEntry point in points)
            {
                if (point == null || !((point.Kind == "NullStub" && point.SourceRole == "OpenNullEnd") ||
                    (point.Kind == "Connection" && point.SourceRole == "OpenConnectionEnd")))
                    throw new FormatException("Invalid open-end archive kind or source role.");
                ValidateSource(point.Source);
            }
        }

        private static void ValidateSource(StubArchiveEntry source)
        {
            if (source == null || source.Pipes == null || source.Pipes.Any(pipe => pipe == null))
                throw new FormatException("Null open-end archive source, pipe array, or pipe.");
        }
    }
}
