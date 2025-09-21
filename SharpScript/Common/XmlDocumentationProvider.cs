using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace SharpScript.Common
{
    public class ContentBasedXmlDocumentationProvider : IDeserializationCallback, IDocumentationProvider
    {
        #region Cache

        private sealed class XmlDocumentationCache
        {
            private readonly KeyValuePair<string, string>[] entries;
            private int pos;

            public XmlDocumentationCache(int size = 50)
            {
                if (size <= 0)
                { throw new ArgumentOutOfRangeException(nameof(size), size, "Value must be positive"); }
                entries = new KeyValuePair<string, string>[size];
            }

            internal bool TryGet(string key, out string value)
            {
                foreach (KeyValuePair<string, string> pair in entries)
                {
                    if (pair.Key == key)
                    {
                        value = pair.Value;
                        return true;
                    }
                }
                value = null;
                return false;
            }

            internal void Add(string key, string value)
            {
                entries[pos++] = new KeyValuePair<string, string>(key, value);
                if (pos == entries.Length)
                { pos = 0; }
            }
        }

        #endregion

        [Serializable]
        private readonly struct IndexEntry : IComparable<IndexEntry>
        {
            /// <summary>
            /// Hash code of the documentation tag
            /// </summary>
            internal readonly int HashCode;

            /// <summary>
            /// Position in the .xml file where the documentation starts
            /// </summary>
            internal readonly int PositionInFile;

            internal IndexEntry(int hashCode, int positionInFile)
            {
                HashCode = hashCode;
                PositionInFile = positionInFile;
            }

            public int CompareTo(IndexEntry other) => HashCode.CompareTo(other.HashCode);
        }

        [NonSerialized]
        private XmlDocumentationCache cache = new();

        private readonly byte[] file;
        private readonly Encoding encoding;
        private volatile IndexEntry[] index; // SORTED array of index entries

        #region Constructor / Redirection support

        /// <summary>
        /// Creates a new <see cref="ContentBasedXmlDocumentationProvider"/>.
        /// </summary>
        /// <param name="xmlDocCommentBytes">Name of the .xml file.</param>
        /// <exception cref="IOException">Error reading from XML file (or from redirected file)</exception>
        /// <exception cref="XmlException">Invalid XML file</exception>
        public ContentBasedXmlDocumentationProvider(byte[] xmlDocCommentBytes)
        {
            ArgumentNullException.ThrowIfNull(xmlDocCommentBytes);

            using MemoryStream fs = new(xmlDocCommentBytes);
            using XmlTextReader xmlReader = new(fs) { XmlResolver = null }; // no DTD resolving
            xmlReader.MoveToContent();
            if (string.IsNullOrEmpty(xmlReader.GetAttribute("redirect")))
            {
                file = xmlDocCommentBytes;
                encoding = xmlReader.Encoding;
                ReadXmlDoc(xmlReader);
            }
            else
            {
                throw new XmlException($"XmlDoc is redirecting to {xmlReader.GetAttribute("redirect")}, but that file was not found.");
            }
        }

        #endregion

        #region Load / Create Index

        private void ReadXmlDoc(XmlTextReader reader)
        {
            //lastWriteDate = File.GetLastWriteTimeUtc(fileName);
            // Open up a second file stream for the line<->position mapping
            using MemoryStream fs = new(file);
            LinePositionMapper linePosMapper = new(fs, encoding);
            List<IndexEntry> indexList = [];
            while (reader.Read())
            {
                if (reader.IsStartElement())
                {
                    switch (reader.LocalName)
                    {
                        case "members":
                            ReadMembersSection(reader, linePosMapper, indexList);
                            break;
                    }
                }
            }
            indexList.Sort();
            index = [.. indexList]; // volatile write
        }

        private sealed class LinePositionMapper(MemoryStream fs, Encoding encoding)
        {
            private readonly Decoder decoder = encoding.GetDecoder();
            private int currentLine = 1;
            private char prevChar;

            // buffers for use with Decoder:
            private readonly byte[] input = new byte[1];
            private readonly char[] output = new char[2];

            public int GetPositionForLine(int line)
            {
                Debug.Assert(line >= currentLine);
                while (line > currentLine)
                {
                    int b = fs.ReadByte();
                    if (b < 0)
                    { throw new EndOfStreamException(); }
                    input[0] = (byte)b;
                    decoder.Convert(input, 0, 1, output, 0, output.Length, false, out int bytesUsed, out int charsUsed, out _);
                    Debug.Assert(bytesUsed == 1);
                    if (charsUsed == 1)
                    {
                        if ((prevChar != '\r' && output[0] == '\n') || output[0] == '\r')
                            currentLine++;
                        prevChar = output[0];
                    }
                }
                return checked((int)fs.Position);
            }
        }

        private static void ReadMembersSection(XmlTextReader reader, LinePositionMapper linePosMapper, List<IndexEntry> indexList)
        {
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.EndElement:
                        if (reader.LocalName == "members")
                        {
                            return;
                        }
                        break;
                    case XmlNodeType.Element:
                        if (reader.LocalName == "member")
                        {
                            int pos = linePosMapper.GetPositionForLine(reader.LineNumber) + Math.Max(reader.LinePosition - 2, 0);
                            string memberAttr = reader.GetAttribute("name");
                            if (memberAttr != null)
                            { indexList.Add(new IndexEntry(GetHashCode(memberAttr), pos)); }
                            reader.Skip();
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Hash algorithm used for the index.
        /// This is a custom implementation so that old index files work correctly
        /// even when the .NET string.GetHashCode implementation changes
        /// (e.g. due to .NET 4.5 hash randomization)
        /// </summary>
        private static int GetHashCode(string key)
        {
            unchecked
            {
                int h = 0;
                foreach (char c in key)
                {
                    h = (h << 5) - h + c;
                }
                return h;
            }
        }

        #endregion

        #region GetDocumentation

        /// <summary>
        /// Get the documentation for the specified member.
        /// </summary>
        public string GetDocumentation(IEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return GetDocumentation(entity.GetIdString());
        }

        /// <summary>
        /// Get the documentation for the member with the specified documentation key.
        /// </summary>
        private string GetDocumentation(string key)
        {
            ArgumentNullException.ThrowIfNull(key);

            int hashcode = GetHashCode(key);
            IndexEntry[] index = this.index; // read volatile field
                                             // index is sorted, so we can use binary search
            int m = Array.BinarySearch(index, new IndexEntry(hashcode, 0));
            if (m < 0)
            { return null; }
            // correct hash code found.
            // possibly there are multiple items with the same hash, so go to the first.
            while (--m >= 0 && index[m].HashCode == hashcode)
            { }
            // m is now 1 before the first item with the correct hash

            XmlDocumentationCache cache = this.cache;
            lock (cache)
            {
                if (!cache.TryGet(key, out string val))
                {
                    try
                    {
                        // go through all items that have the correct hash
                        while (++m < index.Length && index[m].HashCode == hashcode)
                        {
                            val = LoadDocumentation(key, index[m].PositionInFile);
                            if (val != null)
                                break;
                        }
                        // cache the result (even if it is null)
                        cache.Add(key, val);
                    }
                    catch (IOException)
                    {
                        // may happen if the documentation file was deleted/is inaccessible/changed (EndOfStreamException)
                        return null;
                    }
                    catch (XmlException)
                    {
                        // may happen if the documentation file was changed so that the file position no longer starts on a valid XML element
                        return null;
                    }
                }
                return val;
            }
        }

        #endregion

        #region Load / Read XML

        private string LoadDocumentation(string key, int positionInFile)
        {
            using MemoryStream fs = new(file);
            fs.Position = positionInFile;
            XmlParserContext context = new(null, null, null, XmlSpace.None) { Encoding = encoding };
            using XmlTextReader r = new(fs, XmlNodeType.Element, context);
            r.XmlResolver = null; // no DTD resolving
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    string memberAttr = r.GetAttribute("name");
                    return memberAttr == key ? r.ReadInnerXml() : null;
                }
            }
            return null;
        }

        #endregion

        public virtual void OnDeserialization(object sender) => cache = new XmlDocumentationCache();

        /// <summary>
        /// Creates an <see cref="IDocumentationProvider"/> from bytes representing XML documentation data.
        /// </summary>
        /// <param name="xmlDocCommentBytes">The XML document bytes.</param>
        /// <returns>An <see cref="IDocumentationProvider"/>.</returns>
        public static ContentBasedXmlDocumentationProvider CreateFromBytes(byte[] xmlDocCommentBytes) => xmlDocCommentBytes?.Length > 0 ? new(xmlDocCommentBytes) : null;
    }
}
