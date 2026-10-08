using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    public partial class StoryDirector
    {
        [Header("Island one rental fleet")]
        [SerializeField] private StoryNpc _rentalOperator;
        [SerializeField] private Vehicle[] _rentalFleet = System.Array.Empty<Vehicle>();
        [SerializeField] private Transform[] _rentalBerths = System.Array.Empty<Transform>();
        [SerializeField] private Transform _rentalKeySpot;
        private readonly SyncVar<bool> _rentalUnlocked = new(false);
        private StoryNpc _keyThief;
        private Vector3 _rentalHome;
        private Item _keyCase;
        private int _surgeCrew = 1;
        private readonly List<VictimBrain> _surgeVictims = new();
        public bool RentalUnlocked => _rentalUnlocked.Value;
        public IReadOnlyList<Vehicle> RentalFleet => _rentalFleet;

        public static AvatarLook RentalLook
        {
            get
            {
                AvatarLook look = AvatarLook.RandomTourist(715, 0);
                look.Body = AvatarLook.Bodies.Variant(AvatarLook.Bodies.TouristBuddy, 4);
                return look;
            }
        }

        private void SetRentalAccess(bool unlocked)
        {
            _rentalUnlocked.Value = unlocked;
            foreach (Vehicle ski in _rentalFleet)
                if (ski != null) ski.ServerSetLocked(!unlocked, "Milo's fleet - recover the stolen key case");
        }

        private void ResetTsunamiActors()
        {
            if (TsunamiState.Instance != null) TsunamiState.Instance.ServerStop();
            foreach (VictimBrain victim in _surgeVictims)
                if (victim != null && victim.IsSpawned) Remove(victim);
            _surgeVictims.Clear();
            if (_rentalOperator != null)
            {
                _rentalOperator.ServerTeleport(_rentalHome, 180f, keepExact: true);
                _rentalOperator.ServerSetTalkable(false);
                _rentalOperator.ServerSetMood(AvatarMood.Neutral);
            }
        }

        private IEnumerator RentalTheft()
        {
            _island = _island1;
            StopHints();
            SetRentalAccess(false);
            SetObjective("Meet Milo at the jet ski rental dock when you're ready");
            Marker("Milo's rentals", _rentalOperator.NetworkObject);
            yield return WaitTalk(_rentalOperator, "Talk to Milo about the jet skis");
            _rentalOperator.ServerSetTalkable(false);
            yield return Say(_rentalOperator, "Milo. Four jet skis, four helmets, and absolutely no refunds for showing off.");
            yield return Say(_rentalOperator, "Sandy says the hotel might need your crew. I'll get the fleet keys...");
            _keyThief = SpawnNpc("Robber", NpcRole.Robber, RobberLook, _rentalHome + Vector3.left * 4f, 180f);
            _keyThief.ServerSetBag(true);
            _keyThief.ServerShout("Lovely day for borrowing things!", false);
            _keyThief.ServerMoveTo(new Vector3(45f, 0f, 2f), run: true);
            _rentalOperator.ServerSetMood(AvatarMood.Scared);
            yield return Say(_rentalOperator, "HEY! My key case! That backpack thief took ALL FOUR KEYS!");
            SetObjective("The thief has the fleet keys! Follow him toward the shore");
            Marker("Stolen keys", _keyThief.NetworkObject);
            float until = Time.time + 8f;
            while (Time.time < until && StoryNpc.NearestPlayer(_keyThief.transform.position, 6f) == null) yield return null;
            yield return Say(_sandy, "Wait. Why is the sea pulling back? Everybody, away from the water!");
        }

        private IEnumerator IncomingTsunami()
        {
            _island = _island1;
            _surgeCrew = Mathf.Clamp(PlayerHub.All.Count, 1, 4);
            SetRentalAccess(false);
            TitleObservers("TSUNAMI!", "Get inland. A rescue crew is about to be needed.");
            SetObjective("TSUNAMI! Get behind the station, away from the shoreline");
            Marker("Higher ground", new Vector3(0f, 1f, 32f));
            RescueService.Instance.ServerRingBell("tsunami");
            TsunamiState.Instance.ServerBegin();
            if (_keyThief == null)
            {
                _keyThief = SpawnNpc("Robber", NpcRole.Robber, RobberLook, new Vector3(45f, 0f, 2f), 180f);
                _keyThief.ServerSetBag(true);
            }
            _keyThief.ServerShout("I can outrun a little WATER!", false);
            _keyThief.ServerMoveTo(new Vector3(43f, 0f, -10f), 3f, water: true);
            _rentalOperator.ServerMoveTo(new Vector3(32f, 0.35f, -8f), 2.5f, water: true);
            yield return Say(_sandy, "UP THE BEACH! Leave the dock! Move, move, move!");
            while (TsunamiState.Age < 27f) yield return null;
            NoMarker();
            if (_keyThief != null) { Remove(_keyThief); _keyThief = null; }
            // The standing actor is replaced by his physics rescue body during the crisis.
            _rentalOperator.ServerTeleport(new Vector3(37f, -60f, 10f), 180f, keepExact: true);
            yield return Say(_sandy, "Milo's been swept off the dock! People are in the water! Rings first - then bring them to dry sand!");
        }

        private VictimBrain SpawnSurgeVictim(int index, float seconds)
        {
            string name = index == 0 ? "Milo" : index == 1 ? "Robber" : "Stranded " + new[] { "Brenda", "Hank", "Rita", "Duncan" }[(index - 2) % 4];
            AvatarLook look = index == 0 ? RentalLook : index == 1 ? RobberLook : AvatarLook.RandomTourist(740 + index, index % 2);
            var profile = new TouristProfile
            {
                Name = name, Look = look.Pack(), Figure = look.Feminine ? 1 : 0,
                SecondsToUnconscious = seconds, ConditionSeconds = 90f, NeedsCpr = false,
                Shouts = index == 0 ? "MY SKIS!|A RING! THROW ME A RING!" : index == 1 ? "I CAN'T SWIM!|YOU CAN HAVE THE KEYS!|RESCUE ME FIRST!" : "OVER HERE!|I'M OVER HERE!"
            };
            // Spread the group along the reachable beach; additional players create additional simultaneous work.
            float x = index == 0 ? 27f : index == 1 ? 43f : -20f + (index - 2) * 13f;
            Vector3 p = new Vector3(x, 0f, -22f - (index % 2) * 6f);
            p.y = WaterSurface.HeightAt(p) - 0.4f;
            VictimBrain victim = RescueService.Instance.SpawnVictim(p, 0f, VictimState.Distressed, 30f, 1f, profile);
            if (victim != null) { _waveTourists.Add(victim); _surgeVictims.Add(victim); }
            return victim;
        }

        private IEnumerator TsunamiRescue()
        {
            _island = _island1;
            // A save resumes at a calm rescue phase. It never replays a lethal wave over a loaded player.
            _rentalOperator.ServerTeleport(new Vector3(37f, -60f, 10f), 180f, keepExact: true);
            int crew = Mathf.Clamp(PlayerHub.All.Count, 1, 4);
            if (TsunamiState.Active) crew = _surgeCrew;
            int count = crew + 2;
            float seconds = Mathf.Lerp(155f, 105f, (crew - 1f) / 3f);
            var victims = new VictimBrain[count];
            var finished = new bool[count];
            var retryAt = new float[count];
            for (int i = 0; i < count; i++) victims[i] = SpawnSurgeVictim(i, seconds);
            _rescued.Clear();
            Debug.Log($"[Tsunami] rescue crew={crew} victims={count} air={seconds:F0}; field and render share server clock");
            while (true)
            {
                int done = 0;
                VictimBrain marker = null;
                for (int i = 0; i < count; i++)
                {
                    if (finished[i]) { done++; continue; }
                    VictimBrain v = victims[i];
                    if (v != null && (_rescued.Contains(v) || v.State == VictimState.Saved))
                    {
                        finished[i] = true;
                        done++;
                        if (i == 0)
                        {
                            _rentalOperator.ServerTeleport(_rentalHome, 180f, keepExact: true);
                            _rentalOperator.ServerSetMood(AvatarMood.Happy);
                            _rentalOperator.ServerShout("I'm alive! Where's that little key thief?", false);
                        }
                        if (i < 2) Remove(v);
                        continue;
                    }
                    if (v == null || !v.IsSpawned || v.State == VictimState.Lost)
                    {
                        if (retryAt[i] == 0f)
                        {
                            retryAt[i] = Time.time + 7f;
                            RescueService.Instance.ServerAnnounce("A rescue was missed. Sandy has spotted another chance to reach them - keep going!");
                        }
                        if (Time.time >= retryAt[i])
                        {
                            victims[i] = SpawnSurgeVictim(i, seconds + 25f);
                            retryAt[i] = 0f;
                        }
                    }
                    else if (marker == null || v.State == VictimState.Unconscious) marker = v;
                }
                SetObjective("Rescue everyone swept off the beach. Split up, use rings, bring them ashore!", done, count);
                if (marker != null) Marker(marker.Name, marker.NetworkObject);
                if (done == count) break;
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
            // Let the visual recession finish naturally; keys never spawn underwater.
            while (TsunamiState.Active) yield return null;
            if (TsunamiState.Instance != null) TsunamiState.Instance.ServerStop();
            yield return Say("Robber", "You saved ME? ...Fine. The keys are in the orange case. I'm retiring from borrowing.", new Color(1f, 0.85f, 0.5f));
            _keyCase = SpawnItem("Fleet Key Case", _rentalKeySpot.position);
            yield return Say(_rentalOperator, "That's my case! Bring it over. We've got a fleet to get back on the water.");
        }

        private IEnumerator RecoverFleetKeys()
        {
            _island = _island1;
            _rentalOperator.ServerTeleport(_rentalHome, 180f, keepExact: true);
            SetRentalAccess(false);
            _talks.Clear();
            _rentalOperator.ServerSetTalkable(true, "Return the fleet key case to Milo");
            while (true)
            {
                if (_keyCase == null || !_keyCase.IsSpawned)
                {
                    foreach (Item item in Item.All)
                        if (item.DisplayName == "Fleet Key Case") { _keyCase = item; break; }
                    if (_keyCase == null || !_keyCase.IsSpawned) _keyCase = SpawnItem("Fleet Key Case", _rentalKeySpot.position);
                }
                if (_keyCase != null && !_keyCase.IsHeld &&
                    (_keyCase.transform.position.y < -3f || Vector3.Distance(_keyCase.transform.position, _rentalKeySpot.position) > 65f))
                {
                    Remove(_keyCase);
                    _keyCase = SpawnItem("Fleet Key Case", _rentalKeySpot.position);
                    RescueService.Instance.ServerAnnounce("The orange key case washed back beside Milo's dock.");
                }
                bool handed = false;
                foreach (var talk in _talks)
                    if (talk.npc == _rentalOperator && _keyCase != null && _keyCase.Holder == talk.by) handed = true;
                _talks.Clear();
                if (handed) break;
                bool held = _keyCase != null && _keyCase.IsHeld;
                SetObjective(held ? "Take the orange key case to Milo and talk to him" : "Recover the orange case of jet ski keys");
                if (held) Marker("Milo", _rentalOperator.NetworkObject);
                else if (_keyCase != null) Marker("Fleet keys", _keyCase.NetworkObject);
                yield return new WaitForSeconds(0.2f);
            }
            Remove(_keyCase);
            _keyCase = null;
            _rentalOperator.ServerSetTalkable(false);
            NoMarker();
            yield return Say(_rentalOperator, "Four keys. Four skis. You recovered them, and you brought my customers home. That's a proper rescue crew.");
            yield return Say(_sandy, "(on the radio) The Grand Coral Hotel just called. They need lifeguards - the WHOLE crew. South across the channel!");
            yield return Say(_rentalOperator, "Take the fleet. One each, or share a seat. You've earned the ride. Bring them back in one piece!");
            for (int i = 0; i < _rentalFleet.Length; i++)
                if (_rentalFleet[i] != null && i < _rentalBerths.Length)
                    _rentalFleet[i].ServerPlace(_rentalBerths[i].position, 180f);
            SetRentalAccess(true);
            Save();
            TitleObservers("FLEET UNLOCKED", "Four jet skis. Every lifeguard can drive.");
        }

        private IEnumerator FleetDeparture()
        {
            _island = _island1;
            SetRentalAccess(true); // also restores access for a saved departure and late joiners
            _rentalOperator.ServerSetTalkable(true, "Recover an unattended jet ski");
            var arrived = new HashSet<PlayerHub>();
            float nextRecovery = 0f;
            bool underway = false;
            while (true)
            {
                int total = 0, done = 0;
                foreach (PlayerHub p in PlayerHub.All)
                {
                    if (p == null || !p.IsSpawned) continue;
                    total++;
                    Vector3 delta = p.transform.position - Island2Arrival;
                    if (new Vector2(delta.x, delta.z).sqrMagnitude < 40f * 40f) arrived.Add(p);
                    if (arrived.Contains(p)) done++;
                    if (Vehicle.RideOf(p) != null) underway = true;
                }
                if (total > 0 && done == total) break;
                SetObjective(underway ? "Ride south to the hotel together. Stranded? Esc > Travel." : "Take a jet ski from Milo's dock - one for each lifeguard", done, total);
                if (underway) Marker("Hotel island", Island2Arrival);
                else Marker("Rental dock", _rentalFleet[0].NetworkObject);
                if (Time.time >= nextRecovery && _talks.Exists(t => t.npc == _rentalOperator))
                {
                    for (int i = 0; i < _rentalFleet.Length; i++)
                    {
                        Vehicle ski = _rentalFleet[i];
                        if (ski != null && ski.Aboard().Count == 0)
                        {
                            ski.ServerPlace(_rentalBerths[i].position, 180f);
                            RescueService.Instance.ServerAnnounce("Milo has brought an unattended ski back to the rental dock.");
                            break;
                        }
                    }
                    nextRecovery = Time.time + 5f;
                }
                _talks.Clear();
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
            _rentalOperator.ServerSetTalkable(false);
            SetObjective("The whole crew made it to the hotel island");
            yield return ChapterReport("CHAPTER 1 COMPLETE");
        }
    }
}
