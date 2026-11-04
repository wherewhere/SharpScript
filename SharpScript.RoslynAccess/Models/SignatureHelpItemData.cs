using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpScript.RoslynAccess.Models
{
    [method: JsonConstructor]
    public readonly struct SignatureHelpItemData(
       IEnumerable<TaggedText> documentation,
       ImmutableArray<TaggedText> prefixDisplayParts,
       ImmutableArray<TaggedText> separatorDisplayParts,
       ImmutableArray<TaggedText> suffixDisplayParts,
       IEnumerable<SignatureHelpParameterData> parameters,
       int parameterCount)
    {
        public IEnumerable<TaggedText> Documentation => documentation;
        public ImmutableArray<TaggedText> PrefixDisplayParts => prefixDisplayParts;
        public ImmutableArray<TaggedText> SeparatorDisplayParts => separatorDisplayParts;
        public ImmutableArray<TaggedText> SuffixDisplayParts => suffixDisplayParts;
        public IEnumerable<SignatureHelpParameterData> Parameters => parameters;
        public int ParameterCount => parameterCount;
    }
}
