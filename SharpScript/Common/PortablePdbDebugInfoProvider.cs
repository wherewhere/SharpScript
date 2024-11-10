using ICSharpCode.Decompiler.DebugInfo;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;

namespace SharpScript.Common
{
    public partial class PortablePdbDebugInfoProvider : IDebugInfoProvider, IDisposable
    {
        private readonly MetadataReaderProvider _readerProvider;
        private readonly MetadataReader _reader;

        public PortablePdbDebugInfoProvider(Stream symbolStream)
        {
            _readerProvider = MetadataReaderProvider.FromPortablePdbStream(symbolStream);
            _reader = _readerProvider.GetMetadataReader();
        }

        public string SourceFileName => "_";
        public string Description => "";

        public IList<ICSharpCode.Decompiler.DebugInfo.SequencePoint> GetSequencePoints(MethodDefinitionHandle method)
        {
            MethodDebugInformation debugInfo = _reader.GetMethodDebugInformation(method);
            SequencePointCollection points = debugInfo.GetSequencePoints();
            return points.Select(static point => new ICSharpCode.Decompiler.DebugInfo.SequencePoint
            {
                Offset = point.Offset,
                StartLine = point.StartLine,
                StartColumn = point.StartColumn,
                EndLine = point.EndLine,
                EndColumn = point.EndColumn,
                DocumentUrl = "_"
            }).ToArray();
        }

        public IList<Variable> GetVariables(MethodDefinitionHandle method) =>
            EnumerateLocals(method).Select(local => new Variable(local.Index, _reader.GetString(local.Name))).ToArray();

        public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
        {
            foreach (LocalVariable local in EnumerateLocals(method).Where(local => local.Index == index))
            {
                name = _reader.GetString(local.Name);
                return true;
            }
            name = null;
            return false;
        }

        private IEnumerable<LocalVariable> EnumerateLocals(MethodDefinitionHandle method) =>
            from LocalScopeHandle scopeHandle in _reader.GetLocalScopes(method)
            let scope = _reader.GetLocalScope(scopeHandle)
            from LocalVariableHandle variableHandle in scope.GetLocalVariables()
            select _reader.GetLocalVariable(variableHandle);

        bool IDebugInfoProvider.TryGetExtraTypeInfo(MethodDefinitionHandle method, int index, out PdbExtraTypeInfo extraTypeInfo)
        {
            extraTypeInfo = default;
            return false;
        }

        public void Dispose()
        {
            _readerProvider.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
