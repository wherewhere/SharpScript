using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SharpScript.Common
{
    public sealed class MetadataReferenceCollection : IReadOnlyList<PortableExecutableReference>, IAdditionOperators<MetadataReferenceCollection, MetadataReferenceCollection, MetadataReferenceCollection>
    {
        private readonly List<PortableExecutableReference> _references;
        private readonly List<byte[]> _referenceBytes;

        public int Count => _references.Count;

        public MetadataReferenceHost this[int index]
        {
            get => new(_referenceBytes[index], _references[index]);
            set
            {
                _referenceBytes[index] = value.Image;
                _references[index] = value.Reference;
            }
        }

        public MetadataReferenceCollection()
        {
            _references = [];
            _referenceBytes = [];
        }

        public MetadataReferenceCollection(MetadataReferenceCollection collection)
        {
            _references = [.. collection._references];
            _referenceBytes = [.. collection._referenceBytes];
        }

        PortableExecutableReference IReadOnlyList<PortableExecutableReference>.this[int index] => _references[index];

        public void Add(MemoryStream peStream, MetadataReferenceProperties properties = default, DocumentationProvider? documentation = null, string? filePath = null)
        {
            _referenceBytes.Add(peStream.ToArray());
            _references.Add(MetadataReference.CreateFromStream(peStream, properties, documentation, filePath));
        }

        public void Add(byte[] peImage, PortableExecutableReference reference)
        {
            _referenceBytes.Add(peImage);
            _references.Add(reference);
        }

        public void Add(in MetadataReferenceHost value) => Add(value.Image, value.Reference);

        public void AddRange(MetadataReferenceCollection collection)
        {
            _references.AddRange(collection._references);
            _referenceBytes.AddRange(collection._referenceBytes);
        }

        public bool Remove(PortableExecutableReference item)
        {
            int index = _references.IndexOf(item);
            if (index != -1)
            {
                _references.RemoveAt(index);
                _referenceBytes.RemoveAt(index);
                return true;
            }
            return false;
        }

        public int FindIndex(Predicate<PortableExecutableReference> match) => _references.FindIndex(match);

        IEnumerator<PortableExecutableReference> IEnumerable<PortableExecutableReference>.GetEnumerator() => _references.GetEnumerator();

        public IEnumerator<MetadataReferenceHost> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => _references.GetEnumerator();

        public static MetadataReferenceCollection operator +(MetadataReferenceCollection left, MetadataReferenceCollection right)
        {
            return (left, right) switch
            {
                ({ Count: > 0 }, { Count: > 0 }) => [with(left), .. right],
                ({ Count: > 0 }, _) => left,
                (_, { Count: > 0 }) => right,
                (not null, _) => left,
                (_, not null) => right,
                _ => [],
            };
        }
    }

    public readonly record struct MetadataReferenceHost(byte[] Image, PortableExecutableReference Reference)
    {
        public MetadataReferenceHost(MemoryStream peStream, MetadataReferenceProperties properties = default, DocumentationProvider? documentation = null, string? filePath = null) :
            this(peStream.ToArray(), MetadataReference.CreateFromStream(peStream, properties, documentation, filePath))
        {
        }
    }
}