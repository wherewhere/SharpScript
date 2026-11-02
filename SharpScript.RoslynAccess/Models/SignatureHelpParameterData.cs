using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace SharpScript.RoslynAccess.Models
{
    [method: JsonConstructor]
    public class SignatureHelpParameterData(
        string name,
        Func<CancellationToken, IEnumerable<TaggedText>> documentationFactory,
        IList<TaggedText> displayParts,
        IList<TaggedText> prefixDisplayParts,
        IList<TaggedText> suffixDisplayParts)
    {
        public string Name => name;
        public Func<CancellationToken, IEnumerable<TaggedText>> DocumentationFactory => documentationFactory;
        public IList<TaggedText> DisplayParts => displayParts;
        public IList<TaggedText> PrefixDisplayParts => prefixDisplayParts;
        public IList<TaggedText> SuffixDisplayParts => suffixDisplayParts;
    }
}
