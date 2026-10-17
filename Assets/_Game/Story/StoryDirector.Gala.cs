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
                _director = SpawnNpc("Mr. Castellane", NpcRole.Guide, DirectorLook, DirectorSpot, DirectorYaw);
                _director.ServerSetTalkable(false);
                return _director.gameObject;
            });
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
            _casinoClosed.Value = true;
            if (_director != null)
            {
                _director.ServerSetMood(AvatarMood.Scared);
                yield return Say(_director, "(over the speaker) They... they took the casino's money. ALL of it. The gala is over. The casino is closed.");
            }
        }

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
        private static readonly Vector3 BarFront = new(-48f, 1.9f, -247.3f);
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
                KnockStools();
                // Nobody came: they keep at it (a grudge runs out when nobody lands a punch for a while).
                if (_barFighters[0] != null && _barFighters[1] != null && !_barFighters[0].IsDefeated && !_barFighters[1].IsDefeated &&
                    _barFighters[0].Rival == null && StoryNpc.NearestPlayer(BarFront, 6f) == null)
                    _barFighters[0].ServerBrawlWith(_barFighters[1], 20f, null);
                yield return new WaitForSeconds(0.3f);
            }
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
            DevCommands.Register("viptest", "", "A VIP guest (gold hat, tips $100-300) in trouble in the water in front of you.", _ => CheatServer("vip", 0), cheat: true, owner: this);
            DevCommands.Register("galamoney", "<amount>", "Show the gala bar (if the story hasn't) and add money to the team's wallet.", args =>
                CheatServer("galamoney", args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 250), cheat: true, owner: this);
            DevCommands.Register("bartest", "", "Start a bar fight at the Coral Bar now (bouncer job: $50, -$20 per broken chair).", _ => CheatServer("bartest", 0), cheat: true, owner: this);
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
                    Vector3 at = by.transform.position + Vector3.ProjectOnPlane(by.Head.forward, Vector3.up).normalized * 10f;
                    TouristProfile profile = Profile(_island2, Random.value < 0.5f ? 0 : 1);
                    profile.Vip = true;
                    profile.Name = VictimBrain.VipName(profile.Figure == 1);
                    at.y = World.Water.WaterSurface.Exists ? World.Water.WaterSurface.HeightAt(at) - 0.4f : at.y;
                    if (RescueService.Instance != null)
                    {
                        VictimBrain v = RescueService.Instance.SpawnVictim(at, 0f, VictimState.Distressed, 30f, 1f, profile);
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
                    return true;
                case "galatest":
                    _galaNight.Value = value != 0;
                    return true;
            }
            return false;
        }
    }
}
