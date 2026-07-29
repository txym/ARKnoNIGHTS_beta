using System;
using System.Globalization;
using System.Text;

namespace ArknoNights.Match
{
    internal sealed class CanonicalSummaryWriter
    {
        private readonly StringBuilder builder = new StringBuilder();

        internal CanonicalSummaryWriter(string typeName)
        {
            String("type", typeName);
        }

        internal void String(string name, string value)
        {
            Token(name);
            Token(value ?? string.Empty);
        }

        internal void Integer(string name, long value)
        {
            String(name, value.ToString(CultureInfo.InvariantCulture));
        }

        internal void UnsignedInteger(string name, ulong value)
        {
            String(name, value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Boolean(string name, bool value)
        {
            Integer(name, value ? 1 : 0);
        }

        internal void EnumValue<T>(string name, T value) where T : struct
        {
            Integer(name, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        internal void NullableInteger(string name, int? value)
        {
            String(name, value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
        }

        internal void Summary(string name, string summary)
        {
            String(name, summary);
        }

        public override string ToString()
        {
            return builder.ToString();
        }

        private void Token(string value)
        {
            var safe = value ?? string.Empty;
            builder.Append(safe.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(safe)
                .Append(';');
        }
    }
}
