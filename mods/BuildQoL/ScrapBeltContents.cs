using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BuildQoL.Pure;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BuildQoL
{
    // ScrapBeltContents: removing a belt while holding the scrap modifier destroys the canisters riding on it instead of
    // letting them drop.
    //
    // Measured on the 2026-11 build (README, "Hazards"). The builder's UpdateRemoval calls CanRemoveConveyor on the aimed
    // belt every frame and, on the confirm click, TryRemoveConveyor (its only native caller) synchronously in the same
    // Update. TryRemoveConveyor touches no canister: the drop happens frames later in StepConveyors, which lowers every rider
    // whose belt is gone (LowerToGround). Deleting the riders in a prefix, before the original removes the belt and saves,
    // leaves StepConveyors nothing to drop and the save the original writes already without them.
    //
    // Deleting a canister is the game's own consume sequence (HandOff, FeedCarriedCapsule): RemoveCanister(id), then raise
    // Removed (RaiseMachineCanister on the host or solo, RaiseLocalCanister on a joined client: canister ops are
    // peer-broadcast and every receiver applies Removed), then progressDirty.
    //
    //   MoonWorld.TryRemoveConveyor(MoonWalker, int, out string)   576 B, normal prologue: prefix, never skips the original
    //   MoonWorld.CanRemoveConveyor(MoonWalker, int, out string)  1168 B, normal prologue: postfix (records the aimed belt)
    internal sealed class ScrapBeltContentsTweak : Tweak
    {
        public override string Name => "ScrapBeltContents";
        protected override string Toggle =>
            "Hold ScrapModifier while confirming a belt removal to destroy every canister riding on that belt instead of dropping it. " +
            "Removing a belt without the key is unchanged. Destroyed cargo is gone for good. False: base-game behaviour, nothing is patched.";
        public override string[] Needed => new[]
        {
            "MoonCleaners.MoonWorld:TryRemoveConveyor", "MoonCleaners.MoonWorld:CanRemoveConveyor", "MoonCleaners.MoonWorld:RemoveCanister",
            "MoonCleaners.MoonWorld:RaiseMachineCanister", "MoonCleaners.MoonWorld:RaiseLocalCanister", "MoonCleaners.MoonWorld:riders",
            "MoonCleaners.MoonWorld:canisters", "MoonCleaners.MoonWorld:progressDirty", "MoonCleaners.MoonWorld:LocalPlayer",
            "MoonCleaners.MoonWorld+CanisterOp:Removed",
            "MoonCleaners.MoonCrew:Joined", "MoonCleaners.MoonCrew:LocalPlayerId", "MoonCleaners.MoonCrew:PlayerId",
            "MoonCleaners.MoonDepotBuilder:IsRemoving", "MoonCleaners.MoonDepotBuilder:prompt", "MoonCleaners.MoonDepotBuilder:walker",
            "MoonCleaners.MoonDepotBuilder:World", "MoonCleaners.MoonDepotBuilder:removeKeys", "MoonCleaners.MoonDepotBuilder:ForCamera",
            "MoonCleaners.MoonDepotBuilder:RemoveRed",
            "MoonCleaners.MoonSteamLobby:members", "MoonCleaners.MoonSteamLobby:PlayerName",
        };
        public override Type[] Patches => new[] { typeof(ScrapBeltPatches) };

        internal static ConfigEntry<Key> Modifier;
        internal static ConfigEntry<bool> OthersCargo;

        protected override void BindMore(ConfigFile cfg)
        {
            Modifier = cfg.Bind("Tweaks", "ScrapModifier", Key.LeftCtrl,
                "Key (Unity Input System name, e.g. LeftCtrl, LeftAlt, X) held while confirming a belt removal to scrap the canisters on it. " +
                "For Ctrl, Shift and Alt either side counts. None: never scrap.");
            OthersCargo = cfg.Bind("Tweaks", "ScrapOthersCargo", true,
                "True: the scrap destroys every canister on the belt. False: only canisters you own; other players' canisters drop as in the base game.");
        }

        public override void LateUpdate() => ScrapPrompt.LateUpdate();

#if DEVBUILD
        public override void DevBind(ConfigFile cfg) => ScrapProbe.Bind(cfg);
        public override void DevPatch(Harmony h) => ScrapProbe.Patch(h);
        public override void DevTick(float gameT) => ScrapProbe.Tick(gameT);
#endif
    }

    internal static class Scrap
    {
        static Key otherSideFor = Key.None, otherSide = Key.None;

        /// <summary>True while the configured modifier (or, for Ctrl/Shift/Alt, its other side) is held, read from the keyboard.</summary>
        internal static bool Held()
        {
#if DEVBUILD
            if (ScrapProbe.ForceHeld) return true;
#endif
            var key = ScrapBeltContentsTweak.Modifier.Value;
            if (key == Key.None) return false;
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return false;
                if (Pressed(kb, key)) return true;
                if (otherSideFor != key)
                {
                    otherSideFor = key;
                    string other = ScrapText.OtherSide(key.ToString());
                    otherSide = other != null && Enum.TryParse(other, out Key k) ? k : Key.None;
                }
                return otherSide != Key.None && Pressed(kb, otherSide);
            }
            catch (Exception e) { Plugin.Once("scrap-key", $"ScrapBeltContents: reading key {key} threw (no scrapping): " + e); return false; }
        }

        static bool Pressed(Keyboard kb, Key key)
        {
            var control = kb.get_Item(key);
            return control != null && control.isPressed;
        }

        /// <summary>MoonCrew.LocalPlayerId: the id a joined client raises its canister ops under.</summary>
        internal static string CrewId()
        {
            try { return MoonCrew.LocalPlayerId; }
            catch (Exception e) { Plugin.Once("scrap-crew-id", "ScrapBeltContents: reading MoonCrew.LocalPlayerId threw: " + e); return null; }
        }

        /// <summary>MoonWorld.LocalPlayer: the owner id the game and the other mods give the local player's canisters.</summary>
        internal static string WorldId()
        {
            try { return MoonWorld.LocalPlayer; }
            catch (Exception e) { Plugin.Once("scrap-world-id", "ScrapBeltContents: reading MoonWorld.LocalPlayer threw: " + e); return null; }
        }

        /// <summary>The canisters riding <paramref name="belt"/>: id, owner, kg. Read once into a list; never mutates the world.</summary>
        internal static List<(int id, string owner, double kg)> Riders(MoonWorld w, int belt)
        {
            var r = new List<(int, string, double)>();
            var riders = w.riders;
            var canisters = w.canisters;
            if (riders == null || canisters == null) return r;
            foreach (var kv in riders)
            {
                if (kv.Value == null || kv.Value.belt != belt) continue;
                int id = kv.Key;
                if (!canisters.ContainsKey(id)) continue;
                var c = canisters.get_Item(id);
                r.Add((id, c.owner, c.kilograms));
            }
            return r;
        }

        static readonly Dictionary<string, string> names = new();

        /// <summary>A crew member's display name for a canister owner id, or null.</summary>
        internal static string NameOf(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return null;
            if (names.TryGetValue(owner, out var cached)) return cached;
            string name = null;
            try
            {
                var members = MoonSteamLobby.members;
                if (members != null)
                    foreach (var m in members)
                    {
                        if (m == null) continue;
                        if (MoonCrew.PlayerId(m.id) != owner) continue;
                        name = !string.IsNullOrWhiteSpace(m.name) ? m.name : MoonSteamLobby.PlayerName(m.id);
                        break;
                    }
            }
            catch (Exception e) { Plugin.Once("scrap-name", "ScrapBeltContents: looking up a crew name threw (shown as others'): " + e); }
            if (name != null) names[owner] = name; // unknown owners are retried: the lobby list may fill in later
            return name;
        }
    }

    [HarmonyPatch]
    internal static class ScrapBeltPatches
    {
        // The last CanRemoveConveyor answer: UpdateRemoval asks it every frame for the aimed belt.
        internal static int aimFrame = -1, aimBelt = -1;
        internal static bool aimOk;
        internal static IntPtr aimWalker;

        [HarmonyPostfix, HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CanRemoveConveyor))]
        static void CanRemovePostfix(MoonWalker builder, int id, bool __result)
        {
            try
            {
                aimFrame = Time.frameCount;
                aimBelt = id;
                aimOk = __result;
                aimWalker = builder != null ? builder.Pointer : IntPtr.Zero;
            }
            catch (Exception e) { Plugin.Once("scrap-can", "ScrapBeltContents: CanRemoveConveyor postfix threw: " + e); }
        }

        // Always returns true: the original always runs and removes the belt as usual.
        [HarmonyPrefix, HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryRemoveConveyor))]
        static bool TryRemovePrefix(MoonWorld __instance, MoonWalker builder, int id)
        {
            try { if (Scrap.Held()) ScrapRiders(__instance, builder, id); }
            catch (Exception e) { Plugin.Once("scrap-prefix", "ScrapBeltContents: scrapping threw; the rest of the belt's canisters drop as usual: " + e); }
            return true;
        }

        internal static int lastCount, lastBelt = -1;
        internal static double lastKg;

        static void ScrapRiders(MoonWorld w, MoonWalker builder, int id)
        {
            if (w == null) return;
            if (!w.CanRemoveConveyor(builder, id, out _)) return; // the original refuses too: nothing is scrapped
            bool others = ScrapBeltContentsTweak.OthersCargo.Value;
            string worldId = Scrap.WorldId(), crewId = Scrap.CrewId();
            var ids = new List<int>();
            foreach (var (rid, owner, _) in Scrap.Riders(w, id))
                if (others || ScrapText.IsMine(owner, worldId, crewId)) ids.Add(rid);
            if (ids.Count == 0) return;

            bool joined = MoonCrew.Joined;
            // A client raises Removed under its own player id; without one the delete would reach nobody else.
            if (joined && string.IsNullOrEmpty(crewId)) { Plugin.Once("scrap-noid", "ScrapBeltContents: joined with no LocalPlayerId; not scrapping, the canisters drop as usual"); return; }
            int n = 0;
            double kg = 0;
            try
            {
                foreach (int cid in ids)
                {
                    var canisters = w.canisters;
                    if (canisters == null || !canisters.ContainsKey(cid)) continue;
                    var c = canisters.get_Item(cid);
                    w.RemoveCanister(cid);
                    // Gone locally from here on, whether or not the others hear of it.
                    n++;
                    kg += c.kilograms;
                    try
                    {
                        if (joined) w.RaiseLocalCanister(MoonWorld.CanisterOp.Removed, crewId, c);
                        else w.RaiseMachineCanister(MoonWorld.CanisterOp.Removed, c);
                    }
                    catch (Exception e) { Plugin.Once("scrap-raise", $"ScrapBeltContents: announcing the removal of canister {cid} threw (removed here only): " + e); }
                }
            }
            finally
            {
                if (n > 0)
                {
                    w.progressDirty = true;
                    lastCount = n; lastKg = kg; lastBelt = id;
                    Plugin.L.LogInfo($"{ScrapText.Log(n, kg, id, joined)} (MoonWorld.LocalPlayer '{worldId}', MoonCrew.LocalPlayerId '{crewId}')");
                }
            }
        }
    }

    // While the builder is in removal mode and aims at a belt that can be removed, shows one line under the removal key
    // row. In removal mode the game hides its `prompt` text and shows the `removeKeys` row instead (measured in game), so
    // the line is a label of this mod's own: a copy of `prompt` (same font and size), parented beside `removeKeys` and
    // placed just below it. The game's own UI objects are never changed.
    internal static class ScrapPrompt
    {
        static MoonDepotBuilder builder;
        static int nextLookup;
        static TMPro.TMP_Text label;
        static string shown;

        // The line is rebuilt when the aimed belt, the rider count or the key state changes, and every 10 frames otherwise.
        static int cacheBelt = -1, cacheRiders = -1, cacheFrame = -100;
        static bool cacheHeld;
        static string cacheLine;

        /// <summary>The line on show now, or null.</summary>
        internal static string Shown => shown;
        internal static TMPro.TMP_Text Label => label;

        internal static void LateUpdate()
        {
            var b = Builder();
            if (b == null) { Hide(); return; }
            string line = null;
            if (b.IsRemoving && ScrapBeltPatches.aimFrame == Time.frameCount && ScrapBeltPatches.aimOk)
            {
                var walker = b.walker;
                if (walker == null || walker.Pointer == ScrapBeltPatches.aimWalker) line = CachedLine(b.World, ScrapBeltPatches.aimBelt);
            }
            if (line == null) { Hide(); return; }
            var l = Ensure(b);
            if (l == null) { shown = null; return; }
            if (l.text != line) l.text = line;
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
            shown = line;
        }

        static void Hide()
        {
            if (label != null && label.gameObject.activeSelf) label.gameObject.SetActive(false);
            shown = null;
        }

        static TMPro.TMP_Text Ensure(MoonDepotBuilder b)
        {
            if (label != null) return label;
            var prompt = b.prompt;
            var keys = b.removeKeys;
            if (prompt == null || keys == null || keys.parent == null) return null;
            var go = UnityEngine.Object.Instantiate(prompt.gameObject, keys.parent, false);
            go.name = "BuildQoL scrap hint";
            label = go.GetComponent<TMPro.TMP_Text>();
            if (label == null) { UnityEngine.Object.Destroy(go); return null; }
            label.richText = false;
            label.color = Red();
            Place(label, keys);
            go.SetActive(false);
            return label;
        }

        // Same anchors and pivot as the key row, one line below it, at least as wide.
        static void Place(TMPro.TMP_Text l, RectTransform keys)
        {
            var rt = l.rectTransform;
            rt.anchorMin = keys.anchorMin;
            rt.anchorMax = keys.anchorMax;
            rt.pivot = keys.pivot;
            float h = Mathf.Max(l.fontSize * 1.4f, 20f);
            rt.sizeDelta = new Vector2(Mathf.Max(keys.rect.width, 640f), h);
            rt.anchoredPosition = keys.anchoredPosition + new Vector2(0f, -(keys.rect.height * keys.pivot.y + h * (1f - rt.pivot.y) + 6f));
        }

        static string CachedLine(MoonWorld w, int belt)
        {
            if (w == null) return null;
            int riders = w.riders != null ? w.riders.Count : -1;
            bool held = Scrap.Held();
            int frame = Time.frameCount;
            if (belt == cacheBelt && riders == cacheRiders && held == cacheHeld && frame - cacheFrame < 10) return cacheLine;
            cacheBelt = belt; cacheRiders = riders; cacheHeld = held; cacheFrame = frame;
            cacheLine = Line(w, belt, held);
            return cacheLine;
        }

        static string Line(MoonWorld w, int belt, bool held)
        {
            if (ScrapBeltContentsTweak.Modifier.Value == Key.None) return null;
            bool othersScrapped = ScrapBeltContentsTweak.OthersCargo.Value;
            var riders = new List<KeyValuePair<string, double>>();
            foreach (var (_, owner, k) in Scrap.Riders(w, belt)) riders.Add(new KeyValuePair<string, double>(owner, k));
            var t = ScrapText.Count(riders, Scrap.WorldId(), Scrap.CrewId(), othersScrapped);
            var others = new List<KeyValuePair<string, int>>();
            foreach (var o in t.Others) others.Add(new KeyValuePair<string, int>(Scrap.NameOf(o.Key), o.Value));
            return ScrapText.Prompt(held, ScrapText.KeyLabel(ScrapBeltContentsTweak.Modifier.Value.ToString()), t.Count, t.Kg, others, othersScrapped);
        }

        static Color Red()
        {
            try { return MoonDepotBuilder.RemoveRed; } catch { return new Color(1f, 0.35f, 0.3f); }
        }

        static MoonDepotBuilder Builder()
        {
            if (builder != null) return builder;
            // The builder is gone (or never found): drop a label left from an old one.
            if (label != null) { try { UnityEngine.Object.Destroy(label.gameObject); } catch (Exception e) { Plugin.Once("scrap-label", "ScrapBeltContents: destroying the old label threw: " + e); } }
            label = null;
            shown = null;
            cacheBelt = -1;
            if (Time.frameCount < nextLookup) return null; // in menus there is no builder: look again every 30 frames
            nextLookup = Time.frameCount + 30;
            var cam = Camera.main;
            if (cam == null) return null;
            builder = MoonDepotBuilder.ForCamera(cam);
            return builder;
        }
    }
}
