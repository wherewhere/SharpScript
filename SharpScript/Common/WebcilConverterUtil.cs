using SharpScript.Common.NT_Structs;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    internal static class WebcilConverterUtil
    {
        private static readonly Object8<byte> SectionHeaderText = [0x2E, 0x74, 0x65, 0x78, 0x74, 0x00, 0x00, 0x00];     // .text
        private static readonly Object8<byte> SectionHeaderRsRc = [0x2E, 0x72, 0x73, 0x72, 0x63, 0x00, 0x00, 0x00];     // .rsrc
        private static readonly Object8<byte> SectionHeaderReloc = [0x2E, 0x72, 0x65, 0x6C, 0x6F, 0x63, 0x00, 0x00];    // .reloc
        private static readonly byte[] MSDOS =
        [
            0x0E, 0x1F, 0xBA, 0x0E, 0x00, 0xB4, 0x09, 0xCD, 0x21, 0xB8, 0x01, 0x4C, 0xCD, 0x21, 0x54, 0x68,
            0x69, 0x73, 0x20, 0x70, 0x72, 0x6F, 0x67, 0x72, 0x61, 0x6D, 0x20, 0x63, 0x61, 0x6E, 0x6E, 0x6F,
            0x74, 0x20, 0x62, 0x65, 0x20, 0x72, 0x75, 0x6E, 0x20, 0x69, 0x6E, 0x20, 0x44, 0x4F, 0x53, 0x20,
            0x6D, 0x6F, 0x64, 0x65, 0x2E, 0x0D, 0x0D, 0x0A, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];
        private static readonly Object4<ushort> DOSReservedWords1 = new();  // [0, 0, 0, 0]
        private static readonly Object10<ushort> DOSReservedWords2 = new(); // [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        private static readonly DateTime Epoch = new(1970, 1, 1);
        private static readonly unsafe int SizeofDOSHeader = sizeof(IMAGE_DOS_HEADER);          // 64
        private static readonly unsafe int SizeofFileHeader = sizeof(IMAGE_FILE_HEADER);
        private static readonly unsafe int SizeofMSDOS = MSDOS.Length;                          // 64
        private static readonly unsafe int SizeofNTHeaders = sizeof(IMAGE_NT_HEADERS32);        // 248
        private static readonly unsafe int SizeofOptionalHeader = sizeof(IMAGE_OPTIONAL_HEADER32);
        private static readonly unsafe int SizeofSectionHeader = sizeof(IMAGE_SECTION_HEADER);  // 40

        private const uint FileAlignment = 0x0200;
        private const uint SectionAlignment = 0x2000;

        /// <summary>
        /// Convert a Webcil stream into a Portable Executable which can be used to create a valid <see cref="MetadataReference"/>.
        /// </summary>
        /// <param name="inputStream">The input sStream.</param>
        /// <param name="wrappedInWebAssembly">The Webcil is wrapped in Wasm [default value is <c>true</c>].</param>
        /// <returns>A byte[] Portable Executable</returns>
        public static async ValueTask<byte[]> ConvertFromWebcilAsync(Stream inputStream, bool wrappedInWebAssembly = true, CancellationToken cancellationToken = default)
        {
            Stream webcilStream;
            if (wrappedInWebAssembly)
            {
                await using WasmWebcilUnwrapper unwrapper = new(inputStream);
                webcilStream = new MemoryStream();
                await unwrapper.WriteUnwrappedAsync(webcilStream, cancellationToken).ConfigureAwait(false);

                await webcilStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                _ = webcilStream.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                webcilStream = inputStream;
            }

            // These are Webcil variables
            WebcilHeader webcilHeader = await ReadHeaderAsync(webcilStream, cancellationToken).ConfigureAwait(false);
            List<WebcilSectionHeader> webcilSectionHeaders = await ReadSectionHeadersAsync(webcilStream, webcilHeader.coff_sections, cancellationToken).ConfigureAwait(false);
            int webcilSectionHeadersCount = webcilSectionHeaders.Count;
            uint webcilSectionHeadersSizeOfRawData = (uint)webcilSectionHeaders.Sum(x => x.SizeOfRawData);

            // These are PE (Portable Executable) variables
            int sectionStart = SizeofDOSHeader + SizeofMSDOS + SizeofNTHeaders + (webcilSectionHeadersCount * SizeofSectionHeader); // 496
            int sectionStartRounded = sectionStart.RoundToNearest();
            byte[] extraBytesAfterSections = new byte[sectionStartRounded - sectionStart];
            int pointerToRawDataFirstSectionHeader = webcilSectionHeaders[0].PointerToRawData;
            int pointerToRawDataOffsetBetweenWebcilAndPE = sectionStartRounded - pointerToRawDataFirstSectionHeader;

            await using MemoryStream peStream = new();

            IMAGE_DOS_HEADER DOSHeader = new()
            {
                MagicNumber = 0x5A4D,
                BytesOnLastPageOfFile = 0x90,
                PagesInFile = 3,
                Relocations = 0,
                SizeOfHeaderInParagraphs = 4,
                MinimumExtraParagraphs = 0,
                MaximumExtraParagraphs = 0xFFFF,
                InitialSS = 0,
                InitialSP = 0xB8,
                Checksum = 0,
                InitialIP = 0,
                InitialCS = 0,
                AddressOfRelocationTable = 0x40,
                OverlayNumber = 0,
                ReservedWords1 = DOSReservedWords1,
                OEMIdentifier = 0,
                OEMInformation = 0,
                ReservedWords2 = DOSReservedWords2,
                FileAddressOfNewExeHeader = 0x80
            };
            await peStream.WriteStructAsync(DOSHeader, cancellationToken).ConfigureAwait(false);

            await peStream.WriteAsync(MSDOS, cancellationToken).ConfigureAwait(false);

            IMAGE_NT_HEADERS32 IMAGE_NT_HEADERS32 = new()
            {
                Signature = 0x4550, // 'PE'
                FileHeader = new IMAGE_FILE_HEADER
                {
                    Machine = Constants.IMAGE_FILE_MACHINE_I386,
                    NumberOfSections = 3,
                    TimeDateStamp = GetImageTimestamp(),
                    PointerToSymbolTable = 0,
                    NumberOfSymbols = 0,
                    SizeOfOptionalHeader = 0x00E0,
                    Characteristics = 0x0022
                },
                OptionalHeader = new IMAGE_OPTIONAL_HEADER32
                {
                    Magic = 0x010B,             // Signature/Magic - Represents PE32 for 32-bit (0x10b) and PE32+ for 64-bit (0x20B) 
                    MajorLinkerVersion = 0x30,
                    MinorLinkerVersion = 0,
                    SizeOfCode = (uint)webcilSectionHeaders[0].SizeOfRawData,
                    SizeOfInitializedData = (uint)(webcilSectionHeaders[1].SizeOfRawData + webcilSectionHeaders[2].SizeOfRawData),
                    SizeOfUninitializedData = 0,
                    AddressOfEntryPoint = 0,    // This can be set to 0
                    BaseOfCode = 0x2000,
                    BaseOfData = 0xA000,
                    ImageBase = 0x400000,       // The default value for applications is 0x00400000
                    SectionAlignment = SectionAlignment,
                    FileAlignment = FileAlignment,
                    MajorOperatingSystemVersion = 4,
                    MinorOperatingSystemVersion = 0,
                    MajorImageVersion = 0,
                    MinorImageVersion = 0,
                    MajorSubsystemVersion = 4,
                    MinorSubsystemVersion = 0,
                    Win32VersionValue = 0,
                    SizeOfImage = webcilSectionHeadersSizeOfRawData.RoundToNearest(SectionAlignment),
                    SizeOfHeaders = GetSizeOfHeaders(DOSHeader, webcilSectionHeadersCount),
                    CheckSum = 0,
                    Subsystem = 3,              // IMAGE_SUBSYSTEM_WINDOWS_CUI
                    DllCharacteristics = 0x8560,
                    SizeOfStackReserve = 0x100000,
                    SizeOfStackCommit = 0x1000,
                    SizeOfHeapReserve = 0x100000,
                    SizeOfHeapCommit = 0x1000,
                    LoaderFlags = 0,
                    NumberOfRvaAndSizes = 0x10,
                    DataDirectory =
                    [
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_EXPORT
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_IMPORT (can be 0)
                        new() { Size = (uint)webcilSectionHeaders[1].VirtualSize, VirtualAddress = (uint)webcilSectionHeaders[1].VirtualAddress },  // IMAGE_DIRECTORY_ENTRY_RESOURCE
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_EXCEPTION
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_SECURITY
                        new() { Size = (uint)webcilSectionHeaders[2].VirtualSize, VirtualAddress = (uint)webcilSectionHeaders[2].VirtualAddress },  // IMAGE_DIRECTORY_ENTRY_BASERELOC
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_DEBUG (can be 0)
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_ARCHITECTURE
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_GLOBALPTR
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_TLS
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_LOAD_CONFIG
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT
                        new() { Size = 0x0008, VirtualAddress = (uint)webcilSectionHeaders[0].VirtualAddress },     // IMAGE_DIRECTORY_ENTRY_IAT
                        new() { Size = 0x0000, VirtualAddress = 0x0000 },   // IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT
                        new() { Size = 0x0048, VirtualAddress = (uint)webcilSectionHeaders[0].VirtualAddress + 8 }, // TODO ??? IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR
                        new() { Size = 0x0000, VirtualAddress = 0x0000 }    // ?
                    ]
                }
            };
            await peStream.WriteStructAsync(IMAGE_NT_HEADERS32, cancellationToken).ConfigureAwait(false);

            IMAGE_SECTION_HEADER textSectionHeader = new()
            {
                Name = SectionHeaderText,
                Misc = new IMAGE_SECTION_HEADER.UnionType { VirtualSize = (uint)webcilSectionHeaders[0].VirtualSize },
                VirtualAddress = (uint)webcilSectionHeaders[0].VirtualAddress,
                SizeOfRawData = (uint)webcilSectionHeaders[0].SizeOfRawData,
                PointerToRawData = webcilSectionHeaders[0].GetCorrectedPointerToRawData(pointerToRawDataOffsetBetweenWebcilAndPE),
                Characteristics = 0x60000020
            };
            await peStream.WriteStructAsync(textSectionHeader, cancellationToken).ConfigureAwait(false);

            IMAGE_SECTION_HEADER rsrcSectionHeader = new()
            {
                Name = SectionHeaderRsRc,
                Misc = new IMAGE_SECTION_HEADER.UnionType { VirtualSize = (uint)webcilSectionHeaders[1].VirtualSize },
                VirtualAddress = (uint)webcilSectionHeaders[1].VirtualAddress,
                SizeOfRawData = (uint)webcilSectionHeaders[1].SizeOfRawData,
                PointerToRawData = webcilSectionHeaders[1].GetCorrectedPointerToRawData(pointerToRawDataOffsetBetweenWebcilAndPE),
                Characteristics = 0x40000040
            };
            await peStream.WriteStructAsync(rsrcSectionHeader, cancellationToken).ConfigureAwait(false);

            IMAGE_SECTION_HEADER relocSectionHeader = new()
            {
                Name = SectionHeaderReloc,
                Misc = new IMAGE_SECTION_HEADER.UnionType { VirtualSize = (uint)webcilSectionHeaders[2].VirtualSize },
                VirtualAddress = (uint)webcilSectionHeaders[2].VirtualAddress,
                SizeOfRawData = (uint)webcilSectionHeaders[2].SizeOfRawData,
                PointerToRawData = webcilSectionHeaders[2].GetCorrectedPointerToRawData(pointerToRawDataOffsetBetweenWebcilAndPE),
                Characteristics = 0x42000040
            };
            await peStream.WriteStructAsync(relocSectionHeader, cancellationToken).ConfigureAwait(false);

            if (extraBytesAfterSections.Length > 0)
            {
                peStream.Write(extraBytesAfterSections);
            }

            // Just copy all data
            foreach (WebcilSectionHeader webcilSectionHeader in webcilSectionHeaders)
            {
                byte[] buffer = new byte[webcilSectionHeader.SizeOfRawData];
                _ = webcilStream.Seek(webcilSectionHeader.PointerToRawData, SeekOrigin.Begin);
                webcilStream.ReadExactly(buffer);

                peStream.Write(buffer, 0, buffer.Length);
            }

            peStream.Flush();
            _ = peStream.Seek(0, SeekOrigin.Begin);

            return peStream.ToArray();
        }

        private static async ValueTask<WebcilHeader> ReadHeaderAsync(Stream webcilStream, CancellationToken cancellationToken = default)
        {
            WebcilHeader webcilHeader = await ReadStructureAsync<WebcilHeader>(webcilStream, cancellationToken).ConfigureAwait(false);

            if (!BitConverter.IsLittleEndian)
            {
                webcilHeader.version_major = BinaryPrimitives.ReverseEndianness(webcilHeader.version_major);
                webcilHeader.version_minor = BinaryPrimitives.ReverseEndianness(webcilHeader.version_minor);
                webcilHeader.coff_sections = BinaryPrimitives.ReverseEndianness(webcilHeader.coff_sections);
                webcilHeader.pe_cli_header_rva = BinaryPrimitives.ReverseEndianness(webcilHeader.pe_cli_header_rva);
                webcilHeader.pe_cli_header_size = BinaryPrimitives.ReverseEndianness(webcilHeader.pe_cli_header_size);
                webcilHeader.pe_debug_rva = BinaryPrimitives.ReverseEndianness(webcilHeader.pe_debug_rva);
                webcilHeader.pe_debug_size = BinaryPrimitives.ReverseEndianness(webcilHeader.pe_debug_size);
            }

            return webcilHeader;
        }

        private static async ValueTask<List<WebcilSectionHeader>> ReadSectionHeadersAsync(Stream webcilStream, int sectionsHeaders, CancellationToken cancellationToken = default)
        {
            List<WebcilSectionHeader> result = new(sectionsHeaders);
            for (int i = 0; i < sectionsHeaders; i++)
            {
                result.Add(await ReadSectionHeaderAsync(webcilStream, cancellationToken).ConfigureAwait(false));
            }
            return result;
        }

        private static async ValueTask<WebcilSectionHeader> ReadSectionHeaderAsync(Stream webcilStream, CancellationToken cancellationToken = default)
        {
            WebcilSectionHeader sectionHeader = await ReadStructureAsync<WebcilSectionHeader>(webcilStream, cancellationToken).ConfigureAwait(false);

            if (!BitConverter.IsLittleEndian)
            {
                sectionHeader = new WebcilSectionHeader
                (
                    virtualSize: BinaryPrimitives.ReverseEndianness(sectionHeader.VirtualSize),
                    virtualAddress: BinaryPrimitives.ReverseEndianness(sectionHeader.VirtualAddress),
                    sizeOfRawData: BinaryPrimitives.ReverseEndianness(sectionHeader.SizeOfRawData),
                    pointerToRawData: BinaryPrimitives.ReverseEndianness(sectionHeader.PointerToRawData)
                );
            }

            return sectionHeader;
        }

        private static uint GetSizeOfHeaders(IMAGE_DOS_HEADER IMAGE_DOS_HEADER, int numSectionHeaders)
        {
            int soh = IMAGE_DOS_HEADER.FileAddressOfNewExeHeader + // e_lfanew member of IMAGE_DOS_HEADER
                      sizeof(uint) + // 4 byte signature
                      SizeofFileHeader +
                      SizeofOptionalHeader + // size of optional header
                      (numSectionHeaders * SizeofSectionHeader); // size of all section headers
            return (uint)soh.RoundToNearest();
        }

        /// <summary>
        /// The low 32 bits of the time stamp of the image.
        /// This represents the date and time the image was created by the linker.
        /// The value is represented in the number of seconds elapsed since midnight (00:00:00), January 1, 1970, Universal Coordinated Time, according to the system clock.
        /// </summary>
        private static uint GetImageTimestamp()
        {
            // Calculate the total seconds since Unix epoch
            long totalSeconds = (DateTime.UtcNow - Epoch).Ticks / TimeSpan.TicksPerSecond;
            // Convert to uint (low 32 bits)
            return (uint)totalSeconds;
        }

        private static async ValueTask<T> ReadStructureAsync<T>(Stream s, CancellationToken cancellationToken = default) where T : unmanaged
        {
            int size;
            unsafe
            {
                size = sizeof(T);
            }
            byte[] buffer = new byte[size];
            int read = await s.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read != size)
            {
                throw new InvalidOperationException("Couldn't read the full structure from the stream.");
            }
            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    return *(T*)ptr;
                }
            }
        }

        internal static int RoundToNearest(this int number, int nearest = 512)
        {
            int remainder = number % nearest;
            int halfNearest = nearest / 2;
            return remainder >= halfNearest ? number + nearest - remainder : number - remainder;
        }

        internal static uint RoundToNearest(this uint number, uint nearest = 512)
        {
            uint remainder = number % nearest;
            uint halfNearest = nearest / 2;
            return remainder >= halfNearest ? number + nearest - remainder : number - remainder;
        }

        internal static async ValueTask WriteStructAsync<T>(this Stream stream, T structData, CancellationToken cancellationToken = default) where T : unmanaged
        {
            byte[] bytes = StructToBytes(structData);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        private static unsafe byte[] StructToBytes<T>(T structData) where T : unmanaged
        {
            T* ptr = &structData;
            return new ReadOnlySpan<byte>(ptr, sizeof(T)).ToArray();
        }

        internal static uint GetCorrectedPointerToRawData(this WebcilSectionHeader webcilSectionHeader, int offset)
        {
            return (uint)(webcilSectionHeader.PointerToRawData + offset);
        }
    }
}
