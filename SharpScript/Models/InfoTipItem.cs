using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace SharpScript.Models
{
    [method: JsonConstructor]
    public readonly struct InfoTipItem(ImmutableArray<string> tags, TextSpan span, params InfoTipSection[] sections)
    {
        public ImmutableArray<string> Tags => tags;
        public TextSpan Span => span;
        public InfoTipSection[] Sections => sections;
        public InfoTipItem(QuickInfoItem item) : this(item.Tags, item.Span, [.. item.Sections.Select(x => new InfoTipSection(x))]) { }
    }

    [method: JsonConstructor]
    public readonly struct InfoTipSection(string kind, params InfoTipTaggedText[] parts)
    {
        public string Kind => kind;
        public InfoTipTaggedText[] Parts => parts;
        public InfoTipSection(QuickInfoSection section) : this(section.Kind, [.. section.TaggedParts.Select(x => new InfoTipTaggedText(x))]) { }
    }

    [method: JsonConstructor]
    public readonly struct InfoTipTaggedText(string tag, string text)
    {
        public string Tag => tag;
        public string Text => text;
        public InfoTipTaggedText(TaggedText text) : this(text.Tag, text.Text) { }
    }
}
