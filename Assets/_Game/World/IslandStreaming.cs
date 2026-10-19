using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Hides the scenery of islands you are far from, on this machine only. Renderers, lamps, particle systems and
    /// animators standing on an island are switched off while the local player is more than <see cref="ShowMargin"/>
    /// metres from its shore, and back on as you sail up to it. Nothing is unloaded: colliders, networked objects,
    /// story characters and the host's logic keep running, so the story and other players are never affected.
    /// Things bigger than an island (the sea, the shared ground) are left alone.
    /// </summary>
    public class IslandStreaming : MonoBehaviour
    {
        private struct Zone
        {
            public string Name;
            public Vector2 Centre;
            public float Radius;
            public Zone(string name, float x, float z, float radius) { Name = name; Centre = new Vector2(x, z); Radius = radius; }
        }

        // Where the islands are (GameSceneBuilder: IslandCenter, Island2Center, Island3Center, Island4Center, DevIslandCenter), a little generous.
        private static readonly Zone[] Zones =
        {
            new("Island 1", 12.5f, 10f, 110f),
            new("Island 2", -5f, -275f, 130f),
            new("Island 3", 20f, -900f, 170f),
            new("Island 4", 560f, -400f, 150f),
            new("Dev island", -230f, -60f, 70f),
        };

        private const float ShowMargin = 100f;
        private const float HideMargin = 140f;
        private const int TogglesPerFrame = 2000;

        private struct Entry
        {
            public Behaviour Behaviour;   // Light, Animator
            public Renderer Renderer;
            public ParticleSystem Particles;
            public bool WasPlaying;
        }

        private readonly List<Entry>[] _entries = new List<Entry>[Zones.Length];
        private readonly bool[] _shown = new bool[Zones.Length];
        private bool _ready, _disabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Game" || FindFirstObjectByType<IslandStreaming>() != null) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pdd-noculling") >= 0) return;
            new GameObject("IslandStreaming").AddComponent<IslandStreaming>();
        }

        private void Start()
        {
            for (int i = 0; i < Zones.Length; i++)
            {
                _entries[i] = new List<Entry>();
                _shown[i] = true;
            }
            StartCoroutine(Run());
        }

        private void OnEnable()
        {
            DevCommands.Register("islands", "[on|off]", "Show which islands are drawn, or turn hiding far islands off or on.", args =>
            {
                if (args.Length > 0) _disabled = args[0] == "off";
                DevCommands.Print(_disabled ? "hiding is off" : "hiding is on");
                for (int i = 0; _ready && i < Zones.Length; i++)
                    DevCommands.Print($"  {Zones[i].Name}: {(_shown[i] ? "drawn" : "hidden")} ({_entries[i].Count} things)");
            }, owner: this);
        }

        private void OnDisable() => DevCommands.Unregister("islands", this);

        private IEnumerator Run()
        {
            // The scene and its spawned objects need a moment before they can be sorted onto islands.
            yield return new WaitForSecondsRealtime(3f);
            Collect();
            _ready = true;
            Debug.Log($"[Islands] sorted {Total()} things onto {Zones.Length} islands");
            var wait = new WaitForSecondsRealtime(0.4f);
            while (true)
            {
                yield return wait;
                PlayerHub me = PlayerHub.Local;
                for (int i = 0; i < Zones.Length; i++)
                {
                    bool show = _disabled || me == null || IsNear(i, me.transform.position);
                    if (show == _shown[i]) continue;
                    _shown[i] = show;
                    yield return Apply(i, show);
                }
            }
        }

        private bool IsNear(int zone, Vector3 at)
        {
            float away = Vector2.Distance(new Vector2(at.x, at.z), Zones[zone].Centre) - Zones[zone].Radius;
            return away <= (_shown[zone] ? HideMargin : ShowMargin);
        }

        private int Total()
        {
            int n = 0;
            foreach (List<Entry> list in _entries) n += list.Count;
            return n;
        }

        private static int ZoneOf(Bounds bounds)
        {
            Vector2 centre = new(bounds.center.x, bounds.center.z);
            float reach = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            for (int i = 0; i < Zones.Length; i++)
                if (Vector2.Distance(centre, Zones[i].Centre) + reach <= Zones[i].Radius) return i;
            return -1;
        }

        private static bool Dynamic(Component c) => c.GetComponentInParent<NetworkObject>() != null;

        private void Collect()
        {
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!r.enabled || r is ParticleSystemRenderer || Dynamic(r)) continue;
                int zone = ZoneOf(r.bounds);
                if (zone >= 0) _entries[zone].Add(new Entry { Renderer = r });
            }
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!l.enabled || l.type == LightType.Directional || Dynamic(l)) continue;
                int zone = ZoneOf(new Bounds(l.transform.position, Vector3.one * Mathf.Max(0.1f, l.range * 0.1f)));
                if (zone >= 0) _entries[zone].Add(new Entry { Behaviour = l });
            }
            foreach (Animator a in FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!a.enabled || Dynamic(a)) continue;
                int zone = ZoneOf(new Bounds(a.transform.position, Vector3.one));
                if (zone >= 0) _entries[zone].Add(new Entry { Behaviour = a });
            }
            foreach (ParticleSystem p in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (Dynamic(p) || p.GetComponentInParent<ParticleSystem>() != p) continue;
                int zone = ZoneOf(new Bounds(p.transform.position, Vector3.one));
                if (zone >= 0) _entries[zone].Add(new Entry { Particles = p });
            }
        }

        private IEnumerator Apply(int zone, bool show)
        {
            List<Entry> list = _entries[zone];
            Debug.Log($"[Islands] {Zones[zone].Name} {(show ? "drawn" : "hidden")} ({list.Count} things)");
            for (int i = 0; i < list.Count; i++)
            {
                Entry e = list[i];
                if (e.Renderer != null) e.Renderer.enabled = show;
                else if (e.Behaviour != null) e.Behaviour.enabled = show;
                else if (e.Particles != null)
                {
                    if (!show)
                    {
                        e.WasPlaying = e.Particles.isPlaying;
                        list[i] = e;
                        e.Particles.Pause(true);
                    }
                    else if (e.WasPlaying) e.Particles.Play(true);
                }
                if (i % TogglesPerFrame == TogglesPerFrame - 1) yield return null;
                if (_shown[zone] != show) yield break;
            }
        }
    }
}
