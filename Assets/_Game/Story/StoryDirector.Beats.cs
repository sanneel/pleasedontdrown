using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>The story itself: chapter 1 (the first island) and chapter 2 (the hotel island). See Docs/05-Story-Mode.md.</summary>
    public partial class StoryDirector
    {
        // ------------------------------------------------------------------ cast

        public static AvatarLook SandyLook => new()
        {
            Figure = 1, Build = 0, Height = 2, Skin = 0, Hair = HairStyle.Ponytail, HairColor = 3,
            Top = TopStyle.Hawaiian, TopColor = 3, Bottom = BottomStyle.Trousers, BottomColor = 12
        };

        public static AvatarLook ReceptionistLook => new()
        {
            Figure = 1, Build = 1, Height = 1, Skin = 4, Hair = HairStyle.Long, HairColor = 0,
            Top = TopStyle.TShirt, TopColor = 7, Bottom = BottomStyle.Trousers, BottomColor = 14
        };

        public static AvatarLook RobberLook => new()
        {
            Figure = 0, Build = 0, Height = 2, Skin = 2, Hair = HairStyle.Short, HairColor = 0,
            Top = TopStyle.TShirt, TopColor = 14, Bottom = BottomStyle.Trousers, BottomColor = 7,
            Hat = HatStyle.Bandana, HatColor = 14, Glasses = GlassesStyle.Sunglasses, Face = FacialHair.Stubble
        };

        public static AvatarLook PirateLook(int i) => new()
        {
            Figure = 0, Build = (byte)(i % 2 == 0 ? 2 : 3), Height = (byte)(1 + i % 3), Skin = (byte)(2 + i % 4),
            Hair = HairStyle.Long, HairColor = (byte)(i % 3), Top = i % 2 == 0 ? TopStyle.Tank : TopStyle.None, TopColor = 1,
            Bottom = BottomStyle.Trousers, BottomColor = 11, Hat = HatStyle.Bandana, HatColor = 0, Face = i % 3 == 2 ? FacialHair.Mustache : FacialHair.Beard
        };

        private static AvatarLook GuestLook(int seed, int figure) => AvatarLook.RandomTourist(seed, figure);

        // ------------------------------------------------------------------ the beats, in order

        private void BuildBeats()
        {
            _beats.Clear();
            void Add(string id, string title, System.Func<IEnumerator> run) => _beats.Add(new Beat { Id = id, Title = title, Run = run });
            Add("1.1", "Meet Sandy", MeetSandy);
            Add("1.2", "First shift", FirstShift);
            Add("1.3", "The thief", Thief);
            Add("1.4", "Return the loot", ReturnLoot);
            Add("1.5", "Three more", ThreeMen);
            Add("1.6", "The silent one", SilentOne);
            Add("1.7", "Drugs?", DrugReveal);
            Add("1.8", "One more", OneMore);
            Add("1.9", "The thief again", ThiefAgain);
            Add("1.10", "Leave the island", LeaveIsland);
            Add("2.1", "Check in", CheckIn);
            Add("2.2", "Buy a weapon", BuyWeapon);
            Add("2.3", "Harder work", HarderWork);
            Add("2.4", "Shark!", SharkAttack);
            Add("2.5", "Two more", TwoMore);
            Add("2.6", "Pirates", Pirates);
            Add("2.7", "Your boat now", TheBoat);
        }

        // ================================================================== chapter 1

        private IEnumerator MeetSandy()
        {
            SetChapter("Chapter 1: The first island");
            _island = _island1;
            _nextAmbientLoss = Time.time + 90f;
            TitleObservers("CHAPTER 1", "The first island");
            yield return new WaitForSeconds(2f);
            SetObjective("Talk to Sandy at the Lost & Found kiosk");
            Marker("Sandy", _sandy.NetworkObject);
            Coroutine waving = StartCoroutine(WaveNowAndThen(_sandy));
            yield return WaitTalk(_sandy, "Talk to Sandy");
            StopCoroutine(waving);
            _sandy.ServerSetTalkable(false);
            NoMarker();
            _sandy.ServerSetMood(AvatarMood.Happy);
            yield return Say(_sandy, "Oh! You must be the new lifeguard. I'm Sandy. I run the Lost & Found here.");
            yield return Say(_sandy, "The tourists on this island can't swim to save their lives. Literally. When someone's in trouble, the bell rings and you go and get them.");
            yield return Say(_sandy, "Drag them onto the sand. If they're out cold: push on the chest. The ladies need a bit of air too, mouth-to-mouth. The men... a good smack in the face usually does it.");
            yield return Say(_sandy, "And they lose EVERYTHING. Wallets, phones, sunglasses. You find something, you bring it to me, and I pay you.");
            yield return Say(_sandy, "Go on then. I think somebody's already waving out there.");
            _sandy.ServerSetMood(AvatarMood.Neutral);
            // Something to find right away.
            if (_lostItemSpots.Length > 0)
            {
                Item first = SpawnItem("Sunglasses", _lostItemSpots[0].position + Vector3.up * 0.4f);
                if (first != null && first.TryGetComponent(out LostItem lost)) lost.ServerSetup("Brenda", false);
            }
            _nextAmbientLoss = Time.time + 60f;
        }

        private IEnumerator WaveNowAndThen(StoryNpc npc)
        {
            while (true)
            {
                npc.ServerGesture(AvatarGesture.Wave);
                yield return new WaitForSeconds(4.5f);
            }
        }

        private IEnumerator FirstShift()
        {
            _island = _island1;
            NoMarker();
            // Five tourists, three of them women: F, M, F, M, F.
            yield return RescueWave(_island1, 5, "Rescue tourists", i => Profile(_island1, i % 2 == 0 ? 1 : 0));
            yield return Say(_sandy, "Five in one morning! Not bad, not bad at all.");
        }

        private IEnumerator Thief()
        {
            _island = _island1;
            Vector3 spawn = _island1.RobberSpawn != null ? _island1.RobberSpawn.position : new Vector3(-30f, 0f, 22f);
            StoryNpc robber = SpawnNpc("Robber", NpcRole.Robber, RobberLook, spawn, 90f, 3);
            StoryNpc victim = SpawnNpc("Gloria", NpcRole.Guest, GuestLook(7171, 1), spawn + new Vector3(-3f, 0f, 2f), 90f);
            victim.ServerSetPose(AvatarPose.Scared);
            victim.ServerSetMood(AvatarMood.Scared);
            victim.ServerShout("THIEF!! He's got our stuff!", true);
            robber.ServerFlee(true, _island1.LandArea);
            SetObjective("Catch the thief! Punch him with an empty hand (left mouse).");
            Marker("THIEF", robber.NetworkObject);
            _defeated.Clear();
            float nextShout = Time.time + 3f;
            while (!_defeated.Contains(robber))
            {
                if (Time.time > nextShout && victim != null)
                {
                    nextShout = Time.time + 5f;
                    victim.ServerShout("Stop him!", true);
                }
                yield return null;
            }
            NoMarker();
            yield return new WaitForSeconds(1.8f);
            robber.ServerSetPose(AvatarPose.Kneel);
            robber.ServerSetMood(AvatarMood.Scared);
            Scatter(robber.transform.position, ("Wallet", "Gloria", true), ("Phone", "Hank", true), ("Watch", "Rita", true));
            yield return Say(robber, "OK! OK! Take it! Take it all! It's not even mine!");
            yield return You("Get lost. And stay off my beach.");
            // Let him go: he scrambles up and runs off.
            robber.ServerSetPose(AvatarPose.Normal);
            robber.ServerSetMood(AvatarMood.Scared);
            robber.ServerSetHealth(0, 0);
            robber.ServerFlee(false, _island1.LandArea);
            robber.ServerMoveTo(new Vector3(-70f, 0f, 45f), run: true);
            victim.ServerSetPose(AvatarPose.Normal);
            victim.ServerSetMood(AvatarMood.Happy);
            yield return Say(victim, "My wallet! Could you give it to Sandy at the Lost & Found? I'll pick it up there.");
            StartCoroutine(RemoveLater(robber, 12f));
            StartCoroutine(RemoveLater(victim, 30f));
        }

        private IEnumerator RemoveLater(Component thing, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Remove(thing);
        }

        private IEnumerator ReturnLoot()
        {
            _island = _island1;
            // Anything the thief dropped that's still around (after a resume, drop them again at the kiosk).
            int lying = 0;
            foreach (Item i in Item.All)
                if (i.TryGetComponent(out LostItem l) && l.IsStolen) lying++;
            if (lying == 0 && _lostItemSpots.Length > 0)
                Scatter(_lostItemSpots[0].position, ("Wallet", "Gloria", true), ("Phone", "Hank", true), ("Watch", "Rita", true));

            const int need = 3;
            int returned = 0;
            SetObjective("Take the stolen things to Lost & Found", 0, need);
            Marker("Lost & Found", _lostAndFound.NetworkObject);
            _handedIn.Clear();
            bool firstLine = false;
            while (returned < need)
            {
                foreach (LostAndFound.HandedIn h in _handedIn)
                    if (h.Stolen) returned++;
                _handedIn.Clear();
                _progress.Value = Mathf.Min(returned, need);
                if (returned > 0 && !firstLine && returned < need)
                {
                    firstLine = true;
                    StartCoroutine(Say(_sandy, "Gloria's wallet? Where did you find this?"));
                }
                // The thief's loot can end up in the sea: if it's all gone, count what's left as returned.
                int left = 0;
                foreach (Item i in Item.All)
                    if (i.TryGetComponent(out LostItem l) && l.IsStolen) left++;
                if (left == 0 && returned < need) returned = need;
                yield return new WaitForSeconds(0.2f);
            }
            NoMarker();
            _sandy.ServerSetPose(AvatarPose.Scared);
            _sandy.ServerSetMood(AvatarMood.Scared);
            yield return Say(_sandy, "Stolen?! A ROBBER? On MY island?");
            yield return Say(_sandy, "Oh no, no, no. This was such a quiet place. I don't like this at all...");
            yield return Say(_sandy, "Everyone hold on to your bags! ...Please keep your eyes open out there.");
            _sandy.ServerSetPose(AvatarPose.Normal);
            _sandy.ServerSetMood(AvatarMood.Neutral);
        }

        private IEnumerator ThreeMen()
        {
            yield return RescueWave(_island1, 3, "Rescue tourists", _ => Profile(_island1, 0));
        }

        private IEnumerator SilentOne()
        {
            _island = _island1;
            TouristProfile profile = Profile(_island1, 1, silent: true);
            profile.SecondsToUnconscious = 14f; // she's been going under for a while already
            profile.Name = "Jenny";
            VictimBrain her = null;
            StoryNpc friend = null;
            _rescued.Clear();
            while (true)
            {
                if (her == null || !her.IsSpawned || her.State == VictimState.Lost)
                {
                    if (friend != null) Remove(friend);
                    yield return new WaitForSeconds(3f);
                    her = SpawnStoryTourist(_island1, profile, 2f, 5f);
                    if (her == null)
                    {
                        yield return new WaitForSeconds(1f);
                        continue;
                    }
                    // Her friend stands at the water's edge yelling for help (she herself makes no sound at all).
                    Vector3 edge = BeachPointFrom(her.transform.position, _island1);
                    Vector3 face = her.transform.position - edge;
                    friend = SpawnNpc("Carla", NpcRole.Bystander, GuestLook(4242, 1), edge, Quaternion.LookRotation(new Vector3(face.x, 0f, face.z)).eulerAngles.y);
                    friend.ServerSetPose(AvatarPose.Scared);
                    friend.ServerSetMood(AvatarMood.Scared);
                    friend.ServerKeepShouting("HELP! My friend went under!", her.transform.position);
                    if (RescueService.Instance != null)
                    {
                        RescueService.Instance.ServerRingBell("story");
                        RescueService.Instance.ServerAnnounce("<color=#ffd060><b>Someone is screaming for help at the water's edge!</b></color>");
                    }
                    SetObjective("Someone is shouting for help at the water's edge!");
                    Marker("HELP!", friend.NetworkObject);
                }
                if (_rescued.Contains(her)) break;
                // Once you're close, point at her instead.
                if (friend != null && _markerTarget.Value == friend.NetworkObject && StoryNpc.NearestPlayer(friend.transform.position, 8f) != null)
                {
                    friend.ServerKeepShouting("She's out there! She just went under!", her.transform.position, 3.2f);
                    SetObjective($"Get {her.Name} out of the water! She isn't making a sound.");
                    Marker(her.Name, her.NetworkObject);
                }
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
            _rescued.Clear();
            if (friend != null)
            {
                friend.ServerKeepShouting(null, Vector3.zero);
                friend.ServerSetPose(AvatarPose.Normal);
                friend.ServerSetMood(AvatarMood.Happy);
                friend.ServerShout("JENNY! Oh thank god!", false);
            }
            _silentFriend = friend;
            _silentOne = her;
        }

        private StoryNpc _silentFriend;
        private VictimBrain _silentOne;

        private IEnumerator DrugReveal()
        {
            SetObjective("Listen to what happened");
            string name = _silentOne != null ? _silentOne.Name : "Jenny";
            Color her = new(1f, 0.8f, 0.9f);
            yield return new WaitForSeconds(1.5f);
            yield return Say(name, "Ugh... what happened? One minute I was swimming, and then... nothing.", her);
            yield return Say(name, "A guy on the beach gave me a little pill. 'Vitamins', he said. Then everything went black.", her);
            if (_silentFriend != null)
                yield return Say(_silentFriend, "It was that guy with the backpack! The one who's always running around!");
            yield return You("Pills on this beach... So there are drugs on this island.");
            yield return You("And I bet I know whose backpack they're in.");
            if (_silentFriend != null) StartCoroutine(RemoveLater(_silentFriend, 25f));
            _silentFriend = null;
        }

        private IEnumerator OneMore()
        {
            yield return RescueWave(_island1, 1, "Rescue tourists", _ => Profile(_island1, 0));
        }

        private IEnumerator ThiefAgain()
        {
            _island = _island1;
            Vector3 spawn = _island1.RobberSpawn != null ? _island1.RobberSpawn.position + new Vector3(55f, 0f, 5f) : new Vector3(25f, 0f, 25f);
            spawn.y = Shore.GroundHeightAt(spawn + Vector3.up * 5f) is var g && !float.IsNaN(g) ? g : 0f;
            StoryNpc robber = SpawnNpc("Robber", NpcRole.Robber, RobberLook, spawn, -90f, 5);
            robber.ServerFlee(true, _island1.LandArea);
            robber.ServerShout("Vitamins! Cheap vitamins!", false);
            yield return new WaitForSeconds(1f);
            yield return Say(_sandy, "THAT'S HIM! The one with the backpack! Get him!");
            SetObjective("Catch the robber! Hit him 5 times.");
            Marker("ROBBER", robber.NetworkObject);
            _defeated.Clear();
            while (!_defeated.Contains(robber)) yield return null;
            NoMarker();

            // Down he goes and the bag bursts open.
            yield return new WaitForSeconds(0.6f);
            robber.ServerSetBag(false);
            robber.ServerShout("RRRIP!", false);
            Scatter(robber.transform.position, ("Baggie", "", false), ("Baggie", "", false), ("Baggie", "", false));
            yield return new WaitForSeconds(1.8f);
            robber.ServerSetPose(AvatarPose.Kneel);
            robber.ServerSetMood(AvatarMood.Scared);
            yield return You("Drugs. I knew it. You're going to jail, pal.");
            yield return Say(robber, "No, no, no, please! Not jail! I can't go back to jail!");
            yield return Say(robber, "Look, take my jet ski! It's at the dock! Here are the keys, just let me go! Please!");
            Item keys = SpawnItem("Jet Ski Keys", robber.transform.position + robber.transform.forward * 0.8f + Vector3.up * 1.2f, robber.transform.forward * 1.5f + Vector3.up * 2f);
            SetObjective("Take the jet ski keys");
            if (keys != null) Marker("Keys", keys.NetworkObject);
            while (keys != null && keys.IsSpawned && !keys.IsHeld) yield return null;
            NoMarker();
            robber.ServerSetPose(AvatarPose.Normal);
            robber.ServerSetHealth(0, 0);
            robber.ServerMoveTo(new Vector3(70f, 0f, 45f), run: true);
            robber.ServerShout("Thank you! You'll never see me again!", false);
            StartCoroutine(RemoveLater(robber, 12f));
            yield return new WaitForSeconds(1f);
            yield return Say(_sandy, "You let him GO? ...Well. At least the drugs are off the beach. Hand those bags in, I'll give them to the police.");
        }

        private IEnumerator LeaveIsland()
        {
            _island = _island1;
            // Resuming here: make sure somebody can get the keys.
            bool keysExist = false;
            foreach (Item i in Item.All)
                if (string.Equals(i.DisplayName, "Jet Ski Keys", System.StringComparison.OrdinalIgnoreCase)) keysExist = true;
            if (!keysExist && _lostItemSpots.Length > 0) SpawnItem("Jet Ski Keys", _lostItemSpots[0].position + Vector3.up * 0.5f);

            SetObjective("Take the robber's jet ski at the dock (keys in your pocket)");
            if (_jetSki != null) Marker("Jet ski", _jetSki.NetworkObject);
            while (_jetSki != null && _jetSki.Driver == null) yield return null;
            yield return Say(_sandy, "(on the radio) Heading out? There's a big hotel on the island to the south. They could use a lifeguard. Good luck, kid!");
            SetObjective("Ride south to the hotel island");
            if (_island2.Arrival != null) Marker("Hotel island", _island2.Arrival.position);
            while (true)
            {
                bool arrived = false;
                Vector3 arrival = _island2.Arrival != null ? _island2.Arrival.position : new Vector3(0f, 0f, -200f);
                foreach (PlayerHub p in PlayerHub.All)
                    if (new Vector2(p.transform.position.x - arrival.x, p.transform.position.z - arrival.z).sqrMagnitude < 40f * 40f) arrived = true;
                if (arrived) break;
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
        }

        // ================================================================== chapter 2

        private IEnumerator CheckIn()
        {
            SetChapter("Chapter 2: The hotel island");
            _island = _island2;
            EnsureOnIsland2();
            TitleObservers("CHAPTER 2", "The hotel island");
            yield return new WaitForSeconds(2f);
            SetObjective("Go to the hotel reception");
            Marker("Reception", _receptionist.NetworkObject);
            yield return WaitTalk(_receptionist, "Talk to the receptionist");
            _receptionist.ServerSetTalkable(false);
            NoMarker();
            _receptionist.ServerSetMood(AvatarMood.Happy);
            yield return Say(_receptionist, "Welcome to the Grand Coral Hotel! Oh... you're the lifeguard? Finally!");
            yield return Say(_receptionist, "Our guests keep getting into trouble in the water. You'll be keeping them alive out front.");
            _receptionist.ServerSetMood(AvatarMood.Scared);
            yield return Say(_receptionist, "And, between us... you'll need something to protect yourself with. Guests here sometimes get... attacked.");
            _receptionist.ServerSetMood(AvatarMood.Neutral);
            yield return Say(_receptionist, $"I can sell you a pistol. ${_pistolPrice}. Hotel policy. Don't ask.");
        }

        private IEnumerator BuyWeapon()
        {
            _island = _island2;
            EnsureOnIsland2();
            if (_reception != null) _reception.ServerSetAvailable(true);
            _purchases.Clear();
            float nextHelp = Time.time + 25f;
            Transform desk = _receptionDesk != null ? _receptionDesk : _receptionist.transform;
            while (!_purchases.Contains("Pistol"))
            {
                int money = Economy.Money;
                SetObjective(money >= _pistolPrice
                    ? $"Buy the pistol at reception (${_pistolPrice})"
                    : $"Buy the pistol at reception (${_pistolPrice}, you have ${money}: rescue guests to earn more)");
                Marker("Reception", desk.position + Vector3.up * 1.2f);
                // Short on money: guests keep getting into trouble so you can earn it.
                _waveTourists.RemoveWhere(v => v == null || !v.IsSpawned || !v.State.NeedsHelp());
                if (money < _pistolPrice && _waveTourists.Count == 0 && Time.time > nextHelp)
                {
                    SpawnStoryTourist(_island2, Profile(_island2, -1, flatline: 0f));
                    nextHelp = Time.time + 20f;
                }
                yield return new WaitForSeconds(0.3f);
            }
            NoMarker();
            _receptionist.ServerSetMood(AvatarMood.Happy);
            yield return Say(_receptionist, "Pleasure doing business! Your beach is right out front.");
            yield return Say(_receptionist, "Oh, and the first-aid station by the door has a defibrillator. Out here, you'll need it.");
            _receptionist.ServerSetMood(AvatarMood.Neutral);
            // The defibrillator hangs at the first-aid station (hotel equipment).
            bool haveDefib = false;
            foreach (Item i in Item.All)
                if (i.DisplayName == "Defibrillator") haveDefib = true;
            if (!haveDefib && _firstAid != null) SpawnItem("Defibrillator", _firstAid.position);
        }

        private IEnumerator HarderWork()
        {
            _island = _island2;
            EnsureOnIsland2();
            TitleObservers("SHIFT STARTS", "Guests pass out in 10 seconds here");
            // The second guest flatlines almost at once: that's what the defibrillator is for.
            yield return RescueWave(_island2, 3, "Rescue hotel guests (they pass out in 10 s!)", i => Profile(_island2, i == 1 ? 1 : -1, flatline: i == 1 ? 3f : -1f));
        }

        private IEnumerator SharkAttack()
        {
            _island = _island2;
            EnsureOnIsland2();
            while (true)
            {
                TouristProfile profile = Profile(_island2, 0);
                profile.SecondsToUnconscious = 35f; // the bite is the problem, not the swimming
                profile.Name = "Todd";
                VictimBrain guest = SpawnStoryTourist(_island2, profile, 2f, 6f);
                if (guest == null)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }
                Vector3 at = guest.transform.position;
                Shark shark = SpawnShark(at + new Vector3(15f, 0f, 12f), at, 9f);
                SetObjective($"{guest.Name} is in trouble... and there's a fin out there!");
                Marker(guest.Name, guest.NetworkObject);
                yield return new WaitForSeconds(4f);
                if (guest != null && guest.IsSpawned) shark.ServerAttack(guest);
                while (guest != null && guest.IsSpawned && !guest.HasLostLeg && guest.State != VictimState.Lost) yield return null;
                if (guest == null || !guest.IsSpawned || guest.State == VictimState.Lost) continue;

                SetObjective($"Get {guest.Name} out of the water and rush him to the hotel infirmary!");
                bool pointingAtBed = false;
                _rescued.Clear();
                while (guest != null && guest.IsSpawned && guest.State != VictimState.Lost && !_rescued.Contains(guest))
                {
                    bool carried = guest.Item.IsHeld || guest.State == VictimState.Injured;
                    if (carried && !pointingAtBed && _infirmary != null)
                    {
                        pointingAtBed = true;
                        Marker("Infirmary", _infirmary.position + Vector3.up);
                    }
                    else if (!carried && pointingAtBed)
                    {
                        pointingAtBed = false;
                        Marker(guest.Name, guest.NetworkObject);
                    }
                    yield return new WaitForSeconds(0.2f);
                }
                NoMarker();
                if (guest != null && guest.IsSpawned && _rescued.Contains(guest)) break;
                SetObjective("He didn't make it in time. The rival company's helicopter is circling...");
                yield return new WaitForSeconds(4f);
            }
            yield return Say(_receptionist, "(over the speaker) A SHARK?! ...The pool is open, everybody! The pool is very nice!");
        }

        private IEnumerator TwoMore()
        {
            _island = _island2;
            EnsureOnIsland2();
            yield return RescueWave(_island2, 2, "Rescue hotel guests", _ => Profile(_island2, -1));
        }

        private IEnumerator Pirates()
        {
            _island = _island2;
            EnsureOnIsland2();
            yield return PirateRaid(_receptionist);
        }

        /// <summary>The pirate boat comes in, four pirates land and go for the lifeguards; done when they're all down.</summary>
        private IEnumerator PirateRaid(StoryNpc receptionist)
        {
            if (_pirateBoat == null || _pirateBoatStart == null || _pirateLanding == null) yield break;
            _pirateBoat.ServerSetLocked(true, "Pirate boat (take down the pirates first)");
            _pirateBoat.ServerPlace(_pirateBoatStart.position, _pirateBoatStart.eulerAngles.y);
            var pirates = new List<StoryNpc>();
            string[] names = { "Pirate Pete", "One-Eyed Olga", "Big Sal", "Salty Bill" };
            for (int i = 0; i < 4; i++)
            {
                StoryNpc pirate = SpawnNpc(names[i], NpcRole.Pirate, PirateLook(i), _pirateBoatStart.position, 0f, 3);
                pirate.ServerRide(_pirateBoat, new Vector3(i % 2 == 0 ? -0.7f : 0.7f, 0.86f, 1.8f - i / 2 * 1.4f)); // on the fore deck
                pirates.Add(pirate);
            }
            SetObjective("A boat is coming in fast...");
            Marker("?", _pirateBoat.NetworkObject);
            yield return new WaitForSeconds(1f);
            if (receptionist != null) StartCoroutine(Say(receptionist, "(over the speaker) Is that... a PIRATE FLAG?!"));
            // The landing is on the beach; the boat stops in the water just off it.
            Vector3 boatStop = _pirateLanding.position + _pirateLanding.forward * 12f;
            _pirateBoat.ServerAutopilot(boatStop, 9f);
            float giveUp = Time.time + 60f;
            while ((new Vector2(_pirateBoat.transform.position.x - boatStop.x, _pirateBoat.transform.position.z - boatStop.z)).sqrMagnitude > 5f * 5f
                   && Time.time < giveUp)
                yield return null;
            _pirateBoat.ServerAutopilot(null);

            // Everybody off! They charge up the beach toward the hotel.
            Vector3 hotel = _hotelDoor != null ? _hotelDoor.position : _pirateLanding.position + Vector3.forward * -20f;
            for (int i = 0; i < pirates.Count; i++)
            {
                StoryNpc pirate = pirates[i];
                Vector3 off = _pirateLanding.position + _pirateLanding.right * ((i - 1.5f) * 1.6f) - _pirateLanding.forward * 2f;
                float ground = Shore.GroundHeightAt(off + Vector3.up * 5f);
                if (!float.IsNaN(ground)) off.y = ground;
                pirate.ServerTeleport(off, _pirateLanding.eulerAngles.y + 180f); // jumped off onto the beach
                pirate.ServerAggressive(true, hotel);
                pirate.ServerShout(i == 0 ? "ARRR! Grab the loot, lads!" : "YARR!", false);
            }
            _defeated.Clear();
            int down = 0;
            Marker("PIRATES", pirates[0].NetworkObject);
            while (down < pirates.Count)
            {
                down = 0;
                StoryNpc next = null;
                foreach (StoryNpc p in pirates)
                {
                    if (p == null || p.IsDefeated) down++;
                    else if (next == null) next = p;
                }
                SetObjective("PIRATES! Take them down (pistol or fists)", down, pirates.Count);
                if (next != null && _markerTarget.Value != next.NetworkObject) Marker("PIRATE", next.NetworkObject);
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
            foreach (StoryNpc p in pirates) StartCoroutine(RemoveLater(p, 20f));
            if (receptionist != null)
            {
                receptionist.ServerSetMood(AvatarMood.Happy);
                yield return Say(receptionist, "(over the speaker) They're... they're down! You did it! Drinks are on the house!");
            }
        }

        private IEnumerator TheBoat()
        {
            _island = _island2;
            EnsureOnIsland2();
            if (_pirateBoat == null) yield break;
            _pirateBoat.ServerSetLocked(false);
            SetObjective("The pirate boat is yours now. Get on board!");
            Marker("Your boat", _pirateBoat.NetworkObject);
            while (_pirateBoat.Driver == null) yield return null;
            NoMarker();
            yield return Say(_receptionist, "(on the radio) Leaving already? Come back soon! ...Seriously. Please.");
            yield return Say(_sandy, "(on the radio) A jet ski, and now a pirate ship? Who ARE you?");
            SetChapter("End of chapter 2");
            SetObjective("To be continued...");
            TitleObservers("TO BE CONTINUED", "Thanks for playing chapters 1 and 2");
        }
    }
}
