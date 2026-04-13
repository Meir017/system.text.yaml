using System.Buffers;
using System.Globalization;
using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Writes UTF-8 YAML using a forward-only API modeled after <see cref="Utf8JsonWriter"/>.
/// </summary>
/// <remarks>
/// This first emitter milestone focuses on deterministic output for mappings, sequences, property names,
/// and the common scalar types currently supported by the reader/parser core.
/// </remarks>
public sealed class Utf8YamlWriter : IDisposable
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private readonly IBufferWriter<byte>? _output;
    private readonly Stream? _stream;
    private readonly List<ContainerFrame> _stack = [];
    private readonly YamlWriterOptions _options;
    private bool _disposed;
    private bool _hasWrittenRootValue;

    public Utf8YamlWriter(IBufferWriter<byte> output, YamlWriterOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _options = options;
    }

    public Utf8YamlWriter(Stream utf8Stream, YamlWriterOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        _stream = utf8Stream;
        _options = options;
    }

    /// <summary>
    /// Gets the options used by the writer.
    /// </summary>
    public YamlWriterOptions Options => _options;

    /// <summary>
    /// Gets the total number of bytes that have been flushed to the destination.
    /// </summary>
    public long BytesCommitted { get; private set; }

    /// <summary>
    /// Gets the number of bytes buffered but not yet flushed to the destination.
    /// </summary>
    public long BytesPending => _buffer.WrittenCount;

    public void WriteStartObject()
        => WriteStartContainer(ContainerType.Object);

    public void WriteEndObject()
        => WriteEndContainer(ContainerType.Object);

    public void WriteStartArray()
        => WriteStartContainer(ContainerType.Array);

    public void WriteEndArray()
        => WriteEndContainer(ContainerType.Array);

    public void WritePropertyName(string propertyName)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(propertyName);

        if (_stack.Count == 0 || _stack[^1].Type != ContainerType.Object)
        {
            throw new InvalidOperationException("Property names can only be written inside an object.");
        }

        if (_options.Indented)
        {
            EnsureContainerStartWritten(_stack.Count - 1);
        }

        var frame = _stack[^1];
        if (frame.PendingPropertyName is not null)
        {
            throw new InvalidOperationException("A property name has already been written and requires a value.");
        }

        frame.PendingPropertyName = propertyName;
        _stack[^1] = frame;
    }

    public void WriteStringValue(string? value)
    {
        if (value is null)
        {
            WriteNullValue();
            return;
        }

        WriteScalarValue(FormatString(value, forceQuoted: !_options.Indented));
    }

    public void WriteBooleanValue(bool value)
        => WriteScalarValue(value ? "true" : "false");

    public void WriteNullValue()
        => WriteScalarValue("null");

    public void WriteNumberValue(int value)
        => WriteNumberValue(value.ToString(CultureInfo.InvariantCulture));

    public void WriteNumberValue(long value)
        => WriteNumberValue(value.ToString(CultureInfo.InvariantCulture));

    public void WriteNumberValue(double value)
        => WriteNumberValue(value.ToString("R", CultureInfo.InvariantCulture));

    public void WriteNumberValue(decimal value)
        => WriteNumberValue(value.ToString(CultureInfo.InvariantCulture));

    public void Flush()
    {
        ThrowIfDisposed();

        if (_buffer.WrittenCount == 0)
        {
            return;
        }

        var bytes = _buffer.WrittenSpan;
        if (_output is not null)
        {
            var destination = _output.GetSpan(bytes.Length);
            bytes.CopyTo(destination);
            _output.Advance(bytes.Length);
        }
        else if (_stream is not null)
        {
            _stream.Write(bytes);
            _stream.Flush();
        }

        BytesCommitted += bytes.Length;
        _buffer.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Flush();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal void WriteNumberValue(string rawValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawValue);
        WriteScalarValue(rawValue);
    }

    private void WriteStartContainer(ContainerType type)
    {
        ThrowIfDisposed();

        if (_options.Indented)
        {
            WriteStartContainerBlock(type);
            return;
        }

        WriteCompactValuePrefix();
        WriteText(type == ContainerType.Object ? "{" : "[");
        _stack.Add(new ContainerFrame { Type = type, HeaderWritten = true, StartContext = ContainerStartContext.Root });
    }

    private void WriteEndContainer(ContainerType type)
    {
        ThrowIfDisposed();

        if (_stack.Count == 0 || _stack[^1].Type != type)
        {
            throw new InvalidOperationException($"Cannot end a {type} before starting it.");
        }

        var frame = _stack[^1];
        if (frame.PendingPropertyName is not null)
        {
            throw new InvalidOperationException("Cannot close an object while a property is waiting for a value.");
        }

        if (_options.Indented)
        {
            if (frame.ItemCount == 0)
            {
                if (frame.StartContext == ContainerStartContext.Root)
                {
                    WriteText(type == ContainerType.Object ? "{}" : "[]");
                }
                else
                {
                    EnsureContainerStartWritten(_stack.Count - 1);
                    WriteText(type == ContainerType.Object ? " {}" : " []");
                }
            }
        }
        else
        {
            WriteText(type == ContainerType.Object ? "}" : "]");
        }

        _stack.RemoveAt(_stack.Count - 1);
    }

    private void WriteStartContainerBlock(ContainerType type)
    {
        if (_stack.Count == 0)
        {
            EnsureCanWriteRootValue();
            _hasWrittenRootValue = true;
            _stack.Add(new ContainerFrame { Type = type, HeaderWritten = true, StartContext = ContainerStartContext.Root });
            return;
        }

        EnsureContainerStartWritten(_stack.Count - 1);

        var parent = _stack[^1];
        var child = new ContainerFrame
        {
            Type = type,
            StartContext = parent.Type == ContainerType.Object ? ContainerStartContext.ObjectProperty : ContainerStartContext.ArrayItem
        };

        if (parent.Type == ContainerType.Object)
        {
            if (parent.PendingPropertyName is null)
            {
                throw new InvalidOperationException("Cannot write an object or array value inside an object without a property name.");
            }

            child.AssociatedPropertyName = parent.PendingPropertyName;
            parent.PendingPropertyName = null;
        }

        parent.ItemCount++;
        _stack[^1] = parent;
        _stack.Add(child);
    }

    private void WriteScalarValue(string literal)
    {
        ThrowIfDisposed();

        if (_options.Indented)
        {
            WriteScalarValueBlock(literal);
            return;
        }

        WriteCompactValuePrefix();
        WriteText(literal);
    }

    private void WriteScalarValueBlock(string literal)
    {
        if (_stack.Count == 0)
        {
            EnsureCanWriteRootValue();
            _hasWrittenRootValue = true;
            WriteText(literal);
            return;
        }

        EnsureContainerStartWritten(_stack.Count - 1);

        var frame = _stack[^1];
        frame.ItemCount++;

        WriteBlockEntryPreamble(frame, frame.ItemCount);
        WriteIndent(_stack.Count - 1);

        if (frame.Type == ContainerType.Object)
        {
            if (frame.PendingPropertyName is null)
            {
                throw new InvalidOperationException("Cannot write a scalar value inside an object without a property name.");
            }

            WriteText(FormatPropertyName(frame.PendingPropertyName, forceQuoted: false));
            WriteText(": ");
            frame.PendingPropertyName = null;
        }
        else
        {
            WriteText("- ");
        }

        WriteText(literal);
        _stack[^1] = frame;
    }

    private void WriteCompactValuePrefix()
    {
        if (_stack.Count == 0)
        {
            EnsureCanWriteRootValue();
            _hasWrittenRootValue = true;
            return;
        }

        var frame = _stack[^1];
        if (frame.Type == ContainerType.Object)
        {
            if (frame.PendingPropertyName is null)
            {
                throw new InvalidOperationException("Cannot write a value inside an object without a property name.");
            }

            if (frame.ItemCount > 0)
            {
                WriteText(",");
            }

            WriteText(QuoteJsonString(frame.PendingPropertyName));
            WriteText(":");
            frame.PendingPropertyName = null;
        }
        else if (frame.ItemCount > 0)
        {
            WriteText(",");
        }

        frame.ItemCount++;
        _stack[^1] = frame;
    }

    private void EnsureContainerStartWritten(int frameIndex)
    {
        if (frameIndex < 0)
        {
            return;
        }

        var frame = _stack[frameIndex];
        if (frame.HeaderWritten)
        {
            return;
        }

        EnsureContainerStartWritten(frameIndex - 1);

        var parent = _stack[frameIndex - 1];
        WriteBlockEntryPreamble(parent, parent.ItemCount);
        WriteIndent(frameIndex - 1);

        if (frame.StartContext == ContainerStartContext.ObjectProperty)
        {
            WriteText(FormatPropertyName(frame.AssociatedPropertyName!, forceQuoted: false));
            WriteText(":");
        }
        else
        {
            WriteText("-");
        }

        frame.HeaderWritten = true;
        _stack[frameIndex] = frame;
    }

    private void WriteBlockEntryPreamble(ContainerFrame frame, int itemIndex)
    {
        if (itemIndex > 1 || frame.StartContext != ContainerStartContext.Root)
        {
            WriteText(_options.NewLine);
        }
    }

    private void WriteIndent(int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        WriteText(new string(' ', depth * _options.IndentationSize));
    }

    private void EnsureCanWriteRootValue()
    {
        if (_hasWrittenRootValue && _stack.Count == 0)
        {
            throw new InvalidOperationException("Only a single YAML document can be written per Utf8YamlWriter instance.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Utf8YamlWriter));
        }
    }

    private void WriteText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        var destination = _buffer.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value.AsSpan(), destination);
        _buffer.Advance(written);
    }

    private static string FormatPropertyName(string propertyName, bool forceQuoted)
        => forceQuoted || RequiresQuotedPropertyName(propertyName)
            ? QuoteJsonString(propertyName)
            : propertyName;

    private static string FormatString(string value, bool forceQuoted)
        => forceQuoted || RequiresQuotedScalar(value)
            ? QuoteJsonString(value)
            : value;

    private static string QuoteJsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ')
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static bool RequiresQuotedPropertyName(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        foreach (var character in value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '_' or '-'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresQuotedScalar(string value)
    {
        if (value.Length == 0 ||
            char.IsWhiteSpace(value[0]) ||
            char.IsWhiteSpace(value[^1]) ||
            string.Equals(value, "~", StringComparison.Ordinal) ||
            string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ||
            bool.TryParse(value, out _) ||
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        if (value[0] is '-' or '?' or ':' or '!' or '&' or '*' or '@' or '`')
        {
            return true;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character) ||
                character is '#' or ':' or '"' or '\'' or '{' or '}' or '[' or ']' or ',' or '|' or '>' or '%')
            {
                return true;
            }
        }

        return false;
    }

    private enum ContainerType
    {
        Object,
        Array
    }

    private enum ContainerStartContext
    {
        Root,
        ObjectProperty,
        ArrayItem
    }

    private struct ContainerFrame
    {
        public ContainerType Type { get; set; }

        public int ItemCount { get; set; }

        public string? PendingPropertyName { get; set; }

        public bool HeaderWritten { get; set; }

        public ContainerStartContext StartContext { get; set; }

        public string? AssociatedPropertyName { get; set; }
    }
}
