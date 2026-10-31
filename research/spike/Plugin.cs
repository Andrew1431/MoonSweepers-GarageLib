using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonCleaners;
using UnityEngine;

namespace VehicleSpike
{
    // Research-only: logs how the game defines, prices, spawns and saves vehicles.
    [BepInPlugin("com.andrew1431.ms.vehiclespike", "Vehicle Spike", "0.0.1")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;

        public override void Load()
        {
            L = Log;
            ClassInjector.RegisterTypeInIl2Cpp<SpikeDriver>();
            AddComponent<SpikeDriver>();
            var h = new Harmony("com.andrew1431.ms.vehiclespike");
            try { h.PatchAll(typeof(Traces)); } catch (Exception e) { L.LogError("PatchAll: " + e); }
            L.LogInfo("Vehicle spike loaded");
        }
    }

    public class SpikeDriver : MonoBehaviour
    {
        public SpikeDriver(IntPtr ptr) : base(ptr) { }
        float t, inGameT;
        bool dumped, requested, worldDumped;

        void Update()
        {
            try { Tick(); } catch (Exception e) { Plugin.L.LogError("Tick: " + e); dumped = requested = worldDumped = true; }
        }

        void Tick()
        {
            t += Time.unscaledDeltaTime;
            if (!dumped && t > 6f)
            {
                dumped = true;
                try { Dump(); } catch (Exception e) { Plugin.L.LogError("Dump: " + e); }
                var L = Plugin.L;
                L.LogInfo($"[slots] Count={MoonSaveSlots.Count} Active={MoonSaveSlots.Active} ActiveKey={MoonSaveSlots.ActiveKey} Folder={MoonSaveSlots.Folder}");
                for (int i = 0; i <= MoonSaveSlots.Count; i++)
                    L.LogInfo($"[slots] SlotFolder({i})={Safe(() => MoonSaveSlots.SlotFolder(i))} used={Safe(() => MoonSaveSlots.Used(i).ToString())}");
            }
            if (dumped && !requested && t > 8f && MoonSession.InMenu)
            {
                requested = true;
                AutoLoadSlot2();
            }
            if (MoonSession.InGame && !worldDumped)
            {
                inGameT += Time.unscaledDeltaTime;
                if (inGameT > 20f)
                {
                    worldDumped = true;
                    try { Dump(); } catch (Exception e) { Plugin.L.LogError("World dump: " + e); }
                }
            }
        }

        // Only ever starts the cloned test slot; refuses anything else.
        static void AutoLoadSlot2()
        {
            var L = Plugin.L;
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = Safe(() => MoonSaveSlots.SlotFolder(i)).Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith("/slot-2")) { target = i; break; }
            }
            if (target < 0) { L.LogWarning("[autoload] slot-2 not found, staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = MoonSaveSlots.SlotFolder(MoonSaveSlots.Active).Replace('\\', '/').TrimEnd('/');
            if (!chosen.EndsWith("/slot-2")) { L.LogWarning($"[autoload] active slot resolved to {chosen}, refusing"); return; }
            L.LogInfo($"[autoload] starting slot index {target} ({chosen})");
            MoonSession.StartShift();
        }

        static void Dump()
        {
            var L = Plugin.L;
            var vehicles = MoonPrototypeCatalog.Vehicles;
            L.LogInfo($"[catalog] Vehicles={vehicles.Length} CrewTransport={MoonPrototypeCatalog.CrewTransport} LunarSweeper={MoonPrototypeCatalog.LunarSweeper} MegaVacuumRover={MoonPrototypeCatalog.MegaVacuumRover}");
            for (int i = 0; i < vehicles.Length; i++)
            {
                var v = vehicles[i];
                string prefab = Safe(() => MoonPrototypeCatalog.AuthoredPrefab(i));
                bool loads = false;
                try { loads = !string.IsNullOrEmpty(prefab) && Resources.Load<GameObject>(prefab) != null; } catch { }
                L.LogInfo($"[catalog] #{i} name='{v.name}' work={v.work} hint='{v.hint}' w={v.width} d={v.depth} reach={v.reach} speed={v.speed} turn={v.turn} cap={v.capacity} " +
                          $"prefab='{prefab}' resourcesLoad={loads} storageKg={Safe(() => MoonPrototypeCatalog.StorageKg(i).ToString())} heft={Safe(() => MoonPrototypeCatalog.Heft(i).ToString())} " +
                          $"seats={Safe(() => MoonVehicleSeats.Count(i).ToString())} slug={Safe(() => MoonWorld.GarageSlug(i))}");
            }
            var offers = MoonWorld.GarageOffers;
            if (offers == null) { L.LogInfo("[garage] GarageOffers is null (world not loaded yet)"); return; }
            L.LogInfo($"[garage] offers={offers.Length} GarageKind={MoonWorld.GarageKind} GarageVehicleMask={MoonWorld.GarageVehicleMask} note='{MoonWorld.GarageNote}' MaxVehicles={MoonOwnedVehicleSave.MaxVehicles} MaxSeats={MoonVehicleSeats.MaxSeats}");
            for (int i = 0; i < offers.Length; i++)
                L.LogInfo($"[garage] offer[{i}] vehicle={offers[i].vehicle} price={offers[i].price.ToString()} summary='{offers[i].summary}'");
            L.LogInfo($"[save] OdometerVersion={MoonOwnedVehicleSave.OdometerVersion} UpgradesVersion={MoonOwnedVehicleSave.UpgradesVersion}");
        }

        static string Safe(Func<string> f) { try { return f(); } catch (Exception e) { return "ERR:" + e.GetType().Name; } }
    }

    static class Traces
    {
        static readonly Dictionary<string, int> counts = new();
        static void T(string what)
        {
            counts.TryGetValue(what.Split('(')[0], out int n);
            if (n >= 25) return;
            counts[what.Split('(')[0]] = n + 1;
            Plugin.L.LogInfo("[trace] " + what + "\n" + Environment.StackTrace.Split('\n').Length);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuyVehicle)), HarmonyPrefix]
        static void TryBuy(string player, int garage, int vehicle) => T($"TryBuyVehicle(player={player}, garage={garage}, vehicle={vehicle})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CrewVehicleBought)), HarmonyPrefix]
        static void CrewBought(string player, int garage, int vehicle) => T($"CrewVehicleBought(garage={garage}, vehicle={vehicle})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.GarageOfferFor)), HarmonyPrefix]
        static void OfferFor(int vehicle) => T($"GarageOfferFor(vehicle={vehicle})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.GarageVehicle)), HarmonyPrefix]
        static void GarageVehicle(int garage, int vehicle, string owner) => T($"GarageVehicle(garage={garage}, vehicle={vehicle})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.BringOut)), HarmonyPrefix]
        static void BringOut(int vehicle, string storage) => T($"BringOut(vehicle={vehicle}, storage={storage})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SpawnHeld)), HarmonyPrefix]
        static void SpawnHeld(MoonOwnedVehicle v) => T($"SpawnHeld(vehicle={v?.vehicle}, garage={v?.garage}, storage={v?.Storage})");

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.RestoreOwnedVehicles)), HarmonyPostfix]
        static void Restore(MoonWorld __instance)
        {
            foreach (var kv in __instance.ownedVehicles)
                if (kv.Value is var v) Plugin.L.LogInfo($"[owned] garage={v.garage} vehicle={v.vehicle} owner={v.owner} inWorld={v.inWorld} storage={v.Storage} odo={v.odometer} upgrades={v.upgrades?.Length}");
        }

        [HarmonyPatch(typeof(MoonPrototypeModels), nameof(MoonPrototypeModels.Vehicle)), HarmonyPrefix]
        static void ModelVehicle(int index) => T($"MoonPrototypeModels.Vehicle(index={index})");

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.Render)), HarmonyPrefix]
        static void Render(int vehicle) => T($"MoonGarageMenu.Render(vehicle={vehicle})");

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.Row)), HarmonyPrefix]
        static void Row(Il2CppSystem.Collections.Generic.List<MoonWorld.GarageOffer> offers) => T($"MoonGarageMenu.Row(offers={offers?.Count})");

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.Initialize)), HarmonyPrefix]
        static void Init(int index, string storageId) => T($"MoonPrototypeVehicle.Initialize(index={index}, storage={storageId})");

        [HarmonyPatch(typeof(MoonOwnedVehicleSave), nameof(MoonOwnedVehicleSave.ReadOne)), HarmonyPostfix]
        static void ReadOne(MoonOwnedVehicle __result) => T($"MoonOwnedVehicleSave.ReadOne(-> vehicle={__result?.vehicle}, garage={__result?.garage})");
    }
}
