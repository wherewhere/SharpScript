using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using SharpScript.Models;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    public static class InfoTipServer
    {
        private static bool _outOfDate;

        public static CustomWorkspace Workspace { get; } = new CustomWorkspace();

        public static SourceText SourceText
        {
            get;
            private set
            {
                if (field != value)
                {
                    field = value;
                    _outOfDate = true;
                }
            }
        } = SourceText.From(string.Empty);

        private static Document _currentDocument;

        public static QuickInfoService? QuickInfoService
        {
            get
            {
                EnsureUpToDate();
                field ??= QuickInfoService.GetService(_currentDocument);
                return field;
            }
        }

        static InfoTipServer()
        {
            ProjectId projectId = ProjectId.CreateNewId();
            DocumentId docId = DocumentId.CreateNewId(projectId, "SharpScript.CodeSession");
            Solution solution = Workspace.CurrentSolution
                .AddProject(projectId, "SharpScript.Project.CodeSession", "SharpScript.QuickInfo", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, RoslynCodeSession.References)
                .WithProjectParseOptions(projectId,
                    new CSharpParseOptions(
                        LanguageVersion.Preview,
                        DocumentationMode.Parse,
                        SourceCodeKind.Regular))
                .AddDocument(docId, "SharpScript.CodeSession.Document", SourceText);
            _ = Workspace.SetCurrentSolution(solution);
            Workspace.OpenDocument(docId);
            _currentDocument = Workspace.CurrentSolution.GetDocument(docId)!;
        }

        private static void EnsureUpToDate()
        {
            if (!_outOfDate) { return; }
            Document document = _currentDocument.WithText(SourceText);
            _currentDocument = Workspace.SetCurrentSolution(document.Project.Solution).GetDocument(_currentDocument.Id)!;
            _outOfDate = false;
        }

        public static void SetSourceCode(string code) => SourceText = SourceText.From(code, Encoding.Default);

        public static async ValueTask<InfoTipItem> GetInfoTipAsync(int position, CancellationToken cancellationToken = default)
        {
            QuickInfoItem? info = await QuickInfoService!.GetQuickInfoAsync(_currentDocument, position, cancellationToken).ConfigureAwait(false);
            return info is null or { Sections.IsEmpty: true } ? default : new InfoTipItem(info);
        }
    }
}
