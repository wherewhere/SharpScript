using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using Microsoft.JSInterop;
using SharpScript.Common;
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using RoslynCompletionChange = Microsoft.CodeAnalysis.Completion.CompletionChange;

namespace SharpScript.Models
{
    public record struct CompletionChange(ImmutableArray<TextChange> TextChanges, int? NewPosition)
    {
        public CompletionChange(RoslynCompletionChange change) : this(change.TextChanges, change.NewPosition) { }
    }

    public interface ICompletionItem : IDisposable
    {
        string DisplayText { get; }
        string FilterText { get; }
        string SortText { get; }
        string InlineDescription { get; }
        ImmutableArray<string> Tags { get; }
        TextSpan Span { get; }
        DotNetObjectReference<ICompletionItem> Self { get; }
        [JSInvokable]
        Task<ImmutableArray<TaggedText>> GetDescriptionAsync();
        [JSInvokable]
        Task<CompletionChange> GetChangeAsync();
        void IDisposable.Dispose() { Self?.Dispose(); GC.SuppressFinalize(this); }
    }

    public sealed class RoslynCompletionItem(CompletionItem item, RoslynCodeSession session) : ICompletionItem
    {
        public string DisplayText => item.DisplayText;
        public string FilterText => item.FilterText;
        public string SortText => item.SortText;
        public string InlineDescription => item.InlineDescription;
        public ImmutableArray<string> Tags => item.Tags;
        public TextSpan Span => item.Span;
        public DotNetObjectReference<ICompletionItem> Self => DotNetObjectReference.Create<ICompletionItem>(this);
        [JSInvokable]
        public Task<ImmutableArray<TaggedText>> GetDescriptionAsync() => session.GetCompletionDescriptionAsync(item);
        [JSInvokable]
        public Task<CompletionChange> GetChangeAsync() => session.GetCompletionChangeAsync(item);
    }
}
