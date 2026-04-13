using System.Buffers;
using System.Text;

namespace System.Text.Yaml.Tests;

public class Utf8YamlWriterTests
{
    [Fact]
    public void WriteIndented_WritesMappingsSequencesAndScalars()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8YamlWriter(buffer, new YamlWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WritePropertyName("name");
        writer.WriteStringValue("Ada");
        writer.WritePropertyName("count");
        writer.WriteNumberValue(2);
        writer.WritePropertyName("enabled");
        writer.WriteBooleanValue(true);
        writer.WritePropertyName("note");
        writer.WriteNullValue();
        writer.WritePropertyName("textAsBool");
        writer.WriteStringValue("true");
        writer.WritePropertyName("message");
        writer.WriteStringValue("yaml: writer");
        writer.WritePropertyName("tags");
        writer.WriteStartArray();
        writer.WriteStringValue("yaml");
        writer.WriteStringValue("parser");
        writer.WriteEndArray();
        writer.WritePropertyName("emptyObject");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("emptyArray");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        var yaml = Encoding.UTF8.GetString(buffer.WrittenSpan);

        Assert.Equal("""
            name: Ada
            count: 2
            enabled: true
            note: null
            textAsBool: "true"
            message: "yaml: writer"
            tags:
              - yaml
              - parser
            emptyObject: {}
            emptyArray: []
            """.ReplaceLineEndings("\n").TrimEnd(), yaml);
    }

    [Fact]
    public void WriteIndented_RespectsIndentationAndNewLineOptions()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8YamlWriter(buffer, new YamlWriterOptions
        {
            Indented = true,
            IndentationSize = 4,
            NewLine = "\r\n"
        });

        writer.WriteStartObject();
        writer.WritePropertyName("items");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WritePropertyName("value");
        writer.WriteStringValue("x");
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        var yaml = Encoding.UTF8.GetString(buffer.WrittenSpan);

        Assert.Equal("items:\r\n    -\r\n        value: x", yaml);
    }

    [Fact]
    public void CompactMode_WritesJsonSubsetYaml()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8YamlWriter(buffer);

        writer.WriteStartArray();
        writer.WriteStringValue("yaml");
        writer.WriteNumberValue(2);
        writer.WriteStartObject();
        writer.WritePropertyName("enabled");
        writer.WriteBooleanValue(false);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.Flush();

        var yaml = Encoding.UTF8.GetString(buffer.WrittenSpan);

        Assert.Equal("""["yaml",2,{"enabled":false}]""", yaml);
    }
}
