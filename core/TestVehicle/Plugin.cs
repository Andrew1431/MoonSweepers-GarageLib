using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using MoonCleaners;
using MoonVehicleCore;
using UnityEngine;

namespace TestVehicle
{
    // Example consumer: a ride-on sweeper clone with an orange roof box and one custom upgrade.
    [BepInPlugin(Guid, "Moon Vehicle Core Test Vehicle", "0.1.0")]
    [BepInDependency(MoonVehicleCore.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        internal static BepInEx.Logging.ManualLogSource L;
        public const string Guid = "com.andrew1431.moonvehiclecore.testvehicle";

        public override void Load()
        {
            L = Log;
            MoonVehicles.Register(new VehicleDefinition
            {
                Id = "andrew1431.test-rover",
                Name = "Test Rover",
                Summary = "Moon Vehicle Core test: a ride-on with a roof box. Costs $50.",
                Price = 50m,
                BaseVehicle = 1,
                Speed = 9f,
                StorageKg = 750,
                UpgradeKinds = new[] { MoonVehicleUpgrades.Kind.Speed, MoonVehicleUpgrades.Kind.Hopper, MoonVehicleUpgrades.Kind.Lights },
                UpgradePriceScale = 0.5,
                CustomUpgrades =
                {
                    new CustomUpgrade { Id = "roofbox", Title = "Roof box", Summary = "Bigger roof box.", Prices = new[] { 100m, 250m } },
                },
                DecorateModel = (go, ctx) =>
                {
                    var box = ctx.Models.Box(go.transform, "TestRoofBox", new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 0.35f, 1.1f), ctx.Models.orange, false);
                    Log.LogInfo($"Decorated {go.name}: roof box {(box != null ? "added" : "FAILED")}");
                },
            }, Guid);

            MoonVehicles.Register(new VehicleDefinition
            {
                Id = "andrew1431.test-hover",
                Name = "Test Hover",
                Summary = "Moon Vehicle Core test: custom controller that floats the vehicle. Costs $60.",
                Price = 60m,
                BaseVehicle = 4,
                Controller = typeof(HoverController),
                DecorateModel = (go, ctx) => ctx.Models.Box(go.transform, "TestHoverPad", new Vector3(0f, 0.15f, 0f), new Vector3(2.2f, 0.1f, 4f), ctx.Models.orange, false),
            }, Guid);

            MoonVehicles.Spawned += s => Log.LogInfo($"Spawned event: {s.Definition.Id} storage={s.Storage} roofbox level={MoonVehicles.CustomLevel(s.Storage, "roofbox")}");
            MoonVehicles.Bought += (d, garage, owner) => Log.LogInfo($"Bought event: {d.Id} at garage {garage}");
        }
    }

    // Proves the controller hook: vanilla wheel physics is suppressed and this keeps the body floating 1.5 m up.
    public class HoverController : VehicleController
    {
        public HoverController(IntPtr ptr) : base(ptr) { }
        Rigidbody body;
        float nextLog;

        protected override void OnBound()
        {
            body = GetComponent<Rigidbody>();
            Plugin.L.LogInfo($"HoverController bound to {Definition.Id} storage={Storage} rigidbody={(body != null)} speed level={UpgradeLevel(MoonVehicleUpgrades.Kind.Speed)}");
        }

        void FixedUpdate()
        {
            if (body == null) return;
            Vector3 up = body.position.normalized; // the moon is a sphere centred on the origin
            float vUp = Vector3.Dot(body.linearVelocity, up);
            if (Physics.Raycast(body.position + up, -up, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                float error = 1.5f - (hit.distance - 1f);
                body.AddForce(up * (error * 30f - vUp * 6f), ForceMode.Acceleration);
            }
            if (Time.time > nextLog) { nextLog = Time.time + 5f; Plugin.L.LogInfo($"HoverController {Definition.Id} at {body.position} vUp={vUp:F2} useGravity={body.useGravity} kinematic={body.isKinematic}"); }
        }
    }
}
