// Assets/Scripts/Audio/Runtime/Utils/StringHash.cs
// Stable 32-bit FNV-1a hash for deterministic results across platforms and editor sessions.

using System.Runtime.CompilerServices;

namespace DataOrientedAudio.Common
{
    /// <summary>Provides stable 32-bit FNV-1a hash for strings, ensuring deterministic results across platforms.</summary>
    public static class StringHash
    {
        private const uint FNV1A_32_PRIME = 0x01000193;
        private const uint FNV1A_32_OFFSET_BASIS = 0x811c9dc5;

        /// <summary>
        /// Compute stable 32-bit FNV-1a hash for a string. Returns 0 for null strings.
        /// This hash is deterministic across platforms and editor sessions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int StableHash32(string s)
        {
            if (s == null) return 0;

            uint hash = FNV1A_32_OFFSET_BASIS;

            unchecked
            {
                for (int i = 0; i < s.Length; i++)
                {
                    // Use UTF-16 char directly for stable cross-platform results
                    char c = s[i];

                    // Hash low byte
                    hash ^= (byte)(c & 0xFF);
                    hash *= FNV1A_32_PRIME;

                    // Hash high byte
                    hash ^= (byte)((c >> 8) & 0xFF);
                    hash *= FNV1A_32_PRIME;
                }
            }

            return (int)hash;
        }
    }
}
