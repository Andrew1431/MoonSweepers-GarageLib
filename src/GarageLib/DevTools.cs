#if DEVBUILD
using System;
using System.Linq;
using BepInEx.Configuration;
using MoonCleaners;
using GarageLib.Patches;
using UnityEngine;

namespace GarageLib
{
    // Dev-build automation for testing against the cloned save. Never loads anything but the slot-2 folder.
    internal static class DevTools
    {
        static ConfigEntry<bool> autoLoad, autoBringOut, autoSave, autoQuit;
        static ConfigEntry<string> autoBuy;
        static ConfigEntry<int> autoOpenMenu, fillers;
        static ConfigEntry<bool> dumpMenu, dumpStats;
        static bool menuOpened, statsDumped;
        static float menuT, gameT, dumpAt = -1;
        static float tabAt = -1, shotAt = -1, pickAt = -1;
        static bool pickedLast;
        static int shots;
        static bool loadRequested, bought, saveRequested, quitRequested;
        static float saveAt = -1;

        internal static void Bind(ConfigFile cfg)
        {
            autoLoad = cfg.Bind("Dev", "AutoLoadSlot2", false, "Dev build: load save slot-2 (the cloned test save) from the main menu. Refuses any other slot.");
            autoBuy = cfg.Bind("Dev", "AutoBuy", "", "Dev build: comma-separated vehicle ids to buy at the first garage once in game (adds money if short).");
            autoBringOut = cfg.Bind("Dev", "AutoBringOut", false, "Dev build: bring the AutoBuy vehicle out of its garage.");
            autoSave = cfg.Bind("Dev", "AutoSave", false, "Dev build: request a save 10 s after the AutoBuy step.");
            autoQuit = cfg.Bind("Dev", "AutoQuit", false, "Dev build: quit the game once that save finished.");
            autoOpenMenu = cfg.Bind("Dev", "AutoOpenGarageMenu", -1, "Dev build: open the first garage's menu on this tab (0 vehicles, 1 upgrades, 2 fleet) 20 s into the game. -1 = off.");
            fillers = cfg.Bind("Dev", "FillerVehicles", 0, "Dev build: register this many dummy ride-on clones to stress the garage menu layout.");
            dumpMenu = cfg.Bind("Dev", "DumpGarageMenu", false, "Dev build: after AutoOpenGarageMenu, log the menu's UI hierarchy, then reopen it on the upgrades tab.");
            dumpStats = cfg.Bind("Dev", "DumpVanillaStats", false, "Dev build: 10 s into the game, write every vanilla vehicle's stats and upgrade prices to BepInEx/cache/garagelib/vanilla-stats.json.");
            for (int i = 1; i <= fillers.Value; i++)
                MoonVehicles.Register(new VehicleDefinition
                {
                    Id = $"dev.filler-{i:D2}", Name = $"Filler {i}", Summary = $"Dev filler vehicle {i} for menu layout tests.",
                    Price = 100m * i, BaseVehicle = i % 2 == 0 ? 1 : 0,
                }, Plugin.Guid);
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
            if (dumpStats.Value && !statsDumped && gameT > 10f)
            {
                statsDumped = true;
                try { DevStatsDump.Write(); } catch (Exception e) { CoreLog.Error("[dev] vanilla stats dump failed: " + e); }
            }
            if (autoOpenMenu.Value >= 0 && !menuOpened && gameT > 20f)
            {
                menuOpened = true;
                var menus = UnityEngine.Object.FindObjectsByType<MoonGarageMenu>(FindObjectsSortMode.None);
                var g = MoonGarage.all;
                if (menus != null && menus.Length > 0 && g != null && g.Count > 0)
                    CoreLog.Info($"[dev] open garage menu tab {autoOpenMenu.Value}: {menus[0].Open(g.get_Item(0).Id, autoOpenMenu.Value)}");
                else CoreLog.Warn($"[dev] no garage menu ({menus?.Length}) or garage");
                if (dumpMenu.Value) dumpAt = gameT + 3f;
            }
            if (dumpAt > 0 && gameT > dumpAt) { dumpAt = -1; DumpMenu(); tabAt = gameT + 5f; }
            if (tabAt > 0 && gameT > tabAt) { tabAt = -1; OpenTab(1); shotAt = gameT + 2f; }
            if (shotAt > 0 && gameT > shotAt) { shotAt = -1; Shot(); if (!pickedLast) pickAt = gameT + 2f; }
            if (pickAt > 0 && gameT > pickAt) { pickAt = -1; pickedLast = true; PickLast(); shotAt = gameT + 2f; }
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

        // Second step of the menu check: switch the open garage menu to another tab (screenshots are taken from outside).
        static void OpenTab(int tab)
        {
            var menus = UnityEngine.Object.FindObjectsByType<MoonGarageMenu>(FindObjectsSortMode.None);
            var g = MoonGarage.all;
            if (menus == null || menus.Length == 0 || g == null || g.Count == 0) return;
            menus[0].ShowPage((MoonGarageMenu.Page)tab, false);
            CoreLog.Info($"[dev] garage menu switched to {menus[0].page}");
        }

        // Back on the vehicles tab, select the last card: the list should scroll it into view.
        static void PickLast()
        {
            var menus = UnityEngine.Object.FindObjectsByType<MoonGarageMenu>(FindObjectsSortMode.None);
            if (menus == null || menus.Length == 0 || menus[0].cards == null || menus[0].cards.Count == 0) return;
            var m = menus[0];
            m.ShowPage(MoonGarageMenu.Page.Vehicles, false);
            var last = m.cards.get_Item(m.cards.Count - 1);
            m.Pick(last);
            CoreLog.Info($"[dev] picked last card #{last.vehicle}, content y={last.rect.parent?.TryCast<RectTransform>()?.anchoredPosition.y}");
        }

        static void DumpMenu()
        {
            var menus = UnityEngine.Object.FindObjectsByType<MoonGarageMenu>(FindObjectsSortMode.None);
            if (menus == null || menus.Length == 0) return;
            var m = menus[0];
            CoreLog.Info($"[dev] menu dump: page={m.page} cards={m.cards?.Count} strip={m.strip?.Count} BayY={MoonGarageMenu.BayY} BayW={MoonGarageMenu.BayW} BayH={MoonGarageMenu.BayH} Inner={MoonGarageMenu.Inner} Gap={MoonGarageMenu.Gap} StripH={MoonGarageMenu.StripH} MainY={MoonGarageMenu.MainY} MainH={MoonGarageMenu.MainH} LeftW={MoonGarageMenu.LeftW} Left={MoonGarageMenu.Left}");
            foreach (var page in new[] { m.vehiclesPage, m.upgradesPage })
                if (page != null) Dump(page, 0);
            Shot();
        }

        // ScreenCapture.CaptureScreenshot(string) breaks in interop; grab a texture at the end of the frame instead.
        internal static void Shot()
        {
            if (CoreDriver.Instance == null) return;
            CoreDriver.Instance.StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(ShotAtEndOfFrame()));
        }

        static System.Collections.IEnumerator ShotAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string dir = System.IO.Path.Combine(BepInEx.Paths.CachePath, "garagelib");
            System.IO.Directory.CreateDirectory(dir);
            string f = System.IO.Path.Combine(dir, $"menu-{shots++}.png");
            System.IO.File.WriteAllBytes(f, ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.Destroy(tex);
            CoreLog.Info("[dev] screenshot " + f);
        }

        static void Dump(Transform t, int depth)
        {
            if (depth > 5) return;
            var rt = t.TryCast<RectTransform>();
            var comps = string.Join(",", t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "RectTransform" && n != "Transform"));
            string geo = rt == null ? "" : $" amin={rt.anchorMin} amax={rt.anchorMax} piv={rt.pivot} pos={rt.anchoredPosition} size={rt.sizeDelta} rect={rt.rect.size}";
            CoreLog.Info($"[dev] {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf}{geo} [{comps}]");
            for (int i = 0; i < t.childCount && i < 40; i++) Dump(t.GetChild(i), depth + 1);
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
