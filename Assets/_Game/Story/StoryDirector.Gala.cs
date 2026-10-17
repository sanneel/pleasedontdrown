using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The end of chapter 2 (Docs/16-Plan-Islands-2-3.md): the hotel director, the gala fund and the gala night.
    ///  * 2.5c The gala fund: the director wants a casino gala, the hotel throws it once the team has earned
    ///    $1,000 per lifeguard. A plain bar top left fills with the team's money (no numbers). Meanwhile the beach
    ///    shift runs (guests keep getting into trouble, one in five is a VIP who tips), drunks start fights at the
    ///    Coral Bar (the bouncer job) and the cushion ride is open.
    ///  * 2.6 Gala night: island 2 turns to night, the casino is dressed up, then the pirates land and rob it. The
    ///    fighters are beaten but their boat gets away with the casino's money. The casino closes for good.
    ///  * 2.7 The director hands over the yacht keys (Docs/16: straight to island 3).
    /// The money, the night and the casino's state are the host's and synced.
    /// </summary>
    public partial class StoryDirector
    {
        [Header("Island 2: gala (Docs/16)")]
        [Tooltip("The gala needs this much in the team's wallet per lifeguard.")]
        [SerializeField] private int _galaPerPlayer = 1000;
        [Tooltip("How often a guest gets into trouble during the gala fund (seconds, one lifeguard; more lifeguards, more often).")]
        [SerializeField] private Vector2 _shiftGap = new(14f, 26f);
        [Tooltip("One guest in this many is a VIP (gold hat, tips $100-300).")]
        [SerializeField] private float _vipChance = 0.2f;

        private readonly SyncVar<int> _galaGoal = new SyncVar<int>();
        private readonly SyncVar<bool> _galaNight = new SyncVar<bool>();
        private readonly SyncVar<bool> _casinoClosed = new SyncVar<bool>();
        private bool _galaTest; // 'galamoney' showed the bar outside the story (tests)

        /// <summary>What the gala needs in the wallet (0: no gala bar now).</summary>
        public int GalaGoal => _galaGoal.Value;
        /// <summary>How full the gala bar is (0..1).</summary>
        public float GalaFill01 => _galaGoal.Value > 0 ? Mathf.Clamp01((float)Economy.Money / _galaGoal.Value) : 0f;
        /// <summary>The gala is on: island 2 at night, the casino dressed up.</summary>
        public bool GalaNight => _galaNight.Value;
        /// <summary>After the robbery: no more blackjack (the team can't lose its money there).</summary>
        public bool CasinoClosed => _casinoClosed.Value;

        // Lobby spot of the hotel director (in front of the reception, facing the door).
        private static readonly Vector3 DirectorSpot = new(24.5f, 1.85f, -249.6f);
        private const float DirectorYaw = 0f;

        private StoryNpc _director;

        public static AvatarLook DirectorLook => new()
        {
            Figure = 0, Build = 1, Height = 2, Skin = 2, Hair = HairStyle.Short, HairColor = 5,
            Top = TopStyle.TShirt, TopColor = 1, Bottom = BottomStyle.Trousers, BottomColor = 7,
            Body = AvatarLook.Bodies.HotelDirector
        };

        private void GalaAwake()
        {
            _galaNight.OnChange += (_, next, _) => World.IslandNight.HotelGala = next;
        }

        /// <summary>Host: the hotel's own people (the director in the lobby), once per chapter 2 (made again after a jump).</summary>
        private void PopulateIsland2()
        {
            if (_residents.Exists(r => r.Group == "I2Hotel")) return;
            AddResident("I2Hotel", () =>
            {
                _director = SpawnIndoors("Mr. Castellane", NpcRole.Guide, DirectorLook, DirectorSpot, DirectorYaw);
                _director.ServerSetTalkable(false);
                return _director.gameObject;
            });
            // (No staff yet: the only staff-like bodies, the bartender and the barista, are bikini models. A bellhop in a
            // bikini looked wrong; they wait for a uniformed model.)
            SlotResidents();
            LoungerResidents();
        }

        // Guests at the casino's slot machines (GameSceneBuilder.HotelInterior: six along the outer wall, a low stool in
        // front of each): which machine (0-5), sitting facing it.
        private static readonly int[] SlotGuests = { 1, 4 };
        private const float SlotStoolX = -2.15f, SlotStoolTop = 2.16f, SlotFirstZ = -246.4f, SlotStep = 1.15f, SlotSitDrop = 0.5f;

        private void SlotResidents()
        {
            for (int i = 0; i < SlotGuests.Length; i++)
            {
                int k = i;
                AddResident("I2Hotel", () =>
                {
                    var at = new Vector3(SlotStoolX, SlotStoolTop - SlotSitDrop, SlotFirstZ - SlotGuests[k] * SlotStep);
                    StoryNpc guest = SpawnIndoors(VictimBrain.RandomName(k == 0), NpcRole.Guest, GuestLook(8300 + k * 13, k == 0 ? 1 : 0), at, 270f);
                    guest.ServerSetPose(AvatarPose.SitStool);
                    return guest.gameObject;
                });
            }
        }

        // Guests sitting up on the pool's loungers, backs to the backrest (the set's -z), feet toward the pool. The sitting
        // pose's root is at the feet, the hips SitBack behind them: the hips just in front of the backrest.
        private static readonly (string set, float side)[] LoungerGuests = { ("LoungeS0", -1f), ("LoungeN2", 1f) };
        private const float LoungerSide = 1.2f, LoungerTop = 0.66f, LoungerHipsZ = -0.38f;

        private void LoungerResidents()
        {
            for (int i = 0; i < LoungerGuests.Length; i++)
            {
                int k = i;
                GameObject set = GameObject.Find("Environment/Island2Pool/" + LoungerGuests[k].set);
                if (set == null) continue;
                AddResident("I2Hotel", () =>
                {
                    Transform t = set.transform;
                    Vector3 feet = t.TransformPoint(new Vector3(LoungerGuests[k].side * LoungerSide, LoungerTop, LoungerHipsZ + StoryNpc.SitBack));
                    StoryNpc guest = SpawnIndoors(VictimBrain.RandomName(k == 0), NpcRole.Guest, GuestLook(9100 + k * 17, k == 0 ? 1 : 0), feet, t.eulerAngles.y);
                    guest.ServerSetPose(AvatarPose.Sit);
                    guest.ServerSetMood(AvatarMood.Happy);
                    return guest.gameObject;
                });
            }
        }

        /// <summary>A character inside the hotel, exactly where asked (the navmesh snap put the lobby's people on its roof).</summary>
        private StoryNpc SpawnIndoors(string displayName, NpcRole role, AvatarLook look, Vector3 at, float yaw)
        {
            StoryNpc npc = SpawnNpc(displayName, role, look, at, yaw);
            npc.ServerTeleport(at, yaw, keepExact: true);
            return npc;
        }

        /// <summary>Host: the night and the casino follow the beat the story is at (also after a jump or a reload).</summary>
        private void GalaStateFor(string beatId)
        {
            _galaNight.Value = beatId is "2.6" or "2.7";
            _casinoClosed.Value = beatId == "2.7" || beatId.StartsWith("3.") || beatId == "end";
            if (beatId != "2.5c" && !_galaTest) _galaGoal.Value = 0;
        }

        private int GalaTarget => _galaPerPlayer * Mathf.Max(1, PlayerHub.All.Count);

        // ------------------------------------------------------------------ 2.5c the gala fund

        private IEnumerator GalaFund()
        {
            _island = _island2;
            EnsureOnIsland2();
            StoryNpc director = _director;
            if (director != null)
            {
                SetObjective("The hotel director wants a word (lobby)");
                Marker("Director", director.NetworkObject);
                yield return WaitTalk(director, "Talk to the director");
                director.ServerSetTalkable(false);
                NoMarker();
                director.ServerSetMood(AvatarMood.Happy);
                yield return Say(director, "So YOU are the lifeguards who beat the octopus. Castellane. I run this hotel.");
                yield return Say(director, "I want a gala. The Grand Coral Gala! Champagne, chandeliers, the casino full of rich people.");
                director.ServerSetMood(AvatarMood.Neutral);
                yield return Say(director, "But a gala costs money, and the hotel is... between fortunes. You earn it, I throw the party.");
                yield return Say(director, "Save my guests (the VIPs tip very well), keep the peace at the Coral Bar, run the cushion ride. Off you go!");
            }
            _galaGoal.Value = GalaTarget;
            StartCoroutine(ShiftTourists());
            StartCoroutine(BarFights());
            while (Economy.Money < _galaGoal.Value)
            {
                _galaGoal.Value = GalaTarget; // a friend joined or left
                SetObjective("Earn money for the gala: rescue guests (VIPs tip!), stop bar fights, run the cushion ride");
                yield return new WaitForSeconds(0.5f);
            }
            _shiftOn = false;
            EndBarFight();
            _galaGoal.Value = 0;
            if (director != null)
            {
                director.ServerSetMood(AvatarMood.Happy);
                yield return Say(director, "(over the speaker) Ladies and gentlemen! Tonight, the GRAND CORAL GALA! Casino, nine o'clock. Black tie. Or swim trunks. I don't care!");
            }
        }

        private bool _shiftOn;

        /// <summary>Host: the beach shift while the gala fund runs: a guest in trouble every so often (one in five a VIP).</summary>
        private IEnumerator ShiftTourists()
        {
            _shiftOn = true;
            yield return new WaitForSeconds(6f);
            while (_shiftOn && IsServerInitialized)
            {
                int players = Mathf.Max(1, PlayerHub.All.Count);
                _waveTourists.RemoveWhere(v => v == null);
                if (RescueService.Instance != null && RescueService.InTroubleCount < Mathf.Min(3, 1 + players))
                {
                    int figure = Random.value < 0.5f ? 0 : 1;
                    TouristProfile profile = Profile(_island2, figure);
                    profile.Vip = Random.value < _vipChance;
                    SpawnStoryTourist(_island2, profile);
                }
                yield return new WaitForSeconds(Random.Range(_shiftGap.x, _shiftGap.y) / Mathf.Sqrt(players));
            }
        }

        // ------------------------------------------------------------------ 2.6 gala night and the robbery

        private IEnumerator GalaNightRaid()
        {
            _island = _island2;
            EnsureOnIsland2();
            _galaNight.Value = true;
            TitleObservers("GALA NIGHT", "The Grand Coral Gala");
            // Rich guests round the tables (they stand about; the robbery scatters them).
            _galaGuests.Clear();
            for (int i = 0; i < GalaGuestSpots.Length; i++)
            {
                StoryNpc guest = SpawnIndoors(VictimBrain.VipName(i % 2 == 1).Substring(4), NpcRole.Guest, GuestLook(7001 + i * 31, i % 2), GalaGuestSpots[i],
                    Random.Range(0f, 360f));
                guest.ServerFace(CasinoCentre);
                guest.ServerSetMood(AvatarMood.Happy);
                _galaGuests.Add(guest);
            }
            Transform casino = CasinoDoor;
            if (casino != null)
            {
                SetObjective("Go to the gala in the casino (through the lobby, west door)");
                Marker("Gala", casino.position + Vector3.up * 1.5f);
                float started = Time.time;
                while (StoryNpc.NearestPlayer(casino.position, 6f) == null && Time.time - started < 90f) yield return new WaitForSeconds(0.25f);
                NoMarker();
            }
            if (_director != null)
            {
                _director.ServerSetMood(AvatarMood.Happy);
                yield return Say(_director, "(over the speaker) Welcome, welcome! Champagne for everyone! And the tables are open!");
            }
            yield return new WaitForSeconds(3f);
            yield return PirateRaid(_receptionist, robbery: true);
            foreach (StoryNpc g in _galaGuests)
                if (g != null)
                {
                    g.ServerSetMood(AvatarMood.Scared);
                    StartCoroutine(RemoveLater(g, 25f));
                }
            _galaGuests.Clear();
            _casinoClosed.Value = true;
            if (_director != null)
            {
                _director.ServerSetMood(AvatarMood.Scared);
                yield return Say(_director, "(over the speaker) They... they took the casino's money. ALL of it. The gala is over. The casino is closed.");
            }
        }

        // The casino floor (GameSceneBuilder.HotelInterior: the west wing, x -3.7..8, z -245..-262) round the blackjack table.
        private static readonly Vector3 CasinoCentre = new(2.2f, 1.85f, -253.5f);
        private static readonly Vector3[] GalaGuestSpots =
        {
            new(-0.6f, 1.85f, -250.2f), new(4.8f, 1.85f, -250.6f), new(-0.4f, 1.85f, -257.4f), new(4.6f, 1.85f, -256.8f), new(6.2f, 1.85f, -252.6f)
        };
        private readonly List<StoryNpc> _galaGuests = new();

        /// <summary>The casino's door in the lobby (story anchor), or null.</summary>
        private static Transform CasinoDoor
        {
            get
            {
                GameObject entrance = GameObject.Find("Environment/Hotel/CasinoEntrance");
                return entrance != null ? entrance.transform : null;
            }
        }

        // ------------------------------------------------------------------ the bouncer job (Task 3)

        // The front of the Coral Bar (GameSceneBuilder.HotelBar): the stools along the counter face south.
        private static readonly Vector3 BarFront = new(-48f, 1.9f, -247.9f);
        private const float BarFightPay = 50, BrokenChairBill = 20;
        private readonly List<StoryNpc> _barFighters = new();
        private readonly List<(Transform stool, bool upright)> _barStools = new();
        private Coroutine _barFight;
        private float _nextBarFight;

        private static readonly string[] BarDrunkNames = { "Big Dave", "Terry", "Gus", "Ronnie", "Clive", "Mitch" };

        /// <summary>Host: now and then, while a lifeguard is near the Coral Bar, two drunk guests start a fist fight.</summary>
        private IEnumerator BarFights()
        {
            _nextBarFight = Time.time + Random.Range(45f, 90f);
            while (_shiftOn && IsServerInitialized)
            {
                yield return new WaitForSeconds(2f);
                if (_barFight != null || Time.time < _nextBarFight) continue;
                if (StoryNpc.NearestPlayer(BarFront, 40f) == null) continue;
                _barFight = StartCoroutine(BarFight());
            }
        }

        private void EndBarFight()
        {
            if (_barFight != null) StopCoroutine(_barFight);
            _barFight = null;
            foreach (StoryNpc n in _barFighters) if (n != null) StartCoroutine(RemoveLater(n, 25f));
            _barFighters.Clear();
        }

        /// <summary>Host: one bar fight, from the first shout to the pay (or the bill).</summary>
        private IEnumerator BarFight()
        {
            _barFighters.Clear();
            StandStoolsUp(); // (a test fight right after the last one)
            _barStools.Clear();
            foreach (Transform t in StoolsNearBar()) _barStools.Add((t, Vector3.Dot(t.up, Vector3.up) > 0.8f));
            int a = Random.Range(0, BarDrunkNames.Length), b = (a + 1 + Random.Range(0, BarDrunkNames.Length - 1)) % BarDrunkNames.Length;
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = BarFront + new Vector3(i == 0 ? -1.3f : 1.3f, 0f, Random.Range(-0.4f, 0.4f));
                StoryNpc drunk = SpawnNpc(BarDrunkNames[i == 0 ? a : b], NpcRole.Guest, GuestLook(Random.Range(1, 99999), 0), at, i == 0 ? 90f : 270f,
                    3 * Damage.Punch);
                drunk.ServerFight(FighterProfile.Drunk(), BarFront);
                _barFighters.Add(drunk);
            }
            yield return new WaitForSeconds(0.6f);
            _barFighters[0].ServerBrawlWith(_barFighters[1], 30f, "YOU SPILLED MY DRINK!");
            _barFighters[1].ServerBrawlWith(_barFighters[0], 30f, "YOU WANNA GO?!");
            Debug.Log($"[Bar] fight: {_barFighters[0].Name} vs {_barFighters[1].Name}");
            if (RescueService.Instance != null)
                RescueService.Instance.ServerAnnounce("<color=#ffb060><b>BAR FIGHT!</b></color> Two guests are brawling at the Coral Bar. Stop it ($50).");
            if (_shiftOn) Marker("Bar fight", _barFighters[0].NetworkObject);
            float started = Time.time;
            // Over when both are knocked out (by the lifeguards or by each other), or after three minutes.
            while (Time.time - started < 180f)
            {
                bool bothDown = true;
                foreach (StoryNpc n in _barFighters) if (n != null && !n.IsDefeated) bothDown = false;
                if (bothDown) break;
                ToPatients();
                KnockStools();
                // Nobody came: they keep at it (a grudge runs out when nobody lands a punch for a while).
                if (_barFighters[0] != null && _barFighters[1] != null && !_barFighters[0].IsDefeated && !_barFighters[1].IsDefeated &&
                    _barFighters[0].Rival == null && StoryNpc.NearestPlayer(BarFront, 6f) == null)
                    _barFighters[0].ServerBrawlWith(_barFighters[1], 20f, null);
                yield return new WaitForSeconds(0.3f);
            }
            ToPatients(0f);
            if (_markerLabel.Value == "Bar fight") NoMarker();
            int broken = _brokenStools.Count;
            bool stopped = StoryNpc.NearestPlayer(BarFront, 12f) != null;
            int pay = stopped ? Mathf.RoundToInt(BarFightPay - BrokenChairBill * broken) : -Mathf.RoundToInt(BrokenChairBill * broken);
            Debug.Log($"[Bar] over after {Time.time - started:F0}s: stopped by lifeguards {stopped}, chairs broken {broken}, pay {pay}");
            if (Economy.Instance != null && pay != 0)
                Economy.Instance.ServerAdd(pay, pay > 0 ? "bar fight stopped" : "broken chairs", BarFront + Vector3.up * 1.5f);
            if (RescueService.Instance != null)
                RescueService.Instance.ServerAnnounce(stopped
                    ? broken > 0 ? $"Fight stopped: $50, minus ${BrokenChairBill * broken} for {broken} broken chair{(broken > 1 ? "s" : "")}." : "Fight stopped, nothing broken: $50."
                    : broken > 0 ? $"The bar fight is over. The hotel bills you ${BrokenChairBill * broken} for the chairs." : "The bar fight fizzled out.");
            foreach (StoryNpc n in _barFighters) if (n != null) StartCoroutine(RemoveLater(n, 25f));
            _barFighters.Clear();
            yield return new WaitForSeconds(20f);
            StandStoolsUp(); // the staff tidy up
            _nextBarFight = Time.time + Random.Range(120f, 200f);
            _barFight = null;
        }

        private readonly Dictionary<StoryNpc, float> _downSince = new();

        /// <summary>
        /// Host: a fighter knocked out a moment ago becomes a guest lying there to carry to the infirmary's bed (the
        /// same look and name; story characters can't be picked up, tourists can). Nobody comes: up and off in 2 min.
        /// </summary>
        private void ToPatients(float after = 1.5f)
        {
            for (int i = 0; i < _barFighters.Count; i++)
            {
                StoryNpc n = _barFighters[i];
                if (n == null || !n.IsDefeated) continue;
                if (!_downSince.TryGetValue(n, out float since)) _downSince[n] = since = Time.time;
                if (Time.time - since < after || RescueService.Instance == null) continue;
                _downSince.Remove(n);
                var profile = new TouristProfile
                {
                    Figure = AvatarLook.Unpack(n.LookPacked).Feminine ? 1 : 0, Look = n.LookPacked, Name = n.Name, BleedSeconds = 60f
                };
                VictimBrain v = RescueService.Instance.SpawnVictim(n.transform.position + Vector3.up * 0.3f, n.transform.eulerAngles.y, VictimState.Fine, 0f, 1f, profile);
                Remove(n);
                _barFighters[i] = null;
                if (v == null) continue;
                v.ServerKnockedOut();
                StartCoroutine(RemoveLater(v, 120f));
                Debug.Log($"[Bar] {v.Name} is out cold: carry him to the infirmary");
            }
        }

        private static IEnumerable<Transform> StoolsNearBar()
        {
            GameObject stools = GameObject.Find("Environment/Hotel/HotelBarStools");
            if (stools == null) yield break;
            foreach (Transform t in stools.transform) yield return t;
        }

        private readonly HashSet<string> _brokenStools = new();

        /// <summary>Host: a fighter stumbling into a stool knocks it over (that's a broken chair).</summary>
        private void KnockStools()
        {
            foreach (StoryNpc n in _barFighters)
            {
                if (n == null || n.IsDefeated || n.Rival == null) continue;
                foreach (var (stool, _) in _barStools)
                {
                    if (stool == null || _brokenStools.Contains(stool.name)) continue;
                    Vector3 d = stool.position - n.transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1.3f * 1.3f || Random.value > 0.05f) continue;
                    if (stool.TryGetComponent(out Vehicle seat) && seat.Driver != null) continue; // somebody's sitting on it
                    _brokenStools.Add(stool.name);
                    Vector3 push = d.sqrMagnitude > 0.001f ? d.normalized : n.transform.forward;
                    StoolObservers(stool.name, push, true);
                    n.ServerShout(Random.value < 0.5f ? "*CRASH*" : "OOPS!", false);
                    Debug.Log($"[Bar] {n.Name} knocked over {stool.name}");
                }
            }
        }

        /// <summary>Test ('bartest stool'): the second stool from the left goes over now.</summary>
        private IEnumerator TestStool()
        {
            yield return new WaitForSeconds(1f);
            _brokenStools.Add("BarStool1");
            StoolObservers("BarStool1", new Vector3(0.3f, 0f, 1f).normalized, true);
        }

        private void StandStoolsUp()
        {
            foreach (string name in _brokenStools) StoolObservers(name, Vector3.zero, false);
            _brokenStools.Clear();
        }

        private static readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> StoolRest = new();

        /// <summary>Every machine: a bar stool's model knocked onto its side (or stood back up). The seat itself stays.</summary>
        [ObserversRpc(BufferLast = false)]
        private void StoolObservers(string stoolName, Vector3 push, bool over)
        {
            GameObject stools = GameObject.Find("Environment/Hotel/HotelBarStools");
            Transform stool = stools != null ? stools.transform.Find(stoolName) : null;
            if (stool == null) return;
            Vector3 axis = Vector3.Cross(Vector3.up, push.sqrMagnitude > 0.001f ? push : stool.forward).normalized;
            foreach (Transform part in stool)
            {
                if (part.GetComponentInChildren<Renderer>() == null) continue; // the seat and serve points
                if (!StoolRest.ContainsKey(part)) StoolRest[part] = (part.localPosition, part.localRotation);
                (Vector3 p, Quaternion r) = StoolRest[part];
                part.localPosition = p;
                part.localRotation = r;
                if (!over) continue;
                // Over onto its side about the foot it falls toward, lying on the deck.
                part.RotateAround(stool.position + push * 0.22f, axis, 88f);
            }
            if (over) FloatingText.Spawn(stool.position + Vector3.up * 1.2f, "-$20", new Color(1f, 0.5f, 0.4f), 0.9f, 1.4f);
        }

        // ------------------------------------------------------------------ console (tests)

        private void RegisterGalaCommands()
        {
            DevCommands.Register("viptest", "[distance]", "A VIP guest (gold hat, tips $100-300) in trouble in the water in front of you (default 10 m).", args =>
                CheatServer("vip", args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0) * 10f) : 100), cheat: true, owner: this);
            DevCommands.Register("galamoney", "<amount>", "Show the gala bar (if the story hasn't) and add money to the team's wallet.", args =>
                CheatServer("galamoney", args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 250), cheat: true, owner: this);
            DevCommands.Register("bartest", "[stool]", "Start a bar fight at the Coral Bar now (bouncer job: $50, -$20 per broken chair); 'stool' knocks a stool over at once (to look at it).", args => CheatServer("bartest", args.Length > 0 && args[0] == "stool" ? 1 : 0), cheat: true, owner: this);
            DevCommands.Register("galatest", "[day]", "Gala night on island 2 now (night, decorations); 'galatest day' turns it off.", args =>
                CheatServer("galatest", args.Length > 0 && args[0] == "day" ? 0 : 1), cheat: true, owner: this);
        }

        private void UnregisterGalaCommands()
        {
            foreach (string c in new[] { "viptest", "galamoney", "bartest", "galatest" }) DevCommands.Unregister(c, this);
        }

        /// <summary>Host: the gala's test commands. True if <paramref name="what"/> was one.</summary>
        private bool ServerGalaCheat(string what, int value, PlayerHub by)
        {
            switch (what)
            {
                case "vip":
                {
                    if (by == null) return true;
                    Vector3 at = by.transform.position + Vector3.ProjectOnPlane(by.Head.forward, Vector3.up).normalized * Mathf.Max(1.5f, value / 10f);
                    TouristProfile profile = Profile(_island2, Random.value < 0.5f ? 0 : 1);
                    profile.Vip = true;
                    profile.Name = VictimBrain.VipName(profile.Figure == 1);
                    // On land: out cold on the sand in front of you (CPR, carrying); in the water: in trouble.
                    bool onLand = Shore.WaterDepthAt(new Vector3(at.x, 20f, at.z)) < Shore.StandDepth;
                    float ground = Shore.GroundHeightAt(at + Vector3.up * 10f);
                    at.y = onLand && !float.IsNaN(ground) ? ground + 0.5f : World.Water.WaterSurface.Exists ? World.Water.WaterSurface.HeightAt(at) - 0.4f : at.y;
                    if (RescueService.Instance != null)
                    {
                        VictimBrain v = onLand ? RescueService.Instance.SpawnVictim(at, 0f, VictimState.Unconscious, 0f, 0f, profile)
                            : RescueService.Instance.SpawnVictim(at, 0f, VictimState.Distressed, 30f, 1f, profile);
                        Debug.Log($"[Gala] viptest: {v?.Name} at {at:F1}, money ${Economy.Money}");
                    }
                    return true;
                }
                case "galamoney":
                    if (_galaGoal.Value == 0)
                    {
                        _galaTest = true;
                        _galaGoal.Value = GalaTarget;
                    }
                    if (Economy.Instance != null && value != 0) Economy.Instance.ServerAdd(value, "gala test", by != null ? by.Head.position + by.Head.forward * 1.5f : Vector3.zero);
                    Debug.Log($"[Gala] bar {GalaFill01:P0} (${Economy.Money} of ${_galaGoal.Value})");
                    return true;
                case "bartest":
                    if (_barFight != null) StopCoroutine(_barFight);
                    foreach (StoryNpc n in _barFighters) if (n != null) Remove(n);
                    _barFight = StartCoroutine(BarFight());
                    if (value == 1) StartCoroutine(TestStool());
                    return true;
                case "galatest":
                    _galaNight.Value = value != 0;
                    return true;
            }
            return false;
        }
    }
}
