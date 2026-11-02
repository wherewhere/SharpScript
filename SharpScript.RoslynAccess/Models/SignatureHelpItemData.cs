using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Threading;

namespace SharpScript.RoslynAccess.Models
{
    [method: JsonConstructor]
    public readonly struct SignatureHelpItemData(
       Func<CancellationToken, IEnumerable<TaggedText>> documentationFactory,
       ImmutableArray<TaggedText> prefixDisplayParts,
       ImmutableArray<TaggedText> separatorDisplayParts,
       ImmutableArray<TaggedText> suffixDisplayParts,
       IEnumerable<SignatureHelpParameterData> parameters,
       int parameterCount)
    {
        public Func<CancellationToken, IEnumerable<TaggedText>> DocumentationFactory => documentationFactory;
        public ImmutableArray<TaggedText> PrefixDisplayParts => prefixDisplayParts;
        public ImmutableArray<TaggedText> SeparatorDisplayParts => separatorDisplayParts;
        public ImmutableArray<TaggedText> SuffixDisplayParts => suffixDisplayParts;
        public IEnumerable<SignatureHelpParameterData> Parameters => parameters;
        public int ParameterCount => parameterCount;
    }
}
