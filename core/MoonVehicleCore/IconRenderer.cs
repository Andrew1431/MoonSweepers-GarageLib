using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MoonVehicleCore
{
    // Garage card pictures for modded vehicles. Vanilla cards don't know modded indices, so the core renders one: the
    // vehicle's own model (as the game builds it) on a stage far above the world, shot orthographically from the
    // front-left and above like the vanilla cards, onto a transparent background. A PNG from the definition wins.
    // Under URP a manual Camera.Render may draw nothing, so the camera is enabled for one frame and read the next.
    internal static class IconRenderer
    {
        const int Size = 256;
        const int Layer = 0;
        // The moon's centre: 2 km under the surface shell, far outside every other camera's view and the icon camera's
        // clip range sees nothing but the model.
        static readonly Vector3 Stage = Vector3.zero;
        // Vehicle-local direction from the model towards the camera: in front, to the left, ~30° above.
        static readonly Vector3 ViewDir = new Vector3(-0.62f, 0.55f, 0.78f).normalized;

        static readonly Dictionary<int, Sprite> sprites = new();
        static readonly Queue<VehicleDefinition> queue = new();
        static Job job;
        static bool queued, gaveUp;
        static float waited;

        sealed class Job
        {
            public VehicleDefinition Def;
            public GameObject Holder;
            public Camera Cam;
            public Light Light;
            public RenderTexture Rt;
            public int Frames;
            public bool Reframed;
        }

        internal static Sprite Get(VehicleDefinition d) => sprites.TryGetValue(d.Index, out var s) && s != null && !s.WasCollected ? s : null;

        // Called when a session enters the game (before anyone can open a garage menu).
        internal static void QueueAll()
        {
            if (queued) return;
            queued = true;
            foreach (var d in MoonVehicles.All)
            {
                if (d.CardIcon != null) continue;
                if (d.CardIconPng != null && FromPng(d)) continue;
                queue.Enqueue(d);
            }
            if (queue.Count > 0) CoreLog.Info($"Card icons: rendering {queue.Count} vehicle picture(s)");
        }

        internal static void Tick()
        {
            try
            {
                if (job == null)
                {
                    if (queue.Count == 0) return;
                    // The player's equipment owns the prototype materials; a fresh MoonPrototypeModels has none and
                    // renders the model invisible. Wait for it (it appears a little after the session starts).
                    var models = FindModels();
                    if (models == null) { waited += Time.unscaledDeltaTime; if (waited > 60f && !gaveUp) { gaveUp = true; CoreLog.Warn("Card icons: no player equipment after 60 s, still waiting"); } return; }
                    Start(queue.Dequeue(), models);
                    return;
                }
                job.Frames++;
                Unhide(job.Holder);
                if (job.Frames >= 2) Finish();
            }
            catch (Exception e)
            {
                CoreLog.Error($"Card icon for {job?.Def} failed: {e}");
                Cleanup();
            }
        }

        static bool FromPng(VehicleDefinition d)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, d.CardIconPng)) throw new InvalidDataException("not a PNG");
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprites[d.Index] = MakeSprite(tex, d);
                CoreLog.Info($"Card icon for '{d.Id}' loaded from its PNG ({tex.width}x{tex.height})");
                return true;
            }
            catch (Exception e) { CoreLog.Warn($"Card icon PNG for '{d.Id}' unusable, rendering instead: {e.Message}"); return false; }
        }

        static void Start(VehicleDefinition d, MoonPrototypeModels models)
        {
            job = new Job { Def = d };
            job.Holder = new GameObject("MoonVehicleCore Icon Stage") { layer = Layer };
            job.Holder.transform.position = Stage;
            var model = models.Vehicle(d.Index, job.Holder.transform);
            if (model == null) { CoreLog.Warn($"Card icon for '{d.Id}': model build returned null"); Cleanup(); return; }
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var b in model.GetComponentsInChildren<Behaviour>(true))
                if (b != null && !(b is Light)) b.enabled = false;
            foreach (var l in model.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            // The game hands models back inactive until a vehicle spawns them; behaviours are off, so waking it is safe.
            model.SetActive(true);

            bool any = false;
            var bounds = new Bounds(Stage, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any) { CoreLog.Warn($"Card icon for '{d.Id}': model has no visible renderers"); Cleanup(); return; }

            job.Rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = "MoonVehicleCore Icon " + d.Id };
            job.Rt.Create();
            var camGo = new GameObject("MoonVehicleCore Icon Camera");
            var cam = job.Cam = camGo.AddComponent<Camera>();
            cam.cullingMask = ~0; // nothing else is within reach of the moon's centre
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 1f / 255f); // invisible, but shows the camera ran
            cam.orthographic = true;
            cam.allowHDR = false;
            cam.useOcclusionCulling = false;
            cam.targetTexture = job.Rt;
            cam.depth = -100f;

            var t0 = model.transform;
            Vector3 dir = t0.TransformDirection(ViewDir);
            float radius = bounds.extents.magnitude;
            cam.transform.position = bounds.center + dir * (radius * 4f);
            cam.transform.LookAt(bounds.center, t0.up);
            // Fit: project the bounds' corners onto the camera plane.
            float half = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(i % 2 == 0 ? bounds.min.x : bounds.max.x, (i / 2) % 2 == 0 ? bounds.min.y : bounds.max.y, i / 4 == 0 ? bounds.min.z : bounds.max.z);
                var lp = cam.transform.InverseTransformPoint(c);
                half = Mathf.Max(half, Mathf.Abs(lp.x), Mathf.Abs(lp.y));
            }
            cam.orthographicSize = half * 1.05f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = radius * 8f;

            // Key light from above-front so the card doesn't depend on the scene's time of day.
            var lightGo = new GameObject("MoonVehicleCore Icon Light");
            job.Light = lightGo.AddComponent<Light>();
            job.Light.type = LightType.Directional;
            job.Light.intensity = 1.2f;
            job.Light.shadows = LightShadows.None;
            job.Light.cullingMask = 1 << Layer;
            lightGo.transform.rotation = Quaternion.LookRotation(-t0.TransformDirection(new Vector3(-0.3f, 1f, 0.6f).normalized), t0.forward);
            CoreLog.Debug($"Card icon for '{d.Id}': main cam mask {(Camera.main != null ? Camera.main.cullingMask.ToString("X8") : "none")}, bounds {bounds.size}, ortho {cam.orthographicSize:F2}");
        }

        static void Finish()
        {
            var d = job.Def;
            if (CoreLog.Verbose && job.Holder != null)
            {
                int n = 0, vis = 0, off = 0, noMat = 0;
                string sample = "";
                foreach (var r in job.Holder.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled) continue;
                    n++; if (r.isVisible) vis++; if (r.forceRenderingOff) off++;
                    var m = r.sharedMaterial;
                    if (m == null) noMat++;
                    else if (sample.Length < 300) sample += $"{r.name}:{m.name} shadow={r.shadowCastingMode} rl={r.renderingLayerMask:X} active={r.gameObject.activeInHierarchy} ";
                }
                CoreLog.Debug($"Card icon stage: {n} enabled renderers, {vis} visible, {off} forced off, {noMat} without material, active={job.Holder.activeInHierarchy}, cam at {job.Cam.transform.position} -> {sample}");
            }
            var prev = RenderTexture.active;
            RenderTexture.active = job.Rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            int untouched = 0, background = 0, opaque = 0;
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a == 0) untouched++;
                else if (c.a == 1) { background++; px[i] = new Color32(0, 0, 0, 0); }
                else if (c.a == 255) opaque++;
            }
            tex.SetPixels32(px);
            tex.Apply();

            // First pass framed the 3D bounds, which leaves a lot of air; re-frame on the pixels actually drawn.
            if (!job.Reframed && opaque > 0 && Reframe(px))
            {
                UnityEngine.Object.Destroy(tex);
                job.Reframed = true;
                job.Frames = 0;
                return;
            }
            CoreLog.Info($"Card icon for '{d.Id}' rendered: {opaque} opaque, {background} background, {untouched} untouched of {Size * Size} pixels" +
                         (untouched == px.Length ? " (camera never drew)" : opaque == 0 ? " (camera drew but the model is invisible)" : ""));
            sprites[d.Index] = MakeSprite(tex, d);
#if DEVBUILD
            try
            {
                string dir = Path.Combine(BepInEx.Paths.CachePath, "moonvehiclecore");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, d.Id + ".png");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                CoreLog.Info($"[dev] card icon saved to {path}");
            }
            catch (Exception e) { CoreLog.Warn("[dev] saving card icon failed: " + e.Message); }
#endif
            Cleanup();
        }

        static bool Reframe(Color32[] px)
        {
            int minX = Size, minY = Size, maxX = -1, maxY = -1;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (px[y * Size + x].a > 8) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX < 0) return false;
            var cam = job.Cam;
            float unitsPerPixel = cam.orthographicSize * 2f / Size;
            float cx = (minX + maxX + 1) * 0.5f - Size * 0.5f, cy = (minY + maxY + 1) * 0.5f - Size * 0.5f;
            float extent = Math.Max(maxX - minX + 1, maxY - minY + 1) * unitsPerPixel;
            cam.transform.position += cam.transform.right * (cx * unitsPerPixel) + cam.transform.up * (cy * unitsPerPixel);
            cam.orthographicSize = extent * 0.5f / 0.92f;
            CoreLog.Debug($"Card icon reframed: drawn box {maxX - minX + 1}x{maxY - minY + 1} px, ortho now {cam.orthographicSize:F2}");
            return true;
        }

        // The game hides far-away things (horizon culling) by switching renderers to shadows-only; undo that on the stage.
        static void Unhide(GameObject holder)
        {
            if (holder == null) return;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r.renderingLayerMask == 0) r.renderingLayerMask = 1;
                r.forceRenderingOff = false;
            }
        }

        static Sprite MakeSprite(Texture2D tex, VehicleDefinition d)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = "ModVehicleCard_" + d.Id;
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return s;
        }

        static void Cleanup()
        {
            if (job == null) return;
            if (job.Cam != null) { job.Cam.targetTexture = null; UnityEngine.Object.Destroy(job.Cam.gameObject); }
            if (job.Light != null) UnityEngine.Object.Destroy(job.Light.gameObject);
            if (job.Holder != null) UnityEngine.Object.Destroy(job.Holder);
            if (job.Rt != null) { job.Rt.Release(); UnityEngine.Object.Destroy(job.Rt); }
            job = null;
        }

        static MoonPrototypeModels FindModels()
        {
            var eq = UnityEngine.Object.FindObjectsByType<MoonPrototypeEquipment>(FindObjectsSortMode.None);
            if (eq != null)
                foreach (var e in eq)
                    if (e != null && e.Models != null && e.Models.shell != null) return e.Models;
            return null;
        }
    }
}
