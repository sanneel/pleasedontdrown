using System.Collections;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The punch-the-bell strongman game. A needle swings back and forth on the dial by the pad; punch the pad and
    /// the puck flies up the pole as high as the needle stood (the host reads it at the moment of the hit, on the
    /// shared network clock, so everyone sees the same needle). Top of the scale rings the bell.
    /// </summary>
    public class Strongman : NetworkBehaviour, IDamageable
    {
        [SerializeField] private Transform _needle;   // turns about its local z: -70 (weak) .. +70 (beast)
        [SerializeField] private Transform _puck;     // slides up its parent's y
        [SerializeField] private float _poleHeight = 4.6f;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioSource _bellAudio;

        private static readonly string[] Ranks = { "WIMP", "SOGGY NOODLE", "MEH", "NOT BAD", "STRONG", "BEAST", "LEGEND" };
        private Vector3 _puckRest;
        private float _lastHit;

        private void Awake()
        {
            if (_puck != null) _puckRest = _puck.localPosition;
        }

        /// <summary>0..1, the same on every machine (network time).</summary>
        private float Needle01
        {
            get
            {
                double t = TimeManager != null ? TimeManager.TicksToTime(TimeManager.Tick) : Time.time;
                // Sweeps up and down, lingering a moment at the bottom: the top is a narrow window.
                float s = Mathf.Sin((float)(t * 2.4));
                return Mathf.Pow((s + 1f) * 0.5f, 1.6f);
            }
        }

        private void Update()
        {
            if (_needle != null) _needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(70f, -70f, Needle01));
        }

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (Time.time - _lastHit < 2.2f) return false; // the puck is still up there
            _lastHit = Time.time;
            int score = Mathf.Clamp(Mathf.RoundToInt(Needle01 * 100f + Random.Range(-3f, 3f)), 1, 100);
            string who = attacker != null ? attacker.DisplayName : "Somebody";
            Debug.Log($"[Strongman] {who} hits {score}");
            HitObservers(who, score);
            return true;
        }

        [ObserversRpc]
        private void HitObservers(string who, int score)
        {
            StopAllCoroutines();
            StartCoroutine(Fly(who, score));
        }

        private IEnumerator Fly(string who, int score)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 1f);
            float top = _poleHeight * score / 100f;
            float t = 0f;
            // Up fast, easing off near its height...
            while (t < 1f && _puck != null)
            {
                t += Time.deltaTime / 0.55f;
                _puck.localPosition = _puckRest + Vector3.up * (top * (1f - (1f - t) * (1f - t)));
                yield return null;
            }
            bool bell = score >= 96;
            if (bell && _bellAudio != null) _bellAudio.PlayOneShot(ProceduralAudio.Bell, 1f);
            string rank = Ranks[Mathf.Clamp(score * Ranks.Length / 101, 0, Ranks.Length - 1)];
            Vector3 at = (_puck != null ? _puck.position : transform.position) + Vector3.up * 0.5f;
            FloatingText.Spawn(at, bell ? "DING DING DING! LEGEND!" : $"{score} - {rank}", bell ? new Color(1f, 0.85f, 0.2f) : Color.white, bell ? 1.5f : 1.1f, 2f);
            PlayerHud.ShowToast($"<b>{who}</b> hits the strongman: {score} ({rank})", 2.5f);
            if (bell && _audio != null) _audio.PlayOneShot(FunSounds.Cheer, 1f);
            yield return new WaitForSeconds(0.5f);
            // ...and back down.
            float from = _puck != null ? _puck.localPosition.y - _puckRest.y : 0f;
            t = 0f;
            while (t < 1f && _puck != null)
            {
                t += Time.deltaTime / 0.7f;
                _puck.localPosition = _puckRest + Vector3.up * (from * (1f - t * t));
                yield return null;
            }
            if (_puck != null) _puck.localPosition = _puckRest;
        }
    }
}
