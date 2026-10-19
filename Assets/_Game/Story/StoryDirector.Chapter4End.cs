using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// 4.9, the ending (Docs/Prompt-Shako-Chapter-4-Finale.md): the police launch comes to the little dock on the north
    /// shore and takes Sandy and the THIEF; the warehouse empties (every lost thing goes home); everyone from islands 1-3
    /// waves from the beach by the plaza (a cast call); the "CHAPTER 4 COMPLETE" report; the credits
    /// (<see cref="CreditsRoll"/>); then the open shift starts, on all four islands (<see cref="FreePlayIsland"/>).
    /// </summary>
    public partial class StoryDirector
    {
        private const int ReturnedReward = 1000, ThingsReturned = 120;
        private IslandSetup _island3Free;
        private StoryNpc _officer;

        // Where the officer stands on the launch's deck (its own space): by the dock side while it's tied up, behind
        // Sandy and the THIEF on the way out.
        private static readonly Vector3 OfficerAtBerth = new(-0.7f, 0.78f, 3.3f), OfficerAway = new(0f, 0.78f, 0.45f);

        /// <summary>The officer who makes the arrests: the lifeguard body in navy, a cap, a moustache (aviators hid his eyes in the name plate's portrait).</summary>
        public static AvatarLook PoliceLook
        {
            get
            {
                AvatarLook look = AvatarLook.Lifeguard;
                look.TopColor = 7;      // navy
                look.BottomColor = 7;
                look.Hat = HatStyle.Cap;
                look.HatColor = 7;
                look.Glasses = GlassesStyle.None;
                look.Face = FacialHair.Mustache;
                look.Skin = 1;
                return look;
            }
        }

        /// <summary>Skull Cove's sea for the open shift: off the east beach by the pirates' dock. Island 2's rules.</summary>
        private IslandSetup Island3FreeSetup => _island3Free ??= new IslandSetup
        {
            Name = "Skull Cove",
            SeaX = new Vector2(124f, 150f),
            SeaZ = new Vector2(-935f, -872f),
            Shoreward = Vector3.left,
            Profile = _island2.Profile,
            LandArea = new Rect(80f, -940f, 30f, 70f)
        };

        private static ScriptedBoat PoliceBoat => GameObject.Find("Environment/Island4/PoliceBoat") is { } b ? b.GetComponent<ScriptedBoat>() : null;

        private static readonly (string name, string line)[] CastCall =
        {
            ("Marisol", "Come back to the hotel any time! ...Seriously. Please."),
            ("Milo", "Keep the jet skis! ...No. Bring them back."),
            ("Iron Jaw Jack", "REMATCH!"),
            ("The Pirate King", "Arr! Ye did good, lifeguards!"),
            ("Bernie", "You saved me THREE times!"),
            ("Lola", "My sunglasses came back!"),
            ("Mr. Castellane", "The casino reopens Friday. Bring the THIEF... no, don't."),
            ("Rosa", "Free coffee for lifeguards! Forever! ...Until Tuesday."),
        };

        private AvatarLook CastLook(int i) => i switch
        {
            0 => ReceptionistLook,
            1 => RentalLook,
            2 => PirateLookOf(AvatarLook.Bodies.PirateFat),
            3 => PirateLookOf(AvatarLook.Bodies.PirateKing),
            4 => GuestLook(1234, 0),
            5 => new AvatarLook { Body = AvatarLook.Bodies.Lola, Figure = 1 },
            6 => new AvatarLook { Body = AvatarLook.Bodies.HotelDirector },
            _ => new AvatarLook { Body = AvatarLook.Bodies.BaristaGirl, Figure = 1 }
        };

        private IEnumerator TheEnding()
        {
            EnsureOnIsland4();
            ClearFinaleLeftovers();
            if (_officer != null) { Remove(_officer); _officer = null; }
            Transform dock = Island4Point("PoliceDock"), berth = Island4Point("PoliceBoatBerth"), far = Island4Point("PoliceBoatStart"), cast = Island4Point("CastWave");
            ScriptedBoat boat = PoliceBoat;
            if (dock == null || berth == null || far == null || cast == null || boat == null || _sandy == null) yield break;
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyBossLook);
            _sandy.ServerSetTalkable(false);
            _sandy.ServerSetPose(AvatarPose.Normal);
            _sandy.ServerSetMood(AvatarMood.Hurt);
            // The THIEF turns himself in (well: he's brought along).
            if (_thief == null || !_thief.IsSpawned) _thief = SpawnNpc("THIEF", NpcRole.Guide, RobberLook, dock.position + Vector3.back * 6f, 0f);
            _thief.ServerSetTalkable(false);
            _thief.ServerSetMood(AvatarMood.Scared);
            boat.ServerPlace(far.position, far.eulerAngles.y);
            boat.ServerSailTo(berth.position, berth.eulerAngles.y, 10f);
            TitleObservers("THE POLICE", "Finders Keepers Rescue Co. is closed");
            SetObjective("The police launch is coming: to the little dock on the north shore");
            Marker("Police dock", dock.position + Vector3.up * 2f);
            // Sandy and the THIEF walk there with the team.
            _sandy.ServerMoveTo(dock.position + Vector3.back * 2f + Vector3.left * 0.8f, run: false);
            _thief.ServerMoveTo(dock.position + Vector3.back * 2f + Vector3.right * 0.8f, run: false);
            float t0 = Time.time;
            while ((!boat.Arrived || StoryNpc.NearestPlayer(dock.position, 14f) == null) && Time.time - t0 < 120f) yield return new WaitForSeconds(0.3f);
            if (!boat.Arrived) boat.ServerPlace(berth.position, berth.eulerAngles.y);
            // Anyone not there yet is brought along (the ending waits for nobody's walk).
            foreach (StoryNpc n in new[] { _sandy, _thief })
                if (n != null && (n.transform.position - dock.position).sqrMagnitude > 5f * 5f)
                    n.ServerTeleport(dock.position + Vector3.back * 2f + (n == _sandy ? Vector3.left : Vector3.right) * 0.8f, 180f);
            NoMarker();
            // The officer comes out of the cabin onto the deck by the dock, facing Sandy.
            Vector3 aboard = boat.Deck(OfficerAtBerth), toSandy = _sandy.transform.position - aboard;
            toSandy.y = 0f;
            _officer = SpawnNpc("Officer Gill", NpcRole.Police, PoliceLook, aboard, 0f);
            _officer.ServerSetTalkable(false);
            _officer.ServerTeleport(aboard, toSandy.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toSandy).eulerAngles.y : boat.transform.eulerAngles.y, keepExact: true);
            yield return Say(_officer, "Sandy, of Finders Keepers Rescue Co.? You're nicked. Theft, fraud, pills in the smoothies, and seven hundred airlift bills.");
            yield return Say(_sandy, "Seven hundred and TWELVE, officer. I kept records. Very good records.");
            yield return ThiefSays("Can I just say, officer: I was never here.");
            yield return Say(_officer, "You're nicked too. Hands where I can see them.");
            yield return Say(_officer, "And you lot: good work. Every tourist home safe, and their things too.");
            // Aboard, and away.
            Coroutine ride = StartCoroutine(RideAway(boat, far));
            yield return new WaitForSeconds(1f);

            // The lost things go home: the warehouse empties.
            WarehouseEmptiesObservers();
            TitleObservers("THE LOST THINGS GO HOME", "Every wallet, phone and pair of sunglasses, back to its owner");
            _chapterReturned += ThingsReturned;
            if (Economy.Instance != null) Economy.Instance.ServerAdd(ReturnedReward, "every lost thing returned (the islands' thanks)", dock.position + Vector3.up * 2f);
            yield return new WaitForSeconds(3f);

            // Everyone from islands 1-3 on the beach by the plaza, waving.
            var castNpcs = new List<StoryNpc>();
            Vector3 along = Quaternion.Euler(0f, cast.eulerAngles.y + 90f, 0f) * Vector3.forward;
            for (int i = 0; i < CastCall.Length; i++)
            {
                Vector3 at = cast.position + along * ((i - (CastCall.Length - 1) * 0.5f) * 1.6f);
                StoryNpc n = SpawnNpc(CastCall[i].name, i == 0 ? NpcRole.Receptionist : i is 2 or 3 ? NpcRole.Pirate : NpcRole.Bystander, CastLook(i), at, cast.eulerAngles.y + 180f);
                n.ServerSetMood(AvatarMood.Happy);
                castNpcs.Add(n);
            }
            SetObjective("Everyone's on the beach by the plaza!");
            Marker("Everyone", cast.position + Vector3.up * 2.5f);
            t0 = Time.time;
            while (StoryNpc.NearestPlayer(cast.position, 16f) == null && Time.time - t0 < 60f) yield return new WaitForSeconds(0.3f);
            NoMarker();
            if (StoryNpc.NearestPlayer(cast.position, 16f) == null) TeleportObservers(cast.position + Quaternion.Euler(0f, cast.eulerAngles.y + 180f, 0f) * Vector3.forward * -7f);
            Coroutine waving = StartCoroutine(CastWaves(castNpcs));
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < castNpcs.Count; i++)
            {
                if (castNpcs[i] == null) continue;
                castNpcs[i].ServerShout(CastCall[i].line, false);
                yield return new WaitForSeconds(1.4f);
            }
            SetChapter("End of chapter 4");
            SetObjective("The end. (The open shift starts after the credits.)");
            yield return ChapterReport("CHAPTER 4 COMPLETE");
            CreditsObservers();
            yield return new WaitForSeconds(CreditsRoll.Seconds);
            StopCoroutine(waving);
            StopCoroutine(ride);
            foreach (StoryNpc n in castNpcs) if (n != null) Remove(n);
            // Sandy: community service at her old kiosk (the open shift's Sandy), the THIEF gone with the police.
            if (_thief != null) { Remove(_thief); _thief = null; }
            if (_officer != null) { Remove(_officer); _officer = null; }
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyLook);
            Debug.Log("[Story] the end: credits seen");
        }

        /// <summary>Sandy and the THIEF stand on the launch's deck while it sails out to sea, the officer behind them.</summary>
        private IEnumerator RideAway(ScriptedBoat boat, Transform far)
        {
            boat.ServerSailTo(far.position, far.eulerAngles.y, 7f);
            while (boat != null)
            {
                if (_sandy != null) _sandy.ServerTeleport(boat.Deck(new Vector3(-0.5f, 0.78f, 1.6f)), boat.transform.eulerAngles.y, keepExact: true);
                if (_thief != null && _thief.IsSpawned) _thief.ServerTeleport(boat.Deck(new Vector3(0.5f, 0.78f, 1.6f)), boat.transform.eulerAngles.y, keepExact: true);
                if (_officer != null && _officer.IsSpawned) _officer.ServerTeleport(boat.Deck(OfficerAway), boat.transform.eulerAngles.y, keepExact: true);
                if (boat.Arrived) yield break;
                yield return null;
            }
        }

        private static IEnumerator CastWaves(List<StoryNpc> cast)
        {
            while (true)
            {
                foreach (StoryNpc n in cast)
                    if (n != null && Random.value < 0.5f) n.ServerGesture(AvatarGesture.Wave);
                yield return new WaitForSeconds(1.1f);
            }
        }

        [ObserversRpc(BufferLast = true)]
        private void WarehouseEmptiesObservers()
        {
            Transform hangar = GameObject.Find("Environment/Island4/Warehouse")?.transform;
            if (hangar == null) return;
            foreach (Transform t in hangar.GetComponentsInChildren<Transform>(true))
                if (t.parent != null && t.parent.name == "LootTower" && (t.name.StartsWith("Model_") || t.name == "Chips")) t.gameObject.SetActive(false);
        }

        [ObserversRpc]
        private void CreditsObservers() => CreditsRoll.Show();

        /// <summary>chapter4check, 4.9: the police, the warehouse emptying, the cast call, the report, the credits, the
        /// open shift on all four islands.</summary>
        private IEnumerator Chapter4CheckEnding(PlayerHub me)
        {
            void Stand(Vector3 p) => me.Motor.Teleport(p + Vector3.up * 0.3f);
            yield return WaitBeat("4.9", 30f);
            Check("4.9 the ending starts", _beat.Value == "4.9");
            Transform dock = Island4Point("PoliceDock"), berth = Island4Point("PoliceBoatBerth"), cast = Island4Point("CastWave");
            if (dock != null) Stand(dock.position + Vector3.back * 3f);
            float t = Time.time;
            while (PoliceBoat != null && !PoliceBoat.Arrived && Time.time - t < 60f) yield return null;
            Check("4.9 the police launch ties up at the police dock", PoliceBoat != null && berth != null && (PoliceBoat.transform.position - berth.position).magnitude < 3f);
            t = Time.time;
            while (_markerLabel.Value != "Everyone" && Time.time - t < 60f) yield return null;
            Check("4.9 Sandy and the THIEF are taken away on it", _sandy != null && PoliceBoat != null && (_sandy.transform.position - PoliceBoat.transform.position).magnitude < 4f);
            int shown = 0;
            foreach (Transform tr in GameObject.Find("Environment/Island4/Warehouse").GetComponentsInChildren<Transform>())
                if (tr.parent != null && tr.parent.name == "LootTower" && tr.name.StartsWith("Model_")) shown++;
            Check("4.9 the warehouse empties: the lost things go home", shown == 0, $"{shown} still there");
            int there = 0;
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && cast != null && System.Array.Exists(CastCall, c => c.name == n.Name) && (n.transform.position - cast.position).magnitude < 12f) there++;
            Check("4.9 everyone from islands 1-3 waves from the beach", there == CastCall.Length, $"{there}/{CastCall.Length}");
            if (cast != null) Stand(cast.position + Vector3.forward * 6f);
            t = Time.time;
            while (!CreditsRoll.Rolling && Time.time - t < 60f) yield return null;
            Check("4.9 the chapter 4 report, then the credits roll", CreditsRoll.Rolling);
            t = Time.time;
            while (!IsFreePlay && Time.time - t < CreditsRoll.Seconds + 15f) yield return null;
            Check("4.9 after the credits the open shift starts", IsFreePlay && _beat.Value == "end");
            int seas = 0;
            foreach (IslandSetup s in new[] { _island1, _island2, Island3FreeSetup, Island4Setup })
                if (RescueService.Instance != null && RescueService.Instance.TryFindSeaSpot(s.SeaX, s.SeaZ, 2f, 7f, out _)) seas++;
            Check("4.9 the open shift has a sea for tourists on all four islands", seas == 4, $"{seas}/4");
        }
    }
}
