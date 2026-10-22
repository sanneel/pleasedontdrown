using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The scene is ~6,000 separate meshes (wall boards, kerbs, bottles, candles, 1,500 jungle plants...) and each was
    /// drawn at any distance and cast a shadow: ~12,000 draws and 17 M triangles a frame. After a scene loads, every
    /// mesh not already in a LODGroup gets one that hides it once it is smaller on screen than the graphics level
    /// allows (a bottle at 90 m on High is a pixel), and small meshes stop casting shadows below High. Changing the
    /// level re-applies it. Console: <c>renderbudget</c> shows what it did.
    /// </summary>
    public static class RenderBudget
    {
        // Hidden below this share of the screen height (before the quality level's LOD bias). (At 0.6% on High the
        // whole island 3 jungle, 3.6 million triangles, was drawn from island 1 across the sea: 16 million a frame.)
        private static float CullHeight => GraphicsQuality.Current switch
        {
            GraphicsQuality.Tier.Low => 0.03f,
            GraphicsQuality.Tier.Medium => 0.02f,
            _ => 0.012f,
        };

        // Meshes smaller than this (largest side, metres) cast no shadow.
        private static float ShadowlessBelow => GraphicsQuality.Current switch
        {
            GraphicsQuality.Tier.Low => 1.6f,
            GraphicsQuality.Tier.Medium => 0.6f,
            _ => 0.2f,
        };

        private static readonly List<LODGroup> Groups = new();
        private static readonly Dictionary<Renderer, ShadowCastingMode> Shadows = new();
        private static readonly List<Renderer> Scratch = new();
        private static int _shadowless;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded += (scene, _) => Add(scene);
            GraphicsQuality.Changed += Reapply;
            for (int i = 0; i < SceneManager.sceneCount; i++) Add(SceneManager.GetSceneAt(i));
            DevCommands.Register("renderbudget", "", "Show how many meshes hide when small on screen and cast no shadow.", _ =>
                DevCommands.Print($"renderbudget {GraphicsQuality.Current}: {Groups.Count} meshes hide below {CullHeight:P1} of the screen, " +
                                  $"{_shadowless} small ones cast no shadow (< {ShadowlessBelow} m)"));
        }

        private static void Add(Scene scene)
        {
            if (!scene.isLoaded) return;
            var listed = new HashSet<Renderer>();
            foreach (LODGroup group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (LOD lod in group.GetLODs())
                    foreach (Renderer r in lod.renderers)
                        if (r != null) listed.Add(r);

            float t = Time.realtimeSinceStartup;
            int before = Groups.Count;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, Scratch);
                foreach (Renderer r in Scratch)
                {
                    // Plain meshes only: skinned characters, particles, lines and UI keep their own rules.
                    if (r is not MeshRenderer || listed.Contains(r) || r.TryGetComponent(out LODGroup _)) continue;
                    // Never hidden: what glows (lamp bulbs at night are the scene) and what can be picked up (a lost
                    // wallet must stay findable on every level). (Not "anything networked": the whole island is.)
                    if (Glows(r) || r.GetComponentInParent<Items.Item>(true) != null || r.GetComponentInParent<Items.ItemSync>(true) != null) continue;
                    var group = r.gameObject.AddComponent<LODGroup>();
                    group.fadeMode = LODFadeMode.None;
                    group.SetLODs(new[] { new LOD(CullHeight, new[] { r }) });
                    group.RecalculateBounds();
                    Groups.Add(group);
                    if (r.shadowCastingMode == ShadowCastingMode.On) Shadows[r] = ShadowCastingMode.On;
                }
            }
            Scratch.Clear();
            ApplyShadows();
            Debug.Log($"[RenderBudget] {scene.name}: {Groups.Count - before} meshes hide when tiny on screen, {_shadowless} cast no shadow " +
                      $"({GraphicsQuality.Current}, {(Time.realtimeSinceStartup - t) * 1000f:F0} ms)");
        }

        private static bool Glows(Renderer r)
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (m.IsKeywordEnabled("_EMISSION")) return true;
                if (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.01f) return true;
                if (m.HasProperty("emissiveFactor") && m.GetColor("emissiveFactor").maxColorComponent > 0.01f) return true; // glTF
            }
            return false;
        }

        private static void Reapply()
        {
            Groups.RemoveAll(g => g == null);
            foreach (LODGroup group in Groups)
            {
                LOD[] lods = group.GetLODs();
                lods[0].screenRelativeTransitionHeight = CullHeight;
                group.SetLODs(lods);
            }
            ApplyShadows();
        }

        private static void ApplyShadows()
        {
            _shadowless = 0;
            float limit = ShadowlessBelow;
            var gone = new List<Renderer>();
            foreach (var (r, original) in Shadows)
            {
                if (r == null) { gone.Add(r); continue; }
                Vector3 size = r.bounds.size;
                bool small = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < limit;
                // (Only ever between On and Off: code that sets ShadowsOnly etc. later keeps its choice.)
                if (small && r.shadowCastingMode == original) r.shadowCastingMode = ShadowCastingMode.Off;
                else if (!small && r.shadowCastingMode == ShadowCastingMode.Off) r.shadowCastingMode = original;
                if (small) _shadowless++;
            }
            foreach (Renderer r in gone) Shadows.Remove(r);
        }
    }
}
