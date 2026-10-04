using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    /// <summary>Where one island's story happens and how hard it is.</summary>
    [Serializable]
    public class IslandSetup
    {
        public string Name = "Island";
        [Tooltip("x range of the sea where tourists get into trouble.")]
        public Vector2 SeaX = new(-20f, 16f);
        [Tooltip("z range of the sea where tourists get into trouble.")]
        public Vector2 SeaZ = new(-36f, -18f);
        [Tooltip("Direction from the sea to this island's beach (bystanders stand at the water's edge that way).")]
        public Vector3 Shoreward = Vector3.forward;
        public TouristProfile Profile = new() { Figure = -1, SecondsToUnconscious = 20f, ConditionSeconds = 60f, BleedSeconds = 60f };
        [Tooltip("x/z box the robber runs around in (x, z, width, depth).")]
        public Rect LandArea = new(-45f, 6f, 90f, 42f);
        public Transform RobberSpawn;
        public Transform Arrival;
    }

    /// <summary>
    /// Plays the story (host). Beats are coroutines in StoryDirector.Beats.cs, run in order; each sets the objective,
    /// a marker and waits for something to happen (rescues, a conversation, items handed in, a robber knocked down).
    /// Everyone sees the objective, the marker, the dialogue and the title cards through SyncVars and RPCs.
    /// Progress (beat + money) is saved after every beat and a new host continues from there.
    /// </summary>
    public partial class StoryDirector : NetworkBehaviour
    {
        [Header("Characters")]
        [SerializeField] private StoryNpc _npcPrefab;
        [SerializeField] private Shark _sharkPrefab;
        [SerializeField] private StoryNpc _sandy;
        [SerializeField] private StoryNpc _receptionist;

        [Header("Places and things")]
        [SerializeField] private LostAndFound _lostAndFound;
        [SerializeField] private ShopCounter _reception;
        [SerializeField] private Transform _receptionDesk;
        [SerializeField] private Vehicle _jetSki;
        [SerializeField] private Transform _jetSkiIsland2Dock;
        [SerializeField] private Vehicle _pirateBoat;
        [SerializeField] private Transform _pirateBoatStart;
        [SerializeField] private Transform _pirateLanding;
        [SerializeField] private Transform _hotelDoor;
        [SerializeField] private Transform _infirmary;
        [SerializeField] private Transform _firstAid;
        [SerializeField] private Transform[] _lostItemSpots = Array.Empty<Transform>();
        [SerializeField] private Transform _island2Spawn;

        [Header("Islands")]
        [SerializeField] private IslandSetup _island1 = new();
        [SerializeField] private IslandSetup _island2 = new();

        [Header("Tuning")]
        [SerializeField] private int _pistolPrice = 250;
        [SerializeField] private float _spawnGap = 6f;

        private static readonly string[] LostKinds = { "Wallet", "Phone", "Sunglasses", "Watch" };

        private readonly SyncVar<string> _chapter = new SyncVar<string>();
        private readonly SyncVar<string> _objective = new SyncVar<string>();
        private readonly SyncVar<int> _progress = new SyncVar<int>();
        private readonly SyncVar<int> _goal = new SyncVar<int>();
        private readonly SyncVar<string> _markerLabel = new SyncVar<string>();
        private readonly SyncVar<Vector3> _markerPosition = new SyncVar<Vector3>();
        private readonly SyncVar<NetworkObject> _markerTarget = new SyncVar<NetworkObject>();
        private readonly SyncVar<string> _beat = new SyncVar<string>();

        private struct Beat
        {
            public string Id;
            public string Title;
            public Func<IEnumerator> Run;
        }

        private readonly List<Beat> _beats = new();
        private int _beatIndex = -1;
        private Coroutine _runner;
        private bool _running;
        // What the beats wait for (host).
        private readonly HashSet<VictimBrain> _waveTourists = new();
        private readonly List<VictimBrain> _rescued = new();
        private readonly Dictionary<VictimBrain, VictimEvent> _howRescued = new();
        private readonly Dictionary<VictimBrain, PlayerHub> _heroOf = new();
        private readonly List<LostAndFound.HandedIn> _handedIn = new();
        private readonly List<(StoryNpc npc, PlayerHub by)> _talks = new();
        private readonly List<StoryNpc> _defeated = new();
        private readonly List<string> _purchases = new();
        private readonly List<GameObject> _spawnedActors = new();
        private IslandSetup _island;
        private float _nextAmbientLoss;
        private Vector3 _sandyHome;
        private float _sandyHomeYaw;

        public static StoryDirector Instance { get; private set; }
        public string Chapter => _chapter.Value;
        public string Objective => _objective.Value;
        public int Progress => _progress.Value;
        public int Goal => _goal.Value;
        public string BeatId => _beat.Value;
        public string MarkerLabel => _markerLabel.Value;
        /// <summary>Where the jet ski ride of chapter 1 ends (tests steer at it).</summary>
        public Vector3 Island2Arrival => _island2.Arrival != null ? _island2.Arrival.position : new Vector3(0f, 0f, -200f);
        /// <summary>Where the objective marker is (follows its target), or null.</summary>
        public Vector3? MarkerPosition
        {
            get
            {
                if (string.IsNullOrEmpty(_markerLabel.Value)) return null;
                NetworkObject target = _markerTarget.Value;
                if (target != null) return target.transform.position + Vector3.up * 2.1f;
                return _markerPosition.Value;
            }
        }

        private static string SavePath => Path.Combine(Application.persistentDataPath, "story.json");

        [Serializable]
        private class SaveData
        {
            public string Beat;
            public int Money;
            public int Rescued, Lost, Returned, Earned; // this chapter so far (for its report card)
        }

        // What the team did in the current chapter (host; saved with the beat).
        private int _chapterRescued, _chapterLost, _chapterReturned, _chapterEarned;
        private int _moneySeen;

        /// <summary>Host (tests): tourists rescued / lost and lost things handed in during the current chapter.</summary>
        public (int rescued, int lost, int returned, int earned) ChapterStats => (_chapterRescued, _chapterLost, _chapterReturned, _chapterEarned);

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ lifecycle (host)

        public override void OnStartServer()
        {
            base.OnStartServer();
            BuildBeats();
            VictimBrain.ServerEvent += OnVictimEvent;
            LostAndFound.ServerHandedIn += OnHandedIn;
            StoryNpc.ServerTalked += OnTalked;
            StoryNpc.ServerDefeated += OnDefeated;
            ShopCounter.ServerPurchased += OnPurchased;
            Economy.ServerChanged += OnMoneyChanged;
            PlayerHub.ServerJoined += OnPlayerJoined;
            StartCoroutine(Boot());
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            VictimBrain.ServerEvent -= OnVictimEvent;
            LostAndFound.ServerHandedIn -= OnHandedIn;
            StoryNpc.ServerTalked -= OnTalked;
            StoryNpc.ServerDefeated -= OnDefeated;
            ShopCounter.ServerPurchased -= OnPurchased;
            Economy.ServerChanged -= OnMoneyChanged;
            PlayerHub.ServerJoined -= OnPlayerJoined;
        }

        private IEnumerator Boot()
        {
            // Wait for the first lifeguard (the host's own player); by then every scene object is up.
            while (PlayerHub.All.Count == 0) yield return null;
            SetupResidents();
            yield return new WaitForSeconds(1.5f);
            if (DevLaunchArgs.Has("-pdd-nostory"))
            {
                Debug.Log("[Story] off (-pdd-nostory)");
                yield break;
            }
            SaveData save = Load();
            int start = 0;
            if (save != null)
            {
                int found = _beats.FindIndex(b => b.Id == save.Beat);
                if (found >= 0) start = found;
                if (Economy.Instance != null) Economy.Instance.ServerSet(save.Money);
                _chapterRescued = save.Rescued;
                _chapterLost = save.Lost;
                _chapterReturned = save.Returned;
                _chapterEarned = save.Earned;
                Debug.Log($"[Story] continuing from beat {save.Beat} with ${save.Money}");
            }
            _moneySeen = Economy.Money;
            StartAt(start);
        }

        /// <summary>Sandy and the receptionist stand at their posts from the start.</summary>
        private void SetupResidents()
        {
            if (_sandy != null)
            {
                _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyLook);
                _sandyHome = _sandy.transform.position;
                _sandyHomeYaw = _sandy.transform.eulerAngles.y;
                _sandy.ServerSetPose(AvatarPose.SitChair); // at her window, on her stool
            }
            if (_receptionist != null)
                _receptionist.ServerSetup("Marisol", NpcRole.Receptionist, ReceptionistLook);
            if (_receptionist != null) _receptionist.ServerSetTalkable(false);
            if (_reception != null) _reception.ServerSetAvailable(false);
            if (_pirateBoat != null) _pirateBoat.ServerSetLocked(true, "Somebody's boat (not yours... yet)");
        }

        private void StartAt(int index)
        {
            StopAllCoroutines(); // the beat and anything it started (waving, delayed lines...)
            _hints = null;
            _waitingForTalk = false;
            foreach (BeachCrowd crowd in BeachCrowd.All) crowd.ReturnAll(); // anyone lent out for a scene
            if (_sandy != null && index > 0) StartCoroutine(SandyGoesHome()); // interrupted mid-walk: back to the kiosk
            CleanupActors();
            _running = true;
            _runner = StartCoroutine(RunFrom(index));
        }

        private IEnumerator RunFrom(int index)
        {
            for (int i = index; i < _beats.Count; i++)
            {
                _beatIndex = i;
                Beat beat = _beats[i];
                _beat.Value = beat.Id;
                SetChapter(beat.Id.StartsWith("2.") ? "Chapter 2: The hotel island" : "Chapter 1: The first island");
                Debug.Log($"[Story] beat {beat.Id}: {beat.Title}");
                ClearWaits();
                Save();
                yield return beat.Run();
            }
            _running = false;
            _beat.Value = "end";
            Save();
            Debug.Log("[Story] finished");
        }

        private void ClearWaits()
        {
            _rescued.Clear();
            _handedIn.Clear();
            _talks.Clear();
            _defeated.Clear();
            _purchases.Clear();
            _waveTourists.Clear();
            _howRescued.Clear();
            _heroOf.Clear();
        }

        private void CleanupActors()
        {
            foreach (GameObject go in _spawnedActors)
                if (go != null && go.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(go);
            _spawnedActors.Clear();
        }

        /// <summary>Automated tests (-pdd-nosave) never touch the real save.</summary>
        private static bool NoSave => DevLaunchArgs.Has("-pdd-nosave");

        private void Save()
        {
            if (NoSave) return;
            try
            {
                var data = new SaveData
                {
                    Beat = _beat.Value, Money = Economy.Money,
                    Rescued = _chapterRescued, Lost = _chapterLost, Returned = _chapterReturned, Earned = _chapterEarned
                };
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] couldn't save: {e.Message}");
            }
        }

        private static SaveData Load()
        {
            if (NoSave) return null;
            try
            {
                return File.Exists(SavePath) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] couldn't read the save: {e.Message}");
                return null;
            }
        }

        private void OnMoneyChanged(int money)
        {
            if (money > _moneySeen) _chapterEarned += money - _moneySeen; // what came in (spending isn't un-earning)
            _moneySeen = money;
            if (_running) Save();
        }

        private void ResetChapterStats()
        {
            _chapterRescued = _chapterLost = _chapterReturned = _chapterEarned = 0;
            _moneySeen = Economy.Money;
        }

        /// <summary>
        /// The report card at the end of a chapter, on everyone's screen: what the team did. Waits while it is up,
        /// then starts counting afresh for the next chapter.
        /// </summary>
        private IEnumerator ChapterReport(string title)
        {
            Debug.Log($"[Story] {title}: rescued {_chapterRescued}, lost {_chapterLost}, returned {_chapterReturned}, earned ${_chapterEarned}");
            ReportObservers(title, _chapterRescued, _chapterLost, _chapterReturned, _chapterEarned, Economy.Money);
            yield return new WaitForSeconds(StoryHud.ReportSeconds);
            ResetChapterStats();
        }

        [ObserversRpc]
        private void ReportObservers(string title, int rescued, int lost, int returned, int earned, int money) =>
            StoryHud.ShowReport(title, rescued, lost, returned, earned, money);

        // ------------------------------------------------------------------ events (host)

        private void OnVictimEvent(VictimBrain victim, VictimEvent e, PlayerHub credit)
        {
            bool rescued = e is VictimEvent.Saved or VictimEvent.SelfRescue or VictimEvent.Revived or VictimEvent.Zapped or VictimEvent.Hospitalized;
            if (rescued && victim.State != VictimState.Injured) _rescued.Add(victim);
            if (rescued) _howRescued[victim] = e; // what they say afterwards depends on it (kissed, slapped, zapped...)
            if (rescued && credit != null) _heroOf[victim] = credit;
            // For the chapter's report card (someone pulled out and then revived is one rescue, not two).
            if (e is VictimEvent.Revived or VictimEvent.Zapped or VictimEvent.Hospitalized or VictimEvent.SelfRescue ||
                (e == VictimEvent.Saved && victim.State == VictimState.Saved)) _chapterRescued++;
            if (e == VictimEvent.Lost) _chapterLost++;
            // Tourists drop things now and then when they're pulled out (their wallet was in their trunks...).
            if (rescued && _island == _island1 && Random.value < 0.35f)
                StartCoroutine(DropLostItemLater(victim.Name, victim.transform.position));
        }

        private void OnHandedIn(LostAndFound.HandedIn info)
        {
            _handedIn.Add(info);
            _chapterReturned++;
        }
        private void OnTalked(StoryNpc npc, PlayerHub by)
        {
            _talks.Add((npc, by));
            if (npc == _sandy && !_waitingForTalk && _running) StartCoroutine(SandyChats(by));
        }

        private static readonly string[] SandyLines =
        {
            "Found something? Put it on the counter out front, I'll pay you.",
            "Tourists! They'd lose their heads if they weren't screwed on.",
            "Keep an eye on the water, dear. I'll shout if I see anything.",
            "I've run this Lost & Found for twenty years. Twenty!",
            "The bell's right outside. When it rings, you run."
        };

        /// <summary>Talking to Sandy outside a scene: she takes any lost things you carry, or just has a chat.</summary>
        private IEnumerator SandyChats(PlayerHub by)
        {
            if (_lostAndFound != null && _lostAndFound.ServerHandIn(by, _sandy.HeadPosition))
            {
                yield return Say(_sandy, "Ooh, lovely. Somebody will be very happy. Here you go.");
                yield break;
            }
            yield return Say(_sandy, SandyLines[Random.Range(0, SandyLines.Length)]);
        }
        private void OnDefeated(StoryNpc npc, PlayerHub by) => _defeated.Add(npc);
        private void OnPurchased(ShopCounter shop, PlayerHub by, string item) => _purchases.Add(item);

        // ------------------------------------------------------------------ helpers for beats (host)

        private void SetChapter(string title) => _chapter.Value = title;

        private void SetObjective(string text, int progress = 0, int goal = 0)
        {
            _objective.Value = text;
            _progress.Value = progress;
            _goal.Value = goal;
        }

        private void Marker(string label, NetworkObject target)
        {
            _markerTarget.Value = target;
            _markerLabel.Value = label ?? string.Empty;
        }

        private void Marker(string label, Vector3 position)
        {
            _markerTarget.Value = null;
            _markerPosition.Value = position;
            _markerLabel.Value = label ?? string.Empty;
        }

        private void NoMarker()
        {
            _markerTarget.Value = null;
            _markerLabel.Value = string.Empty;
        }

        private IEnumerator Say(StoryNpc npc, string text)
        {
            if (DialogueService.Instance == null) yield break;
            float d = DialogueService.Instance.ServerSay(npc != null ? npc.Name : "?", text, npc);
            yield return new WaitForSeconds(d);
        }

        private IEnumerator Say(string speaker, string text, Color color)
        {
            if (DialogueService.Instance == null) yield break;
            float d = DialogueService.Instance.ServerSay(speaker, text, null, color);
            yield return new WaitForSeconds(d);
        }

        /// <summary>The lifeguards' own line (shown as "You").</summary>
        private IEnumerator You(string text) => Say(DialogueService.PlayerSpeaker, text, new Color(0.6f, 1f, 0.7f));

        [ObserversRpc]
        private void TitleObservers(string title, string subtitle) => StoryHud.ShowTitle(title, subtitle);

        private bool _waitingForTalk;

        private IEnumerator WaitTalk(StoryNpc npc, string prompt)
        {
            _talks.Clear();
            _waitingForTalk = true;
            npc.ServerSetTalkable(true, prompt);
            while (!_talks.Exists(t => t.npc == npc)) yield return null;
            _waitingForTalk = false;
            _talks.Clear();
        }

        private StoryNpc SpawnNpc(string displayName, NpcRole role, AvatarLook look, Vector3 position, float yaw, int health = 0)
        {
            position = StoryNpc.OnNavMesh(position); // not inside a palm, a counter or a wall
            float ground = Shore.GroundHeightAt(position + Vector3.up * 3f);
            if (!float.IsNaN(ground) && Shore.WaterDepthAt(position + Vector3.up * 3f) < 1f) position.y = ground;
            StoryNpc npc = Instantiate(_npcPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            Spawn(npc.gameObject);
            npc.ServerSetup(displayName, role, look, health);
            _spawnedActors.Add(npc.gameObject);
            return npc;
        }

        private void Remove(Component thing)
        {
            if (thing == null) return;
            _spawnedActors.Remove(thing.gameObject);
            if (thing.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(thing.gameObject);
        }

        private Item SpawnItem(string itemName, Vector3 position, Vector3 velocity = default)
        {
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(itemName) : null;
            if (prefab == null)
            {
                Debug.LogError($"[Story] no item '{itemName}' in the catalog");
                return null;
            }
            Item item = Instantiate(prefab, position, Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), 0f));
            Spawn(item.gameObject);
            if (velocity != Vector3.zero) StartCoroutine(Launch(item, velocity));
            return item;
        }

        private static IEnumerator Launch(Item item, Vector3 velocity)
        {
            yield return new WaitForFixedUpdate();
            if (item != null && !item.Sync.Body.isKinematic)
            {
                item.Sync.Body.maxDepenetrationVelocity = 3f; // pushed out of something gently, never shot out
                item.Sync.Body.linearVelocity = velocity;
                item.Sync.Body.angularVelocity = Random.insideUnitSphere * 6f;
            }
        }

        /// <summary>Bursting bag / pockets: items fly up and scatter around a point.</summary>
        private List<Item> Scatter(Vector3 from, params (string item, string owner, bool stolen)[] things)
        {
            var list = new List<Item>();
            float turn = Random.Range(0f, 360f);
            for (int n = 0; n < things.Length; n++)
            {
                var t = things[n];
                // Each thing starts on its own side, clear of whoever dropped them and of the others: three things
                // made in one spot (inside a kneeling robber) shove each other apart hard enough to leave the island.
                Vector3 side = Quaternion.Euler(0f, turn + n * 360f / things.Length, 0f) * Vector3.forward;
                Vector2 r = new Vector2(side.x, side.z) * Random.Range(1.2f, 2.4f);
                Item item = SpawnItem(t.item, from + side * 0.7f + Vector3.up * (1.3f + 0.15f * n), new Vector3(r.x, 4.5f, r.y));
                if (item == null) continue;
                if (item.TryGetComponent(out LostItem lost)) lost.ServerSetup(t.owner, t.stolen);
                list.Add(item);
            }
            return list;
        }

        private IEnumerator DropLostItemLater(string owner, Vector3 near)
        {
            yield return new WaitForSeconds(Random.Range(2f, 5f));
            // In the shallows near where they came ashore.
            Vector3 p = near + new Vector3(Random.Range(-3f, 3f), 0.5f, Random.Range(-2f, 2f));
            Item item = SpawnItem(LostKinds[Random.Range(0, LostKinds.Length)], p);
            if (item != null && item.TryGetComponent(out LostItem lost)) lost.ServerSetup(owner, false);
            Debug.Log($"[Story] {owner} dropped a {item?.DisplayName} at {p:F1}");
        }

        /// <summary>Now and then a lost thing turns up on the beach of island 1 (at most three lying around).</summary>
        private void AmbientLostItems()
        {
            if (_island != _island1 || _lostItemSpots.Length == 0 || Time.time < _nextAmbientLoss) return;
            _nextAmbientLoss = Time.time + Random.Range(50f, 80f);
            int lying = 0;
            foreach (Item i in Item.All)
                if (!i.IsHeld && i.TryGetComponent(out LostItem l) && !l.IsStolen) lying++;
            if (lying >= 3) return;
            Transform spot = _lostItemSpots[Random.Range(0, _lostItemSpots.Length)];
            Item item = SpawnItem(LostKinds[Random.Range(0, LostKinds.Length)], spot.position + Vector3.up * 0.4f + Random.insideUnitSphere * 1.5f);
            if (item != null && item.TryGetComponent(out LostItem lost)) lost.ServerSetup(VictimBrain.RandomName(), false);
        }

        private void Update()
        {
            if (IsServerInitialized && _running) AmbientLostItems();
        }

        // ------------------------------------------------------------------ rescues

        private VictimBrain SpawnStoryTourist(IslandSetup island, TouristProfile profile, float minDepth = 2f, float maxDepth = 7f)
        {
            RescueService rescue = RescueService.Instance;
            if (rescue == null) return null;
            Vector3 spot;
            float yaw = Random.Range(-40f, 40f);
            // Someone who was swimming out there gets into trouble (same face, same swimsuit).
            BeachCrowd crowd = BeachCrowd.Nearest(new Vector3((island.SeaX.x + island.SeaX.y) * 0.5f, 0f, (island.SeaZ.x + island.SeaZ.y) * 0.5f));
            StoryNpc swimmer = crowd != null ? crowd.TakeSwimmer(profile.Figure, island.SeaX, island.SeaZ, minDepth) : null;
            if (swimmer != null)
            {
                spot = swimmer.transform.position;
                spot.y = WaterSurface.Exists ? WaterSurface.HeightAt(spot) - 0.4f : spot.y;
                yaw = swimmer.transform.eulerAngles.y;
                profile.Look = swimmer.LookPacked;
                if (string.IsNullOrEmpty(profile.Name)) profile.Name = swimmer.Name;
                Despawn(swimmer.gameObject);
            }
            else if (!rescue.TryFindSeaSpot(island.SeaX, island.SeaZ, minDepth, maxDepth, out spot)) return null;
            VictimBrain v = rescue.SpawnVictim(spot, yaw, VictimState.Distressed, 30f, 1f, profile);
            if (v == null) return null;
            _waveTourists.Add(v);
            if (!profile.Silent)
            {
                if (island == _island1) rescue.ServerRingBell("story");
                float distance = DistanceToBeach(spot, island);
                rescue.ServerAnnounce($"<color=#ffd060><b>HELP!</b></color> {v.Name} is in trouble in the water, {distance:F0} m out!");
            }
            Debug.Log($"[Story] tourist {v.Name} ({(v.IsFemale ? "F" : "M")}{(profile.Silent ? ", silent" : "")}{(swimmer != null ? ", was swimming" : "")}) at {spot:F1}");
            return v;
        }

        private float DistanceToBeach(Vector3 spot, IslandSetup island) => Vector3.Distance(spot, BeachPointFrom(spot, island));

        /// <summary>The water's edge straight toward the island's beach from a point at sea (ankle deep).</summary>
        private static Vector3 BeachPointFrom(Vector3 spot, IslandSetup island)
        {
            Vector3 dir = new Vector3(island.Shoreward.x, 0f, island.Shoreward.z).normalized;
            Vector3 p = new Vector3(spot.x, 0f, spot.z);
            for (int i = 0; i < 120; i++)
            {
                p += dir;
                if (Shore.WaterDepthAt(p + Vector3.up * 2f) < 0.25f) break;
            }
            float ground = Shore.GroundHeightAt(p + Vector3.up * 5f);
            if (!float.IsNaN(ground)) p.y = ground;
            return p;
        }

        private TouristProfile Profile(IslandSetup island, int figure, bool silent = false, float flatline = -1f)
        {
            TouristProfile p = island.Profile;
            p.Figure = figure;
            p.Silent = silent;
            if (flatline >= 0f) p.FlatlineAfter = flatline;
            return p;
        }

        // ------------------------------------------------------------------ players

        /// <summary>A friend joined while the story is already going: tell them where it is, and put them with the team.</summary>
        private void OnPlayerJoined(PlayerHub player)
        {
            if (!_running || _beatIndex < 0 || player.Owner == null || player.Owner.IsLocalClient) return;
            // Not a coroutine of ours: StartAt stops those whenever the beat is changed by hand.
            player.StartCoroutine(Welcome(player));
        }

        private IEnumerator Welcome(PlayerHub player)
        {
            yield return new WaitForSeconds(2.5f); // their copy has the scene and everyone in it by now
            if (player == null || !_running || _beatIndex < 0 || _beatIndex >= _beats.Count) yield break;
            Beat beat = _beats[_beatIndex];
            bool second = beat.Id.StartsWith("2.");
            // Next to a teammate who's standing on land; if they're all at sea, the island's own landing.
            Vector3 at = second && _island2Spawn != null ? _island2Spawn.position : player.transform.position;
            foreach (PlayerHub mate in PlayerHub.All)
            {
                if (mate == player || mate == null || !Shore.IsAshore(mate.transform.position)) continue;
                Vector3 beside = mate.transform.position + mate.Head.right * 1.6f;
                float ground = Shore.GroundHeightAt(beside + Vector3.up * 3f);
                if (float.IsNaN(ground) || !Shore.IsAshore(beside)) continue;
                at = new Vector3(beside.x, ground + 0.2f, beside.z);
                break;
            }
            bool move = (at - player.transform.position).sqrMagnitude > 20f * 20f;
            int inChapter = 0, done = 0;
            for (int i = 0; i < _beats.Count; i++)
            {
                if (_beats[i].Id.StartsWith("2.") != second) continue;
                inChapter++;
                if (i < _beatIndex) done++;
            }
            string host = "the host";
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.Owner != null && p.Owner.IsLocalClient) host = p.DisplayName;
            string recap = $"You joined <b>{host}</b>'s shift: part {done + 1} of {inChapter}, \"{beat.Title}\". " +
                           $"So far this chapter: {_chapterRescued} rescued, {_chapterLost} lost, ${Economy.Money} in the team's wallet.";
            Debug.Log($"[Story] {player.DisplayName} joined at beat {beat.Id}{(move ? " (moved to the team)" : "")}");
            WelcomeTarget(player.Owner, second ? "CHAPTER 2" : "CHAPTER 1", beat.Title, recap, at, move);
        }

        [TargetRpc]
        private void WelcomeTarget(NetworkConnection target, string chapter, string beatTitle, string recap, Vector3 position, bool move)
        {
            StoryHud.ShowTitle(chapter, "You joined at: " + beatTitle);
            PlayerHud.ShowToast(recap, 9f);
            PlayerHub local = PlayerHub.Local;
            if (move && local != null && local.Motor != null && local.Motor.Seat == null) local.Motor.Teleport(position);
        }

        [ObserversRpc]
        private void TeleportObservers(Vector3 position)
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || local.Motor == null || local.Motor.Seat != null) return;
            local.Motor.Teleport(position + new Vector3(Random.Range(-1.5f, 1.5f), 0.2f, Random.Range(-1.5f, 1.5f)));
        }

        /// <summary>Resuming a save on island 2: bring everyone (and the jet ski) over.</summary>
        private void EnsureOnIsland2()
        {
            if (_island2Spawn == null) return;
            bool anyFar = false;
            foreach (PlayerHub p in PlayerHub.All)
                if ((p.transform.position - _island2Spawn.position).sqrMagnitude > 160f * 160f) anyFar = true;
            if (!anyFar) return;
            TeleportObservers(_island2Spawn.position);
            if (_jetSki != null && _jetSkiIsland2Dock != null) _jetSki.ServerPlace(_jetSkiIsland2Dock.position, _jetSkiIsland2Dock.eulerAngles.y);
            // Resuming a save: the keys (handed over in chapter 1) come along too, so the jet ski still goes.
            bool haveKeys = false; // keys in someone's hands or lying on this island (not the dev island's spare set)
            foreach (Item item in Item.All)
                if (item.DisplayName == "Jet Ski Keys" && (item.IsHeld || (item.transform.position - _island2Spawn.position).sqrMagnitude < 80f * 80f))
                    haveKeys = true;
            if (!haveKeys) SpawnItem("Jet Ski Keys", _island2Spawn.position + Vector3.up * 1.2f, Vector3.zero);
            if (RescueService.Instance != null)
                RescueService.Instance.ServerAnnounce($"Story continues on the hotel island ({_beat.Value}). Purple travel pads or Esc > Travel go to the other islands; Esc > Restart the story starts over.");
        }

        // ------------------------------------------------------------------ console

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("story", "[skip | goto <beat> | reset | off | list]", "Story mode: status, skip a beat, jump to one, start over.", StoryCommand, owner: this);
            DevCommands.Register("report", "", "Show the end-of-chapter report card on this screen (to look at it).", _ =>
                StoryHud.ShowReport("CHAPTER 1 COMPLETE", Mathf.Max(10, _chapterRescued), 1, 12, 1155, Economy.Money), owner: this);
            DevCommands.Register("robber", "", "Spawn a robber running around nearby (test).", _ => CheatServer("robber", 0), cheat: true, owner: this);
            DevCommands.Register("shark", "", "A shark bites the nearest tourist in the water (test).", _ => CheatServer("shark", 0), cheat: true, owner: this);
            DevCommands.Register("pirates", "", "Send the pirate boat (test).", _ => CheatServer("pirates", 0), cheat: true, owner: this);
            DevCommands.Register("whack", "[hits]", "Hit the nearest robber/pirate (automated tests).", args =>
                CheatServer("whack", args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 1), cheat: true, owner: this);
            DevCommands.Register("bring", "<item name part>", "Bring matching loose items to your feet (automated tests).", args =>
                CheatServer("bring:" + (args.Length > 0 ? args[0] : ""), 0), cheat: true, owner: this);
            DevCommands.Register("npcs", "", "List story characters.", _ =>
            {
                foreach (StoryNpc n in StoryNpc.All)
                    DevCommands.Print($"  {n.Name,-14} {n.Role,-12} pose {n.Pose,-7} hp {n.Health}/{n.MaxHealth}  at {n.transform.position:F1}{(n.IsMoving ? "  moving " + n.PathInfo : "")}");
            }, owner: this);
            DevCommands.Register("goto", "<name part>", "Teleport next to a tourist, character, item or vehicle and look at it (tests).", args =>
            {
                if (args.Length == 0) throw new ArgumentException("who?");
                Transform target = FindByName(args[0]);
                PlayerHub me = PlayerHub.Local;
                if (target == null || me == null) throw new InvalidOperationException($"nothing called '{args[0]}'");
                Vector3 p = target.position;
                Vector3 away = me.transform.position - p;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
                float radius = 0.5f;
                foreach (Collider c in target.GetComponentsInChildren<Collider>())
                    radius = Mathf.Max(radius, Mathf.Min(4f, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z)));
                Vector3 stand = p + away * (radius + 1.2f);
                float ground = Shore.GroundHeightAt(stand + Vector3.up * 3f);
                if (float.IsNaN(ground)) ground = p.y;
                // In deep water: tread water at the surface instead of standing on the seabed.
                stand.y = Shore.WaterDepthAt(stand) > 1.3f && WaterSurface.Exists ? WaterSurface.HeightAt(stand) - 1.2f : ground + 0.2f;
                me.Motor.Teleport(stand);
                me.Look.LookAt(p + Vector3.up * 0.4f);
                DevCommands.Print($"at {target.name} {p:F1}");
            }, cheat: true, owner: this);
            DevCommands.Register("aimat", "<name part>", "Look at a character's chest (gun tests).", args =>
            {
                if (args.Length == 0) throw new ArgumentException("who?");
                Transform target = FindByName(args[0]);
                PlayerHub me = PlayerHub.Local;
                if (target == null || me == null) throw new InvalidOperationException($"nothing called '{args[0]}'");
                Vector3 chest = target.TryGetComponent(out StoryNpc npc) ? npc.HeadPosition - Vector3.up * 0.35f : target.position + Vector3.up;
                me.Look.LookAt(chest);
                DevCommands.Print($"aiming at {target.name} {(chest - me.Head.position).magnitude:F1} m away");
            }, cheat: true, owner: this);
            DevCommands.Register("tourist", "<f|m> [silent] [flatline]", "Spawn a story tourist in trouble near you.", args =>
            {
                int figure = args.Length > 0 && args[0].StartsWith("f") ? 1 : args.Length > 0 && args[0].StartsWith("m") ? 0 : -1;
                bool silent = Array.IndexOf(args, "silent") >= 0;
                bool flat = Array.IndexOf(args, "flatline") >= 0;
                CheatServer($"tourist:{figure}:{(silent ? 1 : 0)}:{(flat ? 1 : 0)}", 0);
            }, cheat: true, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            foreach (string c in new[] { "story", "report", "robber", "shark", "pirates", "tourist", "whack", "npcs", "bring", "goto", "aimat" })
                DevCommands.Unregister(c, this);
        }

        private static Transform FindByName(string part)
        {
            bool Match(string s) => s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
            if (part.Equals("tourist", StringComparison.OrdinalIgnoreCase) && VictimBrain.All.Count > 0) return VictimBrain.All[VictimBrain.All.Count - 1].transform;
            foreach (VictimBrain v in VictimBrain.All) if (Match(v.Name)) return v.transform;
            foreach (Vehicle v in Vehicle.All) if (Match(v.DisplayName)) return v.transform;
            foreach (StoryNpc n in StoryNpc.All) if (Match(n.Name)) return n.transform;
            foreach (Item i in Item.All) if (!i.IsHeld && Match(i.DisplayName)) return i.transform;
            return null;
        }

        private void StoryCommand(string[] args)
        {
            if (args.Length == 0)
            {
                DevCommands.Print($"beat {_beat.Value}  {_chapter.Value}: {_objective.Value} {(_goal.Value > 0 ? $"{_progress.Value}/{_goal.Value}" : "")}  money ${Economy.Money}");
                return;
            }
            if (args[0] == "list")
            {
                DevCommands.Print("1.1 meet Sandy, 1.2 five rescues, 1.3 thief, 1.4 return loot, 1.5 three men, 1.6 silent one, 1.7 drugs, 1.8 one more, " +
                                  "1.9 thief again, 1.10 jet ski; 2.1 check in, 2.2 buy pistol, 2.3 three guests, 2.4 shark, 2.5 two more, 2.6 pirates, 2.7 the boat");
                return;
            }
            if (!DevCommands.CheatsAllowed && args[0] != "reset") throw new InvalidOperationException("cheats are off");
            CheatServer("story:" + string.Join(" ", args), 0);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CheatServer(string what, int value, NetworkConnection caller = null)
        {
            PlayerHub by = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) by = p;
            ServerCheat(what, value, by);
        }

        /// <summary>
        /// Host: the test commands (robber, shark, tourist, whack, bring, story...). <paramref name="force"/> skips the
        /// cheats-allowed check (the dev island's buttons).
        /// </summary>
        [Server]
        public void ServerCheat(string what, int value, PlayerHub by, bool force = false)
        {
            if (what.StartsWith("story:"))
            {
                string[] args = what.Substring(6).Split(' ');
                switch (args[0])
                {
                    case "skip":
                        if (_beatIndex + 1 < _beats.Count) StartAt(_beatIndex + 1);
                        break;
                    case "goto" when args.Length > 1:
                        int index = _beats.FindIndex(b => b.Id == args[1]);
                        if (index >= 0) StartAt(index);
                        else Debug.LogWarning($"[Story] no beat '{args[1]}'");
                        break;
                    case "reset":
                        if (Economy.Instance != null) Economy.Instance.ServerSet(0);
                        ResetChapterStats();
                        // Everyone back to the station beach for chapter 1.
                        if (Dev.DevIsland.Instance != null && Dev.DevIsland.Instance.Home != null)
                            TeleportObservers(Dev.DevIsland.Instance.Home.position);
                        StartAt(0);
                        break;
                    case "off":
                        StopAllCoroutines();
                        _running = false;
                        CleanupActors();
                        SetObjective("Free play (story off)");
                        NoMarker();
                        break;
                }
                return;
            }
            if (!DevCommands.CheatsAllowed && !force) return;
            // 6 m ahead on the level (looking down must not put things underground).
            Vector3 at = by != null ? by.transform.position + Vector3.ProjectOnPlane(by.Head.forward, Vector3.up).normalized * 6f : Vector3.zero;
            if (what == "robber")
            {
                StoryNpc robber = SpawnNpc("Robber", NpcRole.Robber, RobberLook, at, 0f, 3 * Combat.Damage.Punch);
                robber.ServerFlee(true, new Rect(at.x - 30f, at.z - 30f, 60f, 60f));
            }
            else if (what == "shark")
            {
                VictimBrain target = null;
                foreach (VictimBrain v in VictimBrain.All)
                    if (!v.IsAshore && v.State != VictimState.Lost && (target == null || (v.transform.position - at).sqrMagnitude < (target.transform.position - at).sqrMagnitude))
                        target = v;
                if (target == null) return;
                Shark shark = SpawnShark(target.transform.position + new Vector3(12f, 0f, -10f), target.transform.position, 8f);
                shark.ServerAttack(target);
            }
            else if (what == "pirates")
                StartCoroutine(PirateRaid(null));
            else if (what.StartsWith("bring:") && by != null)
            {
                string filter = what.Substring(6);
                int n = 0;
                foreach (Item item in Item.All)
                {
                    if (item.IsHeld || item.GetComponent<VictimBrain>() != null) continue;
                    if (filter.Length > 0 && item.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Vector3 p = by.transform.position + by.Head.forward * 1.2f + Vector3.up * (1f + 0.3f * n++);
                    if (item.Owner.IsValid) item.RemoveOwnership();
                    item.Sync.Body.position = p;
                    item.transform.position = p;
                    if (!item.Sync.Body.isKinematic) item.Sync.Body.linearVelocity = Vector3.zero;
                }
            }
            else if (what == "whack")
            {
                StoryNpc best = null;
                foreach (StoryNpc n in StoryNpc.All)
                    if (n.MaxHealth > 0 && n.Health > 0 && (best == null || (n.transform.position - at).sqrMagnitude < (best.transform.position - at).sqrMagnitude))
                        best = n;
                for (int i = 0; best != null && i < Mathf.Max(1, value); i++)
                    ((Combat.IDamageable)best).ServerTakeHit(Combat.Damage.Punch, Combat.DamageKind.Punch, by, best.HeadPosition, by != null ? by.Head.forward : Vector3.forward);
            }
            else if (what.StartsWith("tourist:"))
            {
                string[] p = what.Split(':');
                IslandSetup island = at.z < -120f ? _island2 : _island1;
                TouristProfile profile = Profile(island, int.Parse(p[1]), p[2] == "1", p[3] == "1" ? 3f : -1f);
                Vector3 spot = at;
                // On land: collapsed on the sand right in front of you (CPR practice). In the water: in trouble.
                bool onLand = Shore.WaterDepthAt(new Vector3(spot.x, 20f, spot.z)) < Shore.StandDepth;
                if (onLand && by != null)
                {
                    Vector3 flat = Vector3.ProjectOnPlane(by.Head.forward, Vector3.up).normalized;
                    spot = by.transform.position + flat * 1.6f;
                }
                float ground = Shore.GroundHeightAt(spot + Vector3.up * 10f);
                spot.y = onLand && !float.IsNaN(ground) ? ground + 0.5f : WaterSurface.Exists ? WaterSurface.HeightAt(spot) - 0.4f : spot.y;
                if (RescueService.Instance != null)
                    RescueService.Instance.SpawnVictim(spot, 0f, onLand ? VictimState.Unconscious : VictimState.Distressed, onLand ? 0f : 30f, onLand ? 0f : 1f, profile);
            }
        }

        private Shark SpawnShark(Vector3 at, Vector3 center, float radius)
        {
            Vector3 p = at;
            p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) - 0.55f : -0.9f;
            Shark shark = Instantiate(_sharkPrefab, p, Quaternion.identity);
            Spawn(shark.gameObject);
            shark.ServerCircle(center, radius);
            _spawnedActors.Add(shark.gameObject);
            return shark;
        }
    }
}
