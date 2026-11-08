using System;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    /// <summary>
    /// The sun on a modded solar array, as vanilla's SolvePower computes it for a fixed panel (research/POWER_UX_RECON.md,
    /// "E. Sun factor", read from the machine code and not yet sampled in game): <c>s = SunDirectionAt(LunarTime)</c>,
    /// <c>h = dot(up, s)</c>, <c>d = smoothstep(clamp01((h + 0.0096) / 0.0192))</c>, <c>SunFactor = max(0, dot(s, n)) × d</c>,
    /// with the array's normal <c>n = up</c> (a flat panel; provisional until the sampling). The lunar clock is synced to
    /// every peer, so every peer gets the same factor. When the solar system cannot be read the factor falls back to the
    /// world-wide <c>MoonSolarSystem.Daylight</c>: 1 above 0.5, else 0 (logged once).
    /// </summary>
    internal static class Sun
    {
        internal static float Factor(MoonWorld world, Vector3 position, Vector3 up)
        {
            try
            {
                var solar = world != null ? world.solarSystem : null;
                if (solar == null) return Fallback("no MoonWorld.solarSystem");
                Vector3 s = MoonSolarSystem.SunDirectionAt(solar.LunarTime);
                if (up.sqrMagnitude < 1e-12f) up = position.normalized;
                else up = up.normalized;
                return Formula(s, up, up);
            }
            catch (Exception e) { return Fallback(e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The machine-code formula for a fixed panel with normal <paramref name="normal"/> at a site whose up is <paramref name="up"/>.</summary>
        internal static float Formula(Vector3 sun, Vector3 up, Vector3 normal)
        {
            float h = Vector3.Dot(up, sun);
            float t = Mathf.Clamp01((h + 0.0096f) / 0.0192f);
            float d = t * t * (3f - 2f * t);
            return Mathf.Max(0f, Vector3.Dot(sun, normal)) * d;
        }

        static float Fallback(string why)
        {
            // Daylight is a float (0..1); the fallback is day or night, as vanilla's own Daylight flag (d > 0.5).
            bool day;
            try { day = MoonSolarSystem.Daylight > 0.5f; }
            catch (Exception e)
            {
                CoreLog.Once("sun-daylight", $"Sun: reading MoonSolarSystem.Daylight threw ({e.Message}); solar arrays read no sun", 1);
                return 0f;
            }
            CoreLog.Once("sun-fallback", $"Sun: the sun direction could not be read ({why}); solar arrays use MoonSolarSystem.Daylight ({day}) instead", 1);
            return day ? 1f : 0f;
        }
    }
}
