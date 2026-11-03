using System.Text;

namespace StructureLib
{
    /// <summary>32-bit FNV-1a hash over the UTF-8 bytes of a string. Stable across runs, platforms and runtimes.</summary>
    public static class Fnv1a
    {
        private const uint OffsetBasis = 0x811C9DC5;
        private const uint Prime = 16777619;

        public static uint Hash32(string s)
        {
            uint hash = OffsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                hash ^= b;
                hash *= Prime;
            }
            return hash;
        }
    }
}
