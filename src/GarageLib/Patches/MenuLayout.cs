using System.Collections.Generic;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;
using UnityEngine.UI;

namespace GarageLib.Patches
{
    // The garage menu fits every vehicle into two shelves (vehicles page) and one strip (upgrades page) by shrinking
    // the cards, which stops working past a few modded vehicles. Once there are more vehicles than vanilla can show at
    // a sensible size, cards keep a fixed size and the shelves / strip scroll instead.
    [HarmonyPatch]
    internal static class MenuLayout
    {
        const int BayColumns = 6, StripColumns = 7;
        const int VanillaBayMax = 6;            // vanilla shows up to 3 per shelf at full size
        const float ShelfGap = 4f, ShelfH = 16f;
        const string ScrollName = "GarageLib Scroll";

        static int Offers => MoonWorld.GarageOffers?.Length ?? 0;
        static bool BaysScroll => MoonVehicles.All.Count > 0 && Offers > VanillaBayMax;
        static bool StripScrolls => MoonVehicles.All.Count > 0 && Offers > StripColumns;

        static ScrollRect bays, strip;

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.BuildBays)), HarmonyPostfix]
        static void BuildBays(MoonGarageMenu __instance)
        {
            if (!BaysScroll) return;
            try
            {
                var page = __instance.vehiclesPage;
                var rects = new List<RectTransform>();
                for (int i = 0; i < __instance.cards.Count; i++) { var c = __instance.cards.get_Item(i); if (c?.rect != null) rects.Add(c.rect); }
                if (page == null || rects.Count == 0) return;

                RectTransform shelf = null;
                var vanillaShelves = new List<GameObject>();
                for (int i = 0; i < page.childCount; i++)
                {
                    var ch = page.GetChild(i);
                    if (ch.name == "Shelf board") { vanillaShelves.Add(ch.gameObject); shelf ??= ch.TryCast<RectTransform>(); }
                }

                var content = MakeScroll(page, new Vector2(0, 0), page.sizeDelta, vertical: true, out bays);
                float gap = MoonGarageMenu.Gap, inner = MoonGarageMenu.Inner;
                float width = Width(BayColumns);
                foreach (var r in rects) Resize(r, width, width - MoonGarageMenu.Inner);
                Vector2 card = rects[0].sizeDelta;
                int cols = Mathf.Max(1, Mathf.FloorToInt((page.sizeDelta.x - gap) / (card.x + gap)));
                float pitch = card.y + ShelfGap + ShelfH + ShelfGap;
                int rows = (rects.Count + cols - 1) / cols;
                for (int i = 0; i < rects.Count; i++)
                {
                    rects[i].SetParent(content, false);
                    rects[i].anchoredPosition = new Vector2(gap + (i % cols) * (card.x + gap), -(inner + (i / cols) * pitch));
                }
                if (shelf != null)
                    for (int r = 0; r < rows; r++)
                    {
                        var board = Object.Instantiate(shelf.gameObject, content, false).GetComponent<RectTransform>();
                        board.name = "Shelf board";
                        board.anchoredPosition = new Vector2(shelf.anchoredPosition.x, -(inner + r * pitch + card.y + ShelfGap));
                    }
                foreach (var go in vanillaShelves) Object.Destroy(go);
                content.sizeDelta = new Vector2(page.sizeDelta.x, inner + rows * pitch - ShelfGap + inner);
                CoreLog.Info($"Garage menu: {rects.Count} vehicles in a scrolling {cols}-column list ({rows} shelves, {content.sizeDelta.y:F0} of {page.sizeDelta.y:F0} px visible height)");
            }
            catch (System.Exception e) { CoreLog.Once("bays-layout", "Garage menu list layout failed: " + e, 2); }
        }

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.BuildUpgradesPage)), HarmonyPostfix]
        static void BuildUpgradesPage(MoonGarageMenu __instance)
        {
            if (!StripScrolls) return;
            try
            {
                var page = __instance.upgradesPage;
                var rects = new List<RectTransform>();
                for (int i = 0; i < __instance.strip.Count; i++) { var c = __instance.strip.get_Item(i); if (c?.rect != null) rects.Add(c.rect); }
                if (page == null || rects.Count == 0) return;

                float gap = MoonGarageMenu.Gap, inner = MoonGarageMenu.Inner;
                float width = Width(StripColumns);
                foreach (var r in rects) Resize(r, width, -1f);
                Vector2 card = rects[0].sizeDelta;
                float height = inner + card.y + ShelfGap;   // down to the strip's shelf board, which stays put
                var content = MakeScroll(page, Vector2.zero, new Vector2(page.sizeDelta.x, height), vertical: false, out strip);
                for (int i = 0; i < rects.Count; i++)
                {
                    rects[i].SetParent(content, false);
                    rects[i].anchoredPosition = new Vector2(gap + i * (card.x + gap), -inner);
                }
                content.sizeDelta = new Vector2(gap + rects.Count * (card.x + gap), height);
                CoreLog.Info($"Garage menu: upgrades strip scrolls sideways ({rects.Count} vehicles, {content.sizeDelta.x:F0} of {page.sizeDelta.x:F0} px)");
            }
            catch (System.Exception e) { CoreLog.Once("strip-layout", "Garage menu strip layout failed: " + e, 2); }
        }

        // Keyboard / gamepad selection and clicks on half-hidden cards bring the card fully into view.
        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.Pick)), HarmonyPostfix]
        static void Pick(MoonGarageMenu.Card card)
        {
            if (card?.rect != null) Reveal(bays, card.rect);
        }

        [HarmonyPatch(typeof(MoonGarageMenu), nameof(MoonGarageMenu.PickModel)), HarmonyPostfix]
        static void PickModel(MoonGarageMenu __instance, int vehicle)
        {
            var s = __instance.strip;
            if (s == null) return;
            for (int i = 0; i < s.Count; i++) { var c = s.get_Item(i); if (c != null && c.vehicle == vehicle && c.rect != null) Reveal(strip, c.rect); }
        }

        static float Width(int columns) => (MoonGarageMenu.BayW - MoonGarageMenu.Gap * (columns + 1)) / columns;

        // The builders squeeze cards to fit the page and the methods that size them are inlined, so cards are resized
        // after the fact, matching the builder's layout: picture ("Lift") and name label centred, name text inset
        // 10 px each side, the price / state chips anchored to the right edge (left alone). The picture keeps its
        // bottom edge (it overlaps the name label by design). picture < 0 keeps the picture size.
        static void Resize(RectTransform card, float width, float picture)
        {
            card.sizeDelta = new Vector2(width, card.sizeDelta.y);
            for (int i = 0; i < card.childCount; i++)
            {
                var ch = card.GetChild(i).TryCast<RectTransform>();
                if (ch == null || ch.anchorMin.x > 0.5f) continue;
                if (ch.name == "Lift")
                {
                    float y = ch.anchoredPosition.y;
                    if (picture > 0)
                    {
                        y = y - ch.sizeDelta.y / 2f + picture / 2f;
                        ch.sizeDelta = new Vector2(picture, picture);
                        var pic = ch.childCount > 0 ? ch.GetChild(0).TryCast<RectTransform>() : null;
                        if (pic != null) pic.sizeDelta = new Vector2(picture, picture);
                    }
                    ch.anchoredPosition = new Vector2(width / 2f, y);
                }
                else if (ch.name == "Label")
                {
                    ch.anchoredPosition = new Vector2(width / 2f, ch.anchoredPosition.y);
                    ch.sizeDelta = new Vector2(width, ch.sizeDelta.y);
                    var text = ch.childCount > 0 ? ch.GetChild(0).TryCast<RectTransform>() : null;
                    if (text != null) text.sizeDelta = new Vector2(width - 20f, text.sizeDelta.y);
                }
            }
        }

        static RectTransform MakeScroll(RectTransform page, Vector2 pos, Vector2 size, bool vertical, out ScrollRect scroll)
        {
            var old = page.Find(ScrollName);
            if (old != null) { old.name = ScrollName + " (old)"; Object.Destroy(old.gameObject); }

            var view = new GameObject(ScrollName).AddComponent<RectTransform>();
            view.SetParent(page, false);
            view.SetAsFirstSibling();
            TopLeft(view, pos, size);
            view.gameObject.AddComponent<RectMask2D>();
            // Transparent so the gaps between cards still catch the mouse wheel.
            var catcher = view.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);

            var content = new GameObject("Content").AddComponent<RectTransform>();
            content.SetParent(view, false);
            TopLeft(content, Vector2.zero, size);

            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.vertical = vertical;
            scroll.horizontal = !vertical;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 60f;
            return content;
        }

        static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // Scroll just enough that `card` is inside the viewport (content is top-left anchored, so y grows downwards).
        static void Reveal(ScrollRect scroll, RectTransform card)
        {
            if (scroll == null || scroll.content == null || card.parent != scroll.content) return;
            var content = scroll.content;
            Vector2 view = scroll.viewport.rect.size;
            Vector2 at = content.anchoredPosition;
            float pad = MoonGarageMenu.Inner;
            if (scroll.vertical)
            {
                float top = -card.anchoredPosition.y - pad, bottom = -card.anchoredPosition.y + card.sizeDelta.y + ShelfGap + ShelfH + pad;
                if (top < at.y) at.y = top;
                else if (bottom > at.y + view.y) at.y = bottom - view.y;
                at.y = Mathf.Clamp(at.y, 0, Mathf.Max(0, content.sizeDelta.y - view.y));
            }
            else
            {
                float left = card.anchoredPosition.x - pad, right = card.anchoredPosition.x + card.sizeDelta.x + pad;
                if (left < -at.x) at.x = -left;
                else if (right > -at.x + view.x) at.x = -(right - view.x);
                at.x = Mathf.Clamp(at.x, -Mathf.Max(0, content.sizeDelta.x - view.x), 0);
            }
            content.anchoredPosition = at;
        }
    }
}
