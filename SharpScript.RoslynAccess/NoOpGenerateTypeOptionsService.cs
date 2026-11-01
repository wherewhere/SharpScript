using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.GenerateType;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageService;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.ProjectManagement;
using System;
using System.Composition;

namespace SharpScript.RoslynAccess
{
    [ExportWorkspaceService(typeof(IGenerateTypeOptionsService), ServiceLayer.Test), Shared]
    [method: ImportingConstructor, Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    internal sealed class NoOpGenerateTypeOptionsService() : IGenerateTypeOptionsService
    {
        public GenerateTypeOptionsResult GetGenerateTypeOptions(string className, GenerateTypeDialogOptions generateTypeDialogOptions, Document document, INotificationService? notificationService, IProjectManagementService? projectManagementService, ISyntaxFactsService syntaxFactsService) => GenerateTypeOptionsResult.Cancelled;
    }
}
