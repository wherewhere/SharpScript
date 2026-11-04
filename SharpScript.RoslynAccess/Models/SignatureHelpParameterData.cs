using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpScript.RoslynAccess.Models
{
    [method: JsonConstructor]
    public class SignatureHelpParameterData(
        string name,
        IEnumerable<TaggedText>? documentation,
        IList<TaggedText> displayParts,
        IList<TaggedText> prefixDisplayParts,
        IList<TaggedText> suffixDisplayParts)
    {
        public string Name => name;
        public IEnumerable<TaggedText>? Documentation => documentation;
        public IList<TaggedText> DisplayParts => displayParts;
        public IList<TaggedText> PrefixDisplayParts => prefixDisplayParts;
        public IList<TaggedText> SuffixDisplayParts => suffixDisplayParts;
    }
}
