using Microsoft.CodeAnalysis.Text;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpScript.Models
{
    public readonly struct DocumentChange
    {
        [JsonPropertyName("range")]
        [JsonConverter(typeof(LinePositionSpanJsonConverter))]
        public LinePositionSpan Range { get; init; }

        [JsonPropertyName("text")]
        public string Text { get; init; }

        private class LinePositionSpanJsonConverter : JsonConverter<LinePositionSpan>
        {
            public override LinePositionSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                JsonElement element = JsonDocument.ParseValue(ref reader).RootElement;
                LinePosition start = default, end = default;
                if (element.TryGetProperty("start", out JsonElement startElement) && startElement.ValueKind == JsonValueKind.Object)
                {
                    int line = 0, character = 0;
                    if (element.TryGetProperty("line", out JsonElement lineElement))
                    {
                        _ = lineElement.TryGetInt32(out line);
                    }
                    if (element.TryGetProperty("character", out JsonElement characterElement))
                    {
                        _ = characterElement.TryGetInt32(out character);
                    }
                    start = new LinePosition(line, character);
                }
                if (element.TryGetProperty("start", out JsonElement endElement) && endElement.ValueKind == JsonValueKind.Object)
                {
                    int line = 0, character = 0;
                    if (element.TryGetProperty("line", out JsonElement lineElement))
                    {
                        _ = lineElement.TryGetInt32(out line);
                    }
                    if (element.TryGetProperty("character", out JsonElement characterElement))
                    {
                        _ = characterElement.TryGetInt32(out character);
                    }
                    end = new LinePosition(line, character);
                }
                return new LinePositionSpan(start, end);
            }

            public override void Write(Utf8JsonWriter writer, LinePositionSpan value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, value, options);
        }
    }
}
