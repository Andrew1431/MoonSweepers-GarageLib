using System;
using System.Collections.Generic;

namespace StructureLib
{
    /// <summary>Finds which game members the library relies on are gone (a game update renamed them).</summary>
    public static class MemberCheck
    {
        /// <summary>
        /// Returns the entries of <paramref name="needed"/> (each <c>"Namespace.Type:Member"</c>) for which
        /// <paramref name="hasMember"/>(type, member) is false, in input order. A malformed entry counts as missing.
        /// </summary>
        public static IReadOnlyList<string> Missing(IEnumerable<string> needed, Func<string, string, bool> hasMember)
        {
            var missing = new List<string>();
            foreach (var entry in needed)
            {
                int colon = entry?.IndexOf(':') ?? -1;
                if (colon <= 0 || colon == entry.Length - 1) { missing.Add(entry); continue; }
                if (!hasMember(entry.Substring(0, colon), entry.Substring(colon + 1))) missing.Add(entry);
            }
            return missing;
        }
    }
}
