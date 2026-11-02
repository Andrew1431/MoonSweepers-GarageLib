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
        internal static ConfigEntry<bool> AllowSlot1, RunSelfTest;
        internal static ConfigEntry<string> AutoBuild, AutoFeed;
        static float menuT, gameT;
        static bool loadRequested, selfTestRan;

        internal static void Bind(ConfigFile cfg)
        {
            AutoLoadSlot = cfg.Bind("Dev", "AutoLoadSlot", -1, "Dev build: load the existing save in folder slot-<n> from the main menu. -1 = off. Slot 1 is the real save and is refused unless AllowSlot1 is true.");
            AllowSlot1 = cfg.Bind("Dev", "AllowSlot1", false, "Dev build: let AutoLoadSlot and SelfTest touch slot-1 (the real save). Leave false.");
            RunSelfTest = cfg.Bind("Dev", "SelfTest", false, "Dev build: run the in-game self-test 20 s after a world loads and log [selftest] lines.");
            AutoBuild = cfg.Bind("Dev", "AutoBuild", "", "Dev build: comma-separated structure ids or vanilla kind numbers to build once in game. (Not used yet.)");
            AutoFeed = cfg.Bind("Dev", "AutoFeed", "", "Dev build: '<structureId>:<cargoId|Dust|Sand|Stone>:<kg>' to load into a built structure. (Not used yet.)");
            CoreLog.Info($"[dev] AutoLoadSlot={AutoLoadSlot.Value} AllowSlot1={AllowSlot1.Value} SelfTest={RunSelfTest.Value} AutoBuild='{AutoBuild.Value}' AutoFeed='{AutoFeed.Value}'");
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
            if (RunSelfTest.Value && !selfTestRan && gameT > 20f)
            {
                selfTestRan = true;
                string refusal = SlotRefusal();
                if (refusal != null) CoreLog.Warn("[dev] SelfTest skipped: " + refusal);
                else SelfTest.Run();
            }
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
            int expected = KindAllocator.KindOf("ryanhartwig.example-lamp");
            Assert("example-kind-hashed", lamp != null && lamp.Kind == expected,
                lamp == null ? "'ryanhartwig.example-lamp' is not registered (is ExampleStructure installed?)" : $"kind {lamp.Kind}, expected {expected}");
            Done();
        }

        internal static void Begin()
        {
            passed = failed = 0;
            unityErrors = 0;
            firstUnityError = null;
            logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            FlushDiskLog();
            try { logStart = File.Exists(logPath) ? new FileInfo(logPath).Length : -1; }
            catch (Exception) { logStart = -1; }
            HookUnityLog();
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
        static int unityErrors;
        static string firstUnityError;
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
