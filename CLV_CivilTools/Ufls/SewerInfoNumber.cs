using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    // A plan identifier, not a measurement. Numeric JSON and numeric names retain their legacy form.
    [JsonConverter(typeof(SewerManholeNumberJsonConverter))]
    internal readonly struct SewerManholeNumber : IEquatable<SewerManholeNumber>, IComparable<SewerManholeNumber>, IComparable
    {
        private readonly int? _numeric;
        private readonly string? _letters;
        private SewerManholeNumber(int? numeric, string letters) { _numeric = numeric; _letters = letters; }
        internal bool IsValid => _letters != null && (_numeric is >= 1 and <= 999999 || _numeric == null && _letters.Length > 0);
        internal string NameToken
        {
            get
            {
                if (!IsValid) throw new ArgumentException("Enter a manhole identifier, such as 6, 6A or A.");
                return (_numeric?.ToString("D2", CultureInfo.InvariantCulture) ?? "") + _letters;
            }
        }
        internal static SewerManholeNumber Parse(string? text)
        {
            string value = (text ?? "").Trim().ToUpperInvariant();
            if (value.Length == 0 || value.Length > 64)
                throw new ArgumentException("Enter a manhole identifier, such as 6, 6A or A.");
            Match match = Regex.Match(value, @"^(?<number>[0-9]*)(?<letters>[A-Z]{0,8})$");
            if (!match.Success)
                throw new ArgumentException("Use a number from 1 to 999999 with an optional letter suffix (6A), or up to 8 letters. No spaces or punctuation.");
            string digits = match.Groups["number"].Value, letters = match.Groups["letters"].Value;
            int? numeric = null;
            if (digits.Length > 0)
            {
                if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > 999999)
                    throw new ArgumentException("The numeric part of a manhole identifier must be 1 to 999999.");
                numeric = n;
            }
            return new SewerManholeNumber(numeric, letters);
        }
        public override string ToString() => (_numeric?.ToString(CultureInfo.InvariantCulture) ?? "") + _letters;
        public bool Equals(SewerManholeNumber other) => _numeric == other._numeric && string.Equals(_letters, other._letters, StringComparison.Ordinal);
        public override bool Equals(object? other) => other is SewerManholeNumber n && Equals(n);
        public override int GetHashCode() => HashCode.Combine(_numeric, _letters);
        public int CompareTo(SewerManholeNumber other)
        {
            int order = _numeric.HasValue && other._numeric.HasValue ? _numeric.Value.CompareTo(other._numeric.Value)
                : _numeric.HasValue ? -1 : other._numeric.HasValue ? 1 : 0;
            return order != 0 ? order : string.Compare(_letters, other._letters, StringComparison.Ordinal);
        }
        public int CompareTo(object? other) => other == null ? 1 : other is SewerManholeNumber number
            ? CompareTo(number) : throw new ArgumentException("Compare manhole identifiers with other manhole identifiers.");
        public static bool operator ==(SewerManholeNumber left, SewerManholeNumber right) => left.Equals(right);
        public static bool operator !=(SewerManholeNumber left, SewerManholeNumber right) => !left.Equals(right);
        public static implicit operator SewerManholeNumber(int value) => Parse(value.ToString(CultureInfo.InvariantCulture));
        public static implicit operator SewerManholeNumber(string value) => Parse(value);
        internal void Write(Utf8JsonWriter writer)
        {
            if (!IsValid) throw new JsonException("Missing or invalid manhole identifier.");
            if (_numeric.HasValue && _letters!.Length == 0) writer.WriteNumberValue(_numeric.Value);
            else writer.WriteStringValue(ToString());
        }
    }
    internal sealed class SewerManholeNumberJsonConverter : JsonConverter<SewerManholeNumber>
    {
        public override SewerManholeNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int n)) return n;
            if (reader.TokenType == JsonTokenType.String) return SewerManholeNumber.Parse(reader.GetString());
            throw new JsonException("Manhole number must be a positive integer or an identifier string such as \"6A\".");
        }
        public override void Write(Utf8JsonWriter writer, SewerManholeNumber value, JsonSerializerOptions options) => value.Write(writer);
    }
}
