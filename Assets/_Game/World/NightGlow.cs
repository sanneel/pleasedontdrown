using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Things that change with the night (island 2's lamp posts, the gala's decorations): the <c>Night</c> objects are
    /// shown only at night, the <c>Day</c> ones only by day. Gala-only decorations (red carpet, ropes, string lights)
    /// follow the gala instead (<see cref="IslandNight.HotelGala"/>). No real lights unless a Night object holds one:
    /// the glow is emissive (cheap on low-end PCs). Checked a couple of times a second.
    /// </summary>
    public sealed class NightGlow : MonoBehaviour
    {
        [SerializeField] private GameObject[] _night = System.Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _day = System.Array.Empty<GameObject>();
        [Tooltip("Only for the gala night on island 2 (decorations), not every night.")]
        [SerializeField] private bool _galaOnly;

        private int _shown = -1;
        private float _next;

        private void OnEnable()
        {
            _shown = -1;
            _next = 0f;
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.4f + Random.value * 0.2f;
            bool on = _galaOnly ? IslandNight.HotelGala : IslandNight.Night > 0.3f;
            if (_shown == (on ? 1 : 0)) return;
            _shown = on ? 1 : 0;
            foreach (GameObject g in _night) if (g != null) g.SetActive(on);
            foreach (GameObject g in _day) if (g != null) g.SetActive(!on);
        }
    }
}
