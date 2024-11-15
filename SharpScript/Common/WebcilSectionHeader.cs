using System.Runtime.InteropServices;

namespace SharpScript.Common
{
    /// <summary>
    /// This is the Webcil analog of System.Reflection.PortableExecutable.SectionHeader, but with fewer fields
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct WebcilSectionHeader(int virtualSize, int virtualAddress, int sizeOfRawData, int pointerToRawData)
    {
        public readonly int VirtualSize = virtualSize;
        public readonly int VirtualAddress = virtualAddress;
        public readonly int SizeOfRawData = sizeOfRawData;
        public readonly int PointerToRawData = pointerToRawData;
    }
}