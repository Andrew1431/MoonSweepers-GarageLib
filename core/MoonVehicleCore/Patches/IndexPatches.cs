using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MoonVehicleCore.Patches
{
    // Static lookups keyed by vehicle index. A modded index either gets the definition's override or is rewritten to
    // its base vehicle's index before the vanilla code runs.
    internal static class Remap
    {
        internal static bool ToBase(ref int index, string what)
        {
            var d = MoonVehicles.Definition(index);
            if (d == null) return false;
            CoreLog.Once("remap:" + what + index, $"{what}({index}) -> base {d.BaseVehicle} for '{d.Id}'", 3);
            index = d.BaseVehicle;
            return true;
        }
    }

    [HarmonyPatch]
    internal static class CatalogPatches
    {
        [HarmonyPatch(typeof(MoonPrototypeCatalog), nameof(MoonPrototypeCatalog.AuthoredPrefab)), HarmonyPrefix]
        static void AuthoredPrefab(ref int index) => Remap.ToBase(ref index, "AuthoredPrefab");

        [HarmonyPatch(typeof(MoonPrototypeCatalog), nameof(MoonPrototypeCatalog.StorageKg)), HarmonyPrefix]
        static bool StorageKg(ref int index, ref double __result)
        {
            var d = MoonVehicles.Definition(index);
            if (d == null) return true;
            if (!double.IsNaN(d.StorageKg)) { __result = d.StorageKg; CoreLog.Once("kg" + index, $"StorageKg({index}) = {d.StorageKg} ('{d.Id}')", 2); return false; }
            Remap.ToBase(ref index, "StorageKg");
            return true;
        }

        [HarmonyPatch(typeof(MoonPrototypeCatalog), nameof(MoonPrototypeCatalog.Heft)), HarmonyPrefix]
        static bool Heft(ref int index, ref float __result)
        {
            var d = MoonVehicles.Definition(index);
            if (d == null) return true;
            if (!float.IsNaN(d.Heft)) { __result = d.Heft; return false; }
            Remap.ToBase(ref index, "Heft");
            return true;
        }
    }

    [HarmonyPatch]
    internal static class SeatPatches
    {
        static bool Custom(int vehicle, out VehicleDefinition d)
        {
            d = MoonVehicles.Definition(vehicle);
            return d != null && d.Seats != null && d.Seats.Length > 0;
        }

        [HarmonyPatch(typeof(MoonVehicleSeats), nameof(MoonVehicleSeats.Layout)), HarmonyPrefix]
        static bool Layout(ref int vehicle, ref Il2CppStructArray<MoonVehicleSeats.Seat> __result)
        {
            if (Custom(vehicle, out var d))
            {
                int n = Mathf.Min(d.Seats.Length, MoonVehicleSeats.MaxSeats);
                var arr = new Il2CppStructArray<MoonVehicleSeats.Seat>(n);
                for (int i = 0; i < n; i++) arr[i] = new MoonVehicleSeats.Seat { foot = d.Seats[i].Foot, turn = d.Seats[i].Turn };
                __result = arr;
                CoreLog.Once("layout" + vehicle, $"Seats.Layout({vehicle}) = {n} custom seats ('{d.Id}')", 2);
                return false;
            }
            Remap.ToBase(ref vehicle, "Seats.Layout");
            return true;
        }

        [HarmonyPatch(typeof(MoonVehicleSeats), nameof(MoonVehicleSeats.Count)), HarmonyPrefix]
        static bool Count(ref int vehicle, ref int __result)
        {
            if (Custom(vehicle, out var d)) { __result = Mathf.Min(d.Seats.Length, MoonVehicleSeats.MaxSeats); return false; }
            Remap.ToBase(ref vehicle, "Seats.Count");
            return true;
        }

        [HarmonyPatch(typeof(MoonVehicleSeats), nameof(MoonVehicleSeats.HasPassengers)), HarmonyPrefix]
        static bool HasPassengers(ref int vehicle, ref bool __result)
        {
            if (Custom(vehicle, out var d)) { __result = d.Seats.Length > 1; return false; }
            Remap.ToBase(ref vehicle, "Seats.HasPassengers");
            return true;
        }

        // Pose/Free read the layout internally; only remap when the vehicle has no custom seats.
        [HarmonyPatch(typeof(MoonVehicleSeats), nameof(MoonVehicleSeats.Pose)), HarmonyPrefix]
        static void Pose(ref int vehicle) { if (!Custom(vehicle, out _)) Remap.ToBase(ref vehicle, "Seats.Pose"); }

        [HarmonyPatch(typeof(MoonVehicleSeats), nameof(MoonVehicleSeats.Free)), HarmonyPrefix]
        static void Free(ref int vehicle) { if (!Custom(vehicle, out _)) Remap.ToBase(ref vehicle, "Seats.Free"); }
    }

    [HarmonyPatch]
    internal static class GarageMenuPatches
    {
        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.Render)), HarmonyPrefix]
        static bool Render(ref int vehicle, ref Sprite __result)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d == null) return true;
            if (d.CardIcon != null)
            {
                try
                {
                    var s = d.CardIcon();
                    if (s != null) { __result = s; return false; }
                    CoreLog.Once("icon-null" + d.Id, $"'{d.Id}' CardIcon returned null, using base picture");
                }
                catch (System.Exception e) { CoreLog.Once("icon" + d.Id, $"'{d.Id}' CardIcon threw: {e}"); }
            }
            var rendered = IconRenderer.Get(d);
            if (rendered != null) { __result = rendered; return false; }
            CoreLog.Once("icon-pending" + d.Id, $"'{d.Id}' card icon not ready, using base picture");
            Remap.ToBase(ref vehicle, "GarageMenu.Render");
            return true;
        }

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.Row)), HarmonyPrefix]
        static void Row(Il2CppSystem.Collections.Generic.List<MoonWorld.GarageOffer> offers)
        {
            if (offers == null) return;
            var ids = new System.Text.StringBuilder();
            for (int i = 0; i < offers.Count; i++) ids.Append(offers.get_Item(i)?.vehicle).Append(' ');
            CoreLog.Once("row", $"GarageMenu.Row offers: {ids}", 6);
        }

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.UpgradeDetails)), HarmonyPrefix]
        static void UpgradeDetails(int vehicle)
        {
            var d = MoonVehicles.Definition(vehicle);
            if (d != null) CoreLog.Once("updetails" + vehicle, $"GarageMenu.UpgradeDetails for modded '{d.Id}' (custom upgrades are not shown in the menu yet)", 2);
        }
    }

    [HarmonyPatch]
    internal static class GarageTraces
    {
        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.GarageOfferFor)), HarmonyPostfix]
        static void OfferFor(int vehicle, MoonWorld.GarageOffer __result)
        {
            if (MoonVehicles.IsModded(vehicle))
                CoreLog.Once("offerfor" + vehicle, $"GarageOfferFor({vehicle}) -> {(__result == null ? "null (game will treat it as unsellable!)" : "price " + __result.price.ToString())}", 3);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.GarageSlug)), HarmonyPostfix]
        static void Slug(int vehicle, string __result)
        {
            if (MoonVehicles.IsModded(vehicle)) CoreLog.Once("slug" + vehicle, $"GarageSlug({vehicle}) -> '{__result}'", 2);
        }

        [HarmonyPatch(typeof(MoonGarage), nameof(MoonGarage.Bay)), HarmonyPostfix]
        static void Bay(int vehicle, Pose __result)
        {
            if (MoonVehicles.IsModded(vehicle)) CoreLog.Once("bay" + vehicle, $"MoonGarage.Bay({vehicle}) -> {__result.position}", 3);
        }
    }
}
