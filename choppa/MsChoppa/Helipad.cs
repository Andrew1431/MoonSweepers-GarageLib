using UnityEngine;

namespace MsChoppa
{
    // Finds a spot to put a choppa that a garage has just brought out: open ground near the garage with clear sky,
    // a flat-enough surface and room for the rotor. The moon is a sphere centred on the origin, so "up" is radial.
    internal static class Helipad
    {
        internal static bool Find(Vector3 from, Vector3 forward, Transform self, float rotorRadius, out Vector3 point, out Vector3 up, out string why)
        {
            point = from; up = from.normalized; why = "";
            int tried = 0, steep = 0, roofed = 0, crowded = 0, inHall = 0;
            Vector3 u0 = from.normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(forward, u0).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(Vector3.forward, u0).normalized;

            // Rings outward from the garage, starting straight ahead (vehicles face the door) and alternating sides.
            foreach (float r in new[] { 14f, 20f, 28f, 38f, 50f })
            {
                for (int k = 0; k < 18; k++)
                {
                    float angle = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 20f;
                    Vector3 dir = Quaternion.AngleAxis(angle, u0) * fwd;
                    Vector3 probe = from + dir * r;
                    Vector3 u = probe.normalized;
                    tried++;
                    if (!Ground(probe + u * 80f, -u, 200f, self, out var hit)) continue;
                    if (Vector3.Dot(hit.normal, u) < 0.93f) { steep++; continue; }
                    if (MoonCleaners.MoonGarage.InHall(hit.point)) { inHall++; continue; }
                    if (Ground(hit.point + u * 0.5f, u, 60f, self, out _)) { roofed++; continue; }
                    if (Crowded(hit.point, u, rotorRadius + 0.5f, self)) { crowded++; continue; }
                    point = hit.point; up = u;
                    why = $"ring {r} m, {angle:F0}° from ahead, after {tried} tries (steep {steep}, roofed {roofed}, crowded {crowded}, in hall {inHall})";
                    return true;
                }
            }
            why = $"no spot in {tried} tries (steep {steep}, roofed {roofed}, crowded {crowded}, in hall {inHall})";
            return false;
        }

        static bool Ground(Vector3 origin, Vector3 dir, float dist, Transform self, out RaycastHit best)
        {
            best = default;
            float bestD = float.PositiveInfinity;
            foreach (var h in Physics.RaycastAll(origin, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.collider.transform.IsChildOf(self)) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h; }
            }
            return bestD < float.PositiveInfinity;
        }

        // Anything solid (other than us) in a slab from 1.2 m above the ground to rotor height, rotor-disc wide?
        static bool Crowded(Vector3 ground, Vector3 up, float radius, Transform self)
        {
            var rot = Quaternion.FromToRotation(Vector3.up, up);
            foreach (var c in Physics.OverlapBox(ground + up * 2.6f, new Vector3(radius, 1.4f, radius), rot, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && !c.transform.IsChildOf(self)) return true;
            return false;
        }
    }
}
