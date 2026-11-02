#if DEVBUILD
using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.XrefScans;
using MoonCleaners;
using UnityEngine;

namespace SharedUpgrades
{
    // Dev-build test steps, run once per launch in this order: probe, seed, buy, save, quit.
    internal static class DevTest
    {
        static ConfigEntry<bool> probe, save, quit;
        static ConfigEntry<string> seed, buy;
        static float gameT, saveAt = -1;
        static bool probed, seeded, bought, saved, quitting;

        internal static void Bind(ConfigFile cfg)
        {
            probe = cfg.Bind("Dev", "Probe", false, "Dev build: log what the vanilla upgrade methods call and who calls them.");
            seed = cfg.Bind("Dev", "Seed", "", "Dev build: set levels directly at 18 s, e.g. '1:Speed=2,Lights=1;20:Speed=0' (vehicle index:kind=level, my vehicles).");
            buy = cfg.Bind("Dev", "Buy", "", "Dev build: at 22 s buy one level of this kind ('Speed' or 'Speed@20' for vehicle #20), adding money if short.");
            save = cfg.Bind("Dev", "Save", false, "Dev build: save at 30 s.");
            quit = cfg.Bind("Dev", "Quit", false, "Dev build: quit once that save finished.");
        }

        internal static void Tick()
        {
            if (!MoonSession.InGame) { gameT = 0; return; }
            gameT += Time.unscaledDeltaTime;
            var w = Shared.FindWorld();
            if (w == null) return;
            if (probe.Value && !probed && gameT > 15f) { probed = true; Probe(); }
            if (seed.Value != "" && !seeded && gameT > 18f) { seeded = true; Seed(w); }
            if (buy.Value != "" && !bought && gameT > 22f) { bought = true; Buy(w); }
            if (save.Value && !saved && gameT > 30f) { saved = true; saveAt = gameT; Plugin.L.LogInfo("[dev] RequestSave()"); w.RequestSave(); }
            if (quit.Value && saved && !quitting && gameT > saveAt + 6f && (w.PendingSave == null || w.PendingSave.IsCompleted))
            {
                quitting = true;
                Plugin.L.LogInfo("[dev] Levels at quit:\n" + Shared.Table(Shared.Owned(w)));
                Plugin.L.LogInfo("[dev] save finished, quitting");
                Application.Quit();
            }
        }

        static void Seed(MoonWorld w)
        {
            var owned = Shared.Owned(w);
            foreach (var part in seed.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var halves = part.Split(':');
                int index = int.Parse(halves[0]);
                var v = owned.FirstOrDefault(o => o.vehicle == index && o.owner == MoonWorld.LocalPlayer);
                if (v == null) { Plugin.L.LogWarning($"[dev] Seed: no vehicle #{index} of mine"); continue; }
                MoonWorld.EnsureUpgrades(v);
                foreach (var kv in halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var p = kv.Split('=');
                    var kind = Enum.Parse<MoonVehicleUpgrades.Kind>(p[0]);
                    v.upgrades[(int)kind] = byte.Parse(p[1]);
                }
            }
            w._VehicleUpgradesVersion_k__BackingField = w.VehicleUpgradesVersion + 1;
            Plugin.L.LogInfo("[dev] Seeded:\n" + Shared.Table(owned));
        }

        static void Buy(MoonWorld w)
        {
            var parts = buy.Value.Split('@');
            var kind = Enum.Parse<MoonVehicleUpgrades.Kind>(parts[0]);
            int index = parts.Length > 1 ? int.Parse(parts[1]) : -1;
            var def = MoonVehicleUpgrades.Find(kind);
            string me = MoonWorld.LocalPlayer;
            var v = Shared.Owned(w).FirstOrDefault(o => o.owner == me && (index < 0 || o.vehicle == index) && Shared.Applies(def, o.vehicle));
            if (v == null) { Plugin.L.LogWarning($"[dev] Buy: no vehicle of mine takes {kind}"); return; }
            int level = MoonWorld.VehicleUpgradeLevel(v, kind);
            Il2CppSystem.Decimal p = default;
            bool priced = MoonWorld.TryGetVehicleUpgradePrice(v, kind, out p);
            decimal price = Money.From(p);
            decimal vanilla = Money.From(MoonVehicleUpgrades.Price(def, v.vehicle, level));
            decimal wallet = Money.From(w.WalletDollars(me));
            Plugin.L.LogInfo($"[dev] Buy {kind} on #{v.vehicle} at level {level}: priced={priced} ${price} (Price()=${vanilla}, scale {Money.From(MoonVehicleUpgrades.PriceScale(v.vehicle))}), wallet ${wallet}");
            if (priced && wallet < price) { w.DevAddMoney(me, Money.To(price - wallet + 10)); wallet = Money.From(w.WalletDollars(me)); Plugin.L.LogInfo($"[dev] topped up to ${wallet}"); }
            string msg;
            bool ok = w.TryUpgradeVehicle(me, v.Storage, kind, level, out msg);
            decimal after = Money.From(w.WalletDollars(me));
            Plugin.L.LogInfo($"[dev] TryUpgradeVehicle -> {ok} '{msg}', charged ${wallet - after}, level now {MoonWorld.VehicleUpgradeLevel(v, kind)}");
        }

        static void Probe()
        {
            string[] scan = { "TryUpgradeVehicle", "TryGetVehicleUpgradePrice", "CrewVehicleUpgrade", "ApplyCrewUpgrades", "EnsureUpgrades" };
            foreach (var n in scan) Dump(typeof(MoonWorld).GetMethod(n), true);
            foreach (var n in new[] { "HandleOwnedVehicles", "TickOwnedVehicles", "WriteVehicle" }) Dump(typeof(MoonCrewSession).GetMethod(n), true);
            Dump(typeof(MoonVehicleUpgrades).GetMethod("Price"), false);
            Dump(typeof(MoonGarageMenu).GetMethod("UpgradeDetails"), true);
            Dump(typeof(MoonGarageMenu).GetMethod("ClickUpgrade"), true);
        }

        static void Dump(MethodBase m, bool callees)
        {
            if (m == null) return;
            try
            {
                if (callees)
                {
                    var calls = XrefScanner.XrefScan(m).Select(Describe).Where(s => s != null).Distinct();
                    Plugin.L.LogInfo($"[probe] {m.DeclaringType.Name}.{m.Name} calls: {string.Join(" | ", calls)}");
                }
                var users = XrefScanner.UsedBy(m).Select(x => x.TryResolve()).Where(r => r != null).Select(r => $"{r.DeclaringType?.Name}.{r.Name}").Distinct();
                Plugin.L.LogInfo($"[probe] {m.DeclaringType.Name}.{m.Name} used by: {string.Join(", ", users)}");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[probe] {m.Name}: {e.Message}"); }
        }

        static string Describe(XrefInstance x)
        {
            try
            {
                if (x.Type == XrefType.Global)
                {
                    var o = x.ReadAsObject();
                    return o == null || o.GetIl2CppType().FullName != "System.String" ? null : "\"" + IL2CPP.Il2CppStringToManaged(o.Pointer) + "\"";
                }
                var r = x.TryResolve();
                return r == null ? null : $"{r.DeclaringType?.Name}.{r.Name}";
            }
            catch { return null; }
        }
    }
}
#endif
