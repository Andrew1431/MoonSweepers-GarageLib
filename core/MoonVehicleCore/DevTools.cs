#if DEVBUILD
using System;
using System.Linq;
using BepInEx.Configuration;
using MoonCleaners;
using MoonVehicleCore.Patches;
using UnityEngine;

namespace MoonVehicleCore
{
    // Dev-build automation for testing against the cloned save. Never loads anything but the slot-2 folder.
    internal static class DevTools
    {
        static ConfigEntry<bool> autoLoad, autoBringOut, autoSave, autoQuit;
        static ConfigEntry<string> autoBuy;
        static float menuT, gameT;
        static bool loadRequested, bought, saveRequested, quitRequested;
        static float saveAt = -1;

        internal static void Bind(ConfigFile cfg)
        {
            autoLoad = cfg.Bind("Dev", "AutoLoadSlot2", false, "Dev build: load save slot-2 (the cloned test save) from the main menu. Refuses any other slot.");
            autoBuy = cfg.Bind("Dev", "AutoBuy", "", "Dev build: comma-separated vehicle ids to buy at the first garage once in game (adds money if short).");
            autoBringOut = cfg.Bind("Dev", "AutoBringOut", false, "Dev build: bring the AutoBuy vehicle out of its garage.");
            autoSave = cfg.Bind("Dev", "AutoSave", false, "Dev build: request a save 10 s after the AutoBuy step.");
            autoQuit = cfg.Bind("Dev", "AutoQuit", false, "Dev build: quit the game once that save finished.");
            CoreLog.Info($"[dev] AutoLoadSlot2={autoLoad.Value} AutoBuy='{autoBuy.Value}' AutoBringOut={autoBringOut.Value} AutoSave={autoSave.Value} AutoQuit={autoQuit.Value}");
        }

        internal static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (MoonSession.InMenu && !MoonSession.Loading)
            {
                menuT += dt;
                if (autoLoad.Value && !loadRequested && menuT > 6f) { loadRequested = true; LoadSlot2(); }
            }
            if (!MoonSession.InGame) { gameT = 0; return; }
            gameT += dt;
            if (!string.IsNullOrEmpty(autoBuy.Value) && !bought && gameT > 15f) { bought = true; foreach (var id in autoBuy.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Buy(id); saveAt = gameT + 10f; }
            if (autoSave.Value && bought && !saveRequested && saveAt > 0 && gameT > saveAt)
            {
                saveRequested = true;
                var w = CoreState.FindWorld();
                CoreLog.Info("[dev] RequestSave()");
                w?.RequestSave();
            }
            if (autoQuit.Value && saveRequested && !quitRequested && gameT > saveAt + 8f)
            {
                var w = CoreState.FindWorld();
                var pending = w?.PendingSave;
                if (pending == null || pending.IsCompleted)
                {
                    quitRequested = true;
                    CoreLog.Info("[dev] save finished, quitting");
                    Application.Quit();
                }
            }
        }

        static void LoadSlot2()
        {
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = (MoonSaveSlots.SlotFolder(i) ?? "").Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith("/slot-2")) { target = i; break; }
            }
            if (target < 0) { CoreLog.Warn("[dev] slot-2 not found, staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = (MoonSaveSlots.SlotFolder(MoonSaveSlots.Active) ?? "").Replace('\\', '/').TrimEnd('/');
            if (!chosen.EndsWith("/slot-2")) { CoreLog.Warn($"[dev] active slot resolved to {chosen}, refusing"); return; }
            CoreLog.Info($"[dev] loading slot index {target} ({chosen})");
            MoonSession.StartShift();
        }

        static void Buy(string id)
        {
            var d = MoonVehicles.Definition(id);
            var w = CoreState.FindWorld();
            if (d == null || w == null) { CoreLog.Warn($"[dev] AutoBuy: def={d != null} world={w != null}"); return; }
            string player = MoonWorld.LocalPlayer;
            var garages = MoonGarage.all;
            if (garages == null || garages.Count == 0) { CoreLog.Warn("[dev] AutoBuy: no MoonGarage in scene"); return; }
            // Owned already in any garage? Otherwise try each garage until one accepts (a garage holds one parked vehicle).
            MoonGarage g = null;
            for (int i = 0; i < garages.Count && g == null; i++)
                if (w.Owns(player, garages.get_Item(i).Id, d.Index)) g = garages.get_Item(i);
            CoreLog.Info($"[dev] AutoBuy '{id}' (#{d.Index}) as '{PurchasePatches.Redact(player)}'; {garages.Count} garages; owned at {(g == null ? "none" : g.Id.ToString())}");
            for (int i = 0; i < garages.Count && g == null; i++)
            {
                var cand = garages.get_Item(i);
                decimal wallet = Money.From(w.WalletDollars(player));
                if (wallet < d.Price) { CoreLog.Info($"[dev] wallet {wallet} < {d.Price}, adding money"); w.DevAddMoney(player, Money.To(d.Price - wallet + 100)); }
                string msg = null;
                bool ok = w.TryBuyVehicle(player, cand.Id, d.Index, out msg);
                CoreLog.Info($"[dev] TryBuyVehicle at garage {cand.Id} -> {ok} '{msg}'");
                if (ok) g = cand;
            }
            if (g == null) return;
            int garage = g.Id;
            var owned = w.OwnedVehicle(garage, d.Index, player);
            CoreLog.Info($"[dev] owned record: {(owned == null ? "none" : $"storage={PurchasePatches.Redact(owned.Storage)} inWorld={owned.inWorld}")}");
            if (autoBringOut.Value && owned != null)
            {
                try
                {
                    var v = w.BringOut(g, d.Index, owned.Storage);
                    CoreLog.Info($"[dev] BringOut -> {(v == null ? "null" : v.gameObject.name + " at " + v.transform.position)}");
                }
                catch (Exception e) { CoreLog.Error("[dev] BringOut threw: " + e); }
            }
            Status();
        }

        static void Status()
        {
            var w = CoreState.FindWorld();
            CoreLog.Info($"[dev] status: frozen={MoonVehicles.Frozen} catalog={MoonPrototypeCatalog.Vehicles?.Length} offers={MoonWorld.GarageOffers?.Length} " +
                         $"mask={MoonWorld.GarageVehicleMask} saveRoot={CoreState.SaveRoot} world={(w != null)}");
            if (w == null) return;
            foreach (var kv in w.ownedVehicles)
            {
                var v = kv.Value;
                if (v != null && MoonVehicles.IsModded(v.vehicle))
                    CoreLog.Info($"[dev]   modded owned #{v.vehicle} storage={PurchasePatches.Redact(v.Storage)} inWorld={v.inWorld} pos={v.position} upgrades=[{(v.upgrades == null ? "" : string.Join(",", v.upgrades.ToArray()))}]");
            }
        }
    }
}
#endif
