using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Temporalog.InfluxDB;

public class PointData
{
    private static readonly DateTime EpochStart = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _measurement;

    private readonly Dictionary<string, object> _fields;

    private readonly Dictionary<string, string> _tags;

    private BigInteger? _time;

    public PointData(string measurement)
    {
        _measurement = measurement;
        _fields = new Dictionary<string, object>();
        _tags = new Dictionary<string, string>();
    }

    internal static PointData Measurement(string measurement)
    {
        return new PointData(measurement);
    }

    public string ToLineProtocol()
    {
        var sb = new StringBuilder();
        EscapeKey(sb, _measurement, false);
        AppendTags(sb);
        var appendedFields = AppendFields(sb);
        if (!appendedFields)
        {
            return "";
        }

        AppendTime(sb);
        var s = sb.ToString();
        return s;
    }

    public PointData Timestamp(WritePrecision precision)
    {
        BigInteger time;
        var timestamp = (DateTime.UtcNow - EpochStart);
        switch (precision)
        {
            case WritePrecision.Ns:
                time = timestamp.Ticks * 100;
                break;
            case WritePrecision.Us:
                time = (BigInteger)(timestamp.Ticks * 0.1);
                break;
            case WritePrecision.Ms:
                time = (BigInteger)timestamp.TotalMilliseconds;
                break;
            case WritePrecision.S:
                time = (BigInteger)timestamp.TotalSeconds;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(precision), precision,
                    "WritePrecision value is not supported");
        }
        _time = time;

        return this;
    }

    internal PointData Field(string key, object value)
    {
        _fields.Add(key, value);
        return this;
    }

    internal PointData Tag(string key, string value)
    {
        _tags.Add(key, value);
        return this;
    }

    private void EscapeKey(StringBuilder sb, string key, bool escapeEqual = true)
    {
        foreach (var c in key)
        {
            switch (c)
            {
                case '\n':
                    _ = sb.Append("\\n");
                    continue;
                case '\r':
                    _ = sb.Append("\\r");
                    continue;
                case '\t':
                    _ = sb.Append("\\t");
                    continue;
                case ' ':
                case ',':
                    _ = sb.Append("\\");
                    break;
                case '=':
                    if (escapeEqual)
                    {
                        _ = sb.Append("\\");
                    }
                    break;
            }

            _ = sb.Append(c);
        }
    }

    private void AppendTags(StringBuilder writer)
    {


        foreach (KeyValuePair<string, string> keyValue in _tags)
        {
            var key = keyValue.Key;
            var value = keyValue.Value;

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            _ = writer.Append(',');
            EscapeKey(writer, key);
            _ = writer.Append('=');
            EscapeKey(writer, value);
        }

        _ = writer.Append(' ');
    }

    private bool AppendFields(StringBuilder sb)
    {
        var appended = false;

        foreach (KeyValuePair<string, object> keyValue in _fields)
        {
            var key = keyValue.Key;
            var value = keyValue.Value;

            if (IsNotDefined(value))
            {
                continue;
            }

            EscapeKey(sb, key);
            _ = sb.Append('=');

            switch (value)
            {
                case double:
                case float:
                    _ = sb.Append(((IConvertible)value).ToString(CultureInfo.InvariantCulture));
                    break;
                case uint:
                case ulong:
                case ushort:
                    _ = sb.Append(((IConvertible)value).ToString(CultureInfo.InvariantCulture));
                    _ = sb.Append('u');
                    break;
                case byte:
                case int:
                case long:
                case sbyte:
                case short:
                    _ = sb.Append(((IConvertible)value).ToString(CultureInfo.InvariantCulture));
                    _ = sb.Append('i');
                    break;
                case bool b:
                    _ = sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    _ = sb.Append('"');
                    EscapeValue(sb, s);
                    _ = sb.Append('"');
                    break;
                case IConvertible c:
                    _ = sb.Append(c.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    _ = sb.Append(value);
                    break;
            }

            _ = sb.Append(',');
            appended = true;
        }

        if (appended)
        {
            _ = sb.Remove(sb.Length - 1, 1);
        }

        return appended;
    }

    private void AppendTime(StringBuilder sb)
    {
        if (_time == null)
        {
            return;
        }

        sb.Append(' ');
        sb.Append(((BigInteger)_time).ToString(CultureInfo.InvariantCulture));
    }

    private void EscapeValue(StringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                case '\"':
                    _ = sb.Append("\\");
                    break;
            }

            _ = sb.Append(c);
        }
    }
    private bool IsNotDefined(object? value)
    {
        return value == null
               || value is double d && (double.IsInfinity(d) || double.IsNaN(d))
               || value is float f && (float.IsInfinity(f) || float.IsNaN(f));
    }
}