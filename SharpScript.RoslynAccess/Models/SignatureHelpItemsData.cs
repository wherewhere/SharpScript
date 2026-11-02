using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpScript.RoslynAccess.Models
{
    [method: JsonConstructor]
    public class SignatureHelpItemsData(
        IEnumerable<SignatureHelpItemData> items,
        TextSpan applicableSpan,
        int argumentIndex,
        int argumentCount,
        int? selectedItemIndex)
    {
        public IEnumerable<SignatureHelpItemData> Items => items;
        public TextSpan ApplicableSpan => applicableSpan;
        public int ArgumentIndex => argumentIndex;
        public int ArgumentCount => argumentCount;
        public int? SelectedItemIndex => selectedItemIndex;
    }
}
