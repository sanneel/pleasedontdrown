using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;
using UnityEngine.AI;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Tourists enjoying the beach (host): sunbathing on towels (on the back, on the belly, sitting up), strolling
    /// along the sand, wading in the shallows and swimming about. They're story characters (<see cref="StoryNpc"/>),
    /// so they cost no physics; press Interact for a word with them.
    ///
    /// Everyone keeps busy: strollers walk from spot to spot, stop for a chat with each other and now and then go for
    /// a swim; sunbathers turn over, sit up, and every so often get up, walk into the sea, swim for a while and walk
    /// back to their towel; swimmers and waders never stand still for long. A route that gets blocked is reported
    /// back (<see cref="StoryNpc.ServerRouteEnded"/>) and they simply pick something else.
    ///
    /// The story takes swimmers from here when someone has to get into trouble (<see cref="TakeSwimmer"/>): the
    /// person you saw swimming is the one who starts drowning. It can also borrow someone for a scene (the thief's
    /// victim, a friend yelling for help) and give them back. Swimmers are topped up out of sight.
    /// </summary>
    public class BeachCrowd : NetworkBehaviour
    {
        public enum Activity : byte { Sunbathe, Swim, Wade, Stroll }

        [SerializeField] private StoryNpc _npcPrefab;
        [Tooltip("Towels: position = middle of the towel, forward = where the feet point (toward the sea).")]
        [SerializeField] private Transform[] _towels = System.Array.Empty<Transform>();
        [Range(0f, 1f)] [SerializeField] private float _towelsTaken = 0.85f;
        [SerializeField] private Vector2 _swimX = new(-24f, 20f);
        [SerializeField] private Vector2 _swimZ = new(-34f, -8f);
        [SerializeField] private Vector2 _wadeX = new(-30f, 30f);
        [SerializeField] private Vector2 _wadeZ = new(-12f, 4f);
        [SerializeField] private int _swimmers = 7;
        [SerializeField] private int _waders = 3;
        [SerializeField] private int _strollers = 5;
        [SerializeField] private int _seed = 1000;

        private const float WalkSpeed = 1.25f;
        private const float SwimMin = 1.7f, SwimMax = 6f;   // swimming water depth
        private const float WadeMin = 0.25f, WadeMax = 0.95f;

        private static readonly List<BeachCrowd> _all = new();

        private static readonly string[] SmallTalk =
        {
            "Lovely day, isn't it?", "Is it safe to swim here?", "I'm not going in. There are FISH in there.",
            "Have you seen my sunglasses?", "Could you rub some sunscreen on my back? ...No? OK.",
            "Ooh, a real lifeguard!", "I can't really swim, but I'm going in anyway!", "Shhh, I'm tanning.",
            "The water's lovely! You should come in!", "My husband went for a swim an hour ago...",
            "Is that shark fin real or a costume?", "Five stars for this beach. Minus one for the seagulls."
        };

        private static readonly string[] SwimTalk =
        {
            "The water's great!", "Don't worry, I'm a strong swimmer!", "Is that a jellyfish?!", "Whee!",
            "I can touch the bottom... nope, I can't.", "Race you to the buoy!"
        };

        /// <summary>Two tourists chatting: opener, reply.</summary>
        private static readonly (string, string)[] Chats =
        {
            ("Where are you from?", "Oh, up north. It's raining there right now!"), ("Nice tan!", "Thanks! Day three."),
            ("Have you tried the coconuts?", "They're amazing! Shake a palm."), ("Is the water cold?", "Only for the first minute."),
            ("Did you see that jet ski?", "The lifeguards drive like maniacs."), ("I think I'm burning.", "You're RED. Sunscreen!"),
            ("Want to go for a swim?", "In a bit. After my nap."), ("The lifeguard waved at me!", "He waves at everyone."),
            ("I lost my flip-flop.", "Try the Lost and Found!"), ("Beautiful sunset later?", "Best on the island.")
        };

        private sealed class Member
        {
            public StoryNpc Npc;
            public Activity Home;            // what they came to do (and go back to)
            public NpcActivity Now;
            public int Towel = -1;
            public AvatarPose TowelPose;
            public bool Borrowed;
            public float NextThink;
            public float SwimUntil;          // a trip into the water ends here (then back to the towel / the sand)
            public Vector3? Target;          // where the current walk/swim goes (resumed after a chat)
            public float TargetSpeed;
            public int Failures;             // routes given up in a row
            public Member Partner;           // chatting with
            public int ChatLine;
            public int ChatIndex;
            public float ChatStarted;
            public float BusyUntil;          // talking to a player
            public AvatarPose? NextPose;     // the second half of a pose change (via sitting up)
            public float NextPoseAt;
            // Swimming (see SwimThink): a planned route of legs, a deadline per leg, pauses to tread water.
            public readonly List<Vector3> Legs = new();
            public int LegIndex;
            public string RouteKind;
            public float LegDeadline;
            public float SwimSpeed;          // this person's cruising speed (m/s), picked once
            public float TreadUntil;
            public bool JustTreaded;
            public int SwimFails;            // routes that went nowhere in a row
        }

        private readonly List<Member> _members = new();
        private System.Random _rng;
        private float _nextTopUp;
        private int _spawned;
        private Rect _sand;                  // x/z box of the beach (from the towels), where strollers walk

        public static IReadOnlyList<BeachCrowd> All => _all;
        public int Count => _members.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();

        /// <summary>The crowd whose swimming area is nearest to a point.</summary>
        public static BeachCrowd Nearest(Vector3 point)
        {
            BeachCrowd best = null;
            float bestSq = float.MaxValue;
            foreach (BeachCrowd c in _all)
            {
                var center = new Vector3((c._swimX.x + c._swimX.y) * 0.5f, 0f, (c._swimZ.x + c._swimZ.y) * 0.5f);
                float d = (new Vector3(point.x, 0f, point.z) - center).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = c;
                }
            }
            return best;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _rng = new System.Random(_seed);
            StoryNpc.ServerTalked += OnTalked;
            StartCoroutine(Populate());
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            StoryNpc.ServerTalked -= OnTalked;
        }

        // ------------------------------------------------------------------ populating

        private IEnumerator Populate()
        {
            // Scene objects (terrain colliders, water) are up once the first player is.
            while (PlayerHub.All.Count == 0) yield return null;
            yield return null;
            _sand = SandBox();
            for (int i = 0; i < _towels.Length; i++)
            {
                if (_rng.NextDouble() > _towelsTaken) continue;
                AvatarPose pose = Pick(AvatarPose.Lie, AvatarPose.Lie, AvatarPose.LieFront, AvatarPose.Sit);
                Member m = AddMember(Activity.Sunbathe, TowelPlace(i, out float yaw), yaw, i, pose);
                m.NextThink = Time.time + 20f + (float)_rng.NextDouble() * 60f;
            }
            for (int i = 0; i < _swimmers; i++)
                if (TryWaterSpot(_swimX, _swimZ, SwimMin, 9f, out Vector3 p)) AddMember(Activity.Swim, p, (float)_rng.NextDouble() * 360f);
            for (int i = 0; i < _waders; i++)
                if (TryWaterSpot(_wadeX, _wadeZ, WadeMin, WadeMax, out Vector3 p)) AddMember(Activity.Wade, p, (float)_rng.NextDouble() * 360f);
            for (int i = 0; i < _strollers; i++)
                if (TrySandSpot(null, 0f, out Vector3 p)) AddMember(Activity.Stroll, p, (float)_rng.NextDouble() * 360f);
            Debug.Log($"[Crowd] {name}: {_members.Count} tourists on the beach and in the water");
        }

        /// <summary>The beach: the towels' spread, a few metres more each way.</summary>
        private Rect SandBox()
        {
            if (_towels.Length == 0) return new Rect(_wadeX.x, _wadeZ.y, _wadeX.y - _wadeX.x, 12f);
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Transform t in _towels)
            {
                if (t == null) continue;
                x0 = Mathf.Min(x0, t.position.x);
                x1 = Mathf.Max(x1, t.position.x);
                z0 = Mathf.Min(z0, t.position.z);
                z1 = Mathf.Max(z1, t.position.z);
            }
            return Rect.MinMaxRect(x0 - 6f, z0 - 7f, x1 + 6f, z1 + 7f);
        }

        private Member AddMember(Activity activity, Vector3 position, float yaw, int towel = -1, AvatarPose pose = AvatarPose.Normal)
        {
            int seed = _seed * 31 + ++_spawned * 7919;
            bool female = (seed / 7 + _spawned) % 2 == 0;
            AvatarLook look = AvatarLook.RandomTourist(seed, female ? 1 : 0);
            // Beach people: men mostly shirtless (the look already favours swimwear for women).
            if (!female && activity != Activity.Sunbathe && _rng.NextDouble() < 0.7) look.Top = TopStyle.None;
            StoryNpc npc = Instantiate(_npcPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            Spawn(npc.gameObject);
            string displayName = VictimBrain.RandomName(female);
            npc.ServerSetup(displayName, NpcRole.Guest, look);
            npc.ServerSetPose(pose);
            npc.ServerSetMood(activity == Activity.Sunbathe ? AvatarMood.Happy : AvatarMood.Neutral);
            npc.ServerSetTalkable(true, $"Chat with {displayName}");
            var m = new Member
            {
                Npc = npc, Home = activity, Towel = towel, TowelPose = pose,
                Now = activity switch { Activity.Sunbathe => NpcActivity.Sunbathe, Activity.Swim => NpcActivity.Swim, Activity.Wade => NpcActivity.Wade, _ => NpcActivity.Stroll },
                NextThink = Time.time + (float)_rng.NextDouble() * 4f
            };
            npc.Activity = m.Now;
            npc.ServerRouteEnded += OnRouteEnded;
            _members.Add(m);
            return m;
        }

        /// <summary>
        /// Where the character's root goes for its towel: the feet end. Lying on the back or belly and sitting up all
        /// use this one spot (the animator puts the body behind it), so changing pose never moves them.
        /// </summary>
        private Vector3 TowelPlace(int towel, out float yaw)
        {
            Transform t = _towels[towel];
            yaw = t.eulerAngles.y;
            return t.position + t.forward * 0.45f;
        }

        private bool TryWaterSpot(Vector2 xRange, Vector2 zRange, float minDepth, float maxDepth, out Vector3 spot)
        {
            for (int i = 0; i < 30; i++)
            {
                var p = new Vector3(Mathf.Lerp(xRange.x, xRange.y, (float)_rng.NextDouble()), 0f, Mathf.Lerp(zRange.x, zRange.y, (float)_rng.NextDouble()));
                if (WaterSpotAt(ref p, minDepth, maxDepth))
                {
                    spot = p;
                    return true;
                }
            }
            spot = default;
            return false;
        }

        /// <summary>Is (x, z) water of this depth, clear of posts and buoys? Sets y: standing on the bottom, or treading water.</summary>
        private static bool WaterSpotAt(ref Vector3 p, float minDepth, float maxDepth)
        {
            p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) : -0.35f;
            float depth = Shore.WaterDepthAt(p);
            if (depth < minDepth || depth > maxDepth) return false;
            if (Blocked(p) || !StoryNpc.OpenSky(p)) return false; // not in a post, not under the dock
            float ground = Shore.GroundHeightAt(p + Vector3.up * 3f);
            p.y = depth > 1.4f ? p.y - 1.4f : float.IsNaN(ground) ? p.y : ground;
            return true;
        }

        /// <summary>
        /// Something solid (a dock post, a buoy, a rock, a moored jet ski, a floating crate) right at this spot (the
        /// seabed doesn't count, nor do other characters: they move and are steered round).
        /// </summary>
        private static bool Blocked(Vector3 p)
        {
            Collider[] hits = Physics.OverlapSphere(p + Vector3.down * 0.3f, 0.6f, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                if (c.GetComponent<Seabed>() != null || c is TerrainCollider) continue;
                if (c.attachedRigidbody != null && StoryNpc.IsCharacter(c.attachedRigidbody)) continue;
                return true;
            }
            return false;
        }

        /// <summary>A dry, open spot on the beach (not indoors, not in a palm), near <paramref name="near"/> if given.</summary>
        private bool TrySandSpot(Vector3? near, float radius, out Vector3 spot)
        {
            for (int i = 0; i < 25; i++)
            {
                Vector3 p;
                if (near.HasValue)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Mathf.Lerp(radius * 0.4f, radius, (float)_rng.NextDouble());
                    p = new Vector3(Mathf.Clamp(near.Value.x + r.x, _sand.xMin, _sand.xMax), 0f, Mathf.Clamp(near.Value.z + r.y, _sand.yMin, _sand.yMax));
                }
                else p = new Vector3(Mathf.Lerp(_sand.xMin, _sand.xMax, (float)_rng.NextDouble()), 0f, Mathf.Lerp(_sand.yMin, _sand.yMax, (float)_rng.NextDouble()));
                if (!NavMesh.SamplePosition(p + Vector3.up * 2f, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                p = hit.position;
                float ground = Shore.GroundHeightAt(p + Vector3.up * 1f);
                if (float.IsNaN(ground)) continue;
                p.y = ground;
                if (Shore.WaterDepthAt(p + Vector3.up * 0.2f) > -0.05f) continue;  // wet
                if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.up, 12f, ~0, QueryTriggerInteraction.Ignore)) continue; // under a roof
                if (CapsuleBlocked(p)) continue;
                if (TowelNear(p, 1.3f)) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        private static bool CapsuleBlocked(Vector3 p)
        {
            foreach (Collider c in Physics.OverlapCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody == null && c.GetComponent<Seabed>() == null && !(c is TerrainCollider)) return true;
            return false;
        }

        /// <summary>Don't stop on someone's towel.</summary>
        private bool TowelNear(Vector3 p, float radius)
        {
            foreach (Transform t in _towels)
                if (t != null && (new Vector2(t.position.x - p.x, t.position.z - p.z)).sqrMagnitude < radius * radius) return true;
            return false;
        }

        private T Pick<T>(params T[] options) => options[_rng.Next(options.Length)];
        private float Range(float a, float b) => Mathf.Lerp(a, b, (float)_rng.NextDouble());

        // ------------------------------------------------------------------ living (host)

        private void Update()
        {
            if (!IsServerInitialized) return;
            for (int i = _members.Count - 1; i >= 0; i--)
                if (_members[i].Npc == null || !_members[i].Npc.IsSpawned) Forget(_members[i]);
            foreach (Member m in _members.ToArray())
            {
                if (m.NextPose.HasValue && Time.time >= m.NextPoseAt)
                {
                    m.Npc.ServerSetPose(m.NextPose.Value);
                    m.NextPose = null;
                }
                if (m.Borrowed || Time.time < m.NextThink || Time.time < m.BusyUntil) continue;
                Think(m);
            }
            TopUp();
        }

        private void Think(Member m)
        {
            StoryNpc npc = m.Npc;
            m.NextThink = Time.time + 0.5f;
            switch (m.Now)
            {
                case NpcActivity.Sunbathe:
                    if (m.Home == Activity.Sunbathe && _rng.NextDouble() < 0.4)
                    {
                        GoSwimming(m); // up for a dip
                        return;
                    }
                    // Turn over now and then (or sit up for a look around). Same root: no jump.
                    AvatarPose next = m.TowelPose == AvatarPose.Sit ? Pick(AvatarPose.Lie, AvatarPose.LieFront)
                        : _rng.NextDouble() < 0.5 ? AvatarPose.Sit : m.TowelPose == AvatarPose.Lie ? AvatarPose.LieFront : AvatarPose.Lie;
                    ChangePose(m, next);
                    m.TowelPose = next;
                    m.NextThink = Time.time + Range(35f, 90f);
                    return;

                case NpcActivity.Stroll:
                    if (npc.IsMoving) { m.NextThink = Time.time + 1f; return; }
                    double roll = _rng.NextDouble();
                    if (roll < 0.25 && StartChat(m)) return;
                    if (roll < 0.4)
                    {
                        GoSwimming(m);
                        return;
                    }
                    if (TrySandSpot(npc.transform.position, 18f, out Vector3 sand)) Walk(m, sand, WalkSpeed * Range(0.8f, 1.15f), water: false);
                    else m.NextThink = Time.time + 2f;
                    return;

                case NpcActivity.Swim:
                    SwimThink(m);
                    return;

                case NpcActivity.Wade:
                    if (npc.IsMoving) { m.NextThink = Time.time + 1f; return; }
                    if (m.Home is Activity.Sunbathe or Activity.Stroll && Time.time > m.SwimUntil)
                    {
                        GoBack(m);
                        return;
                    }
                    bool wade = m.Now == NpcActivity.Wade;
                    if (NearbyWaterSpot(npc.transform.position, wade ? 9f : 12f, wade ? _wadeX : _swimX, wade ? _wadeZ : _swimZ,
                            wade ? WadeMin : SwimMin, wade ? WadeMax : SwimMax, out Vector3 water))
                        Walk(m, water, wade ? Range(0.7f, 1f) : Range(0.65f, 1.1f), water: true);
                    else m.NextThink = Time.time + 1f;
                    if (!wade && _rng.NextDouble() < 0.05 && Near(npc.transform.position, 14f)) npc.ServerSay(SwimTalk[_rng.Next(SwimTalk.Length)]);
                    return;

                case NpcActivity.Chat:
                    ChatTick(m);
                    return;

                case NpcActivity.GoSwim:
                case NpcActivity.Return:
                    // Walking somewhere; resumed here after a chat with a player or a blocked route.
                    if (!npc.IsMoving && m.Target.HasValue) Walk(m, m.Target.Value, m.TargetSpeed, water: true);
                    else m.NextThink = Time.time + 1f;
                    return;
            }
        }

        /// <summary>Off into the sea (from a towel or the sand), for a while.</summary>
        private void GoSwimming(Member m)
        {
            StoryNpc npc = m.Npc;
            Vector3 from = npc.transform.position;
            bool wasLying = m.Now == NpcActivity.Sunbathe;
            // Out from here: water of swimming depth, within the swimming area, not far along the beach.
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector3(Mathf.Clamp(from.x + Range(-8f, 8f), _swimX.x, _swimX.y), 0f, Range(_swimZ.x, _swimZ.y));
                if (!WaterSpotAt(ref p, SwimMin, 4f)) continue;
                SetNow(m, NpcActivity.GoSwim);
                float getUp = ChangePose(m, AvatarPose.Normal); // sit up, then stand
                npc.ServerSetMood(AvatarMood.Happy);
                m.SwimUntil = Time.time + Range(40f, 100f);
                m.Target = p;
                m.TargetSpeed = WalkSpeed * Range(0.9f, 1.15f);
                m.NextThink = Time.time + (wasLying ? getUp + 0.6f : 0.1f); // a moment to get up off the towel
                return;
            }
            m.NextThink = Time.time + 5f;
        }

        /// <summary>Out of the water: back to the towel (then lie down) or back onto the sand.</summary>
        private void GoBack(Member m)
        {
            if (m.Home == Activity.Sunbathe && m.Towel >= 0)
            {
                m.Target = TowelPlace(m.Towel, out _);
            }
            else if (TrySandSpot(ClosestOnSand(m.Npc.transform.position), 8f, out Vector3 sand)) m.Target = sand;
            else if (TrySandSpot(null, 0f, out sand)) m.Target = sand;
            else
            {
                m.NextThink = Time.time + 3f;
                return;
            }
            SetNow(m, NpcActivity.Return);
            m.TargetSpeed = WalkSpeed;
            Walk(m, m.Target.Value, WalkSpeed, water: true);
        }

        /// <summary>
        /// Changes pose the way people do: lying down, getting up and turning over all go through sitting up (a direct
        /// blend from lying on the back to lying on the belly swings the body up through a stiff plank). Returns how
        /// long until the final pose starts.
        /// </summary>
        private float ChangePose(Member m, AvatarPose pose)
        {
            AvatarPose now = m.NextPose ?? m.Npc.Pose;
            bool lyingNow = now is AvatarPose.Lie or AvatarPose.LieFront;
            bool lyingNext = pose is AvatarPose.Lie or AvatarPose.LieFront;
            bool via = (lyingNow && pose != AvatarPose.Sit && pose != now) || (now == AvatarPose.Normal && lyingNext);
            m.NextPose = null;
            if (!via)
            {
                m.Npc.ServerSetPose(pose);
                return 0f;
            }
            m.Npc.ServerSetPose(AvatarPose.Sit);
            m.NextPose = pose;
            m.NextPoseAt = Time.time + 0.9f;
            return 0.9f;
        }

        private Vector3 ClosestOnSand(Vector3 p) =>
            new(Mathf.Clamp(p.x, _sand.xMin + 2f, _sand.xMax - 2f), p.y, Mathf.Clamp(p.z, _sand.yMin + 2f, _sand.yMax - 2f));

        private void Walk(Member m, Vector3 target, float speed, bool water)
        {
            m.Target = target;
            m.TargetSpeed = speed;
            m.Npc.ServerMoveTo(target, speed, water);
            m.NextThink = Time.time + 1f;
        }

        private void SetNow(Member m, NpcActivity now)
        {
            m.Now = now;
            m.Npc.Activity = now;
            if (now is NpcActivity.Stroll or NpcActivity.Swim or NpcActivity.Wade or NpcActivity.Sunbathe) m.Target = null;
            m.Legs.Clear();
            m.LegIndex = 0;
            m.TreadUntil = 0f;
        }

        /// <summary>A route ended (host): arrived somewhere, or gave up. Decide what's next right away.</summary>
        private void OnRouteEnded(StoryNpc npc, bool arrived)
        {
            Member m = _members.Find(x => x.Npc == npc);
            if (m == null || m.Borrowed) return;
            m.Failures = arrived ? 0 : m.Failures + 1;
            switch (m.Now)
            {
                case NpcActivity.GoSwim:
                    if (arrived || npc.IsSwimming)
                    {
                        SetNow(m, NpcActivity.Swim);
                        m.NextThink = Time.time + Range(0.5f, 2f);
                    }
                    else if (m.Failures > 2) GoBack(m);
                    else m.NextThink = Time.time + 0.5f; // Think re-walks to the same target
                    break;

                case NpcActivity.Return:
                    bool towel = m.Home == Activity.Sunbathe && m.Towel >= 0;
                    Vector3 spot = towel ? TowelPlace(m.Towel, out float yaw) : npc.transform.position;
                    float off = Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(npc.transform.position.x, npc.transform.position.z));
                    if (towel && (arrived || off < 0.6f || (m.Failures > 3 && !Near(npc.transform.position, 25f))))
                    {
                        // Onto the towel: settle exactly on it and lie back down.
                        TowelPlace(m.Towel, out yaw);
                        npc.ServerTeleport(spot, yaw, keepExact: true);
                        m.TowelPose = Pick(AvatarPose.Lie, AvatarPose.Lie, AvatarPose.LieFront, AvatarPose.Sit);
                        ChangePose(m, m.TowelPose); // sit down first, then lie back
                        SetNow(m, NpcActivity.Sunbathe);
                        m.NextThink = Time.time + Range(40f, 100f);
                    }
                    else if (!towel && (arrived || m.Failures > 2) && !npc.IsSwimming)
                    {
                        SetNow(m, NpcActivity.Stroll);
                        m.NextThink = Time.time + Range(1f, 4f);
                    }
                    else if (m.Failures > 2) GoBack(m); // a different way / spot
                    else m.NextThink = Time.time + 0.5f;
                    break;

                case NpcActivity.Stroll:
                    // Arrived: look around a little (not long), then off again.
                    m.NextThink = Time.time + (arrived ? Range(1.5f, 6f) : 0.3f);
                    break;

                case NpcActivity.Swim:
                    if (arrived)
                    {
                        // Carry on with the next leg almost at once (a swimmer turning at a buoy, not stopping).
                        if (m.Target.HasValue) RememberGoodSwimSpot(m.Target.Value);
                        m.SwimFails = 0;
                        m.NextThink = Time.time + Range(0.15f, 0.7f);
                    }
                    else
                    {
                        m.Legs.Clear(); // this route is off; a fresh one next think
                        m.SwimFails++;
                        m.NextThink = Time.time + 0.3f;
                    }
                    break;

                case NpcActivity.Wade:
                    m.NextThink = Time.time + (arrived ? Range(1f, 5f) : 0.3f);
                    break;
            }
        }

        // ------------------------------------------------------------------ swimming (host)

        private const float MaxTread = 12f;           // never tread water longer than this in one go
        private readonly List<Vector3> _goodSwimSpots = new();
        private Creatures.Shark[] _sharks = System.Array.Empty<Creatures.Shark>();
        private float _nextSharkScan;

        /// <summary>
        /// A swimmer's next move, checked in this order (the first that applies wins):
        /// 1. on a leg: keep going; past the leg's deadline, or a jet ski bearing down, or a shark close: break off;
        /// 2. a visitor whose swim time is up: back to the towel / the sand;
        /// 3. drifted out of the swimming area or into the shallows: swim back in;
        /// 4. treading water: until the pause ends (never more than <see cref="MaxTread"/> s);
        /// 5. more legs on the route: the next one (re-checked: still water, still a clear line);
        /// 6. a new plan: now and then a pause to tread water (never twice running), otherwise a route (a lap along
        ///    the shore and back, out to the buoys and back, out and back, or a loop), then plain nearby spots, then
        ///    spots toward the middle of the area, then places swimmers reached before; nothing at all: tread 2 s.
        /// Every target is swimming-depth water inside the area, open sky, a line a swimmer can really swim.
        /// </summary>
        private void SwimThink(Member m)
        {
            StoryNpc npc = m.Npc;
            Vector3 p = npc.transform.position;
            if (m.SwimSpeed <= 0f) m.SwimSpeed = Range(0.9f, 1.35f);
            m.NextThink = Time.time + 0.5f;

            if (SharkNear(p, 16f, out Vector3 shark))
            {
                // Everyone heads for the beach; visitors go home, locals wait near the shallow edge.
                if (m.Home != Activity.Swim) { npc.ServerStop(); GoBack(m); return; }
                if (!npc.IsMoving || m.RouteKind != "flee")
                {
                    npc.ServerShout(Pick("SHARK!", "Get out of the water!", "Aaah!"), cry: true);
                    SwimAwayFrom(m, shark, 10f, "flee");
                }
                return;
            }

            if (npc.IsMoving)
            {
                if (m.RouteKind != "dodge" && m.RouteKind != "flee" && VehicleBearingDown(p, out Vector3 vehicle))
                {
                    SwimAwayFrom(m, vehicle, 6f, "dodge");
                    return;
                }
                if (Time.time > m.LegDeadline)
                {
                    Debug.Log($"[Crowd] {npc.Name}: swim leg ({m.RouteKind}) took too long, new plan");
                    npc.ServerStop();
                    m.Legs.Clear();
                    m.SwimFails++;
                    m.NextThink = Time.time + 0.2f;
                }
                return;
            }

            if (m.Home is Activity.Sunbathe or Activity.Stroll && Time.time > m.SwimUntil)
            {
                GoBack(m);
                return;
            }

            if (!InSwimArea(p, 3f) || Shore.WaterDepthAt(p + Vector3.up * 0.1f) < SwimMin - 0.6f)
            {
                m.Legs.Clear();
                if (TowardMiddle(p, out Vector3 back))
                {
                    SetRoute(m, "back in", back);
                    StartLeg(m);
                    return;
                }
            }

            if (Time.time < m.TreadUntil)
            {
                m.NextThink = Mathf.Min(m.TreadUntil, Time.time + 1f);
                return;
            }

            if (m.LegIndex < m.Legs.Count && StartLeg(m)) return;

            if (!m.JustTreaded && m.SwimFails == 0 && _rng.NextDouble() < 0.3)
            {
                // A breather: tread water, look around, maybe say something.
                m.JustTreaded = true;
                m.TreadUntil = Time.time + Mathf.Min(MaxTread, Range(3f, 9f));
                m.NextThink = Time.time + 1f;
                if (_rng.NextDouble() < 0.2 && Near(p, 14f)) npc.ServerSay(SwimTalk[_rng.Next(SwimTalk.Length)]);
                return;
            }
            m.JustTreaded = false;

            if (PlanSwimRoute(m) && StartLeg(m)) return;
            foreach (float radius in new[] { 12f, 20f })
                if (NearbyWaterSpot(p, radius, _swimX, _swimZ, SwimMin, SwimMax, out Vector3 near))
                {
                    SetRoute(m, "nearby", near);
                    if (StartLeg(m)) return;
                }
            if (TowardMiddle(p, out Vector3 middle))
            {
                SetRoute(m, "middle", middle);
                if (StartLeg(m)) return;
            }
            foreach (Vector3 known in _goodSwimSpots.ToArray())
            {
                if ((known - p).sqrMagnitude < 9f || !StoryNpc.SwimLineClear(p, known)) continue;
                SetRoute(m, "known", known);
                if (StartLeg(m)) return;
            }

            // Boxed in (a crowd of floating things all round): tread a moment and try again. A visitor who keeps
            // failing goes back to the beach; a regular swimmer who is truly stuck is moved when nobody's looking.
            m.SwimFails++;
            m.TreadUntil = Time.time + 2f;
            if (m.SwimFails > 5 && m.Home != Activity.Swim) GoBack(m);
            else if (m.SwimFails > 8 && !Near(p, 30f) && TryWaterSpot(_swimX, _swimZ, SwimMin, SwimMax, out Vector3 fresh))
            {
                Debug.Log($"[Crowd] {npc.Name}: boxed in at {p:F1}, moved out of sight");
                npc.ServerTeleport(fresh, (float)_rng.NextDouble() * 360f, keepExact: true);
                m.SwimFails = 0;
            }
        }

        private void SetRoute(Member m, string kind, params Vector3[] legs)
        {
            m.Legs.Clear();
            m.Legs.AddRange(legs);
            m.LegIndex = 0;
            m.RouteKind = kind;
        }

        /// <summary>Swim the next leg that's still good; false when none is left.</summary>
        private bool StartLeg(Member m)
        {
            Vector3 p = m.Npc.transform.position;
            while (m.LegIndex < m.Legs.Count)
            {
                Vector3 t = m.Legs[m.LegIndex++];
                if (!WaterSpotAt(ref t, SwimMin - 0.4f, SwimMax + 1f)) continue;   // the water's not right there any more
                bool straight = StoryNpc.SwimLineClear(p, t);
                if (!straight && !StoryNpc.SwimDetour(p, t, out _)) continue;       // no way round either
                float distance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(t.x, t.z));
                if (distance < 1f) continue;
                float speed = m.SwimSpeed * Range(0.92f, 1.08f);
                Walk(m, t, speed, water: true);
                m.NextThink = Time.time + 0.5f;
                // Twice the time it should take (a detour is longer), plus slack for dodging others.
                m.LegDeadline = Time.time + distance / speed * (straight ? 2f : 2.8f) + 6f;
                return true;
            }
            m.Legs.Clear();
            m.LegIndex = 0;
            return false;
        }

        /// <summary>
        /// A route of a few legs from here, all swimming-depth water in the area with clear lines between them.
        /// Kinds: a lap along the shore and back, out to the buoy line and back, out and back, or a loop.
        /// </summary>
        private bool PlanSwimRoute(Member m)
        {
            Vector3 p = m.Npc.transform.position;
            Vector3 along = AlongShore(p), outward = Outward(p);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int kind = _rng.Next(4);
                var legs = new List<Vector3>();
                string name;
                switch (kind)
                {
                    case 0: // lap: along the beach, a little further out, and back
                        name = "lap";
                        float dir = _rng.NextDouble() < 0.5 ? -1f : 1f;
                        float length = Range(9f, 18f);
                        legs.Add(p + along * (dir * length) + outward * Range(-1.5f, 2.5f));
                        legs.Add(p + along * (dir * Range(1f, 4f)) + outward * Range(-1f, 1f));
                        break;
                    case 1: // out to the buoy line (or the far edge), then back toward the beach
                        name = "buoys";
                        Vector3 buoy = NearestBuoy(p, 30f) ?? p + outward * Range(8f, 14f);
                        Vector3 inside = buoy - outward * 2.5f; // just this side of the line, not tangled in it
                        legs.Add(inside);
                        legs.Add(inside - outward * Range(6f, 11f) + along * Range(-4f, 4f));
                        break;
                    case 2: // straight out and back
                        name = "out and back";
                        Vector3 far = p + outward * Range(6f, 12f) + along * Range(-3f, 3f);
                        legs.Add(far);
                        legs.Add(p + along * Range(-3f, 3f));
                        break;
                    default: // a loop of 3-4 points round a circle
                        name = "loop";
                        float radius = Range(4f, 7f);
                        Vector3 centre = p + outward * radius;
                        float start = (float)_rng.NextDouble() * 360f;
                        int points = _rng.Next(3, 5);
                        for (int i = 1; i <= points; i++)
                            legs.Add(centre + Quaternion.Euler(0f, start + i * 360f / points, 0f) * Vector3.forward * radius);
                        break;
                }
                // Keep only a chain of good points: each in the area, right depth, reachable from the one before.
                Vector3 from = p;
                var good = new List<Vector3>();
                foreach (Vector3 leg in legs)
                {
                    Vector3 t = ClampToSwimArea(leg);
                    if (!WaterSpotAt(ref t, SwimMin, SwimMax)) break;
                    if (VehicleNear(t, 6f)) break;
                    if (!StoryNpc.SwimLineClear(from, t) && !StoryNpc.SwimDetour(from, t, out _)) break;
                    good.Add(t);
                    from = t;
                }
                if (good.Count == 0) continue;
                SetRoute(m, name, good.ToArray());
                return true;
            }
            return false;
        }

        /// <summary>Break off and swim away from a danger (a shark, a jet ski), toward the beach side if possible.</summary>
        private void SwimAwayFrom(Member m, Vector3 danger, float distance, string kind)
        {
            Vector3 p = m.Npc.transform.position;
            Vector3 away = p - danger;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -Outward(p);
            Vector3 shoreward = -Outward(p);
            foreach (Vector3 dir in new[] { (away + shoreward * 0.5f).normalized, away, Quaternion.Euler(0f, 60f, 0f) * away, Quaternion.Euler(0f, -60f, 0f) * away })
            {
                Vector3 t = ClampToSwimArea(p + dir * distance);
                if (!WaterSpotAt(ref t, SwimMin - 0.4f, SwimMax + 1f) || !StoryNpc.SwimLineClear(p, t)) continue;
                SetRoute(m, kind, t);
                float keep = m.SwimSpeed;
                m.SwimSpeed = Mathf.Max(keep, 1.5f); // a burst
                StartLeg(m);
                m.SwimSpeed = keep;
                return;
            }
        }

        private bool InSwimArea(Vector3 p, float margin) =>
            p.x > _swimX.x - margin && p.x < _swimX.y + margin && p.z > _swimZ.x - margin && p.z < _swimZ.y + margin;

        private Vector3 ClampToSwimArea(Vector3 p) =>
            new(Mathf.Clamp(p.x, _swimX.x + 0.5f, _swimX.y - 0.5f), p.y, Mathf.Clamp(p.z, _swimZ.x + 0.5f, _swimZ.y - 0.5f));

        private bool TowardMiddle(Vector3 p, out Vector3 spot)
        {
            var centre = new Vector3((_swimX.x + _swimX.y) * 0.5f, p.y, (_swimZ.x + _swimZ.y) * 0.5f);
            for (int i = 0; i < 10; i++)
            {
                Vector3 t = Vector3.Lerp(ClampToSwimArea(p), centre, 0.3f + i * 0.07f) + new Vector3(Range(-3f, 3f), 0f, Range(-3f, 3f));
                if (WaterSpotAt(ref t, SwimMin, SwimMax) && StoryNpc.SwimLineClear(p, t))
                {
                    spot = t;
                    return true;
                }
            }
            spot = default;
            return false;
        }

        /// <summary>Which way the water gets deeper here (the open sea), flat and normalised.</summary>
        private static Vector3 Outward(Vector3 p)
        {
            Vector3 grad = Vector3.zero;
            foreach (Vector3 d in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
                grad += d * Shore.WaterDepthAt(p + d * 5f + Vector3.up * 0.1f);
            grad.y = 0f;
            return grad.sqrMagnitude > 1e-4f ? grad.normalized : Vector3.back;
        }

        private static Vector3 AlongShore(Vector3 p) => Vector3.Cross(Vector3.up, Outward(p));

        private void RememberGoodSwimSpot(Vector3 spot)
        {
            if (!InSwimArea(spot, 0f)) return;
            foreach (Vector3 s in _goodSwimSpots) if ((s - spot).sqrMagnitude < 16f) return;
            if (_goodSwimSpots.Count >= 24) _goodSwimSpots.RemoveAt(0);
            _goodSwimSpots.Add(spot);
        }

        private static readonly List<Transform> _buoys = new();
        private static float _buoysFoundAt = float.NegativeInfinity;

        private static Vector3? NearestBuoy(Vector3 p, float range)
        {
            if (Time.time - _buoysFoundAt > 30f)
            {
                _buoysFoundAt = Time.time;
                _buoys.Clear();
                foreach (WaveBobber b in FindObjectsByType<WaveBobber>(FindObjectsSortMode.None))
                    if (b.name.StartsWith("Buoy")) _buoys.Add(b.transform);
            }
            Vector3? best = null;
            float bestSq = range * range;
            foreach (Transform b in _buoys)
            {
                if (b == null) continue;
                float d = (b.position - p).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = b.position; }
            }
            return best;
        }

        private bool SharkNear(Vector3 p, float range, out Vector3 at)
        {
            if (Time.time > _nextSharkScan)
            {
                _nextSharkScan = Time.time + 3f;
                _sharks = FindObjectsByType<Creatures.Shark>(FindObjectsSortMode.None);
            }
            foreach (Creatures.Shark s in _sharks)
                if (s != null && s.isActiveAndEnabled && (s.transform.position - p).sqrMagnitude < range * range)
                {
                    at = s.transform.position;
                    return true;
                }
            at = default;
            return false;
        }

        private static bool VehicleNear(Vector3 p, float range)
        {
            foreach (Vehicles.Vehicle v in Vehicles.Vehicle.All)
                if (v != null && (v.transform.position - p).sqrMagnitude < range * range) return true;
            return false;
        }

        /// <summary>A driven (or autopiloted) vehicle close by and coming this way.</summary>
        private static bool VehicleBearingDown(Vector3 p, out Vector3 at)
        {
            foreach (Vehicles.Vehicle v in Vehicles.Vehicle.All)
            {
                if (v == null || v.Speed < 2f) continue;
                Vector3 d = p - v.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 9f * 9f) continue;
                Vector3 vel = v.Body != null ? v.Body.linearVelocity : Vector3.zero;
                vel.y = 0f;
                if (Vector3.Dot(vel, d) > 0f) // heading our way
                {
                    at = v.transform.position;
                    return true;
                }
            }
            at = default;
            return false;
        }

        // ------------------------------------------------------------------ chatting

        private bool StartChat(Member m)
        {
            Vector3 p = m.Npc.transform.position;
            Member best = null;
            float bestSq = 12f * 12f;
            foreach (Member o in _members)
            {
                if (o == m || o.Borrowed || o.Now != NpcActivity.Stroll || o.Npc.IsMoving || Time.time < o.BusyUntil) continue;
                float d = (o.Npc.transform.position - p).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = o;
                }
            }
            if (best == null) return false;
            // Walk up to them and stand 1.3 m apart; they wait, facing us.
            Vector3 there = best.Npc.transform.position;
            Vector3 away = p - there;
            away.y = 0f;
            Vector3 stand = there + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right) * 1.3f;
            foreach (Member x in new[] { m, best })
            {
                SetNow(x, NpcActivity.Chat);
                x.Partner = x == m ? best : m;
                x.ChatLine = 0;
                x.ChatStarted = Time.time;
                x.NextThink = Time.time + 1f;
            }
            m.ChatIndex = best.ChatIndex = _rng.Next(Chats.Length);
            best.Npc.ServerFace(p);
            m.Npc.ServerMoveTo(stand, WalkSpeed);
            best.NextThink = float.MaxValue; // the one who started the chat drives it
            return true;
        }

        private void ChatTick(Member m)
        {
            Member other = m.Partner;
            if (other == null || other.Now != NpcActivity.Chat || other.Npc == null)
            {
                EndChat(m);
                return;
            }
            if (m.Npc.IsMoving)
            {
                if (Time.time > m.ChatStarted + 15f) EndChat(m); // couldn't get there
                m.NextThink = Time.time + 0.5f;
                return;
            }
            m.Npc.ServerFace(other.Npc.transform.position);
            other.Npc.ServerFace(m.Npc.transform.position);
            (string opener, string reply) = Chats[m.ChatIndex];
            switch (m.ChatLine++)
            {
                case 0: m.Npc.ServerSay(opener); m.Npc.ServerGesture(AvatarGesture.Interact); m.NextThink = Time.time + 2.8f; break;
                case 1: other.Npc.ServerSay(reply); m.NextThink = Time.time + 3.2f; break;
                case 2:
                    if (_rng.NextDouble() < 0.5)
                    {
                        m.Npc.ServerSay(Pick("Ha! True.", "Right?!", "See you around!", "Enjoy the sun!"));
                        m.NextThink = Time.time + 2.4f;
                    }
                    else m.NextThink = Time.time + 0.5f;
                    break;
                default: EndChat(m); break;
            }
        }

        private void EndChat(Member m)
        {
            foreach (Member x in new[] { m, m.Partner })
            {
                if (x == null || x.Now != NpcActivity.Chat) continue;
                x.Partner = null;
                x.Npc.ServerFace(null);
                SetNow(x, NpcActivity.Stroll); // only strollers chat
                x.NextThink = Time.time + Range(0.2f, 1.5f);
            }
        }

        // ------------------------------------------------------------------ helpers

        private bool NearbyWaterSpot(Vector3 from, float radius, Vector2 xRange, Vector2 zRange, float minDepth, float maxDepth, out Vector3 spot)
        {
            for (int i = 0; i < 14; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Range(radius * 0.35f, radius);
                var p = new Vector3(Mathf.Clamp(from.x + r.x, xRange.x, xRange.y), 0f, Mathf.Clamp(from.z + r.y, zRange.x, zRange.y));
                if (!WaterSpotAt(ref p, minDepth, maxDepth)) continue;
                if (minDepth > 1.1f && !StoryNpc.SwimLineClear(from, p)) continue; // round the dock, not under it
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        private static bool Near(Vector3 p, float range)
        {
            foreach (PlayerHub player in PlayerHub.All)
                if ((player.transform.position - p).sqrMagnitude < range * range) return true;
            return false;
        }

        /// <summary>Keep the water busy: new swimmers appear far from any lifeguard.</summary>
        private void TopUp()
        {
            if (Time.time < _nextTopUp) return;
            _nextTopUp = Time.time + 12f;
            int swimming = 0;
            foreach (Member m in _members) if (m.Home == Activity.Swim) swimming++;
            if (swimming >= _swimmers) return;
            if (!TryWaterSpot(_swimX, _swimZ, SwimMin, 9f, out Vector3 p)) return;
            if (Near(p, 25f)) return; // someone would see them pop in
            AddMember(Activity.Swim, p, (float)_rng.NextDouble() * 360f);
        }

        private void OnTalked(StoryNpc npc, PlayerHub by)
        {
            Member m = _members.Find(x => x.Npc == npc);
            if (m == null || m.Borrowed) return;
            // Stop, turn to the lifeguard, answer; carry on a few seconds later (Think resumes the walk).
            if (m.Now == NpcActivity.Chat) EndChat(m); // first: ending a chat clears the facing
            bool lying = m.Now == NpcActivity.Sunbathe;
            if (!lying)
            {
                npc.ServerStop();
                npc.ServerFace(by.transform.position);
            }
            string line = npc.IsSwimming ? SwimTalk[_rng.Next(SwimTalk.Length)] : SmallTalk[Random.Range(0, SmallTalk.Length)];
            npc.ServerSay(line);
            if (!lying && !npc.IsSwimming) npc.ServerGesture(AvatarGesture.Wave, by.transform.position);
            m.BusyUntil = Time.time + 4.5f;
            StartCoroutine(StopFacing(m, 4.5f));
        }

        private IEnumerator StopFacing(Member m, float after)
        {
            yield return new WaitForSeconds(after);
            // (Talked to again meanwhile: that talk's own timer lets go.)
            if (m.Npc != null && m.Now != NpcActivity.Chat && !m.Borrowed && Time.time >= m.BusyUntil - 0.05f) m.Npc.ServerFace(null);
        }

        private void Forget(Member m)
        {
            if (m.Npc != null) m.Npc.ServerRouteEnded -= OnRouteEnded;
            if (m.Partner != null && m.Partner.Partner == m) EndChat(m.Partner);
            _members.Remove(m);
        }

        // ------------------------------------------------------------------ the story's hooks (host)

        /// <summary>
        /// Remove and return a swimmer inside the box, deeper than <paramref name="minDepth"/>, of the given figure
        /// (-1 = anyone): they're about to get into trouble. Prefers ones nobody is standing right next to.
        /// </summary>
        [Server]
        public StoryNpc TakeSwimmer(int figure, Vector2 xRange, Vector2 zRange, float minDepth)
        {
            Member best = null;
            float bestScore = float.MinValue;
            foreach (Member m in _members)
            {
                if (m.Borrowed || m.Now != NpcActivity.Swim) continue;
                Vector3 p = m.Npc.transform.position;
                if (p.x < xRange.x || p.x > xRange.y || p.z < zRange.x || p.z > zRange.y) continue;
                if (figure >= 0 && (m.Npc.Look.Feminine ? 1 : 0) != figure) continue;
                if (Shore.WaterDepthAt(new Vector3(p.x, WaterSurface.Exists ? WaterSurface.HeightAt(p) : 0f, p.z)) < minDepth) continue;
                float score = (float)_rng.NextDouble();
                foreach (PlayerHub player in PlayerHub.All)
                    if ((player.transform.position - p).sqrMagnitude < 8f * 8f) score -= 2f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            if (best == null) return null;
            best.Npc.ServerStop();
            best.Npc.Activity = NpcActivity.None;
            Forget(best);
            return best.Npc;
        }

        /// <summary>Borrow the nearest person doing <paramref name="activity"/> for a scene; give them back with <see cref="Return"/>.</summary>
        [Server]
        public StoryNpc Borrow(Activity activity, Vector3 near, float maxDistance = 60f)
        {
            Member best = null;
            float bestSq = maxDistance * maxDistance;
            foreach (Member m in _members)
            {
                if (m.Borrowed || m.Home != activity) continue;
                // Sunbathers are borrowed from their towel; the others from what they're doing now (not mid-trip).
                if (activity == Activity.Sunbathe ? m.Now != NpcActivity.Sunbathe : m.Now is NpcActivity.GoSwim or NpcActivity.Return) continue;
                float d = (m.Npc.transform.position - near).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = m;
                }
            }
            if (best == null) return null;
            if (best.Now == NpcActivity.Chat) EndChat(best);
            best.Borrowed = true;
            best.Npc.ServerStop();
            best.Npc.ServerFace(null);
            best.Npc.Activity = NpcActivity.None;
            best.Npc.ServerSetTalkable(false);
            return best.Npc;
        }

        /// <summary>Back to what they were doing (sunbathers walk back to their towel and lie down).</summary>
        [Server]
        public void Return(StoryNpc npc)
        {
            Member m = _members.Find(x => x.Npc == npc);
            if (m == null) return;
            m.Borrowed = false;
            npc.ServerKeepShouting(null, Vector3.zero);
            npc.ServerFace(null);
            npc.ServerSetMood(m.Home == Activity.Sunbathe ? AvatarMood.Happy : AvatarMood.Neutral);
            npc.ServerSetTalkable(true, $"Chat with {npc.Name}");
            npc.ServerSetPose(AvatarPose.Normal);
            m.Failures = 0;
            if (m.Home == Activity.Sunbathe) GoBack(m);
            else
            {
                SetNow(m, m.Home switch { Activity.Swim => NpcActivity.Swim, Activity.Wade => NpcActivity.Wade, _ => NpcActivity.Stroll });
                if (m.Now == NpcActivity.Stroll && npc.IsSwimming) GoBack(m);
                m.NextThink = Time.time + 2f;
            }
        }

        [Server]
        public void ReturnAll()
        {
            foreach (Member m in _members.ToArray())
                if (m.Borrowed) Return(m.Npc);
        }

        /// <summary>Is this one of ours (and not lent out)?</summary>
        public bool IsMember(StoryNpc npc) => _members.Exists(m => m.Npc == npc);
    }
}
