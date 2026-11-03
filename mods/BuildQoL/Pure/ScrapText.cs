using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BuildQoL.Pure
{
    /// <summary>The ScrapBeltContents prompt line and log text (BCL only).</summary>
    public static class ScrapText
    {
        /// <summary>A short label for an input-system key name: "LeftCtrl" -> "Ctrl", "RightShift" -> "Shift", "Q" -> "Q".</summary>
        public static string KeyLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            string rest = key.StartsWith("Left") ? key.Substring(4) : key.StartsWith("Right") ? key.Substring(5) : null;
            // Only the paired modifiers lose their side; "LeftArrow" stays "LeftArrow".
            return rest is "Ctrl" or "Shift" or "Alt" or "Meta" or "Command" or "Windows" or "Apple" ? rest : key;
        }

        /// <summary>"5.0 kg", one decimal, invariant culture.</summary>
        public static string Kg(double kg) => kg.ToString("0.0", CultureInfo.InvariantCulture) + " kg";

        /// <summary>"1 canister" / "5 canisters".</summary>
        public static string Canisters(int n) => n == 1 ? "1 canister" : n + " canisters";

        /// <summary>
        /// The prompt line for an aimed belt, or null when there is nothing to scrap.
        /// <paramref name="scrapCount"/>/<paramref name="scrapKg"/>: the canisters the scrap would destroy (with
        /// ScrapOthersCargo off, only the player's own). <paramref name="others"/>: other players' canisters on the belt,
        /// by display name (null or empty name = unknown player), in the order to show them.
        /// <paramref name="othersScrapped"/>: true when those are part of <paramref name="scrapCount"/>, false when they
        /// stay (ScrapOthersCargo off).
        /// </summary>
        public static string Prompt(bool held, string keyLabel, int scrapCount, double scrapKg, IList<KeyValuePair<string, int>> others, bool othersScrapped)
        {
            if (scrapCount <= 0) return null;
            var sb = new StringBuilder();
            if (held) sb.Append(keyLabel).Append(": scrap contents (").Append(Canisters(scrapCount)).Append(", ").Append(Kg(scrapKg));
            else sb.Append("Hold ").Append(keyLabel).Append(" to scrap contents (").Append(scrapCount);
            string o = Others(others, othersScrapped);
            if (o != null) sb.Append(othersScrapped ? ", " : "; ").Append(o);
            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>"2 are Andrew's, 1 is Bob's, 1 is another player's" (scrapped) or "Andrew's 2 stay" (kept).</summary>
        internal static string Others(IList<KeyValuePair<string, int>> others, bool scrapped)
        {
            if (others == null || others.Count == 0) return null;
            var named = new List<KeyValuePair<string, int>>();
            int unknown = 0;
            foreach (var kv in others)
            {
                if (kv.Value <= 0) continue;
                if (string.IsNullOrWhiteSpace(kv.Key)) unknown += kv.Value;
                else named.Add(kv);
            }
            if (named.Count == 0 && unknown == 0) return null;
            var parts = new List<string>();
            if (scrapped)
            {
                foreach (var kv in named) parts.Add($"{kv.Value} {(kv.Value == 1 ? "is" : "are")} {kv.Key}'s");
                if (unknown > 0) parts.Add(unknown == 1 ? "1 is another player's" : $"{unknown} are others'");
            }
            else
            {
                foreach (var kv in named) parts.Add($"{kv.Key}'s {kv.Value}");
                if (unknown > 0) parts.Add($"others' {unknown}");
                return string.Join(", ", parts) + " stay";
            }
            return string.Join(", ", parts);
        }

        /// <summary>The once-per-removal log line.</summary>
        public static string Log(int count, double kg, int belt, bool client) =>
            $"scrapped {Canisters(count)} ({Kg(kg)}) from belt {belt} as {(client ? "client" : "host")}";

        /// <summary>
        /// True when a canister owner is the local player: it equals either local id (the game has two,
        /// MoonWorld.LocalPlayer and MoonCrew.LocalPlayerId, shown equal only in solo). An empty owner counts as mine.
        /// </summary>
        public static bool IsMine(string owner, string localA, string localB) =>
            string.IsNullOrEmpty(owner)
            || (!string.IsNullOrEmpty(localA) && owner == localA)
            || (!string.IsNullOrEmpty(localB) && owner == localB);

        /// <summary>What a scrap of these riders would destroy, and other players' canisters by owner id in first-seen order.</summary>
        public sealed class Tally
        {
            public int Count;
            public double Kg;
            public readonly List<KeyValuePair<string, int>> Others = new List<KeyValuePair<string, int>>();
        }

        /// <summary>
        /// Counts riders (owner, kg): mine always count; others count too when <paramref name="othersScrapped"/>, and are
        /// listed by owner either way.
        /// </summary>
        public static Tally Count(IEnumerable<KeyValuePair<string, double>> riders, string localA, string localB, bool othersScrapped)
        {
            var t = new Tally();
            var index = new Dictionary<string, int>();
            foreach (var r in riders)
            {
                bool mine = IsMine(r.Key, localA, localB);
                if (!mine)
                {
                    if (index.TryGetValue(r.Key, out int i)) t.Others[i] = new KeyValuePair<string, int>(r.Key, t.Others[i].Value + 1);
                    else { index[r.Key] = t.Others.Count; t.Others.Add(new KeyValuePair<string, int>(r.Key, 1)); }
                }
                if (mine || othersScrapped) { t.Count++; t.Kg += r.Value; }
            }
            return t;
        }

        /// <summary>
        /// For a paired modifier ("LeftCtrl", "RightShift", "LeftAlt", ...) the name of the other side, so either key
        /// counts; null for any other key.
        /// </summary>
        public static string OtherSide(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string rest = key.StartsWith("Left") ? key.Substring(4) : key.StartsWith("Right") ? key.Substring(5) : null;
            if (rest is not ("Ctrl" or "Shift" or "Alt" or "Meta" or "Command" or "Windows" or "Apple")) return null;
            return (key.StartsWith("Left") ? "Right" : "Left") + rest;
        }
    }
}
