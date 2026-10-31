using UnityEngine;

namespace Rotorcraft;

// Tuning for RotorcraftFlight / RotorcraftPilot. Defaults are Big Choppa's (Earth-like); games override per vehicle.
public sealed class FlightSettings
{
    public float MaxLiftG = 2.0f;           // lift at full collective as a multiple of local gravity
    public float SpinUpSeconds = 3f;
    public float ControlResponse = 6f;      // how fast the airframe reaches the commanded rates
    public float AutoLevel = 0f;            // self-levelling towards the local up; 0 = holds attitude
    public float Weathervane = 0.5f;        // tail swings the nose into the direction of travel
    public float LinearDrag = 0.15f;        // low-speed drag in all directions
    public float ForwardDrag = 0.003f, SideDrag = 0.02f, VerticalDrag = 0.04f; // quadratic, per local axis

    public float CollectiveRate = 0.45f;        // fraction of full range per second while held
    public float CollectiveReturnRate = 1.0f;   // spring back when released
    public bool CollectiveSpringBack = true;    // released: settle at hover (airborne) or idle (grounded)
    public bool HoverTiltCompensation = true;
    public float MouseSensitivity = 0.3f;       // deg of pitch/roll per mouse unit
    public float MaxPitchRollRate = 90f;        // deg/s
    public float YawRate = 65f;                 // deg/s at full pedal
    public float MouseSmoothing = 0.05f;        // seconds
    public bool InvertPitch, InvertRoll;
}

// The airframe model: a Rigidbody pushed by a deliberately simple model. Lift always points out of the rotor top, so
// tilting is how you move. Game-agnostic: the caller supplies local up and gravity (planets, moons, flat worlds).
public sealed class RotorcraftFlight
{
    public FlightSettings Settings;
    public bool EngineOn;
    public float Collective;            // 0..1
    public float RotorSpin;             // 0..1, lift scales with its square
    public Vector3 CommandedRates;      // deg/s: x = pitch (+ nose down), y = yaw (+ right), z = roll (+ left)
    public bool Grounded;

    public RotorcraftFlight(FlightSettings settings) { Settings = settings; }

    public float HoverCollective => 1f / Mathf.Max(0.01f, Settings.MaxLiftG);

    // worldUp: unit "up" at the vehicle. gravity: m/s² magnitude. applyGravity: true when the game's own gravity for
    // this body is switched off (e.g. its vehicle controller normally adds it).
    public void Step(Rigidbody body, float dt, Vector3 worldUp, float gravity, bool applyGravity)
    {
        var s = Settings;
        var t = body.transform;
        if (applyGravity) body.AddForce(-worldUp * gravity, ForceMode.Acceleration);

        RotorSpin = Mathf.MoveTowards(RotorSpin, EngineOn ? 1f : 0f, dt / Mathf.Max(0.1f, s.SpinUpSeconds));
        if (!EngineOn) Collective = Mathf.MoveTowards(Collective, 0f, dt * 0.5f);

        float lift = Collective * s.MaxLiftG * gravity * RotorSpin * RotorSpin;
        body.AddForce(t.up * lift, ForceMode.Acceleration);

        Vector3 lv = t.InverseTransformDirection(body.linearVelocity);
        var drag = new Vector3(
            -lv.x * (s.LinearDrag + s.SideDrag * Mathf.Abs(lv.x)),
            -lv.y * (s.LinearDrag + s.VerticalDrag * Mathf.Abs(lv.y)),
            -lv.z * (s.LinearDrag + s.ForwardDrag * Mathf.Abs(lv.z)));
        body.AddForce(t.TransformDirection(drag), ForceMode.Acceleration);

        if (RotorSpin > 0.05f)
        {
            Vector3 target = CommandedRates * Mathf.Deg2Rad;
            if (s.AutoLevel > 0f)
            {
                Vector3 tiltAxis = t.InverseTransformDirection(Vector3.Cross(t.up, worldUp));
                target.x += tiltAxis.x * s.AutoLevel * 3f;
                target.z += tiltAxis.z * s.AutoLevel * 3f;
            }
            // Tail fin catching sideways air: sliding right yaws the nose right.
            float speedFactor = Mathf.Clamp01(new Vector2(lv.x, lv.z).magnitude / 15f);
            target.y += Mathf.Clamp(lv.x * 0.1f, -1f, 1f) * s.Weathervane * speedFactor;

            Vector3 localAng = t.InverseTransformDirection(body.angularVelocity);
            Vector3 accel = (target - localAng) * s.ControlResponse * RotorSpin;
            body.AddTorque(t.TransformDirection(accel), ForceMode.Acceleration);
        }
    }
}
