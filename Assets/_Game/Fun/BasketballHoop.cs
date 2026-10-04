using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The beach basketball hoop. Host: every physics step each basketball's path is swept against the rim's plane;
    /// passing down through it inside the rim is a basket (no trigger callbacks: they would only fire on whichever
    /// machine is simulating the ball, and a fast ball can skip a thin trigger). Two points, three from beyond the
    /// line; "SWISH" when it never came near the rim. Everyone sees the points, hears the net and the beach cheers.
    /// </summary>
    public class BasketballHoop : NetworkBehaviour
    {
        public const string BallName = "Basketball";

        [SerializeField] private Transform _rim;          // centre of the rim, its plane level (local y up)
        [SerializeField] private float _rimRadius = 0.23f;
        [SerializeField] private float _rimTube = 0.012f;
        [SerializeField] private float _ballRadius = 0.12f;
        [SerializeField] private float _threePointDistance = 6f;
        [SerializeField] private Transform _net;
        [SerializeField] private AudioSource _audio;

        private sealed class Track
        {
            public Vector3 Last;
            public float Closest = float.MaxValue; // nearest it came to the rim tube since it was last thrown
            public float ThrowTime;
            public bool Primed;
        }

        private readonly Dictionary<Item, Track> _tracks = new();
        private readonly Dictionary<string, int> _scores = new();
        private float _netPulse;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Core.DevCommands.Register("dunk", "", "Drop a basketball through the beach hoop (tests the scoring).", _ =>
            {
                Item prefab = Core.GameContent.Items != null ? Core.GameContent.Items.Find(BallName) : null;
                if (prefab == null || _rim == null) return;
                Item ball = Instantiate(prefab, _rim.position + Vector3.up * 1.2f, Quaternion.identity);
                Spawn(ball.gameObject);
                Core.DevCommands.Print("A ball drops toward the hoop...");
            }, cheat: true, owner: this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Core.DevCommands.Unregister("dunk", this);
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || _rim == null) return;
            Vector3 c = _rim.position;
            foreach (Item ball in Item.All)
            {
                if (ball == null || ball.DisplayName != BallName) continue;
                if (!_tracks.TryGetValue(ball, out Track t)) _tracks[ball] = t = new Track();
                Vector3 p = ball.transform.position;
                if (ball.IsHeld || !t.Primed || ball.ReleasedAt != t.ThrowTime)
                {
                    // In someone's hands, or a fresh throw: start watching this flight afresh.
                    t.Last = p;
                    t.Primed = true;
                    t.ThrowTime = ball.ReleasedAt;
                    t.Closest = float.MaxValue;
                    continue;
                }
                t.Closest = Mathf.Min(t.Closest, RimDistance(p, c));
                if (t.Last.y > c.y && p.y <= c.y)
                {
                    float k = (t.Last.y - c.y) / Mathf.Max(1e-5f, t.Last.y - p.y);
                    Vector3 through = Vector3.Lerp(t.Last, p, k);
                    var flat = new Vector2(through.x - c.x, through.z - c.z);
                    if (flat.magnitude < _rimRadius - _ballRadius * 0.35f)
                        Score(ball, t.Closest > _ballRadius + _rimTube + 0.015f);
                }
                t.Last = p;
            }
        }

        /// <summary>From the ball's centre to the nearest point of the rim's ring.</summary>
        private float RimDistance(Vector3 p, Vector3 c)
        {
            var flat = new Vector2(p.x - c.x, p.z - c.z);
            float radial = flat.magnitude - _rimRadius;
            return Mathf.Sqrt(radial * radial + (p.y - c.y) * (p.y - c.y));
        }

        [Server]
        private void Score(Item ball, bool swish)
        {
            PlayerHub shooter = ball.LastHolder;
            Vector3 from = ball.ReleasedFrom;
            Vector3 c = _rim.position;
            float distance = new Vector2(from.x - c.x, from.z - c.z).magnitude;
            int points = shooter != null && distance > _threePointDistance ? 3 : 2;
            string who = shooter != null ? shooter.DisplayName : "Somebody";
            _scores.TryGetValue(who, out int total);
            _scores[who] = total += points;
            Debug.Log($"[Hoop] {who} scores {points}{(swish ? " (swish)" : "")} from {distance:F1} m, total {total}");
            ScoredObservers(who, points, swish, total, distance);
            // The beach loves it: a few people nearby cheer.
            int cheering = 0;
            foreach (StoryNpc npc in StoryNpc.All)
            {
                if (cheering >= 3 || npc == null || npc.Role is not (NpcRole.Guest or NpcRole.Bystander)) continue;
                if ((npc.transform.position - c).sqrMagnitude > 25f * 25f) continue;
                npc.ServerShout(Cheers[Random.Range(0, Cheers.Length)], false);
                npc.ServerGesture(Avatars.AvatarGesture.Wave, c);
                cheering++;
            }
        }

        private static readonly string[] Cheers = { "WOOO!", "NICE SHOT!", "BUCKETS!", "DO IT AGAIN!", "MVP! MVP!" };

        [ObserversRpc]
        private void ScoredObservers(string who, int points, bool swish, int total, float distance)
        {
            Vector3 at = _rim.position + Vector3.up * 0.5f;
            string word = swish ? (points == 3 ? "SWISH! +3" : "SWISH! +2") : points == 3 ? "THREE! +3" : "+2";
            FloatingText.Spawn(at, word, swish ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.8f, 0.25f), 1.4f, 2f);
            PlayerHud.ShowToast($"<b>{who}</b> scores {points}{(points == 3 ? $" from {distance:F0} m" : "")}! ({total} today)", 3f);
            if (_audio != null)
            {
                _audio.PlayOneShot(FunSounds.Swish, 1f);
                _audio.PlayOneShot(FunSounds.Cheer, 0.8f);
                if (points == 3) _audio.PlayOneShot(FunSounds.Whoop, 0.6f);
            }
            _netPulse = 1f;
        }

        private void Update()
        {
            if (_net == null || _netPulse <= 0f) return;
            _netPulse = Mathf.Max(0f, _netPulse - Time.deltaTime * 2.5f);
            // The net kicks down and wobbles back.
            float s = 1f + Mathf.Sin(_netPulse * 18f) * 0.15f * _netPulse;
            _net.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s));
        }
    }
}
