using UnityEngine;

namespace Rotorcraft;

public struct PilotInput
{
    public float Collective;   // -1..1 (down/up keys)
    public float Pedal;        // -1..1 (left/right)
    public Vector2 Mouse;      // raw mouse delta this frame
    public bool FreeLook;      // mouse moves the camera instead of the cyclic
}

// Turns raw input into collective and commanded rates the way Big Choppa does (Arma-style: W/S collective that
// springs back to hover, pedals, mouse cyclic). Call once per rendered frame.
public sealed class RotorcraftPilot
{
    Vector2 smoothedCyclic;

    public void Update(RotorcraftFlight heli, PilotInput input, float dt)
    {
        var s = heli.Settings;
        dt = Mathf.Max(dt, 1e-4f);
        if (!s.CollectiveSpringBack)
            heli.Collective = Mathf.Clamp01(heli.Collective + input.Collective * s.CollectiveRate * dt);
        else if (input.Collective != 0f)
            heli.Collective = Mathf.MoveTowards(heli.Collective, input.Collective > 0f ? 1f : 0f, s.CollectiveRate * dt);
        else
        {
            // On the ground it spools to idle so it doesn't skate around; airborne it settles at hover.
            float rest = 0f;
            if (!heli.Grounded)
            {
                rest = heli.HoverCollective;
                if (s.HoverTiltCompensation) rest /= Mathf.Max(0.75f, TiltDot);
            }
            heli.Collective = Mathf.MoveTowards(heli.Collective, Mathf.Clamp01(rest), s.CollectiveReturnRate * dt);
        }

        Vector2 cyclicTarget = Vector2.zero;
        if (!input.FreeLook)
        {
            float k = s.MouseSensitivity / dt;
            float pitch = input.Mouse.y * k * (s.InvertPitch ? -1f : 1f);
            float roll = -input.Mouse.x * k * (s.InvertRoll ? -1f : 1f);
            float max = s.MaxPitchRollRate;
            cyclicTarget = new Vector2(Mathf.Clamp(pitch, -max, max), Mathf.Clamp(roll, -max, max));
        }
        float a = s.MouseSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-dt / s.MouseSmoothing);
        smoothedCyclic = Vector2.Lerp(smoothedCyclic, cyclicTarget, a);
        heli.CommandedRates = new Vector3(smoothedCyclic.x, input.Pedal * s.YawRate, smoothedCyclic.y);
    }

    // Dot of the airframe's up with the local up; set by the caller before Update for hover tilt compensation.
    public float TiltDot = 1f;

    public void Reset() => smoothedCyclic = Vector2.zero;
}
