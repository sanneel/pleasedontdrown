using System.Collections;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A rope over island 3's river (plan, step 6): grab it and you swing across in an arc, dipping low over the water
    /// (and the alligators) and up onto the far bank. Your own machine moves you (your movement is yours); the others
    /// see you fly. The rope you see swings with you.
    /// </summary>
    public class RopeSwing : MonoBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _landing;
        [SerializeField] private Transform _pivot;
        [SerializeField] private float _seconds = 1.7f;

        private bool _swinging;
        private Vector3 _restPosition, _restScale;
        private Quaternion _restRotation;

        /// <summary>Every machine: someone grabbed a rope (tests and story: who swung across).</summary>
        public static event System.Action<PlayerHub> LocalSwung;

        private void Awake()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restScale = transform.localScale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            LocalSwung = null;
            DevCommands.Register("swing", "", "Grab the nearest rope swing and swing across (tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                RopeSwing best = null;
                foreach (RopeSwing r in FindObjectsByType<RopeSwing>(FindObjectsSortMode.None))
                    if (me != null && (best == null || (r.transform.position - me.transform.position).sqrMagnitude < (best.transform.position - me.transform.position).sqrMagnitude))
                        best = r;
                if (best == null || me == null || (best.transform.position - me.transform.position).sqrMagnitude > 8f * 8f)
                    throw new System.InvalidOperationException("no rope swing within 8 m");
                best.OnInteract(me);
            }, cheat: true);
        }

        public bool CanInteract(PlayerHub player) => !_swinging && player != null && player.Motor != null && player.Motor.Seat == null;
        public string GetPrompt(PlayerHub player) => "Swing across";

        public void OnInteract(PlayerHub player)
        {
            if (!CanInteract(player) || !player.IsOwner || _landing == null || _pivot == null) return;
            StartCoroutine(Swing(player));
        }

        private IEnumerator Swing(PlayerHub player)
        {
            _swinging = true;
            Vector3 from = player.transform.position;
            Vector3 to = _landing.position;
            Vector3 pivot = _pivot.position;
            float low = Mathf.Max(Mathf.Min(from.y, to.y) - 1.2f, (World.Water.WaterSurface.Exists ? World.Water.WaterSurface.HeightAt(pivot) : from.y) + 0.8f);
            LocalSwung?.Invoke(player);
            float start = Time.time;
            while (true)
            {
                float k = Mathf.Clamp01((Time.time - start) / _seconds);
                float eased = k * k * (3f - 2f * k);
                // Down in an arc and back up: low over the middle of the river.
                Vector3 p = Vector3.Lerp(from, to, eased);
                p.y = Mathf.Lerp(Mathf.Lerp(from.y, to.y, eased), low, Mathf.Sin(eased * Mathf.PI));
                if (player.Motor != null)
                {
                    player.Motor.Stun(0.2f);
                    player.Motor.Teleport(p);
                }
                // The rope runs from the beam to the hands.
                Vector3 hands = p + Vector3.up * 1.7f, d = hands - pivot;
                transform.SetPositionAndRotation((pivot + hands) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized));
                transform.localScale = new Vector3(_restScale.x, d.magnitude * 0.5f, _restScale.z);
                if (k >= 1f) break;
                yield return null;
            }
            if (player.Motor != null) player.Motor.Teleport(to);
            PlayerHud.ShowToast("Wheee!", 1f);
            yield return new WaitForSeconds(0.3f);
            transform.SetPositionAndRotation(_restPosition, _restRotation);
            transform.localScale = _restScale;
            _swinging = false;
        }
    }
}
