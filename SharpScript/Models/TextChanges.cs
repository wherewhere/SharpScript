using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpScript.Models
{
    [JsonConverter(typeof(JsonConverter))]
    public readonly struct TextChanges(List<TextChange> value) : IReadOnlyList<TextChange>
    {
        private readonly List<TextChange> value = value;

        public TextChange this[int index] => value[index];

        public int Count => value.Count;

        public IEnumerator<TextChange> GetEnumerator() => value.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)value).GetEnumerator();

        public static implicit operator List<TextChange>(TextChanges changes) => changes.value;
        public static implicit operator TextChanges(List<TextChange> changes) => new(changes);

        public class JsonConverter : JsonConverter<TextChanges>
        {
            public override TextChanges Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                List<TextChange> list = [];
                JsonElement element = JsonDocument.ParseValue(ref reader).RootElement;
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            TextSpan span = default;
                            if (item.TryGetProperty("span", out JsonElement spanElement) && spanElement.ValueKind == JsonValueKind.Object)
                            {
                                int start = 0, end = 0;
                                if (spanElement.TryGetProperty("start", out JsonElement startElement))
                                {
                                    _ = startElement.TryGetInt32(out start);
                                }
                                if (spanElement.TryGetProperty("end", out JsonElement endElement))
                                {
                                    _ = endElement.TryGetInt32(out end);
                                }
                                span = TextSpan.FromBounds(start, end);
                            }
                            string newText = string.Empty;
                            if (item.TryGetProperty("newText", out JsonElement newTextElement) && newTextElement.ValueKind == JsonValueKind.String)
                            {
                                newText = newTextElement.GetString() ?? newText;
                            }
                            list.Add(new TextChange(span, newText));
                        }
                    }
                }
                return new(list);
            }

            public override void Write(Utf8JsonWriter writer, TextChanges value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, value.value, options);
        }
    }
}
