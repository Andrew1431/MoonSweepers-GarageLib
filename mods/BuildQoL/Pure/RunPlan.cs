using System;
using System.Collections.Generic;
using System.Globalization;

namespace BuildQoL.Pure
{
    /// <summary>
    /// Game-independent rules of a foundation run (BCL only): run length from wheel ticks, run-axis choice with
    /// hysteresis, the step-distance guard, cell-id encoding, the HUD prompt and the result notice.
    /// </summary>
    public static class RunPlan
    {
        /// <summary>Hard bounds of the RunMax setting.</summary>
        public const int MaxCap = 32;

        /// <summary>A step between neighbouring cell centres must be in [StepMin, StepMax] m (cells are 8 m, measured 7.96 m).</summary>
        public const float StepMin = 7.5f, StepMax = 8.5f;

        /// <summary>Default hysteresis of the axis choice: a new axis must beat the current one by this many degrees.</summary>
        public const float AxisHysteresisDeg = 10f;

        /// <summary>RunMax as configured, clamped to 1..MaxCap.</summary>
        public static int ClampMax(int configured) => Clamp.Int(configured, 1, MaxCap);

        /// <summary>The run length after one wheel reading: +1 per positive tick, -1 per negative, kept in 1..max.</summary>
        public static int NextLength(int length, float wheel, int max)
        {
            int step = wheel > 0f ? 1 : wheel < 0f ? -1 : 0;
            return Clamp.Int(length + step, 1, ClampMax(max));
        }

        /// <summary>
        /// Index of the axis to run along, given the dot product of the facing with each candidate axis (unit vectors).
        /// The best candidate replaces <paramref name="previous"/> only when it is at least <paramref name="hysteresisDeg"/>
        /// closer to the facing; previous &lt; 0 means no previous choice.
        /// </summary>
        public static int PickAxis(IReadOnlyList<float> dots, int previous, float hysteresisDeg = AxisHysteresisDeg)
        {
            if (dots == null || dots.Count == 0) return -1;
            int best = 0;
            for (int k = 1; k < dots.Count; k++) if (dots[k] > dots[best]) best = k;
            if (previous < 0 || previous >= dots.Count || previous == best) return best;
            return AngleDeg(dots[previous]) - AngleDeg(dots[best]) >= hysteresisDeg ? best : previous;
        }

        /// <summary>Angle in degrees for a dot product of two unit vectors.</summary>
        public static float AngleDeg(float dot) => (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))) * 180.0 / Math.PI);

        /// <summary>True when the distance between two consecutive run cell centres is a plausible single-cell step.</summary>
        public static bool StepOk(float metres) => metres >= StepMin && metres <= StepMax;

        /// <summary>A plan's cell for a world-square piece is -(id + 1); returns the square id, or -1 when not on the square grid.</summary>
        public static int Decode(int planCell) => planCell < 0 ? -planCell - 1 : -1;

        /// <summary>The plan encoding of square id <paramref name="id"/>, as MoonWorld.GridCenter/GridFrame/GridCorners take it.</summary>
        public static int Encode(int id) => -(id + 1);

        /// <summary>True when the build must be refused at click time: a blocked cell, or not enough money for every piece.</summary>
        public static bool Refuse(int blocked, bool affordable) => blocked > 0 || !affordable;

        /// <summary>The HUD prompt for a run of <paramref name="length"/> pieces; <paramref name="ok"/> is false when the run is refused or partial.</summary>
        public static string Prompt(int length, int price, int blocked, int outOfReach, bool affordable, out bool ok)
        {
            ok = false;
            if (blocked > 0) return $"Run {length} · {blocked} blocked";
            string text = $"Run {length} · {Money((long)length * price)}";
            if (!affordable) return text + " · not enough money";
            if (outOfReach > 0) return text + $" · {outOfReach} out of reach";
            ok = true;
            return text;
        }

        public static string Money(long dollars) => "$" + dollars.ToString(CultureInfo.InvariantCulture);

        /// <summary>Reason categories for refused pieces, as the notice reports them.</summary>
        public const string Blocked = "blocked", OutOfReach = "out of reach", NoMoney = "not enough money", Refused = "refused";

        /// <summary>Maps a refusal reason from the game (TryBuildStructure / the site check) to a notice category.</summary>
        public static string Category(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return Refused;
            if (reason.IndexOf("closer", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("reach", StringComparison.OrdinalIgnoreCase) >= 0) return OutOfReach;
            if (reason.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("occupied", StringComparison.OrdinalIgnoreCase) >= 0) return Blocked;
            if (reason.IndexOf("money", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("afford", StringComparison.OrdinalIgnoreCase) >= 0) return NoMoney;
            return Refused;
        }

        /// <summary>Parses a wallet amount as the game prints it ("99980", "99,980.00"); false when it is not a number.</summary>
        public static bool TryParseMoney(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().TrimStart('$');
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
                || decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>
        /// The reach a piece at <paramref name="distance"/> m needs: the game refuses beyond reach + 2 m, so a raised reach
        /// is distance - 2 + 1 (1 m margin). Returns <paramref name="reach"/> unchanged when the piece is already in reach.
        /// </summary>
        public static float ReachFor(float reach, float distance) => distance > reach + 2f ? distance - 2f + 1f : reach;
    }

    /// <summary>Counts the pieces of one run by outcome and writes the result notice.</summary>
    public sealed class RunTally
    {
        readonly Dictionary<string, int> refused = new();
        readonly List<string> order = new();

        public int Total { get; }
        public int Built { get; private set; }
        /// <summary>Set when the run stopped early (the remaining pieces were not tried).</summary>
        public string Stopped { get; private set; }

        public RunTally(int total) { Total = total; }

        public void AddBuilt() => Built++;

        public void AddRefused(string category)
        {
            if (string.IsNullOrEmpty(category)) category = RunPlan.Refused;
            if (!refused.ContainsKey(category)) { refused[category] = 0; order.Add(category); }
            refused[category]++;
        }

        public void Stop(string category) { if (Stopped == null) Stopped = string.IsNullOrEmpty(category) ? RunPlan.Refused : category; }

        public int Count(string category) => refused.TryGetValue(category, out int n) ? n : 0;

        /// <summary>"Built 9 of 10 (1 blocked)", "Built 3 of 6 (stopped: out of reach)", "Built 6 of 6".</summary>
        public string Notice()
        {
            var parts = new List<string>();
            foreach (var c in order) parts.Add($"{refused[c]} {c}");
            if (Stopped != null) parts.Add("stopped: " + Stopped);
            string s = $"Built {Built} of {Total}";
            return parts.Count > 0 ? s + " (" + string.Join(", ", parts) + ")" : s;
        }
    }
}
