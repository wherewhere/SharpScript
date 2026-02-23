using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;

namespace SharpScript.Models
{
    public record struct InfoTipItem(ImmutableArray<string> Tags, TextSpan Span, params InfoTipSection[] Sections)
    {
        public InfoTipItem(QuickInfoItem item) : this(item.Tags, item.Span, [.. item.Sections.Select(x => new InfoTipSection(x))]) { }
    }

    public record struct InfoTipSection(string Kind, params InfoTipTaggedText[] Parts)
    {
        public InfoTipSection(QuickInfoSection section) : this(section.Kind, [.. section.TaggedParts.Select(x => new InfoTipTaggedText(x))]) { }
    }

    public record struct InfoTipTaggedText(string Tag, string Text)
    {
        public InfoTipTaggedText(TaggedText text) : this(text.Tag, text.Text) { }
    }
}
