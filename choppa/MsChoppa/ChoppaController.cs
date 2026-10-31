using System;
using MoonCleaners;
using MoonVehicleCore;
using Rotorcraft;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MsChoppa
{
    // Flies the choppa with the shared Rotorcraft model. The game's MoonPrototypeVehicle keeps seats, enter/exit,
    // cargo and the camera; the core suppresses its wheel physics (FixedUpdate), which also applied gravity.
    public class ChoppaController : VehicleController
    {
        public ChoppaController(IntPtr ptr) : base(ptr) { }

        RotorcraftFlight flight;
        readonly RotorcraftPilot pilot = new();
        Rigidbody body;
        Transform mainRotor, tailRotor;
        float gravity, skidY, nextLog;
        bool wasDriven, warnedInput, placed;
        float boundAt;

        protected override void OnBound()
        {
            flight = new RotorcraftFlight(Plugin.Flight);
            body = Vehicle.body != null ? Vehicle.body : GetComponent<Rigidbody>();
            mainRotor = FindDeep(transform, "MainRotor");
            tailRotor = FindDeep(transform, "TailRotor");
            gravity = Plugin.Gravity.Value > 0f ? Plugin.Gravity.Value : GameGravity();
            skidY = SkidBottom();
            boundAt = Time.time;
            Plugin.L.LogInfo($"ChoppaController bound: storage={Storage} body={(body != null)} mass={body?.mass} useGravity={body?.useGravity} kinematic={body?.isKinematic} " +
                             $"Physics.gravity={Physics.gravity} -> using {gravity:F2} m/s², radius {transform.position.magnitude:F0} m, skidY={skidY:F2}, rotors main={mainRotor != null} tail={tailRotor != null}");
        }

        public override bool SuppressVanillaFixedUpdate => true;

        void Update()
        {
            try
            {
                if (flight == null || Vehicle == null) return;
                if (!placed && Time.time > boundAt + 0.5f && body != null && !body.isKinematic) { placed = true; LeaveGarage(); }
                bool driven = Vehicle.IsDriven;
                bool local = driven && IsLocalDriver();
                if (driven != wasDriven)
                {
                    wasDriven = driven;
                    pilot.Reset();
                    Plugin.L.LogInfo($"Choppa {(driven ? "boarded" : "left")}: driver='{Redact(Vehicle.DriverId)}' local={local} cursor={Cursor.lockState}");
                }
                flight.EngineOn = driven;
#if DEVBUILD
                if (!driven && DevSelfTest()) { }
                else
#endif
                if (local)
                {
                    pilot.TiltDot = Vector3.Dot(transform.up, Up);
                    pilot.Update(flight, ReadInput(), Time.deltaTime);
                }
                else flight.CommandedRates = Vector3.zero;

                float dt = Time.deltaTime;
                if (mainRotor != null) mainRotor.Rotate(0f, flight.RotorSpin * 900f * dt, 0f, Space.Self);
                if (tailRotor != null) tailRotor.Rotate(flight.RotorSpin * 1600f * dt, 0f, 0f, Space.Self);

                if (driven && Plugin.VerboseLogging.Value && Time.time > nextLog && body != null)
                {
                    nextLog = Time.time + 3f;
                    Plugin.L.LogInfo($"Choppa: collective={flight.Collective:F2} spin={flight.RotorSpin:F2} grounded={flight.Grounded} rates={flight.CommandedRates} " +
                                     $"v={body.linearVelocity.magnitude:F1} m/s vUp={Vector3.Dot(body.linearVelocity, Up):F2} height={Height():F1} tilt={Vector3.Angle(transform.up, Up):F0}° kinematic={body.isKinematic}");
                }
            }
            catch (Exception e)
            {
                if (!warnedInput) { warnedInput = true; Plugin.L.LogError("ChoppaController.Update: " + e); }
            }
        }

        void FixedUpdate()
        {
            if (flight == null || body == null || body.isKinematic) return;
            flight.Grounded = CheckGrounded();
            flight.Step(body, Time.fixedDeltaTime, Up, gravity, applyGravity: true);
        }

#if DEVBUILD
        float selfTestT = -1f;

        // [Dev] SelfTest: unpiloted spin-up, climb, hover and let-down, logging height, to check lift/gravity/colliders.
        bool DevSelfTest()
        {
            if (!Plugin.DevSelfTest.Value || body == null || body.isKinematic) return false;
            if (selfTestT < 0f) { if (!placed) return false; selfTestT = 0f; Plugin.L.LogInfo("[dev] self-test start"); }
            selfTestT += Time.deltaTime;
            float t = selfTestT;
            flight.EngineOn = t < 40f;
            flight.Grounded = CheckGrounded();
            flight.Collective = t < 4f ? 0f : t < 12f ? 0.75f : t < 22f ? flight.HoverCollective / Mathf.Max(0.75f, Vector3.Dot(transform.up, Up)) : 0.3f;
            flight.CommandedRates = t > 14f && t < 17f ? new Vector3(10f, 20f, 0f) : Vector3.zero;
            if (Time.time > nextLog)
            {
                nextLog = Time.time + 1f;
                Plugin.L.LogInfo($"[dev] self-test t={t:F0}s collective={flight.Collective:F2} spin={flight.RotorSpin:F2} height={Height():F2} vUp={Vector3.Dot(body.linearVelocity, Up):F2} v={body.linearVelocity.magnitude:F2} tilt={Vector3.Angle(transform.up, Up):F0}° grounded={flight.Grounded}");
            }
            if (t > 50f) { Plugin.DevSelfTest.Value = false; Plugin.L.LogInfo("[dev] self-test done"); }
            return true;
        }
#endif

        // Garages bring vehicles out into their hall, which has a roof and walls too close for the rotor. Right after
        // spawning inside one, hop onto a helipad spot outside. Parking in a garage later (to store it) is left alone.
        void LeaveGarage()
        {
            Vector3 pos = body.position;
            if (!MoonGarage.InHall(pos)) { Plugin.L.LogInfo($"Choppa spawned outside a garage hall at {pos}, leaving it there"); return; }
            float rotor = 6.2f * Plugin.Scale.Value;
            if (!Helipad.Find(pos, transform.forward, transform, rotor, out var point, out var up, out string why))
            {
                Plugin.L.LogWarning($"Choppa is in a garage hall but no helipad spot was found: {why}");
                return;
            }
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            body.position = point + up * (0.3f - skidY);
            body.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.ProjectOnPlane(Vector3.forward, up), up);
            transform.SetPositionAndRotation(body.position, body.rotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Plugin.L.LogInfo($"Choppa moved out of the garage hall to {point} ({Vector3.Distance(pos, point):F0} m away): {why}");
        }

        // The game's own moon gravity (MoonSettings.gravity); Unity's Physics.gravity is left at Earth's and unused.
        static float GameGravity()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<MoonSettings>();
                if (all != null && all.Length > 0 && all[0].gravity > 0.05f)
                {
                    Plugin.L.LogInfo($"Game gravity from MoonSettings: {all[0].gravity:F2} m/s² ({all.Length} settings assets)");
                    return all[0].gravity;
                }
                Plugin.L.LogWarning($"MoonSettings not found or gravity unset ({all?.Length ?? 0} assets); using 1.62");
            }
            catch (Exception e) { Plugin.L.LogWarning("Reading MoonSettings.gravity failed: " + e.Message); }
            return 1.62f;
        }

        Vector3 Up => transform.position.sqrMagnitude > 1f ? transform.position.normalized : Vector3.up; // moon centred on the origin

        PilotInput ReadInput()
        {
            var input = new PilotInput();
            var kb = Keyboard.current;
            if (kb != null)
            {
                input.Collective = Held(kb, Plugin.CollectiveUp.Value) - Held(kb, Plugin.CollectiveDown.Value);
                input.Pedal = Held(kb, Plugin.PedalRight.Value) - Held(kb, Plugin.PedalLeft.Value);
                input.FreeLook = Held(kb, Plugin.FreeLook.Value) > 0f;
            }
            var mouse = Mouse.current;
            if (mouse != null) input.Mouse = new Vector2(mouse.delta.x.ReadValue(), mouse.delta.y.ReadValue()) * 0.1f; // Input System deltas are pixels; legacy axes were ~0.1 px
            return input;
        }

        static float Held(Keyboard kb, Key k) => k != Key.None && kb.get_Item(k).isPressed ? 1f : 0f;

        bool IsLocalDriver()
        {
            string d = Vehicle.DriverId, me = MoonWorld.LocalPlayer;
            return string.IsNullOrEmpty(d) || d == me || d == "local";
        }

        bool CheckGrounded()
        {
            Vector3 up = Up;
            Vector3 origin = transform.TransformPoint(new Vector3(0f, skidY, 0f)) + up * 0.5f;
            foreach (var h in Physics.RaycastAll(origin, -up, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider != null && !h.collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        // Distance from skids to ground below (for logs).
        float Height()
        {
            Vector3 up = Up;
            Vector3 origin = transform.TransformPoint(new Vector3(0f, skidY, 0f)) + up * 0.5f;
            float best = float.PositiveInfinity;
            foreach (var h in Physics.RaycastAll(origin, -up, 500f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider != null && !h.collider.transform.IsChildOf(transform)) best = Mathf.Min(best, h.distance - 0.5f);
            return best;
        }

        float SkidBottom()
        {
            float y = 0f;
            bool any = false;
            foreach (var c in GetComponentsInChildren<BoxCollider>(true))
                if (c.name == "ChoppaCollider_Skids") { y = c.center.y - c.size.y * 0.5f; any = true; }
            return any ? y : 0f;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static string Redact(string s) => s != null && s.StartsWith("steam-") && s.Length > 10 ? "steam-…" + s[^4..] : s;
    }
}
