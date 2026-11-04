using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.SignatureHelp;
using SharpScript.RoslynAccess.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RoslynSignatureHelpTriggerReason = Microsoft.CodeAnalysis.SignatureHelp.SignatureHelpTriggerReason;
using SignatureHelpTriggerReason = SharpScript.RoslynAccess.Models.SignatureHelpTriggerReason;

namespace SharpScript.RoslynAccess
{
    public static class SignatureHelper
    {
        public static async ValueTask<SignatureHelpItemsData?> GetSignatureHelpAsync(this Document document, int position, SignatureHelpTriggerReason reason, char? triggerCharacter, CancellationToken cancellationToken = default)
        {
            SignatureHelpService signatureHelpService = document.Project.Solution.Services.ExportProvider.GetExports<SignatureHelpService>().Single().Value;

            // This is quite complicated to implement correctly and is still shifting around.
            // For now we will only allow default options. There is no way to check if user
            // intended something different, but that can be implemented later.
            MemberDisplayOptions mappedOptions = MemberDisplayOptions.Default;
            SignatureHelpTriggerInfo mappedTriggerInfo = new(
                (RoslynSignatureHelpTriggerReason)reason,
                triggerCharacter
            );

            (_, SignatureHelpItems? items) = await signatureHelpService.GetSignatureHelpAsync(document, position, mappedTriggerInfo, cancellationToken).ConfigureAwait(false);

            return items == null ? null
                : new SignatureHelpItemsData(
                    items.Items.Select(i => new SignatureHelpItemData(
                        i.DocumentationFactory(cancellationToken),
                        prefixDisplayParts: i.PrefixDisplayParts,
                        separatorDisplayParts: i.SeparatorDisplayParts,
                        suffixDisplayParts: i.SuffixDisplayParts,
                        parameters: i.Parameters.Select((p, i) => new SignatureHelpParameterData(
                            p.Name,
                            i == items.SemanticParameterIndex ? p.DocumentationFactory(cancellationToken) : null,
                            displayParts: p.DisplayParts,
                            prefixDisplayParts: p.PrefixDisplayParts,
                            suffixDisplayParts: p.SuffixDisplayParts
                        )),
                        i.Parameters.Length
                    )),
                    applicableSpan: items.ApplicableSpan,
                    argumentIndex: items.SemanticParameterIndex,
                    argumentCount: items.SyntacticArgumentCount,
                    selectedItemIndex: items.SelectedItemIndex
                );
        }
    }
}
