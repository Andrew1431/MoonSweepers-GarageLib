#if DEVBUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;
using MoonCleaners;
using UnityEngine;

namespace StructureLib
{
    // Dev-build automation for testing against a cloned save. Loads slot-1 (the real save) only with [Dev] AllowSlot1.
    internal static class DevTools
    {
        internal static ConfigEntry<int> AutoLoadSlot;
        internal static ConfigEntry<bool> AllowSlot1, RunSelfTest, ScreenshotMenu, AutoRemove, AutoSave;
        internal static ConfigEntry<string> AutoBuild, AutoFeed;
        static float menuT, gameT, saveAt = -1f;
        static bool loadRequested, selfTestRan, menuShot, autoBuilt;

        /// <summary>What AutoBuild did per entry (id or kind number as written in the config): ok, depot id, reason.</summary>
        internal static readonly Dictionary<string, (bool ok, int depot, string reason)> BuildResults = new(StringComparer.Ordinal);

        /// <summary>True once AutoSave asked the world to save; SaveSeqAtRequest is the sidecar's write count then.</summary>
        internal static bool SaveRequested;
        internal static int SaveSeqAtRequest;

        /// <summary>True when [Dev] AutoBuild names the given structure id.</summary>
        internal static bool AutoBuildNames(string id) =>
            (AutoBuild?.Value ?? "").Split(',').Any(s => s.Trim() == id);

        internal static void Bind(ConfigFile cfg)
        {
            AutoLoadSlot = cfg.Bind("Dev", "AutoLoadSlot", -1, "Dev build: load the existing save in folder slot-<n> from the main menu. -1 = off. Slot 1 is the real save and is refused unless AllowSlot1 is true.");
            AllowSlot1 = cfg.Bind("Dev", "AllowSlot1", false, "Dev build: let AutoLoadSlot and SelfTest touch slot-1 (the real save). Leave false.");
            RunSelfTest = cfg.Bind("Dev", "SelfTest", false, "Dev build: run the in-game self-test 20 s after a world loads and log [selftest] lines.");
            AutoBuild = cfg.Bind("Dev", "AutoBuild", "", "Dev build: comma-separated structure ids or vanilla kind numbers to build once, 10 s after a world loads, 8 m apart behind (or beside) the walker. An entry whose kind already has a depot reuses it.");
            AutoFeed = cfg.Bind("Dev", "AutoFeed", "", "Dev build: '<structureId>:<cargoId|Dust|Sand|Stone>:<kg>' to load into a built structure. (Not used yet.)");
            AutoRemove = cfg.Bind("Dev", "AutoRemove", true, "Dev build: the self-test removes the example lamp it checked. false leaves it in place (the sidecar carries it into the next launch).");
            AutoSave = cfg.Bind("Dev", "AutoSave", false, "Dev build: request a save 10 s after the AutoBuild step (whether or not AutoBuild names anything). The self-test waits for that save and checks the structurelib.json sidecar.");
            SelfTest.Subscribe();
            ScreenshotMenu = cfg.Bind("Dev", "ScreenshotMenu", false, "Dev build: 5 s after the self-test, open the build menu on the example lamp and save a screenshot to BepInEx/cache/structurelib/menu.png.");
            CoreLog.Info($"[dev] AutoLoadSlot={AutoLoadSlot.Value} AllowSlot1={AllowSlot1.Value} SelfTest={RunSelfTest.Value} ScreenshotMenu={ScreenshotMenu.Value} AutoRemove={AutoRemove.Value} AutoSave={AutoSave.Value} AutoBuild='{AutoBuild.Value}' AutoFeed='{AutoFeed.Value}'");
            // MoonStructures.Register reads this static at plugin Load time; prove the interop read works that early.
            try { CoreLog.Info($"[dev] MoonProductionSave.MaxSlots at Load = {MoonProductionSave.MaxSlots}"); }
            catch (Exception e) { CoreLog.Error("[dev] reading MoonProductionSave.MaxSlots at Load threw: " + e); }
        }

        internal static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (MoonSession.InMenu && !MoonSession.Loading)
            {
                menuT += dt;
                if (AutoLoadSlot.Value >= 0 && !loadRequested && menuT > 6f) { loadRequested = true; LoadSlot(AutoLoadSlot.Value); }
            }
            if (!MoonSession.InGame) { gameT = 0; return; }
            gameT += dt;
            if (!autoBuilt && gameT > 10f)
            {
                autoBuilt = true;
                string refusal = SlotRefusal();
                // The self-test's exception watch starts here, before any build or save of this run, so a save the
                // game refuses (it logs "Moon save failed") fails the run.
                if (RunSelfTest.Value && refusal == null) SelfTest.StartWatch();
                if (!string.IsNullOrWhiteSpace(AutoBuild.Value))
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoBuild skipped: " + refusal);
                    else AutoBuilder.Run(AutoBuild.Value);
                }
                if (AutoSave.Value)
                {
                    if (refusal != null) CoreLog.Warn("[dev] AutoSave skipped: " + refusal);
                    else saveAt = gameT + 10f;
                }
            }
            if (saveAt > 0f && gameT > saveAt) { saveAt = -1f; RequestSave(); }
            if (RunSelfTest.Value && !selfTestRan && gameT > 20f)
            {
                selfTestRan = true;
                string refusal = SlotRefusal();
                if (refusal != null) CoreLog.Warn("[dev] SelfTest skipped: " + refusal);
                else SelfTest.Run();
            }
            SelfTest.Tick();
            if (ScreenshotMenu.Value && !menuShot && SelfTest.FinishedAt > 0f && Time.unscaledTime > SelfTest.FinishedAt + 5f) { menuShot = true; MenuShot.Start(); }
        }

        // The same call GarageLib's AutoSave uses. The save itself runs later (and possibly off the main thread).
        static void RequestSave()
        {
            var w = CoreState.World ?? CoreState.FindWorld();
            if (w == null) { CoreLog.Warn("[dev] AutoSave: no MoonWorld"); return; }
            SaveSeqAtRequest = Sidecar.WriteSeq;
            SaveRequested = true;
            CoreLog.Info($"[dev] RequestSave() (sidecar writes so far: {SaveSeqAtRequest})");
            w.RequestSave();
            try { CoreLog.Info($"[dev] after RequestSave: progressDirty={w.progressDirty} nextSave={w.nextSave:F1} t={Time.time:F1} unscaled={Time.unscaledTime:F1} SoonSaveSeconds={MoonWorld.SoonSaveSeconds} ReleaseSaveSeconds={MoonWorld.ReleaseSaveSeconds} SaveRetrySeconds={MoonWorld.SaveRetrySeconds}"); }
            catch (Exception e) { CoreLog.Warn("[dev] reading save timers: " + e.Message); }
        }

        static string ActiveFolder() => (MoonSaveSlots.SlotFolder(MoonSaveSlots.Active) ?? "").Replace('\\', '/').TrimEnd('/');

        // Null when the loaded world may be touched by dev automation.
        static string SlotRefusal()
        {
            if (AllowSlot1.Value) return null;
            string active = ActiveFolder();
            return active.EndsWith("/slot-1") ? "slot-1 (the real save) is loaded and [Dev] AllowSlot1 is false" : null;
        }

        // The menu's own play methods do nothing when called from a plugin; setting MoonSaveSlots.Active then
        // StartShift() does load. The slot is matched by folder name, never by index.
        static void LoadSlot(int slot)
        {
            if (slot == 1 && !AllowSlot1.Value) { CoreLog.Warn("[dev] AutoLoadSlot: refusing slot-1 (the real save); clone it and load the clone, or set AllowSlot1"); return; }
            string suffix = "/slot-" + slot;
            int target = -1;
            for (int i = 0; i <= MoonSaveSlots.Count; i++)
            {
                string f = (MoonSaveSlots.SlotFolder(i) ?? "").Replace('\\', '/').TrimEnd('/');
                if (f.EndsWith(suffix)) { target = i; break; }
            }
            if (target < 0) { CoreLog.Warn($"[dev] AutoLoadSlot: no slot index maps to folder slot-{slot}; staying in menu"); return; }
            MoonSaveSlots.Active = target;
            string chosen = ActiveFolder();
            if (!chosen.EndsWith(suffix)) { CoreLog.Warn($"[dev] AutoLoadSlot: active slot resolved to '{chosen.Substring(chosen.LastIndexOf('/') + 1)}', refusing"); return; }
            CoreLog.Info($"[dev] AutoLoadSlot: loading slot index {target} (folder slot-{slot})");
            MoonSession.StartShift();
        }
    }

    // [Dev] AutoBuild: builds each entry through the game's own TryBuildDepot with a null placement check (fact (e)),
    // entry i at 8 * (i + 1) m from the local walker. Behind first: slot-2 has vanilla structures 12 m and 22 m ahead of
    // the spawn point. A refused site is retried to the sides, then ahead. Tops the team wallet up when it is short.
    internal static class AutoBuilder
    {
        static readonly float[] Headings = { 180f, 90f, 270f, 0f };

        internal static void Run(string spec)
        {
            var entries = spec.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var world = CoreState.World ?? CoreState.FindWorld();
            var walkers = UnityEngine.Object.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            var walker = walkers != null && walkers.Length > 0 ? walkers[0] : null;
            if (world == null || walker == null)
            {
                foreach (var e in entries) Report(e, false, -1, world == null ? "no MoonWorld" : "no MoonWalker");
                return;
            }
            SelfTest.ResetEvents(world);
            for (int i = 0; i < entries.Count; i++)
            {
                try { Build(world, walker, entries[i], i); }
                catch (Exception e) { Report(entries[i], false, -1, "threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        static void Build(MoonWorld world, MoonWalker walker, string entry, int index)
        {
            int kind;
            if (!int.TryParse(entry, out kind))
            {
                var d = MoonStructures.Definition(entry);
                if (d == null || !MoonStructures.IsModded(d.Kind)) { Report(entry, false, -1, "no registered structure with this id"); return; }
                kind = d.Kind;
            }
            // Only a modded kind is reused (one built earlier in this run, or restored from the save once saves carry modded
            // kinds); a vanilla kind is always built anew, since slot-2 already holds vanilla structures.
            int existing = MoonStructures.IsModded(kind) ? SelfTest.DepotOfKind(world, kind) : -1;
            if (existing >= 0)
            {
                CoreLog.Info($"[dev] AutoBuild: '{entry}' (modded kind {kind}) already has depot {existing} in this world; reusing it");
                Report(entry, true, existing, "reused existing depot");
                return;
            }
            var def = world.BuildDefinition(kind);
            if (def == null) { Report(entry, false, -1, $"no build definition for kind {kind}"); return; }
            EnsureMoney(world, def.priceDollars);

            var wt = walker.transform;
            var up = wt.position.normalized;
            float dist = 8f * (index + 1);
            bool ok = false;
            string reason = "";
            int id = -1;
            foreach (float h in Headings)
            {
                var at = wt.position + Quaternion.AngleAxis(h, up) * wt.forward * dist;
                var before = DepotIds(world);
                ok = world.TryBuildDepot(walker, at.normalized, 0f, out reason, null, kind);
                if (ok) foreach (var kv in world.depots) if (!before.Contains(kv.Key)) { id = kv.Key; break; }
                CoreLog.Info($"[dev] AutoBuild: '{entry}' (kind {kind}) at {dist:F0} m, heading {h:F0} deg from facing: ok={ok} reason='{reason}'");
                if (ok) break;
            }
            Report(entry, ok, id, reason);
        }

        // DevAddMoney is a no-op in this build; the wallet is team-wide, so it is set directly.
        static void EnsureMoney(MoonWorld world, int price)
        {
            string player = MoonWorld.LocalPlayer;
            decimal have = SelfTest.DecimalOf(world.WalletDollars(player));
            if (have >= price) return;
            world.SetTeamDollars(new Il2CppSystem.Decimal((double)(price + 100)));
            decimal now = SelfTest.DecimalOf(world.WalletDollars(player));
            CoreLog.Info($"[dev] AutoBuild: wallet ${have} was short of ${price}; now ${now}");
        }

        static HashSet<int> DepotIds(MoonWorld world)
        {
            var s = new HashSet<int>();
            if (world.depots != null) foreach (var kv in world.depots) s.Add(kv.Key);
            return s;
        }

        static void Report(string entry, bool ok, int depot, string reason)
        {
            DevTools.BuildResults[entry] = (ok, depot, reason);
            CoreLog.Info($"[dev] built {entry} depot={depot} ok={ok} reason='{reason}'");
        }
    }

    // Evidence for the build-menu check without a screen: open the first builder's menu on the example lamp's tab,
    // select the lamp, and save a screenshot to BepInEx/cache/structurelib/menu.png.
    internal static class MenuShot
    {
        internal static void Start()
        {
            if (CoreDriver.Instance == null) return;
            CoreDriver.Instance.StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(Run()));
        }

        static System.Collections.IEnumerator Run()
        {
            var lamp = MoonStructures.Definition("ryanhartwig.example-lamp");
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            if (lamp == null || builder == null) { CoreLog.Warn($"[dev] menu shot: lamp {(lamp != null)}, builder {(builder != null)}"); yield break; }
            Step("SetOpen(true)", () => builder.SetOpen(true));
            CoreLog.Info($"[dev] menu shot: IsOpen={builder.IsOpen} after SetOpen(true)");
            if (!builder.IsOpen)
            {
                Step("ToggleBuildMode()", () => builder.ToggleBuildMode());
                CoreLog.Info($"[dev] menu shot: IsOpen={builder.IsOpen} after ToggleBuildMode()");
            }
            if (!builder.IsOpen)
            {
                // Both refuse unless the build tool is in hand; show the menu's own UI objects instead.
                Step("EnsureBuildUI()", () => builder.EnsureBuildUI());
                Step("ui/menu SetActive(true)", () => { builder.ui?.SetActive(true); builder.menu?.SetActive(true); });
                Step("RefreshMenu()", () => builder.RefreshMenu());
                CoreLog.Info($"[dev] menu shot: menu active={builder.menu != null && builder.menu.activeInHierarchy}, IsOpen={builder.IsOpen}");
            }
            yield return null;
            int tab = Array.IndexOf(MoonDepotBuilder.catalogCategories?.ToArray() ?? Array.Empty<string>(), lamp.Category);
            if (tab >= 0) Step($"FilterCatalog({tab})", () => builder.FilterCatalog(tab));
            Step($"SelectStructure({lamp.Kind})", () => builder.SelectStructure(lamp.Kind));
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "menu.png");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                UnityEngine.Object.Destroy(tex);
                CoreLog.Info($"[dev] menu shot saved to {path} (selected {builder.selectedStructure}, category {builder.selectedCategory}, open {builder.IsOpen})");
            }
            catch (Exception e) { CoreLog.Warn("[dev] menu shot: capture failed: " + e.Message); }
        }

        static void Step(string what, Action a)
        {
            try { a(); CoreLog.Info("[dev] menu shot: " + what); }
            catch (Exception e) { CoreLog.Warn($"[dev] menu shot: {what} threw {e.GetType().Name}: {e.Message}"); }
        }
    }

    // In-game assertions. Line formats are fixed; tooling greps them.
    internal static class SelfTest
    {
        static int passed, failed;
        static string logPath;
        static long logStart = -1;
        static readonly Regex Bad = new Regex(@"Exception|Error  :", RegexOptions.Compiled);

        internal static void Run()
        {
            Begin();
            Assert("registry-frozen", MoonStructures.Frozen, "MoonStructures.Frozen is false after the world loaded");
            var lamp = MoonStructures.Definition("ryanhartwig.example-lamp");
            orphanMode = lamp == null && Sidecar.Orphans.Contains(LampId);
            if (orphanMode)
            {
                // ExampleStructure is not installed, and the save's sidecar holds its lamp: only the orphan checks apply.
                CoreLog.Info("[selftest] the example lamp is not registered and the sidecar keeps it as an orphan: orphan checks only");
                WaitForSave();
                return;
            }
            int expected = KindAllocator.KindOf("ryanhartwig.example-lamp");
            Assert("example-kind-hashed", lamp != null && lamp.Kind == expected,
                lamp == null ? "'ryanhartwig.example-lamp' is not registered (is ExampleStructure installed?)" : $"kind {lamp.Kind}, expected {expected}");
            DefinitionChecks(lamp);
            // Finishes the run (Done) itself, possibly a few frames later.
            CatalogChecks(lamp);
        }

        // The build menu lists the lamp under Services, in the static catalog and in the menu's own copy of it. Its card
        // art is polled for up to 5 s (the renderer needs a few frames); then the rendered picture itself is awaited for
        // up to 10 s and must be what CardArt answers (fallback art does not count). Then the run finishes.
        const string LampImage = "structurelib:ryanhartwig.example-lamp";
        static MoonDepotBuilder artBuilder;
        static StructureDefinition artLamp;
        static float artUntil = -1f, renderUntil = -1f;

        /// <summary>Unscaled time at which the last run printed its done line; -1 until then.</summary>
        internal static float FinishedAt = -1f;

        static void CatalogChecks(StructureDefinition lamp)
        {
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            string noLamp = lamp == null ? "example lamp not registered" : null;
            string noBuilder = builder == null ? "no MoonDepotBuilder found" : noLamp;
            Check("catalog-has-modded", noLamp, () =>
            {
                var entries = MoonDepotBuilder.baseStructures;
                if (entries == null) return (false, "baseStructures is null");
                bool ok = entries.Any(e => e != null && e.kind == lamp.Kind && e.category == "Services");
                return (ok, $"no Services entry for kind {lamp.Kind} among {entries.Length} baseStructures");
            });
            Check("catalog-instance-has-modded", noBuilder, () =>
            {
                builder.RefreshCatalog();
                var entries = builder.structureEntries;
                if (entries == null) return (false, "structureEntries is null after RefreshCatalog");
                return (entries.Any(e => e != null && e.kind == lamp.Kind), $"no entry for kind {lamp.Kind} among {entries.Length} structureEntries");
            });
            if (noBuilder != null) { Assert("cardart-modded", false, noBuilder); Assert("cardart-rendered", false, noBuilder); Finish(); return; }
            artBuilder = builder;
            artLamp = lamp;
            artUntil = Time.unscaledTime + 5f;
            renderUntil = Time.unscaledTime + 10f;
        }

        // Every frame in game: answers the pending card art checks, then finishes the run.
        internal static void Tick()
        {
            if (artUntil >= 0f) { TickCardArt(); return; }
            if (renderUntil >= 0f) { TickRendered(); return; }
            if (saveUntil >= 0f) TickSaveWait();
        }

        // cardart-modded: CardArt answers something once nothing is pending, or after 5 s.
        static void TickCardArt()
        {
            Sprite art = null;
            string error = null;
            bool timedOut = Time.unscaledTime >= artUntil;
            try
            {
                if (!(artBuilder != null && artBuilder.Pointer != IntPtr.Zero && artBuilder)) error = "the MoonDepotBuilder went away";
                else if (ArtSettled(artLamp) || timedOut) art = artBuilder.CardArt(LampImage);
            }
            catch (Exception e) { error = "threw " + e.GetType().Name + ": " + e.Message; }
            if (art == null && error == null && !timedOut) return;
            artUntil = -1f;
            Assert("cardart-modded", art != null, error ?? $"CardArt(\"{LampImage}\") returned null after 5 s");
            if (art != null)
            {
                var rendered = IconRenderer.Get(artLamp);
                string source = rendered != null && rendered.Pointer == art.Pointer ? "the rendered picture" : "fallback art";
                CoreLog.Info($"[selftest] card art for '{artLamp.Id}': sprite '{art.name}' {art.rect.width}x{art.rect.height} ({source})");
            }
        }

        // cardart-rendered: once IconRenderer has a sprite (up to 10 s), CardArt must answer that very sprite.
        static void TickRendered()
        {
            Sprite rendered = null;
            string error = null;
            bool timedOut = Time.unscaledTime >= renderUntil;
            if (!(artBuilder != null && artBuilder.Pointer != IntPtr.Zero && artBuilder)) error = "the MoonDepotBuilder went away";
            else rendered = IconRenderer.Get(artLamp);
            if (rendered == null && error == null && !timedOut) return;
            renderUntil = -1f;
            if (error == null && rendered == null) error = "IconRenderer.Get returned null after 10 s (render failed or never queued)";
            Sprite art = null;
            if (error == null)
            {
                try { art = artBuilder.CardArt(LampImage); }
                catch (Exception e) { error = "CardArt threw " + e.GetType().Name + ": " + e.Message; }
            }
            bool ok = error == null && art != null && art.Pointer == rendered.Pointer;
            Assert("cardart-rendered", ok, error ?? $"CardArt(\"{LampImage}\") returned '{art?.name ?? "null"}', not the rendered sprite '{rendered.name}'");
            artBuilder = null;
            artLamp = null;
            Finish();
        }

        // AutoBuild placed the example lamp: the [dev] built line said ok, the world holds a depot of its kind, its view
        // was decorated and renamed (and Built was raised), and removing it refunds part or all of its price, drops the
        // record and raises Removed.
        const string LampId = "ryanhartwig.example-lamp";

        static void BuildChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            string noLamp = lamp == null ? "example lamp not registered" : null;
            string noWorld = world == null ? "no MoonWorld found" : noLamp;
            bool autoBuilt = DevTools.AutoBuildNames(LampId);
            if (!autoBuilt && lamp != null && world != null && DepotOfKind(world, lamp.Kind) < 0)
            {
                CoreLog.Info($"[selftest] '{LampId}' was neither built (AutoBuild) nor restored this run: build checks not applicable");
                return;
            }
            if (autoBuilt) Check("build-modded-ok", noLamp, () =>
            {
                if (!DevTools.BuildResults.TryGetValue(LampId, out var r))
                    return (false, $"no [dev] built line for '{LampId}' (AutoBuild='{DevTools.AutoBuild.Value}')");
                return (r.ok, $"[dev] built {LampId} depot={r.depot} ok={r.ok} reason='{r.reason}'");
            });
            int depot = -1;
            Check("build-modded-depot-kind", noWorld, () =>
            {
                depot = DepotOfKind(world, lamp.Kind);
                return (depot >= 0, $"world.depots ({world.depots?.Count}) has no record of kind {lamp.Kind}");
            });
            Check("build-modded-view-decorated", noLamp, () =>
            {
                string name = "ModStructure_" + lamp.Id;
                var view = GameObject.Find(name);
                if (view == null) return (false, $"no GameObject named '{name}'");
                if (view.transform.Find("ExampleLampTag") == null) return (false, $"'{name}' has no child 'ExampleLampTag'");
                if (depot >= 0 && !builtEvents.Contains(depot)) return (false, $"MoonStructures.Built was not raised for depot {depot}");
                return (true, null);
            });
            Check("remove-modded-refund", noWorld ?? (depot < 0 ? "no example lamp depot to remove" : null), () =>
            {
                decimal refund = DecimalOf(world.StructureRefund(depot));
                decimal price = lamp.Price;
                CoreLog.Info($"[selftest] refund for depot {depot} ('{lamp.Id}', price ${price}): ${refund} = {(price != 0 ? refund / price : 0):0.###} of the price");
                if (!(refund > 0 && refund <= price)) return (false, $"StructureRefund({depot}) = {refund}, expected > 0 and <= {price}");
                if (!DevTools.AutoRemove.Value)
                {
                    CoreLog.Info($"[selftest] AutoRemove=false: depot {depot} left in place (refund checked, removal not run)");
                    return (true, null);
                }
                world.RemoveStructureNow(depot);
                if (world.depots.ContainsKey(depot)) return (false, $"depot {depot} is still in world.depots after RemoveStructureNow");
                if (!removedEvents.Contains(depot)) return (false, $"MoonStructures.Removed was not raised for depot {depot}");
                return (true, null);
            });
        }

        static readonly HashSet<int> builtEvents = new(), builtViews = new(), removedEvents = new();

        // Called when AutoBuild starts: forget every event id except Built for modded depots that exist in this world now
        // (raised when the save restored them), so an id from an earlier build cannot satisfy a later assertion.
        internal static void ResetEvents(MoonWorld world)
        {
            var live = new HashSet<int>();
            if (world?.depots != null)
                foreach (var kv in world.depots)
                    if (kv.Value != null && MoonStructures.IsModded(kv.Value.kind)) live.Add(kv.Key);
            builtEvents.IntersectWith(live);
            builtViews.IntersectWith(live);
            removedEvents.Clear();
        }

        // Records the library's events so the checks can see they fired. Called once from DevTools.Bind.
        internal static void Subscribe()
        {
            MoonStructures.Built += b =>
            {
                if (b == null) return;
                builtEvents.Add(b.DepotId);
                if (b.View != null) builtViews.Add(b.DepotId);
            };
            MoonStructures.Removed += id => removedEvents.Add(id);
        }

        internal static int DepotOfKind(MoonWorld world, int kind)
        {
            if (world?.depots == null) return -1;
            foreach (var kv in world.depots)
                if (kv.Value != null && kv.Value.kind == kind) return kv.Key;
            return -1;
        }

        internal static decimal DecimalOf(Il2CppSystem.Decimal d)
        {
            try { return decimal.Parse(d.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return (decimal)(double)d; }
        }

        // True once nothing more is coming for this structure's card art (no render pending).
        static bool ArtSettled(StructureDefinition d) => !IconRenderer.Pending(d);

        static void Finish() => WaitForSave();

        // With [Dev] AutoSave, the checks wait (up to 180 s) for the requested save: a MoonDepotSave.Write after the
        // request, the world's pending save completed, then 3 s for the game to report a failed save.
        static bool orphanMode;
        static float saveUntil = -1f, saveGraceUntil = -1f, saveWaitFrom;

        static void WaitForSave()
        {
            saveGraceUntil = -1f;
            saveUntil = DevTools.AutoSave.Value ? Time.unscaledTime + 180f : Time.unscaledTime;
            saveWaitFrom = Time.unscaledTime;
            if (DevTools.AutoSave.Value) CoreLog.Info($"[selftest] waiting up to 180 s for the AutoSave save (t={saveWaitFrom:F1})");
        }

        static void TickSaveWait()
        {
            float now = Time.unscaledTime;
            if (now < saveUntil && saveGraceUntil < 0f)
            {
                if (!DevTools.SaveRequested || Sidecar.WriteSeq <= DevTools.SaveSeqAtRequest) return;
                Il2CppSystem.Threading.Tasks.Task pending = null;
                try { pending = (CoreState.World ?? CoreState.FindWorld())?.PendingSave; } catch (Exception) { }
                if (pending != null && !pending.IsCompleted) return;
                saveGraceUntil = now + 3f;
                return;
            }
            if (saveGraceUntil >= 0f && now < saveGraceUntil) return;
            if (DevTools.AutoSave.Value) CoreLog.Info($"[selftest] save wait over after {now - saveWaitFrom:F1} s ({(saveGraceUntil >= 0f ? "save finished" : "timed out")}, t={now:F1})");
            saveUntil = saveGraceUntil = -1f;
            Complete();
        }

        static void Complete()
        {
            var lamp = MoonStructures.Definition(LampId);
            if (orphanMode) OrphanChecks();
            else
            {
                // Before BuildChecks, which may remove the lamp.
                if (DevTools.AutoSave.Value) SaveChecks(lamp);
                ReloadChecks(lamp);
                BuildChecks(lamp);
            }
            Done();
            FinishedAt = Time.unscaledTime;
        }

        static string SidecarPath() => Sidecar.PathFor(CoreState.SaveRoot);

        // Structure records with the given id in the sidecar on disk; -1 (and why) when it cannot be read.
        static int SidecarCount(string id, out string why)
        {
            why = null;
            string path = SidecarPath();
            if (path == null) { why = "CoreState.SaveRoot is unknown"; return -1; }
            if (!File.Exists(path)) { why = $"no {Sidecar.FileName} in the save root"; return -1; }
            try { return SidecarCodec.Deserialize(File.ReadAllText(path)).Structures.Count(s => s.Id == id); }
            catch (Exception e) { why = "reading it threw " + e.GetType().Name + ": " + e.Message; return -1; }
        }

        static Sidecar.WriteStats WriteAfterRequest(out string why)
        {
            why = null;
            if (!DevTools.SaveRequested) { why = "AutoSave never requested a save"; return null; }
            var w = Sidecar.LastWrite;
            if (w == null || w.Seq <= DevTools.SaveSeqAtRequest)
            {
                why = $"no MoonDepotSave.Write after the save request (writes: {Sidecar.WriteSeq}, at request {DevTools.SaveSeqAtRequest})";
                return null;
            }
            return w;
        }

        // [Dev] AutoSave with the lamp in the world: the Write prefix took it out of the vanilla save, the finalizer
        // put it back, the sidecar on disk has it, and the game accepted the save (no "Moon save failed").
        static void SaveChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            string skip = lamp == null ? "example lamp not registered" : world == null ? "no MoonWorld found" : null;
            Check("save-strips-modded", skip, () =>
            {
                var w = WriteAfterRequest(out string why);
                if (w == null) return (false, why);
                int lampIdx = w.StrippedKinds.IndexOf(lamp.Kind);
                if (lampIdx < 0) return (false, $"write #{w.Seq} stripped kinds [{string.Join(",", w.StrippedKinds)}], none is {lamp.Kind}");
                if (w.PutBack != w.Stripped) return (false, $"write #{w.Seq}: the finalizer put back {w.PutBack} of {w.Stripped}");
                int depot = w.StrippedIds[lampIdx];
                bool inWorld = world.depots != null && world.depots.ContainsKey(depot) && world.depots.get_Item(depot)?.kind == lamp.Kind;
                return (inWorld, $"depot {depot} is not in world.depots after the save");
            });
            Check("sidecar-written", skip ?? (SidecarPath() == null ? "CoreState.SaveRoot is unknown" : null), () =>
            {
                int n = SidecarCount(LampId, out string why);
                if (n < 0) return (false, why);
                CoreLog.Info($"[selftest] sidecar {CoreLog.Redact(SidecarPath())} holds {n} '{LampId}' record(s)");
                return (n > 0, $"{Sidecar.FileName} has no structure with Id '{LampId}'");
            });
            Check("vanilla-save-clean", skip, () =>
            {
                var w = WriteAfterRequest(out string why);
                if (w == null) return (false, why);
                int worldTotal = world.depots?.Count ?? 0, worldModded = 0;
                if (world.depots != null)
                    foreach (var kv in world.depots) if (kv.Value != null && kv.Value.kind >= KindAllocator.FirstModdedKind) worldModded++;
                string failedNote = "";
                try { failedNote = MoonWorld.saveFailed ?? ""; } catch (Exception) { }
                CoreLog.Info($"[selftest] save write #{w.Seq}: snapshot {w.Before} depots, {w.Modded} modded, {w.Stripped} stripped, {w.VanillaAfter} written to the vanilla save, {w.PutBack} put back ({(w.MainThread ? "main thread" : "worker thread")}); world {worldTotal} depots, {worldModded} modded; 'Moon save failed' seen {saveFailures}x; MoonWorld.saveFailed='{Trim(CoreLog.Redact(failedNote), 200)}'");
                if (w.Stripped != w.Modded) return (false, $"stripped {w.Stripped} of {w.Modded} modded records");
                if (w.VanillaAfter != worldTotal - worldModded) return (false, $"vanilla save got {w.VanillaAfter} depots, world has {worldTotal} - {worldModded} modded");
                if (saveFailures > 0) return (false, $"the game logged 'Moon save failed' {saveFailures}x: {Trim(CoreLog.Redact(firstSaveFailure ?? ""), 300)}");
                return (true, null);
            });
        }

        // A launch that did not build the lamp, after one that saved it: the sidecar brought it back into the world and
        // its view was spawned (Built with a view).
        static void ReloadChecks(StructureDefinition lamp)
        {
            if (lamp == null || DevTools.AutoBuildNames(LampId) || !Sidecar.LoadedIds().Contains(LampId)) return;
            var world = CoreState.World ?? CoreState.FindWorld();
            Check("reload-restores", world == null ? "no MoonWorld found" : null, () =>
            {
                int depot = DepotOfKind(world, lamp.Kind);
                if (depot < 0) return (false, $"the sidecar held '{LampId}' but world.depots ({world.depots?.Count}) has no record of kind {lamp.Kind}");
                if (!builtViews.Contains(depot)) return (false, $"MoonStructures.Built was not raised with a view for restored depot {depot}");
                CoreLog.Info($"[selftest] restored '{LampId}' is depot {depot}, Built fired with its view");
                return (true, null);
            });
        }

        // ExampleStructure removed: its lamp was logged as kept, the vanilla save loaded, and the sidecar on disk (rewritten
        // by the AutoSave write, if on) still holds the record.
        static void OrphanChecks()
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            Check("orphan-kept", null, () =>
            {
                if (world == null || world.depots == null || world.depots.Count == 0) return (false, "the vanilla save did not load (no world or no depots)");
                if (!Sidecar.Orphans.Contains(LampId)) return (false, $"'{LampId}' was not kept as an orphan at load");
                string rewritten = "not rewritten (AutoSave off)";
                if (DevTools.AutoSave.Value)
                {
                    var w = WriteAfterRequest(out string why);
                    if (w == null) return (false, why);
                    rewritten = $"rewritten by write #{w.Seq}";
                }
                int n = SidecarCount(LampId, out string err);
                if (n < 0) return (false, err);
                CoreLog.Info($"[selftest] orphan '{LampId}': sidecar {rewritten}, holds {n} record(s); world loaded with {world.depots.Count} depots");
                return (n > 0, $"the sidecar no longer holds '{LampId}'");
            });
        }

        // A modded kind answers BuildDefinition with its own clone, the build menu answers for it, and an unregistered
        // hashed kind still falls through to vanilla (null, no exception).
        static void DefinitionChecks(StructureDefinition lamp)
        {
            var world = CoreState.World ?? CoreState.FindWorld();
            var builder = UnityEngine.Object.FindFirstObjectByType<MoonDepotBuilder>();
            string noWorld = world == null ? "no MoonWorld found" : lamp == null ? "example lamp not registered" : null;
            string noBuilder = builder == null ? "no MoonDepotBuilder found" : lamp == null ? "example lamp not registered" : null;

            MoonDepotBuildDefinition def = null;
            Check("builddef-modded-nonnull", noWorld, () =>
            {
                def = world.BuildDefinition(lamp.Kind);
                return (def != null, $"BuildDefinition({lamp.Kind}) returned null");
            });
            Check("builddef-modded-distinct", noWorld, () =>
            {
                if (def == null) return (false, "no modded definition");
                var donor = world.BuildDefinition(4);
                return (donor == null || def.Pointer != donor.Pointer, "the modded definition is the donor's own object");
            });
            Check("builddef-modded-named", noWorld, () =>
            {
                if (def == null) return (false, "no modded definition");
                return (def.displayName == "Example lamp" && def.priceDollars == 50, $"displayName '{def.displayName}', priceDollars {def.priceDollars}");
            });
            Check("builddef-unknown-null", world == null ? "no MoonWorld found" : null, () =>
            {
                var unknown = world.BuildDefinition(1_000_000 + 12345);
                return (unknown == null, $"BuildDefinition(1012345) returned '{unknown?.displayName}'");
            });
            Check("canbuild-modded", noBuilder, () => (builder.CanBuild(lamp.Kind), $"CanBuild({lamp.Kind}) is false"));
            Check("note-modded", noBuilder, () =>
            {
                string note = builder.ProductionNote(lamp.Kind);
                return (note == "StructureLib example.", $"ProductionNote({lamp.Kind}) = '{note}'");
            });
        }

        // An assertion that fails with `skip` as detail when it is set, and with the exception when the probe throws.
        static void Check(string name, string skip, Func<(bool ok, string detail)> probe)
        {
            if (skip != null) { Assert(name, false, skip); return; }
            try { var (ok, detail) = probe(); Assert(name, ok, detail); }
            catch (Exception e) { Assert(name, false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        static bool watching;

        // The exception watch (disk log scan from here, Unity log hook). Started at the AutoBuild step, before any build
        // or save; Begin starts it if that step did not.
        internal static void StartWatch()
        {
            if (watching) return;
            watching = true;
            unityErrors = 0;
            firstUnityError = null;
            saveFailures = 0;
            firstSaveFailure = null;
            logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            FlushDiskLog();
            try { logStart = File.Exists(logPath) ? new FileInfo(logPath).Length : -1; }
            catch (Exception) { logStart = -1; }
            HookUnityLog();
            CoreLog.Info("[selftest] watch started (log scan and Unity log hook)");
        }

        internal static void Begin()
        {
            passed = failed = 0;
            StartWatch();
            CoreLog.Info("[selftest] begin");
        }

        internal static void Assert(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; CoreLog.Info($"[selftest] PASS {name}"); }
            else { failed++; CoreLog.Warn($"[selftest] FAIL {name}: {detail ?? "assertion false"}"); }
        }

        internal static void Done()
        {
            UnhookUnityLog();
            watching = false;
            ScanLog();
            CoreLog.Info($"[selftest] done: {passed} passed, {failed} failed");
        }

        // Any exception or error logged between begin and now fails the run: BepInEx lines from the disk log, plus Unity's
        // own Error/Exception messages (the disk log leaves those out when [Logging.Disk] WriteUnityLog is false).
        static void ScanLog()
        {
            var problems = new List<string>();
            if (logStart < 0) problems.Add($"could not read the log length at begin ({logPath})");
            else
            {
                try
                {
                    FlushDiskLog();
                    string text;
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(Math.Min(logStart, fs.Length), SeekOrigin.Begin);
                        using var reader = new StreamReader(fs, Encoding.UTF8);
                        text = reader.ReadToEnd();
                    }
                    var hits = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => Bad.IsMatch(l) && !l.Contains("[selftest]")).ToList();
                    if (hits.Count > 0) problems.Add($"{hits.Count} log-file line(s), first: {Trim(CoreLog.Redact(hits[0]), 300)}");
                }
                catch (Exception e) { problems.Add("reading the log failed: " + e.Message); }
            }
            if (!unityHooked) problems.Add("the Unity log hook was not installed");
            if (unityErrors > 0) problems.Add($"{unityErrors} Unity error/exception message(s), first: {Trim(CoreLog.Redact(firstUnityError), 300)}");
            Assert("no-vanilla-exceptions", problems.Count == 0, string.Join("; ", problems));
        }

        // Unity log hook. The converted delegate stays in a static field so the GC does not collect it while hooked.
        static int unityErrors, saveFailures;
        static string firstUnityError, firstSaveFailure;
        static bool unityHooked;
        static Application.LogCallback unityHook;

        static void HookUnityLog()
        {
            unityHooked = false;
            try
            {
                unityHook ??= Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Application.LogCallback>(
                    new Action<string, string, LogType>(OnUnityLog));
                Application.add_logMessageReceived(unityHook);
                unityHooked = true;
            }
            catch (Exception e) { CoreLog.Warn("[dev] SelfTest: hooking Application.logMessageReceived failed: " + e.Message); }
        }

        static void UnhookUnityLog()
        {
            if (!unityHooked) return;
            try { Application.remove_logMessageReceived(unityHook); }
            catch (Exception e) { CoreLog.Warn("[dev] SelfTest: unhooking Application.logMessageReceived failed: " + e.Message); }
        }

        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (condition != null && condition.StartsWith("[selftest]", StringComparison.Ordinal)) return;
            if (condition != null && condition.Contains("Moon save failed")) { saveFailures++; firstSaveFailure ??= condition; }
            unityErrors++;
            firstUnityError ??= $"{type}: {condition}";
        }

        static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        // The disk log is buffered (InstantFlushing=false); flush so the file holds everything logged so far.
        static void FlushDiskLog()
        {
            foreach (var l in BepInEx.Logging.Logger.Listeners.OfType<DiskLogListener>())
                try { l.LogWriter?.Flush(); } catch (Exception) { }
        }
    }
}
#endif
