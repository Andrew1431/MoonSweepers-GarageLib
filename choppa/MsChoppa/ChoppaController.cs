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
        Vector3 tailAxis = Vector3.right; // tail rotor's local spin axis: whichever local axis points out sideways
        float gravity, skidY, nextLog;
        bool wasDriven, warnedInput, placed;
        MoonWalker rider;          // local walker mounted in this choppa (pilot or passenger)
        bool lookDisabled;
        MoonWalker lookWalker;     // the walker whose look action we switched off
        internal static readonly System.Collections.Generic.Dictionary<IntPtr, ChoppaController> ByVehicle = new();
        internal bool CameraOverride;
        float lookYaw, lookPitch;
        Vector3 chaseVel;
        readonly System.Collections.Generic.List<Renderer> hiddenRenderers = new();
        Vector3 lastPos;
        float relocateAt = -1f;
        float flipHeld;
        float boundAt;

        protected override void OnBound()
        {
            flight = new RotorcraftFlight(Plugin.Flight);
            body = Vehicle.body != null ? Vehicle.body : GetComponent<Rigidbody>();
            mainRotor = FindDeep(transform, "MainRotor");
            tailRotor = FindDeep(transform, "TailRotor");
            if (tailRotor != null)
            {
                // The Blender empty is rotated, so pick its local axis that lines up with the choppa's sideways axis.
                Vector3 side = tailRotor.InverseTransformDirection(transform.right);
                Vector3 a = new Vector3(Mathf.Abs(side.x), Mathf.Abs(side.y), Mathf.Abs(side.z));
                tailAxis = a.x >= a.y && a.x >= a.z ? Vector3.right : a.y >= a.z ? Vector3.up : Vector3.forward;
            }
            gravity = Plugin.Gravity.Value > 0f ? Plugin.Gravity.Value : GameGravity();
            skidY = SkidBottom();
            ByVehicle[Vehicle.Pointer] = this;
            boundAt = Time.time;
            Plugin.L.LogInfo($"ChoppaController bound: storage={Storage} body={(body != null)} mass={body?.mass} useGravity={body?.useGravity} kinematic={body?.isKinematic} " +
                             $"Physics.gravity={Physics.gravity} -> using {gravity:F2} m/s², radius {transform.position.magnitude:F0} m, skidY={skidY:F2}, rotors main={mainRotor != null} tail={tailRotor != null} tailAxis={tailAxis}");
        }

        public override bool SuppressVanillaFixedUpdate => true;

        void Update()
        {
            try
            {
                if (flight == null || Vehicle == null) return;
                if (!placed && Time.time > boundAt + 0.5f && body != null && !body.isKinematic) { placed = true; LeaveGarage(); lastPos = body.position; }
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
                    HoldFlip();
                }
                else flight.CommandedRates = Vector3.zero;

                UpdateRider(local);

                float dt = Time.deltaTime;
                if (mainRotor != null) mainRotor.Rotate(0f, flight.RotorSpin * 900f * dt, 0f, Space.Self);
                if (tailRotor != null) tailRotor.Rotate(tailAxis, flight.RotorSpin * 1600f * dt, Space.Self);

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
            // Recalled or brought out again by a garage: the game teleports the existing choppa into the hall.
            Vector3 p = body.position;
            if (placed && (p - lastPos).sqrMagnitude > 9f && !Vehicle.IsDriven && MoonGarage.InHall(p))
            {
                relocateAt = Time.time + 0.5f;
                Plugin.L.LogInfo($"Choppa teleported {Vector3.Distance(p, lastPos):F0} m into a garage hall; moving it out shortly");
            }
            lastPos = p;
            if (relocateAt > 0f && Time.time >= relocateAt) { relocateAt = -1f; LeaveGarage(); lastPos = body.position; }
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

        // The game's Flip binding (Z by default) rights vehicles; its wheel-physics flip is suppressed here, so hold it
        // for the game's hold time while tipped over and the choppa is set back upright.
        void HoldFlip()
        {
            bool held = false;
            try { held = MoonInput.Flip != null && MoonInput.Flip.Held; } catch { }
            float tilt = Vector3.Angle(transform.up, Up);
            if (!held || tilt < 60f || body == null) { flipHeld = 0f; return; }
            flipHeld += Time.deltaTime;
            float need = MoonPrototypeVehicle.FlipHoldSeconds > 0.1f ? MoonPrototypeVehicle.FlipHoldSeconds : 1f;
            if (flipHeld < need) return;
            flipHeld = 0f;
            Vector3 up = Up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.up, up);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position += up * 1.5f;
            body.rotation = Quaternion.LookRotation(fwd.normalized, up);
            transform.SetPositionAndRotation(body.position, body.rotation);
            flight.Collective = 0f;
            Plugin.L.LogInfo($"Choppa flipped upright (was tilted {tilt:F0}°)");
        }

        // Riders are hidden (the borrowed van's seat poses don't fit the cabin), and while the local pilot flies the
        // game's mouse-look is switched off so the camera stays behind the choppa; hold FreeLook to look around.
        void UpdateRider(bool localPilot)
        {
            if (rider == null || rider.WasCollected || !RiderInThis(rider)) rider = FindLocalRider();
            bool freeLook = Keyboard.current != null && Held(Keyboard.current, Plugin.FreeLook.Value) > 0f;
            CameraOverride = localPilot && rider != null;
            SetLook(!CameraOverride);
            if (CameraOverride)
            {
                var m = Mouse.current;
                if (freeLook && m != null)
                {
                    lookYaw = Mathf.Clamp(lookYaw + m.delta.x.ReadValue() * 0.15f, -170f, 170f);
                    lookPitch = Mathf.Clamp(lookPitch - m.delta.y.ReadValue() * 0.15f, -60f, 70f);
                }
                else
                {
                    float back = 1f - Mathf.Exp(-Time.deltaTime * 8f);
                    lookYaw = Mathf.Lerp(lookYaw, 0f, back);
                    lookPitch = Mathf.Lerp(lookPitch, 0f, back);
                }
            }

            var keep = new System.Collections.Generic.HashSet<IntPtr>();
            if (rider != null)
                foreach (var r in rider.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    keep.Add(r.Pointer);
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; hiddenRenderers.Add(r); }
                }
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
            {
                var r = hiddenRenderers[i];
                if (r == null || r.WasCollected) { hiddenRenderers.RemoveAt(i); continue; }
                if (keep.Contains(r.Pointer)) continue;
                r.forceRenderingOff = false;
                hiddenRenderers.RemoveAt(i);
            }
        }

        bool RiderInThis(MoonWalker w) =>
            w.mountedVehicle != null && w.mountedVehicle.Pointer == Vehicle.Pointer ||
            w.vehicleSeat != null && w.vehicleSeat.IsChildOf(transform);

        MoonWalker FindLocalRider()
        {
            foreach (var w in UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None))
                if (w != null && RiderInThis(w)) { Plugin.L.LogInfo($"Choppa rider found: {w.name} (pilot={w.mountedVehicle != null})"); return w; }
            return null;
        }

        // Off while piloting: the game's mouse-look would orbit its vehicle camera, which we replace with a chase cam.
        void SetLook(bool enabled)
        {
            if (enabled)
            {
                if (!lookDisabled) return;
                try { if (lookWalker != null && !lookWalker.WasCollected) lookWalker.look?.Enable(); }
                catch (Exception e) { Plugin.L.LogWarning("Re-enabling mouse-look failed: " + e.Message); }
                lookDisabled = false; lookWalker = null;
                Plugin.L.LogInfo("Choppa camera released, game mouse-look back on");
                return;
            }
            if (lookDisabled || rider == null) return;
            try
            {
                var look = rider.look;
                if (look == null) return;
                look.Disable();
                lookDisabled = true; lookWalker = rider;
                lookYaw = lookPitch = 0f;
                Plugin.L.LogInfo("Choppa chase camera on, game mouse-look off (hold FreeLook to look around)");
            }
            catch (Exception e) { Plugin.L.LogWarning("Disabling mouse-look failed: " + e.Message); }
        }

        // Called after MoonWalker.LateUpdate placed its vehicle camera: put the camera behind the choppa instead.
        internal void PlaceCamera(MoonWalker w)
        {
            var cam = w.viewCamera;
            if (cam == null) return;
            float s = Plugin.Scale.Value / 0.75f;
            Vector3 up = Up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, up);
            var orbit = Quaternion.LookRotation(fwd.normalized, up) * Quaternion.Euler(10f + lookPitch, lookYaw, 0f);
            Vector3 focus = transform.position + up * (2f * s);
            Vector3 desired = focus + orbit * new Vector3(0f, 0f, -Plugin.ChaseDistance.Value * s) + up * (Plugin.ChaseHeight.Value - 2f) * s;
            var ct = cam.transform;
            ct.position = Vector3.SmoothDamp(ct.position, desired, ref chaseVel, 0.12f);
            ct.rotation = Quaternion.LookRotation(focus - ct.position, up);
        }

        void OnDisable()
        {
            CameraOverride = false;
            SetLook(true);
            ByVehicle.Remove(Vehicle != null ? Vehicle.Pointer : IntPtr.Zero);
            foreach (var r in hiddenRenderers) if (r != null && !r.WasCollected) r.forceRenderingOff = false;
            hiddenRenderers.Clear();
        }

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
