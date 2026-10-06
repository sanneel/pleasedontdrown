using System;
using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// Scene service for rescues (host-run). Spawns tourists in trouble: rescue drills from the station board now,
    /// the emergency director later (M9). Also the rescue console commands.
    /// </summary>
    public class RescueService : NetworkBehaviour
    {
        [Header("Drill spawn area (sea in front of the station)")]
        [SerializeField] private Vector2 _drillX = new Vector2(-24f, 20f);
        [SerializeField] private Vector2 _drillZ = new Vector2(-38f, -16f);
        [SerializeField] private float _minDrillDepth = 1.8f;
        [SerializeField] private int _maxInTrouble = 4;
        [SerializeField] private string _touristItem = "Tourist";

        private StationBell _bell;

        public static RescueService Instance { get; private set; }

        public static int InTroubleCount
        {
            get
            {
                int n = 0;
                foreach (VictimBrain v in VictimBrain.All)
                    if (v.State.NeedsHelp()) n++;
                return n;
            }
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ drills

        /// <summary>Anyone can start a drill (station board). The host picks a spot out in the water.</summary>
        public void RequestDrill() => DrillServer();

        [ServerRpc(RequireOwnership = false)]
        private void DrillServer(NetworkConnection caller = null)
        {
            if (InTroubleCount >= _maxInTrouble)
            {
                Tell(caller, $"Already {InTroubleCount} tourists in the water. Rescue them first!");
                return;
            }
            if (!TryFindDrillSpot(out Vector3 spot))
            {
                Tell(caller, "Couldn't find deep enough water for a drill.");
                return;
            }
            VictimBrain victim = SpawnVictim(spot, Random.Range(-40f, 40f), VictimState.Distressed, Random.Range(22f, 34f), 1f);
            if (victim == null) return;
            if (_bell == null) _bell = FindFirstObjectByType<StationBell>();
            if (_bell != null) _bell.ServerRing("drill");
            else Debug.LogWarning("[Rescue] no station bell to ring");
            Vector3 station = new Vector3(0f, 0f, 10f);
            float distance = Vector3.Distance(new Vector3(spot.x, 0f, spot.z), station);
            AnnounceObservers($"<color=#ffd060><b>RESCUE DRILL:</b></color> {victim.Name} is in trouble in the water, {distance:F0} m out!");
            Debug.Log($"[Rescue] drill started by {PlayerHub.NameOf(caller)}: {victim.Name} at {spot:F1} (depth {Shore.WaterDepthAt(spot):F1} m)");
        }

        [Server]
        private bool TryFindDrillSpot(out Vector3 spot)
        {
            for (int i = 0; i < 30; i++)
            {
                var p = new Vector3(Random.Range(_drillX.x, _drillX.y), 0f, Random.Range(_drillZ.x, _drillZ.y));
                p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) - 0.4f : -0.7f;
                float depth = Shore.WaterDepthAt(p);
                if (depth < _minDrillDepth || depth > 8f) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        /// <summary>Host: a spot in open water inside an x/z box, between the given depths (story tourists).</summary>
        [Server]
        public bool TryFindSeaSpot(Vector2 xRange, Vector2 zRange, float minDepth, float maxDepth, out Vector3 spot)
        {
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector3(Random.Range(xRange.x, xRange.y), 0f, Random.Range(zRange.x, zRange.y));
                p.y = WaterSurface.Exists ? WaterSurface.HeightAt(p) - 0.4f : -0.7f;
                float depth = Shore.WaterDepthAt(p);
                if (depth < minDepth || depth > maxDepth) continue;
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        /// <summary>Host: ring the station bell (if there is one).</summary>
        [Server]
        public void ServerRingBell(string why)
        {
            if (_bell == null) _bell = FindFirstObjectByType<StationBell>();
            if (_bell != null) _bell.ServerRing(why);
        }

        [Server]
        public void ServerAnnounce(string text) => AnnounceObservers(text);

        /// <summary>Host: put a tourist in the world with a behaviour profile (story tourists).</summary>
        [Server]
        public VictimBrain SpawnVictim(Vector3 position, float yaw, VictimState state, float panic, float air, TouristProfile profile)
        {
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(_touristItem) : null;
            if (prefab == null)
            {
                Debug.LogError($"[Rescue] no '{_touristItem}' in the item catalog");
                return null;
            }
            Item instance = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            Spawn(instance.gameObject);
            var brain = instance.GetComponent<VictimBrain>();
            brain.ServerSetup(null, Random.Range(1, int.MaxValue), state, panic, air, profile);
            return brain;
        }

        /// <summary>Host: put a tourist in the world.</summary>
        [Server]
        public VictimBrain SpawnVictim(Vector3 position, float yaw, VictimState state, float panic, float air)
        {
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(_touristItem) : null;
            if (prefab == null)
            {
                Debug.LogError($"[Rescue] no '{_touristItem}' in the item catalog");
                return null;
            }
            Item instance = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            Spawn(instance.gameObject);
            var brain = instance.GetComponent<VictimBrain>();
            brain.ServerSetup(null, Random.Range(1, int.MaxValue), state, panic, air);
            return brain;
        }

        [ObserversRpc]
        private void AnnounceObservers(string text) => PlayerHud.ShowToast(text, 6f);

        [TargetRpc]
        private void Tell(NetworkConnection target, string text)
        {
            PlayerHud.ShowToast(text);
            DevCommands.Print(text);
        }

        // ------------------------------------------------------------------ console

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("drill", "", "Start a rescue drill (same as the board at the station).", _ => RequestDrill(), owner: this);
            DevCommands.Register("victims", "", "List tourists: state, air, panic, who simulates them.", _ =>
            {
                if (VictimBrain.All.Count == 0) DevCommands.Print("  no tourists");
                foreach (VictimBrain v in VictimBrain.All) DevCommands.Print("  " + v.Describe());
            }, owner: this);
            DevCommands.Register("victim", "[distance] [state] [f|m]", "Spawn a tourist in front of you (default 10 m, distressed, either figure).", VictimCommand, cheat: true, owner: this);
            DevCommands.Register("vset", "<state|air|panic|condition> <value>", "Change the nearest tourist (state: fine distressed panicking drowning unconscious saved lost).",
                VsetCommand, cheat: true, owner: this);
            DevCommands.Register("cpr", "[pumps] [seconds between]", "Do CPR on the nearest tourist (automated tests).", args =>
            {
                int pumps = args.Length > 0 ? Mathf.Clamp((int)DevCommands.ParseFloat(args, 0), 1, 40) : 1;
                float gap = args.Length > 1 ? Mathf.Max(0.1f, DevCommands.ParseFloat(args, 1)) : 0.2f;
                StartCoroutine(PumpRoutine(pumps, gap));
            }, cheat: true, owner: this);
            DevCommands.Register("clearvictims", "", "Remove every tourist.", _ => ClearServer(), cheat: true, owner: this);
            DevCommands.Register("ragdoll", "", "Show where the nearest tourist's limbs point (pose debugging).", _ =>
            {
                VictimBrain v = Nearest();
                DevCommands.Print(v == null ? "no tourists" : $"{v.Name} ({v.State}), torso up {v.transform.up:F2}\n{v.Body.DescribeLimbs()}");
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            foreach (string c in new[] { "drill", "victims", "victim", "vset", "cpr", "clearvictims", "ragdoll" })
                DevCommands.Unregister(c, this);
        }

        private void VictimCommand(string[] args)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null) throw new InvalidOperationException("no local player");
            float distance = args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 10f;
            VictimState state = args.Length > 1 ? ParseState(args[1]) : VictimState.Distressed;
            Vector3 flat = me.Head.forward;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            Vector3 spot = me.transform.position + flat * distance;
            spot.y = WaterSurface.Exists ? WaterSurface.HeightAt(spot) - 0.4f : me.transform.position.y + 1f;
            float ground = Shore.GroundHeightAt(spot + Vector3.up * 20f);
            if (!float.IsNaN(ground)) spot.y = Mathf.Max(spot.y, ground + 0.5f); // on land: lying on the sand, not under it
            float yaw = Quaternion.LookRotation(-flat).eulerAngles.y; // facing you
            int figure = args.Length > 2 ? args[2].StartsWith("f") ? 1 : args[2].StartsWith("m") ? 0 : -1 : -1;
            SpawnVictimServer(spot, yaw, state, figure);
            DevCommands.Print($"spawning a {state} tourist {distance:F0} m ahead (water depth {Shore.WaterDepthAt(spot):F1} m)");
        }

        [ServerRpc(RequireOwnership = false)]
        private void SpawnVictimServer(Vector3 position, float yaw, VictimState state, int figure)
        {
            if (!DevCommands.CheatsAllowed) return;
            float panic = state switch { VictimState.Panicking => 60f, VictimState.Drowning => 90f, VictimState.Distressed => 25f, _ => 0f };
            float air = state switch { VictimState.Drowning => 0.3f, VictimState.Unconscious => 0f, _ => 1f };
            TouristProfile profile = TouristProfile.Default;
            profile.Figure = figure;
            VictimBrain v = SpawnVictim(position, yaw, state, panic, air, profile);
            if (v != null) Debug.Log($"[Rescue] cheat-spawned {v.Name} ({state}) at {position:F1}");
        }

        private void VsetCommand(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("need a field and a value");
            VictimBrain v = Nearest();
            if (v == null) throw new InvalidOperationException("no tourists"); // explicit: "??" misses destroyed Unity objects
            float value = args[0] == "state" ? (float)ParseState(args[1]) : DevCommands.ParseFloat(args, 1);
            SetServer(v, args[0], value);
            DevCommands.Print($"{v.Name}: {args[0]} = {args[1]}");
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetServer(VictimBrain victim, string field, float value)
        {
            if (!DevCommands.CheatsAllowed || victim == null) return;
            victim.ServerCheat(field, value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ClearServer()
        {
            if (!DevCommands.CheatsAllowed) return;
            foreach (VictimBrain v in new System.Collections.Generic.List<VictimBrain>(VictimBrain.All))
                Despawn(v.gameObject);
        }

        private IEnumerator PumpRoutine(int pumps, float gap)
        {
            VictimBrain v = Nearest();
            if (v == null)
            {
                DevCommands.Print("no tourists");
                yield break;
            }
            float last = -10f;
            for (int i = 0; i < pumps && v != null; i++)
            {
                // A breath or a slap takes its time (and the host ignores one within 0.45 s of the last press).
                if (v.NextCprStep != CprStep.Compress && Time.time - last < 0.9f) yield return new WaitForSeconds(0.9f - (Time.time - last));
                if (v == null) break;
                v.RequestPump();
                last = Time.time;
                yield return new WaitForSeconds(gap);
            }
            if (v != null) DevCommands.Print($"{v.Name}: {v.State}, cpr {v.Cpr01:P0}");
        }

        private static VictimBrain Nearest()
        {
            PlayerHub me = PlayerHub.Local;
            Vector3 from = me != null ? me.transform.position : Vector3.zero;
            VictimBrain best = null;
            float bestSq = float.MaxValue;
            foreach (VictimBrain v in VictimBrain.All)
            {
                float d = (v.transform.position - from).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = v;
                }
            }
            return best;
        }

        private static VictimState ParseState(string s)
        {
            if (Enum.TryParse(s, true, out VictimState state)) return state;
            throw new ArgumentException($"unknown state '{s}'");
        }
    }
}
