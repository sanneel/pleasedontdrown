using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Tourists enjoying the beach (host): sunbathing on towels (on the back, on the belly, sitting up), wading in
    /// the shallows and swimming about. They're story characters (<see cref="StoryNpc"/>), so they cost no physics;
    /// press Interact for a word with them.
    ///
    /// The story takes swimmers from here when someone has to get into trouble (<see cref="TakeSwimmer"/>): the
    /// person you saw swimming is the one who starts drowning. It can also borrow someone for a scene (the thief's
    /// victim, a friend yelling for help) and give them back. Swimmers are topped up out of sight.
    /// </summary>
    public class BeachCrowd : NetworkBehaviour
    {
        public enum Activity : byte { Sunbathe, Swim, Wade }

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
        [SerializeField] private int _seed = 1000;

        private static readonly List<BeachCrowd> _all = new();

        private static readonly string[] SmallTalk =
        {
            "Lovely day, isn't it?", "Is it safe to swim here?", "I'm not going in. There are FISH in there.",
            "Have you seen my sunglasses?", "Could you rub some sunscreen on my back? ...No? OK.",
            "Ooh, a real lifeguard!", "I can't really swim, but I'm going in anyway!", "Shhh, I'm tanning.",
            "The water's lovely! You should come in!", "My husband went for a swim an hour ago...",
            "Is that shark fin real or a costume?", "Five stars for this beach. Minus one for the seagulls."
        };

        private sealed class Member
        {
            public StoryNpc Npc;
            public Activity Activity;
            public int Towel = -1;
            public AvatarPose TowelPose;
            public bool Borrowed;
            public float NextThink;
        }

        private readonly List<Member> _members = new();
        private System.Random _rng;
        private float _nextTopUp;
        private int _spawned;

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
            for (int i = 0; i < _towels.Length; i++)
            {
                if (_rng.NextDouble() > _towelsTaken) continue;
                AvatarPose pose = Pick(AvatarPose.Lie, AvatarPose.Lie, AvatarPose.LieFront, AvatarPose.Sit);
                AddMember(Activity.Sunbathe, TowelPlace(i, pose, out float yaw), yaw, i, pose);
            }
            for (int i = 0; i < _swimmers; i++)
                if (TryWaterSpot(_swimX, _swimZ, 1.7f, 9f, out Vector3 p)) AddMember(Activity.Swim, p, (float)_rng.NextDouble() * 360f);
            for (int i = 0; i < _waders; i++)
                if (TryWaterSpot(_wadeX, _wadeZ, 0.25f, 0.95f, out Vector3 p)) AddMember(Activity.Wade, p, (float)_rng.NextDouble() * 360f);
            Debug.Log($"[Crowd] {name}: {_members.Count} tourists on the beach and in the water");
        }

        private Member AddMember(Activity activity, Vector3 position, float yaw, int towel = -1, AvatarPose pose = AvatarPose.Normal)
        {
            int seed = _seed * 31 + ++_spawned * 7919;
            bool female = (seed / 7 + _spawned) % 2 == 0;
            AvatarLook look = AvatarLook.RandomTourist(seed, female ? 1 : 0);
            // Beach people: no shirts or hats indoors-style; the look already favours swimwear for women.
            if (!female && activity != Activity.Sunbathe && _rng.NextDouble() < 0.7) look.Top = TopStyle.None;
            StoryNpc npc = Instantiate(_npcPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            Spawn(npc.gameObject);
            string displayName = VictimBrain.RandomName(female);
            npc.ServerSetup(displayName, NpcRole.Guest, look);
            npc.ServerSetPose(pose);
            npc.ServerSetMood(activity == Activity.Sunbathe ? AvatarMood.Happy : AvatarMood.Neutral);
            npc.ServerSetTalkable(true, $"Chat with {displayName}");
            var m = new Member { Npc = npc, Activity = activity, Towel = towel, TowelPose = pose, NextThink = Time.time + (float)_rng.NextDouble() * 5f };
            _members.Add(m);
            return m;
        }

        /// <summary>Where the character stands so their body lies on the towel in this pose.</summary>
        private Vector3 TowelPlace(int towel, AvatarPose pose, out float yaw)
        {
            Transform t = _towels[towel];
            yaw = t.eulerAngles.y;
            // Lying bodies stretch from the feet (at the root) back 1.8 m; sitting ones sit on the middle.
            return pose == AvatarPose.Sit ? t.position - t.forward * 0.35f : t.position + t.forward * 0.45f;
        }

        private bool TryWaterSpot(Vector2 xRange, Vector2 zRange, float minDepth, float maxDepth, out Vector3 spot)
        {
            for (int i = 0; i < 30; i++)
            {
                var p = new Vector3(Mathf.Lerp(xRange.x, xRange.y, (float)_rng.NextDouble()), 0f, Mathf.Lerp(zRange.x, zRange.y, (float)_rng.NextDouble()));
                p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) : -0.35f;
                float depth = Shore.WaterDepthAt(p);
                if (depth < minDepth || depth > maxDepth) continue;
                float ground = Shore.GroundHeightAt(p + Vector3.up * 3f);
                p.y = depth > 1.4f ? p.y - 1.4f : float.IsNaN(ground) ? p.y : ground;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        private T Pick<T>(params T[] options) => options[_rng.Next(options.Length)];

        // ------------------------------------------------------------------ living (host)

        private void Update()
        {
            if (!IsServerInitialized) return;
            _members.RemoveAll(m => m.Npc == null || !m.Npc.IsSpawned);
            foreach (Member m in _members)
            {
                if (m.Borrowed || Time.time < m.NextThink) continue;
                switch (m.Activity)
                {
                    case Activity.Swim:
                        m.NextThink = Time.time + 6f + (float)_rng.NextDouble() * 9f;
                        // A leisurely swim somewhere nearby in the swimming area.
                        if (NearbyWaterSpot(m.Npc.transform.position, 10f, _swimX, _swimZ, 1.7f, 9f, out Vector3 swim))
                            m.Npc.ServerMoveTo(swim, 0.6f + (float)_rng.NextDouble() * 0.5f);
                        break;
                    case Activity.Wade:
                        m.NextThink = Time.time + 5f + (float)_rng.NextDouble() * 8f;
                        if (_rng.NextDouble() < 0.35) m.Npc.ServerStop(); // stand and look at the sea for a bit
                        else if (NearbyWaterSpot(m.Npc.transform.position, 8f, _wadeX, _wadeZ, 0.2f, 0.95f, out Vector3 wade))
                            m.Npc.ServerMoveTo(wade, 0.8f);
                        break;
                    case Activity.Sunbathe:
                        m.NextThink = Time.time + 30f + (float)_rng.NextDouble() * 40f;
                        // Turn over now and then (or sit up for a look around).
                        AvatarPose next = m.TowelPose == AvatarPose.Sit ? Pick(AvatarPose.Lie, AvatarPose.LieFront) : _rng.NextDouble() < 0.5 ? AvatarPose.Sit : m.TowelPose == AvatarPose.Lie ? AvatarPose.LieFront : AvatarPose.Lie;
                        SetTowelPose(m, next);
                        break;
                }
            }
            TopUp();
        }

        private bool NearbyWaterSpot(Vector3 from, float radius, Vector2 xRange, Vector2 zRange, float minDepth, float maxDepth, out Vector3 spot)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                var p = new Vector3(Mathf.Clamp(from.x + r.x, xRange.x, xRange.y), 0f, Mathf.Clamp(from.z + r.y, zRange.x, zRange.y));
                p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) : -0.35f;
                float depth = Shore.WaterDepthAt(p);
                if (depth < minDepth || depth > maxDepth) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        private void SetTowelPose(Member m, AvatarPose pose)
        {
            if (m.Towel < 0) return;
            m.TowelPose = pose;
            Vector3 p = TowelPlace(m.Towel, pose, out float yaw);
            m.Npc.ServerSetPose(pose);
            m.Npc.ServerTeleport(p, yaw, keepExact: true); // towels are laid out clear of obstacles
        }

        /// <summary>Keep the water busy: new swimmers appear far from any lifeguard.</summary>
        private void TopUp()
        {
            if (Time.time < _nextTopUp) return;
            _nextTopUp = Time.time + 12f;
            int swimming = 0;
            foreach (Member m in _members) if (m.Activity == Activity.Swim) swimming++;
            if (swimming >= _swimmers) return;
            if (!TryWaterSpot(_swimX, _swimZ, 1.7f, 9f, out Vector3 p)) return;
            foreach (PlayerHub player in PlayerHub.All)
                if ((player.transform.position - p).sqrMagnitude < 25f * 25f) return; // someone would see them pop in
            AddMember(Activity.Swim, p, (float)_rng.NextDouble() * 360f);
        }

        private void OnTalked(StoryNpc npc, PlayerHub by)
        {
            Member m = _members.Find(x => x.Npc == npc);
            if (m == null || m.Borrowed) return;
            npc.ServerShout(SmallTalk[Random.Range(0, SmallTalk.Length)], false);
            if (m.Activity != Activity.Sunbathe) npc.ServerGesture(AvatarGesture.Wave);
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
                if (m.Borrowed || m.Activity != Activity.Swim) continue;
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
            _members.Remove(best);
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
                if (m.Borrowed || m.Activity != activity) continue;
                float d = (m.Npc.transform.position - near).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = m;
                }
            }
            if (best == null) return null;
            best.Borrowed = true;
            best.Npc.ServerSetTalkable(false);
            return best.Npc;
        }

        /// <summary>Back to what they were doing (sunbathers lie back down on their towel).</summary>
        [Server]
        public void Return(StoryNpc npc)
        {
            Member m = _members.Find(x => x.Npc == npc);
            if (m == null) return;
            m.Borrowed = false;
            m.NextThink = Time.time + 3f;
            npc.ServerKeepShouting(null, Vector3.zero);
            npc.ServerSetMood(m.Activity == Activity.Sunbathe ? AvatarMood.Happy : AvatarMood.Neutral);
            npc.ServerSetTalkable(true, $"Chat with {npc.Name}");
            if (m.Activity == Activity.Sunbathe) SetTowelPose(m, m.TowelPose);
            else npc.ServerSetPose(AvatarPose.Normal);
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
