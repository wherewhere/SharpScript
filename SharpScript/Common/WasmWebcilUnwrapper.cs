using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    internal sealed class WasmWebcilUnwrapper(Stream wasmStream) : IDisposable, IAsyncDisposable
    {
        private bool disposed;

        /// <summary>
        /// Everything from the above wat module before the data section
        /// extracted by wasm-reader -s wrapper.wasm
        /// </summary>
        private static readonly byte[] s_wasmWrapperPrefix = [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00, 0x01, 0x0a, 0x02, 0x60, 0x01, 0x7f, 0x00, 0x60, 0x02, 0x7f, 0x7f, 0x00, 0x02, 0x12, 0x01, 0x06, 0x77, 0x65, 0x62, 0x63, 0x69, 0x6c, 0x06, 0x6d,
            0x65, 0x6d, 0x6f, 0x72, 0x79, 0x02, 0x00, 0x01, 0x03, 0x03, 0x02, 0x00, 0x01, 0x06, 0x0b, 0x02, 0x7f, 0x00, 0x41, 0x00, 0x0b, 0x7f, 0x00, 0x41, 0x00, 0x0b, 0x07, 0x41, 0x04, 0x0d, 0x77, 0x65,
            0x62, 0x63, 0x69, 0x6c, 0x56, 0x65, 0x72, 0x73, 0x69, 0x6f, 0x6e, 0x03, 0x00, 0x0a, 0x77, 0x65, 0x62, 0x63, 0x69, 0x6c, 0x53, 0x69, 0x7a, 0x65, 0x03, 0x01, 0x0d, 0x67, 0x65, 0x74, 0x57, 0x65,
            0x62, 0x63, 0x69, 0x6c, 0x53, 0x69, 0x7a, 0x65, 0x00, 0x00, 0x10, 0x67, 0x65, 0x74, 0x57, 0x65, 0x62, 0x63, 0x69, 0x6c, 0x50, 0x61, 0x79, 0x6c, 0x6f, 0x61, 0x64, 0x00, 0x01, 0x0c, 0x01, 0x02,
            0x0a, 0x1b, 0x02, 0x0c, 0x00, 0x20, 0x00, 0x41, 0x00, 0x41, 0x04, 0xfc, 0x08, 0x00, 0x00, 0x0b, 0x0c, 0x00, 0x20, 0x00, 0x41, 0x00, 0x20, 0x01, 0xfc, 0x08, 0x01, 0x00, 0x0b,
        ];

        public async ValueTask WriteUnwrappedAsync(Stream outputStream, CancellationToken cancellationToken = default)
        {
            await ValidateWasmPrefixAsync(cancellationToken).ConfigureAwait(false);

            using BinaryReader reader = new(wasmStream, System.Text.Encoding.UTF8, leaveOpen: true);
            byte[] bytes = ReadDataSection(reader);
            await outputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ValidateWasmPrefixAsync(CancellationToken cancellationToken = default)
        {
            // Create a byte array matching the length of the prefix.
            byte[] prefix = s_wasmWrapperPrefix;
            byte[] buffer = new byte[prefix.Length];
            int bytesRead = await wasmStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead < buffer.Length)
            {
                throw new InvalidOperationException("Unable to read Wasm prefix.");
            }

            // Compare the read prefix with the expected one.
            if (!buffer.SequenceEqual(prefix))
            {
                throw new InvalidOperationException("Invalid Wasm prefix.");
            }
        }

        private static void SkipSection(BinaryReader reader)
        {
            uint size = ULEB128Decode(reader);
            _ = reader.BaseStream.Seek(size, SeekOrigin.Current);
        }

        private static byte[] ReadDataSection(BinaryReader reader)
        {
            // Skip until we find the data section, which contains the Webcil payload.
            Span<byte> buffer = stackalloc byte[1];
            while (true)
            {
                // Read the Data section
                int dataRead = reader.Read(buffer);
                if (dataRead == 0)
                {
                    throw new InvalidOperationException("Unable to read Data Section.");
                }

                // Check the Data section (ID = 11)
                if (buffer[0] == 11)
                {
                    break;
                }

                // Skip other sections by reading and ignoring their content.
                SkipSection(reader);
            }

            // Read and ignore the size of the data section.
            _ = ULEB128Decode(reader);

            // Read the number of segments.
            int segmentsCount = (int)ULEB128Decode(reader);
            int lastSegment = segmentsCount - 1;
            for (int segmentIndex = 0; segmentIndex < segmentsCount; segmentIndex++)
            {
                // Ignore segmentType (1 = passive segment)
                int segmentType = reader.Read(buffer);
                if (segmentType != 1)
                {
                    throw new InvalidOperationException($"Unexpected segment code for segment {segmentIndex}.");
                }

                // Read the segment size.
                uint segmentSize = ULEB128Decode(reader);

                // The actual Webcil payload is expected to be in the last segment.
                if (segmentIndex == lastSegment)
                {
                    return reader.ReadBytes((int)segmentSize);
                }

                // Skip other segments.
                _ = reader.BaseStream.Seek(segmentSize, SeekOrigin.Current);
            }

            throw new Exception("Unable to read DataSection.");
        }

        /// <summary>
        /// Decodes a variable-length quantity (VLQ) encoded as unsigned LEB128.
        /// LEB128 (Little Endian Base 128) is used to encode integers in a variable number of bytes.
        /// The method reads bytes from the provided binary reader and decodes them into an unsigned integer.
        /// </summary>
        /// <param name="reader">The binary reader from which to read the ULEB128 encoded data.</param>
        /// <returns>The decoded unsigned integer from the ULEB128 encoded data.</returns>
        private static uint ULEB128Decode(BinaryReader reader)
        {
            uint result = 0;
            int shift = 0;
            byte byteValue;

            do
            {
                byteValue = reader.ReadByte();
                uint byteAsUInt = byteValue & 0x7Fu;
                result |= byteAsUInt << shift;
                shift += 7;
            } while ((byteValue & 0x80) != 0);

            return result;
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    wasmStream.Dispose();
                }
                disposed = true;
            }
        }

        private async ValueTask DisposeAsyncCore(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    await wasmStream.DisposeAsync().ConfigureAwait(false);
                }
                Dispose(disposing: false);
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore(disposing: true).ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}