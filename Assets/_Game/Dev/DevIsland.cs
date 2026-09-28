using System;
using System.Collections;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>Where the travel pads and the pause menu can take you.</summary>
    public enum Destination : byte { StationBeach, HotelIsland, DevIsland }

    public enum DevAction : byte
    {
        DrowningWoman, DrowningMan, SilentWoman, CprWoman, CprMan, Flatline, Robber, Shark, Money, ClearTourists
    }

    /// <summary>
    /// The dev island: a sandbox off the west side of island 1 with every gun and part for free, a shooting range,
    /// the generated characters standing on pedestals, every item laid out (taken ones come back), a board of buttons
    /// that start rescues, and a jet ski. Teleport pads (and the 'devisland' / 'home' commands) go there and back.
    /// </summary>
    public class DevIsland : NetworkBehaviour
    {
        [Serializable]
        public struct Shelf
        {
            public string Item;       // catalog name
            public Transform Spot;
        }

        [Serializable]
        public struct Model
        {
            public string Name;
            public byte Body;         // AvatarLook.Bodies id, 0 = code-built
            public bool Feminine;
            public StoryNpc Npc;
        }

        [SerializeField] private Shelf[] _shelves = Array.Empty<Shelf>();
        [SerializeField] private Model[] _gallery = Array.Empty<Model>();
        [SerializeField] private Transform _arrival;
        [SerializeField] private Transform _home;
        [SerializeField] private Transform _hotel;
        [SerializeField] private Transform _seaSpot;
        [SerializeField] private Transform _beachSpot;

        public static DevIsland Instance { get; private set; }
        public Transform Arrival => _arrival;
        public Transform Home => _home;

        public static readonly string[] DestinationNames = { "Station beach (island 1)", "Hotel island (island 2)", "Dev island" };

        /// <summary>The local player goes to <paramref name="where"/> (players move themselves: nothing to send).</summary>
        public static void Travel(Destination where)
        {
            PlayerHub me = PlayerHub.Local;
            if (Instance == null || me == null) return;
            if (me.Motor.Seat != null)
            {
                PlayerHud.ShowToast("Get off the vehicle first.", 3f);
                return;
            }
            Transform spot = where switch
            {
                Destination.HotelIsland => Instance._hotel,
                Destination.DevIsland => Instance._arrival,
                _ => Instance._home
            };
            Teleport(me, spot);
            PlayerHud.ShowToast(where switch
            {
                Destination.DevIsland => "Dev island: guns (table and armory), the range, rescue test buttons, the models. Console: ` or F1 / F2.",
                Destination.HotelIsland => "The hotel island. Purple pads (or Esc > Travel) take you back.",
                _ => "The station beach."
            }, 5f);
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            StartCoroutine(SetUpGallery());
            StartCoroutine(Restock());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("devisland", "", "Go to the dev island (guns, range, test buttons, models).", _ => Travel(Destination.DevIsland), owner: this);
            DevCommands.Register("home", "", "Go to the station beach (island 1).", _ => Travel(Destination.StationBeach), owner: this);
            DevCommands.Register("hotel", "", "Go to the hotel island (island 2).", _ => Travel(Destination.HotelIsland), owner: this);
            DevCommands.Register("devtest", "<action>", $"Press a dev island test button: {string.Join(", ", Enum.GetNames(typeof(DevAction)))}.", args =>
            {
                if (args.Length == 0 || !Enum.TryParse(args[0], true, out DevAction action)) throw new ArgumentException("which button?");
                PressServer(action);
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("devisland", this);
            DevCommands.Unregister("home", this);
            DevCommands.Unregister("hotel", this);
            DevCommands.Unregister("devtest", this);
        }

        /// <summary>Moves a player (on their own machine) and turns them the way the spot faces.</summary>
        private static void Teleport(PlayerHub player, Transform spot)
        {
            if (player == null || spot == null) return;
            player.Motor.Teleport(spot.position + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0f, UnityEngine.Random.Range(-0.8f, 0.8f)));
            player.Look.LookAt(spot.position + spot.forward * 10f + Vector3.up * 1.5f);
        }

        private IEnumerator SetUpGallery()
        {
            yield return null; // NPCs start their own networking first
            foreach (Model m in _gallery)
            {
                if (m.Npc == null) continue;
                AvatarLook look = m.Body == 0 ? AvatarLook.Lifeguard : new AvatarLook { Body = m.Body, Figure = (byte)(m.Feminine ? 1 : 0) };
                if (m.Body == 0 && m.Feminine) look.Figure = 1;
                m.Npc.ServerSetup(m.Name, NpcRole.Bystander, look);
                m.Npc.ServerSetPose(AvatarPose.Normal);
                m.Npc.ServerFace(m.Npc.transform.position + m.Npc.transform.forward * 5f);
            }
        }

        /// <summary>Every few seconds: anything taken from a shelf spot comes back.</summary>
        private IEnumerator Restock()
        {
            var wait = new WaitForSeconds(4f);
            yield return new WaitForSeconds(1f);
            while (true)
            {
                foreach (Shelf shelf in _shelves)
                {
                    if (shelf.Spot == null || GameContent.Items == null) continue;
                    Item prefab = GameContent.Items.Find(shelf.Item);
                    if (prefab == null) continue;
                    bool there = false;
                    foreach (Item item in Item.All)
                        if (!item.IsHeld && item.DisplayName == prefab.DisplayName && (item.transform.position - shelf.Spot.position).sqrMagnitude < 1.5f * 1.5f)
                        {
                            there = true;
                            break;
                        }
                    if (there) continue;
                    Item fresh = Instantiate(prefab, shelf.Spot.position, shelf.Spot.rotation);
                    Spawn(fresh.gameObject);
                }
                yield return wait;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PressServer(DevAction action, NetworkConnection caller = null)
        {
            PlayerHub by = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) by = p;
            ServerAction(action, by);
        }

        /// <summary>Host: a test button was pressed.</summary>
        [Server]
        public void ServerAction(DevAction action, PlayerHub by)
        {
            Debug.Log($"[Dev] {action} by {(by != null ? by.DisplayName : "?")}");
            RescueService rescue = RescueService.Instance;
            TouristProfile profile = TouristProfile.Default;
            profile.BleedSeconds = 90f;
            Vector3 sea = _seaSpot.position + new Vector3(UnityEngine.Random.Range(-4f, 4f), 0f, UnityEngine.Random.Range(-4f, 4f));
            Vector3 beach = _beachSpot.position + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0.4f, UnityEngine.Random.Range(-1.5f, 1.5f));
            float yaw = UnityEngine.Random.Range(0f, 360f);
            switch (action)
            {
                case DevAction.DrowningWoman:
                case DevAction.DrowningMan:
                case DevAction.SilentWoman:
                    profile.Figure = action == DevAction.DrowningMan ? 0 : 1;
                    profile.Silent = action == DevAction.SilentWoman;
                    profile.SecondsToUnconscious = 25f;
                    profile.NeedsCpr = true;
                    if (rescue != null) rescue.SpawnVictim(sea, yaw, VictimState.Distressed, 30f, 1f, profile);
                    break;
                case DevAction.CprWoman:
                case DevAction.CprMan:
                case DevAction.Flatline:
                    profile.Figure = action == DevAction.CprMan ? 0 : 1;
                    profile.NeedsCpr = true;
                    profile.FlatlineAfter = action == DevAction.Flatline ? 1f : 0f;
                    if (rescue != null) rescue.SpawnVictim(beach, yaw, VictimState.Unconscious, 0f, 0f, profile);
                    break;
                case DevAction.Robber:
                    if (StoryDirector.Instance != null) StoryDirector.Instance.ServerCheat("robber", 0, by, force: true);
                    break;
                case DevAction.Shark:
                    if (rescue != null && !AnyoneInTheSea()) rescue.SpawnVictim(sea, yaw, VictimState.Distressed, 30f, 1f, profile);
                    if (StoryDirector.Instance != null) StartCoroutine(SharkSoon(by));
                    break;
                case DevAction.Money:
                    if (Story.Economy.Instance != null) Story.Economy.Instance.ServerAdd(1000, "dev island", by != null ? by.Head.position + Vector3.up * 0.3f : transform.position);
                    break;
                case DevAction.ClearTourists:
                    foreach (VictimBrain v in VictimBrain.All.ToArray())
                        if ((v.transform.position - transform.position).sqrMagnitude < 120f * 120f) Despawn(v.gameObject);
                    break;
            }
        }

        private bool AnyoneInTheSea()
        {
            foreach (VictimBrain v in VictimBrain.All)
                if (!v.IsAshore && (v.transform.position - _seaSpot.position).sqrMagnitude < 40f * 40f) return true;
            return false;
        }

        private IEnumerator SharkSoon(PlayerHub by)
        {
            yield return new WaitForSeconds(0.5f);
            StoryDirector.Instance.ServerCheat("shark", 0, by, force: true);
        }
    }
}
