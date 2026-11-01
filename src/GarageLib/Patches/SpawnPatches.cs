using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace GarageLib.Patches
{
    [HarmonyPatch]
    internal static class ModelPatches
    {
        [HarmonyPatch(typeof(MoonPrototypeModels), nameof(MoonPrototypeModels.Vehicle)), HarmonyPrefix]
        static bool Vehicle(MoonPrototypeModels __instance, int index, Transform parent, ref GameObject __result)
        {
            var d = MoonVehicles.Definition(index);
            if (d == null) return true;
            var ctx = new ModelContext { Definition = d, Parent = parent, Models = __instance };
            try
            {
                if (d.BuildModel != null)
                {
                    __result = d.BuildModel(ctx);
                    CoreLog.Info($"Model for '{d.Id}' built by its mod: {(__result ? __result.name : "NULL")}");
                }
                if (__result == null)
                {
                    // Recursive call re-enters this prefix with a vanilla index, which passes straight through.
                    __result = __instance.Vehicle(d.BaseVehicle, parent);
                    CoreLog.Info($"Model for '{d.Id}' borrowed from base {d.BaseVehicle}: {(__result ? __result.name : "NULL")}");
                }
                if (__result != null && d.DecorateModel != null) d.DecorateModel(__result, ctx);
                if (__result != null) __result.name = "ModVehicle_" + d.Id;
            }
            catch (Exception e)
            {
                CoreLog.Error($"Building model for '{d.Id}' failed, falling back to base {d.BaseVehicle}: {e}");
                if (__result == null) __result = __instance.Vehicle(d.BaseVehicle, parent);
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ControllerPatches
    {
        internal static readonly Dictionary<IntPtr, VehicleController> controllers = new();

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.Initialize)), HarmonyPostfix]
        static void Initialize(MoonPrototypeVehicle __instance, int index, string storageId)
        {
            var d = MoonVehicles.Definition(index);
            if (d == null) return;
            CoreLog.Info($"Spawned modded vehicle '{d.Id}' storage={storageId} go={__instance.gameObject.name}");
            VehicleController ctrl = null;
            if (d.Controller != null)
            {
                try
                {
                    var comp = __instance.gameObject.AddComponent(Il2CppType.From(d.Controller));
                    ctrl = comp?.TryCast<VehicleController>();
                    if (ctrl == null)
                        CoreLog.Error($"'{d.Id}': AddComponent({d.Controller.Name}) did not give a VehicleController (got {comp?.GetIl2CppType()?.FullName ?? "null"})");
                    else
                    {
                        ctrl.Bind(d, __instance, storageId);
                        controllers[__instance.Pointer] = ctrl;
                        CoreLog.Info($"'{d.Id}': controller {d.Controller.Name} attached (suppress FixedUpdate={ctrl.SuppressVanillaFixedUpdate}, Update={ctrl.SuppressVanillaUpdate})");
                    }
                }
                catch (Exception e) { CoreLog.Error($"'{d.Id}': attaching controller failed: {e}"); }
            }
            MoonVehicles.RaiseSpawned(new VehicleSpawn { Definition = d, Vehicle = __instance, Storage = storageId, Controller = ctrl });
        }

        static bool Suppressed(MoonPrototypeVehicle v, bool fixedStep)
        {
            if (controllers.Count == 0) return false;
            if (!controllers.TryGetValue(v.Pointer, out var c)) return false;
            if (c == null || c.WasCollected) { controllers.Remove(v.Pointer); return false; }
            return fixedStep ? c.SuppressVanillaFixedUpdate : c.SuppressVanillaUpdate;
        }

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.FixedUpdate)), HarmonyPrefix]
        static bool FixedUpdate(MoonPrototypeVehicle __instance) => !Suppressed(__instance, true);

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.Update)), HarmonyPrefix]
        static bool Update(MoonPrototypeVehicle __instance) => !Suppressed(__instance, false);

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.OnDestroy)), HarmonyPostfix]
        static void OnDestroy(MoonPrototypeVehicle __instance) => controllers.Remove(__instance.Pointer);
    }

    [HarmonyPatch]
    internal static class PurchasePatches
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuyVehicle)), HarmonyPrefix]
        static void TryBuyPre(string player, int garage, int vehicle)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d != null) CoreLog.Info($"TryBuyVehicle '{d.Id}' (#{vehicle}) garage={garage} player={Redact(player)}");
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryBuyVehicle)), HarmonyPostfix]
        static void TryBuyPost(MoonWorld __instance, bool __result, string player, int garage, int vehicle, string message)
        {
            CoreState.World = __instance;
            var d = MoonVehicles.Definition(vehicle);
            if (d == null) return;
            CoreLog.Info($"TryBuyVehicle '{d.Id}' -> {__result} message='{message}'");
            if (__result) MoonVehicles.RaiseBought(d, garage, player);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CrewVehicleBought)), HarmonyPrefix]
        static void CrewBought(string player, int garage, int vehicle)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d != null) CoreLog.Info($"CrewVehicleBought '{d.Id}' garage={garage} player={Redact(player)}");
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.BringOut)), HarmonyPrefix]
        static void BringOut(int vehicle, string storage)
        {
            if (MoonVehicles.IsModded(vehicle)) CoreLog.Info($"BringOut modded #{vehicle} storage={Redact(storage)}");
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.SpawnHeld)), HarmonyPrefix]
        static void SpawnHeld(MoonOwnedVehicle v)
        {
            if (v != null && MoonVehicles.IsModded(v.vehicle)) CoreLog.Info($"SpawnHeld modded #{v.vehicle} storage={Redact(v.Storage)}");
        }

        // Steam ids in logs get shared when people post logs; keep only the tail.
        internal static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.IndexOf("steam-", StringComparison.Ordinal);
            if (i < 0) return s;
            int end = i + 6;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            string digits = s.Substring(i + 6, end - i - 6);
            return s.Substring(0, i) + "steam-…" + (digits.Length > 4 ? digits[^4..] : digits) + s.Substring(end);
        }
    }
}
