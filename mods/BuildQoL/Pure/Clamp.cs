namespace BuildQoL.Pure
{
    /// <summary>Game-independent helpers (BCL only; compiled into tests/BuildQoL.Tests as well as the mod).</summary>
    public static class Clamp
    {
        /// <summary>Clamps a value into [min, max]; max below min yields min.</summary>
        public static int Int(int value, int min, int max) => value < min ? min : value > max ? max < min ? min : max : value;
    }
}
