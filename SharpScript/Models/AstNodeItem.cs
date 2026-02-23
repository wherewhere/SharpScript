using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace SharpScript.Models
{
    [JsonConverter(typeof(JsonConverter))]
    public abstract class AstItemBase
    {
        public abstract string Type { get; }

        public class JsonConverter : JsonConverter<AstItemBase>
        {
            public override AstItemBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                JsonElement element = JsonDocument.ParseValue(ref reader).RootElement;
                if (element.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    switch (typeElement.GetString())
                    {
                        case "trivia":
                            return element.Deserialize<AstTriviaItem>(options);
                        case "token":
                            return element.Deserialize<AstTokenItem>(options);
                        case "node":
                            return element.Deserialize<AstNodeItem>(options);
                        case "operation":
                            return element.Deserialize<AstOperationItem>(options);
                        case "value":
                            return element.Deserialize<AstValueItem>(options);
                    }
                }
                return element.Deserialize(typeToConvert, options) as AstItemBase;
            }

            public override void Write(Utf8JsonWriter writer, AstItemBase value, JsonSerializerOptions options)
            {
                switch (value)
                {
                    case AstTriviaItem trivia:
                        JsonSerializer.Serialize(writer, trivia, options);
                        break;
                    case AstTokenItem token:
                        JsonSerializer.Serialize(writer, token, options);
                        break;
                    case AstNodeItem node:
                        JsonSerializer.Serialize(writer, node, options);
                        break;
                    case AstOperationItem operation:
                        JsonSerializer.Serialize(writer, operation, options);
                        break;
                    case AstValueItem val:
                        JsonSerializer.Serialize(writer, val, options);
                        break;
                    default:
                        JsonSerializer.Serialize(writer, value, options);
                        break;
                }
            }
        }
    }

    public class AstNodeItem() : AstItemBase
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Func<SyntaxNode, SyntaxNode, string>>> _compiledSyntaxNodeGetParentPropertyName = [];
        private static readonly ConcurrentDictionary<Type, Lazy<Func<SyntaxToken, SyntaxNode, string>>> _compiledSyntaxTokenGetParentPropertyName = [];

        protected static readonly Dictionary<int, string> _kindNames =
            Enum.GetValues<Microsoft.CodeAnalysis.CSharp.SyntaxKind>().OfType<Enum>()
                .Concat(Enum.GetValues<Microsoft.CodeAnalysis.VisualBasic.SyntaxKind>().OfType<Enum>())
                .Select(e => (name: e.ToString("G"), value: ((IConvertible)e).ToInt32(null)))
                .Distinct()
                .ToDictionary(t => t.value, t => t.name);

        public override string Type => "node";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Property { get; protected init; }
        public string Kind { get; protected init; } = string.Empty;
        public TextSpan Span { get; protected init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<AstItemBase> Children { get; protected init; } = [];

        public AstNodeItem(SyntaxNode node, SemanticModel model, string? specialParentPropertyName = null) : this()
        {
            Kind = _kindNames[node.RawKind];
            Span = node.Span;

            string? parentPropertyName = specialParentPropertyName ?? GetParentPropertyName(node);
            if (parentPropertyName != null)
            {
                Property = parentPropertyName;
            }

            List<AstItemBase> children = [];
            IOperation? operation = model.GetOperation(node);
            if (operation != null)
            {
                children.Add(new AstOperationItem(operation));
            }
            foreach (SyntaxNodeOrToken child in node.ChildNodesAndTokens())
            {
                children.Add(child.IsNode ? new AstNodeItem(child.AsNode()!, model) : new AstTokenItem(child.AsToken(), model));
            }
            Children = children;
        }

        public static string? GetParentPropertyName(SyntaxToken token) => GetParentPropertyName(token, token.Parent, _compiledSyntaxTokenGetParentPropertyName);

        public static string? GetParentPropertyName(SyntaxNode node) => GetParentPropertyName(node, node.Parent, _compiledSyntaxNodeGetParentPropertyName);

        private static string? GetParentPropertyName<T>(T value, SyntaxNode? parent, ConcurrentDictionary<Type, Lazy<Func<T, SyntaxNode, string>>> compiledCache)
        {
            if (parent == null)
            { return null; }
            Func<T, SyntaxNode, string> compiled = compiledCache.GetOrAdd(
                parent.GetType(),
                t => new Lazy<Func<T, SyntaxNode, string>>(() => SlowCompileGetParentPropertyName<T>(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return compiled(value, parent);
        }

        private static Func<T, SyntaxNode, string> SlowCompileGetParentPropertyName<T>(Type parentSyntaxType)
        {
            ParameterExpression value = Expression.Parameter(typeof(T));
            ParameterExpression parent = Expression.Parameter(typeof(SyntaxNode), "parent");
            ParameterExpression parentTyped = Expression.Variable(parentSyntaxType);

            LabelTarget end = Expression.Label(typeof(string));
            List<Expression> statements = [Expression.Assign(parentTyped, Expression.Convert(parent, parentSyntaxType))];
            foreach (PropertyInfo property in parentSyntaxType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == nameof(SyntaxNode.Parent))
                { continue; }
                if (!IsSameAsOrSubclassOf(property.PropertyType.GetTypeInfo()))
                { continue; }
                static bool IsSameAsOrSubclassOf(Type type) => type == typeof(T) || type.IsSubclassOf(typeof(T));
                BinaryExpression propertyEqualsNode = Expression.Equal(Expression.Property(parentTyped, property), value);
                statements.Add(Expression.IfThen(propertyEqualsNode, Expression.Return(end, Expression.Constant(property.Name))));
            }
            statements.Add(Expression.Label(end, Expression.Constant(null, typeof(string))));
            return Expression
                .Lambda<Func<T, SyntaxNode, string>>(Expression.Block([parentTyped], statements), value, parent)
                .Compile();
        }
    }

    public sealed class AstOperationItem(IOperation operation) : AstItemBase
    {
        private Action<IOperation, Dictionary<string, string>>? _cache;

        public override string Type => "operation";
        public string Property => "Operation";
        public string Kind => operation.Kind.ToString();
        public Dictionary<string, string> Properties
        {
            get
            {
                Dictionary<string, string> writer = [];
                Action<IOperation, Dictionary<string, string>> serialize = _cache ??= SlowCompileSerializeProperties(operation.GetType());
                serialize(operation, writer);
                return writer;
            }
        }

        private static Action<IOperation, Dictionary<string, string>> SlowCompileSerializeProperties(Type operationType)
        {
            ParameterExpression operation = Expression.Parameter(typeof(IOperation), "operation");
            ParameterExpression writer = Expression.Parameter(typeof(Dictionary<string, string>), "writer");
            IEnumerable<Expression> statements = operationType
                .GetProperties()
                .Where(p => !SlowShouldSkip(p))
                .OrderBy(p => p.Name)
                .Select(p => SlowExpressSerializeProperty(operation, p, writer));

            return Expression.Lambda<Action<IOperation, Dictionary<string, string>>>(
                Expression.Block(statements),
                operation, writer).Compile();
        }

        private static Expression SlowExpressSerializeProperty(ParameterExpression operation, PropertyInfo property, ParameterExpression writer)
        {
            // MemberInfo.DeclaringType is null only on global module methods.
            MemberExpression propertyValue = Expression.Property(Expression.Convert(operation, property.DeclaringType!), property);
            Type propertyType = property.PropertyType;

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Optional<>))
            {
                return Expression.Condition(
                    Expression.Property(propertyValue, nameof(Optional<>.HasValue)),
                    SlowExpressWriteNameAndValue(
                        property.Name,
                        Expression.Property(propertyValue, nameof(Optional<>.Value)),
                        writer),
                    Expression.Empty());
            }

            if (propertyType == typeof(bool))
            {
                return Expression.Condition(
                    propertyValue,
                    SlowExpressWriteNameAndValue(property.Name, Expression.Constant(true), writer),
                    Expression.Empty());
            }

            return SlowHandleNulls(
                propertyValue,
                SlowExpressWriteNameAndValue(property.Name, propertyValue, writer),
                Expression.Empty());
        }

        private static MethodCallExpression SlowExpressWriteNameAndValue(string name, Expression value, ParameterExpression writer)
        {
            Expression valueToWrite = SlowGetValueToWrite(value);
            return Expression.Call(writer, nameof(Dictionary<,>.Add), typeArguments: null,
                Expression.Constant(name),
                valueToWrite);
        }

        private static readonly MethodInfo ObjectToString = typeof(object).GetMethod(nameof(ToString))!;
        private static readonly Expression Skipped = Expression.Constant("<skipped>");
        private static Expression SlowGetValueToWrite(Expression value)
        {
            System.Reflection.TypeInfo type = value.Type.GetTypeInfo();
            if (type.IsAssignableTo(typeof(IEnumerable)) || type.IsAssignableTo(typeof(SyntaxNode)))
            { return Skipped; }

            if (value.Type != typeof(string))
            {
                return SlowHandleNulls(
                    value,
                    Expression.Call(value, ObjectToString),
                    Expression.Constant(null, typeof(string)));
            }

            return value;
        }

        private static Expression SlowHandleNulls(Expression value, Expression ifNotNull, Expression ifNull)
        {
            if (value.Type.IsValueType)
            { return ifNotNull; }

            return Expression.Condition(
                Expression.ReferenceNotEqual(value, Expression.Constant(null, value.Type)),
                ifNotNull,
                ifNull);
        }

        private static bool SlowShouldSkip(PropertyInfo property) =>
            property.Name == nameof(IOperation.Language)
                || property.Name == nameof(IOperation.Kind)
                || property.Name == nameof(IOperation.Parent)
#pragma warning disable CS0618 // Type or member is obsolete
                || property.Name == nameof(IOperation.Children)
#pragma warning restore CS0618 // Type or member is obsolete
                || property.Name == nameof(IOperation.ChildOperations)
                || property.Name == nameof(IOperation.Syntax)
                || property.PropertyType.IsAssignableTo(typeof(IOperation))
                || property.PropertyType.IsAssignableTo(typeof(IEnumerable<IOperation>));
    }

    public class AstTokenItem() : AstNodeItem
    {
        public override string Type => "token";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Value { get; protected init; } = string.Empty;

        public AstTokenItem(SyntaxToken token, SemanticModel model) : this()
        {
            Kind = _kindNames[token.RawKind];
            Span = token.FullSpan;

            string? parentPropertyName = GetParentPropertyName(token);
            if (parentPropertyName != null)
            {
                Property = parentPropertyName;
            }

            if (token.HasLeadingTrivia || token.HasTrailingTrivia)
            {
                Value = token.ValueText;
                List<AstItemBase> children = [];
                foreach (SyntaxTrivia trivia in token.LeadingTrivia)
                {
                    children.Add(new AstTriviaItem(trivia, model));
                }
                children.Add(new AstValueItem(token.ValueText, token.Span));
                foreach (SyntaxTrivia trivia in token.TrailingTrivia)
                {
                    children.Add(new AstTriviaItem(trivia, model));
                }
                Children = children;
            }
            else
            {
                Value = token.ToString();
            }
        }
    }

    public sealed class AstTriviaItem : AstTokenItem
    {
        public override string Type => "trivia";

        public AstTriviaItem(SyntaxTrivia trivia, SemanticModel model) : base()
        {
            Kind = _kindNames[trivia.RawKind];
            Span = trivia.Span;

            if (trivia.HasStructure)
            {
                Children = [new AstNodeItem(trivia.GetStructure()!, model, "Structure")];
            }
            else
            {
                Value = trivia.ToString();
            }
        }
    }

    public sealed class AstValueItem(string value, TextSpan span) : AstItemBase
    {
        public override string Type => "value";
        public TextSpan Span => span;
        public string Value => value;
    }
}
