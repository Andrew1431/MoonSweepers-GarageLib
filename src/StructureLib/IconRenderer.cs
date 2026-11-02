using System;
using System.Collections.Generic;
using System.IO;
using MoonCleaners;
using StructureLib.Patches;
using UnityEngine;

namespace StructureLib
{
    // Build-menu card pictures for modded structures (adapted from GarageLib's IconRenderer). The model is the donor's
    // prefab (from the structure's cloned definition) with DecorateModel applied, on a stage at the moon's centre, shot
    // orthographically from the front-left and above onto a transparent background. A PNG from the definition wins;
    // otherwise a CardImageKey means vanilla art, so nothing is rendered. Under URP a manual Camera.Render may draw nothing, so the
    // camera is created disabled, enabled the next frame, and read two frames after that. No render starts until
    // SettleTicks ticks after the model materials changed or QueueAll ran.
    //
    // The prefab is instantiated under an inactive parent so none of its game scripts wake up (Awake would run even on
    // disabled behaviours), and the stage gets a script-free copy of it: the same transform tree with only its mesh
    // renderers. DecorateModel runs on that copy.
    internal static class IconRenderer
    {
        const int Size = 256;
        const int Layer = 0;
        // The moon's centre: 2 km under the surface shell, far outside every other camera's view, so the icon camera's
        // clip range sees nothing but the model.
        static readonly Vector3 Stage = Vector3.zero;
        // Structure-local direction from the model towards the camera: in front, to the left, ~30° above.
        static readonly Vector3 ViewDir = new Vector3(-0.62f, 0.55f, 0.78f).normalized;

        static readonly Dictionary<int, Sprite> sprites = new();
        static readonly Queue<StructureDefinition> queue = new();
        static readonly HashSet<int> pending = new();
        static Job job;
        static bool queued, gaveUp;
        static float waited;
        static float modelWait;
        static readonly List<StructureDefinition> standIn = new();
        // Ticks since the model materials last changed state (StructureMaterials.Waiting flipped: the stand-ins' shaders
        // were swapped for the game's) or QueueAll ran. A render starts only once this reaches SettleTicks, so a shader
        // swap and a camera's first draw never share a frame (a mitigation for the load-time crash, see the recon).
        const int SettleTicks = 5;
        static int calm;
        static bool lastWaiting;
        static int changeFrame = -1;

        sealed class Job
        {
            public StructureDefinition Def;
            public GameObject Holder;
            public Camera Cam;
            public Light Light;
            public RenderTexture Rt;
            public int Frames;
            public bool Reframed;
        }

        /// <summary>The card picture of a structure (rendered or from its PNG); null until there is one.</summary>
        internal static Sprite Get(StructureDefinition d) =>
            d != null && sprites.TryGetValue(d.Kind, out var s) && s != null && !s.WasCollected ? s : null;

        /// <summary>True while a picture for the structure is queued or being rendered.</summary>
        internal static bool Pending(StructureDefinition d) => d != null && pending.Contains(d.Kind);

        // Called when a session enters the game and after the first complete catalog. Precedence: CardImagePng, then
        // CardImageKey (vanilla art, no render), then a render. Once per process: sprites outlive worlds.
        internal static void QueueAll()
        {
            if (queued || !MoonStructures.Frozen || MoonStructures.Disabled) return;
            queued = true;
            calm = 0;
            changeFrame = Time.frameCount;
            foreach (var d in MoonStructures.All)
            {
                if (d.Kind <= 0 || !MoonStructures.IsModded(d.Kind)) continue;
                if (d.CardImagePng != null && FromPng(d)) continue;
                if (CatalogPatches.UsableKey(d) != null) continue;
                queue.Enqueue(d);
                pending.Add(d.Kind);
            }
            if (queue.Count > 0) CoreLog.Info($"Card art: rendering {queue.Count} structure picture(s)");
        }

        internal static void Tick()
        {
            try
            {
                bool waiting = StructureMaterials.Waiting;
                if (waiting != lastWaiting)
                {
                    lastWaiting = waiting;
                    calm = 0;
                    changeFrame = Time.frameCount;
                    CoreLog.Info($"Card art: model materials {(waiting ? "now wait for the player's equipment" : "changed to the game's")} at frame {changeFrame}; renders hold for {SettleTicks} ticks");
                }
                else if (calm < int.MaxValue) calm++;

                // A card taken while the model still wore tinted stand-ins is taken again once they are the game's own.
                if (standIn.Count > 0 && !StructureMaterials.Waiting)
                {
                    // One still being rendered waits for its job to finish (pending), then goes back in the queue.
                    standIn.RemoveAll(s =>
                    {
                        if (!pending.Add(s.Kind)) return false;
                        queue.Enqueue(s);
                        CoreLog.Info($"Card art for '{s.Id}': re-rendering, its model now has the game's materials");
                        return true;
                    });
                }
                if (job == null)
                {
                    if (queue.Count == 0) return;
                    var world = CoreState.World;
                    if (world == null || world.Pointer == IntPtr.Zero || !world)
                    {
                        waited += Time.unscaledDeltaTime;
                        if (waited > 60f && !gaveUp) { gaveUp = true; CoreLog.Warn("Card art: no world after 60 s, still waiting"); }
                        return;
                    }
                    // A Model's materials may still be tinted stand-ins (no player equipment yet): photograph it once they
                    // are the game's own, or after 60 s anyway.
                    var next = queue.Peek();
                    if (next.ModelPath != null && modelWait < 60f)
                    {
                        Injection.DefinitionFor(next, world);
                        if (StructureMaterials.Waiting)
                        {
                            modelWait += Time.unscaledDeltaTime;
                            if (modelWait >= 60f) CoreLog.Warn($"Card art for '{next.Id}': model materials still waiting for the player's equipment after 60 s; rendering anyway");
                            return;
                        }
                    }
                    // Building the template above may itself have created stand-ins (Waiting flips next tick): check again.
                    if (calm < SettleTicks || StructureMaterials.Waiting != lastWaiting) return;
                    modelWait = 0f;
                    if (next.ModelPath != null && StructureMaterials.Waiting) standIn.Add(next);
                    CoreLog.Info($"Card art for '{next.Id}': render starts at frame {Time.frameCount}, {calm} tick(s) after the last material/queue change (frame {changeFrame})");
                    Start(queue.Dequeue(), world);
                    return;
                }
                // The camera is created disabled (Start) and enabled here, a frame later, so creation and its first
                // draw happen in different frames; Finish reads two frames after that first draw.
                if (job.Cam != null && !job.Cam.enabled)
                {
                    job.Cam.enabled = true;
                    Unhide(job.Holder);
                    CoreLog.Info($"Card art for '{job.Def.Id}': camera enabled at frame {Time.frameCount}");
                    return;
                }
                job.Frames++;
                Unhide(job.Holder);
                if (job.Frames >= 2) Finish();
            }
            catch (Exception e)
            {
                CoreLog.Error($"Card art for {job?.Def} failed: {e}");
                Cleanup();
            }
        }

        static bool FromPng(StructureDefinition d)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, d.CardImagePng)) throw new InvalidDataException("not a PNG");
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprites[d.Kind] = MakeSprite(tex, d);
                CoreLog.Info($"Card art for '{d.Id}' loaded from its PNG ({tex.width}x{tex.height})");
                return true;
            }
            catch (Exception e) { CoreLog.Warn($"Card art PNG for '{d.Id}' unusable, rendering instead: {e.Message}"); return false; }
        }

        static void Start(StructureDefinition d, MoonWorld world)
        {
            var clone = Injection.DefinitionFor(d, world);
            var prefab = clone != null ? clone.prefab : null;
            if (prefab == null) { CoreLog.Warn($"Card art for '{d.Id}': donor kind {d.BaseStructure} has no prefab, no picture"); pending.Remove(d.Kind); return; }

            job = new Job { Def = d };
            job.Holder = new GameObject("StructureLib Icon Stage") { layer = Layer };
            job.Holder.transform.position = Stage;

            var source = new GameObject("StructureLib Icon Source");
            source.SetActive(false);
            GameObject model;
            try
            {
                var inst = UnityEngine.Object.Instantiate<GameObject>(prefab, source.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                model = InertCopy(inst.transform, job.Holder.transform);
                CoreLog.Debug($"Card art for '{d.Id}': prefab '{prefab.name}', {inst.GetComponentsInChildren<LODGroup>(true).Length} LOD group(s)");
            }
            finally { UnityEngine.Object.Destroy(source); }
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.SetActive(true);

            if (d.DecorateModel != null)
            {
                try { d.DecorateModel(model, new ModelContext { Definition = d, Parent = job.Holder.transform, Donor = clone }); }
                catch (Exception e) { CoreLog.Warn($"Card art for '{d.Id}': DecorateModel threw, picturing the undecorated model: {e.Message}"); }
            }
            // Whatever the decorator added: no lights, no physics, all on the stage layer.
            foreach (var l in model.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            bool any = false;
            var bounds = new Bounds(Stage, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any) { CoreLog.Warn($"Card art for '{d.Id}': model has no visible renderers"); pending.Remove(d.Kind); Cleanup(); return; }

            job.Rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = "StructureLib Icon " + d.Id };
            job.Rt.Create();
            var camGo = new GameObject("StructureLib Icon Camera");
            var cam = job.Cam = camGo.AddComponent<Camera>();
            cam.enabled = false; // Tick enables it next frame: no first draw in the frame it is created
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
            var lightGo = new GameObject("StructureLib Icon Light");
            job.Light = lightGo.AddComponent<Light>();
            job.Light.type = LightType.Directional;
            job.Light.intensity = 1.2f;
            job.Light.shadows = LightShadows.None;
            job.Light.cullingMask = 1 << Layer;
            lightGo.transform.rotation = Quaternion.LookRotation(-t0.TransformDirection(new Vector3(-0.3f, 1f, 0.6f).normalized), t0.forward);
            CoreLog.Debug($"Card art for '{d.Id}': bounds {bounds.size}, ortho {cam.orthographicSize:F2}");
        }

        // The same transform tree as src, carrying only enabled mesh renderers (a skinned mesh is drawn in its bind
        // pose). No game scripts, colliders, lights or audio come along.
        static GameObject InertCopy(Transform src, Transform parent)
        {
            var go = new GameObject(src.name) { layer = Layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
            go.SetActive(src.gameObject.activeSelf);

            var mr = src.GetComponent<MeshRenderer>();
            var mf = src.GetComponent<MeshFilter>();
            Mesh mesh = null;
            Renderer from = null;
            if (mr != null && mr.enabled && mf != null && mf.sharedMesh != null) { mesh = mf.sharedMesh; from = mr; }
            else
            {
                var smr = src.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.enabled && smr.sharedMesh != null) { mesh = smr.sharedMesh; from = smr; }
            }
            if (mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = from.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int i = 0; i < src.childCount; i++) InertCopy(src.GetChild(i), go.transform);
            return go;
        }

        static void Finish()
        {
            var d = job.Def;
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
            CoreLog.Info($"Card art for '{d.Id}' rendered (read at frame {Time.frameCount}): {opaque} opaque, {background} background, {untouched} untouched of {Size * Size} pixels" +
                         (untouched == px.Length ? " (camera never drew)" : opaque == 0 ? " (camera drew but the model is invisible)" : ""));
            var sprite = sprites[d.Kind] = MakeSprite(tex, d);
#if DEVBUILD
            try
            {
                string dir = Path.Combine(BepInEx.Paths.CachePath, "structurelib");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, d.Id + ".png");
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                CoreLog.Info($"[dev] card art saved to {path}");
            }
            catch (Exception e) { CoreLog.Warn("[dev] saving card art failed: " + e.Message); }
#endif
            pending.Remove(d.Kind);
            Cleanup();
            // After Cleanup the job is gone; name the structure here if swapping the art into open cards fails.
            try { CatalogPatches.ShowArt(d, sprite); }
            catch (Exception e) { CoreLog.Error($"Card art for '{d.Id}': swapping it into existing cards failed: {e}"); }
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
            CoreLog.Debug($"Card art reframed: drawn box {maxX - minX + 1}x{maxY - minY + 1} px, ortho now {cam.orthographicSize:F2}");
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

        static Sprite MakeSprite(Texture2D tex, StructureDefinition d)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = "ModStructureCard_" + d.Id;
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
            if (job.Def != null) pending.Remove(job.Def.Kind);
            job = null;
        }
    }
}
