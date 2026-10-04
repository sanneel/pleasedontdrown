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

        private static readonly List<BasketballHoop> _hoops = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _hoops.Clear();

        private void OnEnable() => _hoops.Add(this);
        private void OnDisable() => _hoops.Remove(this);

        /// <summary>
        /// Shot assist (the thrower's machine): a real arc is a guess of power and angle at once, which made baskets
        /// a 1-in-50 thing. Throwing a basketball roughly at a hoop (within <paramref name="maxAimAngle"/> degrees,
        /// 1..11 m away, a firm throw) sends it on a proper arc to the rim instead, with a miss that grows with the
        /// distance and with how far off the aim was: close shots mostly go in, threes about a third of the time.
        /// </summary>
        public static bool TryAssist(Vector3 origin, Vector3 aim, float charge, float linearDamping, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            const float maxAimAngle = 35f;
            if (charge < 0.15f) return false; // a soft lob or a pass: just a throw
            BasketballHoop best = null;
            float bestAngle = maxAimAngle;
            var aimFlat = new Vector3(aim.x, 0f, aim.z);
            if (aimFlat.sqrMagnitude < 1e-4f || aim.y < -0.35f) return false; // looking down at the sand
            foreach (BasketballHoop hoop in _hoops)
            {
                if (hoop == null || hoop._rim == null) continue;
                Vector3 to = hoop._rim.position - origin;
                float d = new Vector2(to.x, to.z).magnitude;
                if (d < 1f || d > 13f) continue;
                float angle = Vector3.Angle(aimFlat, new Vector3(to.x, 0f, to.z));
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = hoop;
                }
            }
            if (best == null) return false;

            Vector3 target = best._rim.position;
            Vector3 flatTo = new Vector3(target.x - origin.x, 0f, target.z - origin.z);
            float distance = flatTo.magnitude + 0.07f; // a touch past the middle: the front of the rim is the usual miss
            Vector3 dir = flatTo.normalized;
            float h = target.y - origin.y;
            float g = -Physics.gravity.y;
            // A nice high arc (it drops in from above), higher when the rim is far above us.
            float theta = Mathf.Clamp(60f + distance * 0.6f, 61f, 67f) * Mathf.Deg2Rad; // high: it drops in steeply, clear of the front rim, from any distance
            float denom = 2f * Mathf.Cos(theta) * Mathf.Cos(theta) * (distance * Mathf.Tan(theta) - h);
            if (denom <= 0.01f) return false;
            float speed = Mathf.Sqrt(g * distance * distance / denom);
            // Then fine-tuned by flying it the way the physics will (fixed steps, gravity, the ball's drag): the speed
            // whose arc comes down through the rim's height right over the middle of the rim.
            float lo = speed * 0.8f, hi = speed * 1.4f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Reach(mid, theta, h, linearDamping, g) < distance) lo = mid;
                else hi = mid;
            }
            speed = (lo + hi) * 0.5f;

            // How it misses: a bit more power or less, a bit to the side; more from far away and from a sloppy aim.
            float sloppy = Mathf.InverseLerp(12f, maxAimAngle, bestAngle);
            float spread = 0.004f + 0.0016f * distance + 0.012f * sloppy;
            speed *= 1f + Gaussian() * spread;
            float yaw = Gaussian() * (0.2f + 0.07f * distance + 2.5f * sloppy);
            Vector3 side = Quaternion.Euler(0f, yaw, 0f) * dir;
            velocity = side * (speed * Mathf.Cos(theta)) + Vector3.up * (speed * Mathf.Sin(theta));
            return true;
        }

        /// <summary>How far out a ball thrown at this speed and angle is when it comes back down through height <paramref name="h"/>.</summary>
        private static float Reach(float speed, float theta, float h, float damping, float g)
        {
            float dt = Time.fixedDeltaTime;
            float vx = speed * Mathf.Cos(theta), vy = speed * Mathf.Sin(theta), x = 0f, y = 0f;
            for (int step = 0; step < 600; step++)
            {
                vy -= g * dt;
                float keep = 1f / (1f + damping * dt);
                vx *= keep;
                vy *= keep;
                float py = y;
                x += vx * dt;
                y += vy * dt;
                if (vy < 0f && py >= h && y < h) return x - vx * dt * (h - y) / Mathf.Max(1e-5f, py - y);
            }
            return 0f; // never got up that high
        }

        private static float Gaussian()
        {
            float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

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
            Core.DevCommands.Register("shots", "<distance> <count>", "Shoot balls at the beach hoop with the shot assist (tests).", args =>
            {
                float distance = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dd) ? dd : 4f;
                int count = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 10;
                StartCoroutine(TestShots(distance, count));
            }, cheat: true, owner: this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Core.DevCommands.Unregister("dunk", this);
            Core.DevCommands.Unregister("shots", this);
        }

        /// <summary>Test: shoot balls with the shot assist from a distance in front of the hoop (see how often they go in).</summary>
        private System.Collections.IEnumerator TestShots(float distance, int count)
        {
            Item prefab = Core.GameContent.Items != null ? Core.GameContent.Items.Find(BallName) : null;
            if (prefab == null) yield break;
            Vector3 front = _rim.parent != null ? _rim.parent.forward : Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                // From a spot this far out, a little to one side or the other, at a lifeguard's hands.
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f) * front;
                Vector3 from = _rim.position + dir * distance;
                from.y = _rim.position.y - 3.05f + 1.7f;
                Item ball = Instantiate(prefab, from, Quaternion.identity);
                Spawn(ball.gameObject);
                yield return new WaitForFixedUpdate();
                Rigidbody body = ball.Sync.Body;
                for (int wait = 0; wait < 10 && body.isKinematic; wait++) yield return new WaitForFixedUpdate();
                body.position = from;
                bool shot = TryAssist(from, (_rim.position - from).normalized, 1f, body.linearDamping, out Vector3 v) && !body.isKinematic;
                if (shot) body.linearVelocity = v;
                Debug.Log($"[Hoop] test shot {i + 1}: {(shot ? $"launched at {v.magnitude:F1} m/s ({v.x:F1}, {v.y:F1}, {v.z:F1})" : "NOT launched (kinematic)")}");
                float top = float.MinValue;
                for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
                {
                    yield return new WaitForFixedUpdate();
                    if (ball != null) top = Mathf.Max(top, body.position.y);
                }
                Despawn(ball.gameObject);
            }
            Core.DevCommands.Print($"shots done: {count} from {distance:0.#} m");
        }

        private const float MagnetRadius = 0.45f, MagnetHeight = 1.1f;

        /// <summary>
        /// Arcade rim magnet (every machine, for balls it simulates): a ball dropping toward the rim from just above it
        /// and not too far off is eased onto a line through the middle, so near misses drop in.
        /// </summary>
        private void Magnet()
        {
            Vector3 c = _rim.position;
            foreach (Item ball in Item.All)
            {
                if (ball == null || ball.IsHeld || ball.DisplayName != BallName) continue;
                Rigidbody body = ball.Sync.Body;
                if (body == null || body.isKinematic) continue;
                Vector3 rel = body.position - c;
                Vector3 v = body.linearVelocity;
                var flat = new Vector2(rel.x, rel.z);
                if (v.y >= -0.5f || rel.y <= 0.05f || rel.y > MagnetHeight || flat.magnitude > MagnetRadius) continue;
                float t = Mathf.Max(0.06f, rel.y / -v.y);
                Vector2 want = -flat / t;
                var now = new Vector2(v.x, v.z);
                now = Vector2.Lerp(now, want, 0.35f);
                body.linearVelocity = new Vector3(now.x, v.y, now.y);
            }
        }

        private void FixedUpdate()
        {
            if (_rim != null) Magnet();
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
