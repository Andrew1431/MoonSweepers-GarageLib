using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GarageLib;
using MoonCleaners;
using UnityEngine;

namespace __NAME__
{
    [BepInPlugin(Guid, "__TITLE__", Version)]
    [BepInDependency(GarageLib.Plugin.Guid)]
    public class Plugin : BasePlugin
    {
        // Never change Guid or VehicleId after release: the Guid names players' config files, the id is in their saves.
        public const string Guid = "com.__AUTHOR__.__LOWER__";
        public const string Version = "0.1.0";
        public const string VehicleId = "__AUTHOR__.__KEBAB__";

        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            MoonVehicles.Register(new VehicleDefinition
            {
                Id = VehicleId,
                Name = "__TITLE__",
                Summary = "Garage card text.",
                Price = 3000m,
                // Vanilla vehicle to borrow the model, seats and upgrade rules from:
                // 0 push sweeper, 1 ride-on, 4 crew van, 20 lunar sweeper, 21 mega vacuum rover.
                BaseVehicle = 1,
                Speed = 10f,
                DecorateModel = (go, ctx) =>
                    ctx.Models.Box(go.transform, "__NAME__Roof", new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 0.3f, 1.1f), ctx.Models.orange, false),
            }, Guid);
            L.LogInfo($"__TITLE__ {Version} loaded");
        }
    }
}
