using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// The Grand Coral's windows light up at night (the gala): the hotel's darker window glass gets a warm glow, the
    /// lighter tint stays dark, so the tower reads as lit rooms here and there, not a lamp. Nothing in the scene
    /// changes: the hotel's renderers swap to a runtime copy of the glass while it's night. (The hotel itself is
    /// Shako's model and is never rebuilt for this.)
    /// </summary>
    public sealed class HotelNightLights : MonoBehaviour
    {
        private const string Glass = "HotelReal_glass";
        private static readonly Color Warm = new(1f, 0.72f, 0.38f);

        private readonly List<(Renderer renderer, Material[] day, Material[] night)> _renderers = new();
        private int _shown = -1;
        private float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            GameObject hotel = GameObject.Find("Environment/Hotel");
            if (hotel == null) return; // not the game scene
            hotel.AddComponent<HotelNightLights>();
        }

        private void Start()
        {
            Material lit = null;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Material[] day = r.sharedMaterials;
                Material[] night = null;
                for (int i = 0; i < day.Length; i++)
                {
                    if (day[i] == null || day[i].name != Glass) continue;
                    if (lit == null)
                    {
                        lit = new Material(day[i]) { name = Glass + " (night)" };
                        lit.EnableKeyword("_EMISSION");
                        lit.SetColor("_EmissionColor", Warm * 1.6f);
                        lit.SetColor("_BaseColor", Color.Lerp(day[i].GetColor("_BaseColor"), Warm, 0.5f));
                    }
                    night ??= (Material[])day.Clone();
                    night[i] = lit;
                }
                if (night != null) _renderers.Add((r, day, night));
            }
            Debug.Log($"[HotelNight] {_renderers.Count} hotel renderers have windows that light up at night");
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            bool on = IslandNight.Night > 0.35f;
            if (_shown == (on ? 1 : 0)) return;
            _shown = on ? 1 : 0;
            foreach (var (r, day, night) in _renderers)
                if (r != null) r.sharedMaterials = on ? night : day;
        }
    }
}
