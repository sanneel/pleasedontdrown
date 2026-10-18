using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The end of chapter 3 (Docs/Prompt-Shako-Chapter-4-Finale.md):
    ///  3.9 four money sacks from the gold vault to the pirate boat at the night dock (throw or drop them on its deck;
    ///      they fit a pocket slot, so one lifeguard can take them all); the THIEF minds the boat and rides along ·
    ///  3.10 sail the gold back to the hotel island; the THIEF slips away with one sack on the way; Marisol counts
    ///      three, the team keeps a finder's fee; where the fourth sack was, a Lost &amp; Found tag in Sandy's
    ///      handwriting ("Finders Keepers. Deliver to the island.") and a sea chart → chapter 3's report.
    /// The sacks on the deck are the boat's cargo: a sack that lands there is stowed (the item goes, a copy of its
    /// look sits on the deck on every machine, see <see cref="CargoObservers"/>).
    /// </summary>
    public partial class StoryDirector
    {
        private const int GoldSacks = 4;
        private const int FindersFee = 750;
        private readonly List<Item> _sacks = new();
        private readonly SyncVar<int> _cargo = new SyncVar<int>();
        private Transform _cargoRoot; // (each machine's own copies)

        /// <summary>Host (tests): sacks stowed on the pirate boat.</summary>
        public int CargoSacks => _cargo.Value;

        /// <summary>Where the pirate boat waits off the hotel beach: 32 m out (13 m out it sat on the sand).</summary>
        private Vector3 HotelBerth => _pirateLanding != null ? _pirateLanding.position + _pirateLanding.forward * 32f + Vector3.down * _pirateLanding.position.y : Vector3.zero;

        /// <summary>Host: the pirate boat, nobody driving it, tied up at <see cref="HotelBerth"/>.</summary>
        private void MooredAtHotel()
        {
            if (_pirateBoat == null || _pirateLanding == null || _pirateBoat.Driver != null) return;
            _pirateBoat.ServerKickAll();
            _pirateBoat.ServerPlace(HotelBerth, _pirateLanding.eulerAngles.y);
            Vehicles.Mooring.Tie(_pirateBoat, HotelBerth, _pirateLanding.eulerAngles.y);
        }

        // Where the boat ties up: along the west side of the night dock's head, bow out to sea.
        private Vector3 NightBerth => NightDock + new Vector3(-4.6f, 0f, 0f);

        private void BerthPirateBoat()
        {
            if (_pirateBoat == null) return;
            _pirateBoat.ServerAutopilot(null);
            _pirateBoat.ServerKickAll();
            _pirateBoat.ServerPlace(NightBerth, 0f);
            Vehicles.Mooring.Tie(_pirateBoat, NightBerth, 0f); // (it drifted off while the sacks were coming)
        }

        // ------------------------------------------------------------------ 3.9 load the gold

        private IEnumerator LoadTheGold()
        {
            EnsureOnIsland3();
            Transform ledge = Island3Point("CaveLedge");
            if (ledge == null || _pirateBoat == null) yield break;
            BerthPirateBoat();
            _pirateBoat.ServerSetLocked(true, "Load the gold first");
            SetCargo(0);
            ClearSacks();
            // The sacks on the vault floor in front of the gold, a step apart.
            for (int i = 0; i < GoldSacks; i++)
            {
                Vector3 at = ledge.position + ledge.right * ((i - 1.5f) * 0.9f) + ledge.forward * (1.6f + (i % 2) * 0.5f) + Vector3.up * 0.45f;
                Item sack = SpawnItem("MoneySack", at);
                if (sack != null) _sacks.Add(sack);
            }
            // The THIEF turns up at the boat to "help" (his own sack in mind).
            StoryNpc thief = ThiefAtTheBoat();
            StartCoroutine(ThiefSays("Gold! Sacks of it! Bring them down to the boat, I'll mind it. Throw them on the deck, I'll stack them.")); // (while the marker's already up)
            SetObjective("Carry the money sacks to the pirate boat at the night dock (throw them on deck)", 0, GoldSacks);
            int seen = 0;
            float nextMarker = 0f;
            while (_cargo.Value < GoldSacks)
            {
                StowSacksOnDeck();
                if (_cargo.Value != seen)
                {
                    seen = _cargo.Value;
                    SetObjective("Carry the money sacks to the pirate boat at the night dock (throw them on deck)", seen, GoldSacks);
                    if (thief != null && seen < GoldSacks)
                        thief.ServerShout(seen switch { 1 => "One! Lovely.", 2 => "Two! Ooh, heavy.", _ => "Three! One more!" });
                }
                if (Time.time > nextMarker)
                {
                    nextMarker = Time.time + 0.5f;
                    MarkNextSack();
                }
                RespawnLostSacks(ledge);
                yield return new WaitForSeconds(0.2f);
            }
            NoMarker();
            ClearSacks();
            SetObjective("All four sacks on board", GoldSacks, GoldSacks);
            yield return ThiefSays("Four sacks. I'll sit in the back and guard them. With my LIFE. Nobody look at the sacks.");
            yield return You("...Why would we look at the sacks?");
        }

        /// <summary>The THIEF, standing on the night dock by the boat (Skull Cove's own THIEF when he's there).</summary>
        private StoryNpc ThiefAtTheBoat()
        {
            Vector3 at = NightDock + new Vector3(-1.6f, 0f, -1.5f);
            if (_thief == null || !_thief.IsSpawned) _thief = SpawnNpc("THIEF", NpcRole.Guide, RobberLook, at, 270f);
            else _thief.ServerTeleport(at, 270f);
            _thief.ServerSetTalkable(false);
            _thief.ServerSetPose(AvatarPose.Normal);
            return _thief;
        }

        /// <summary>A sack lying on the deck (not in anyone's hands) is stowed.</summary>
        private void StowSacksOnDeck()
        {
            for (int i = _sacks.Count - 1; i >= 0; i--)
            {
                Item sack = _sacks[i];
                if (sack == null || sack.IsHeld) continue;
                Vector3 local = _pirateBoat.transform.InverseTransformPoint(sack.transform.position);
                if (Mathf.Abs(local.x) > 1.6f || Mathf.Abs(local.z) > 3.9f || local.y < 0.2f || local.y > 3f) continue;
                _sacks.RemoveAt(i);
                Despawn(sack.gameObject);
                SetCargo(_cargo.Value + 1);
                Debug.Log($"[Story] a money sack stowed on the pirate boat ({_cargo.Value}/{GoldSacks})");
            }
        }

        /// <summary>The marker: the boat while someone carries a sack, else the nearest sack still lying about.</summary>
        private void MarkNextSack()
        {
            bool carried = false;
            Item loose = null;
            float best = float.MaxValue;
            foreach (Item sack in _sacks)
            {
                if (sack == null) continue;
                if (sack.IsHeld) { carried = true; continue; }
                float d = (sack.transform.position - _pirateBoat.transform.position).sqrMagnitude;
                if (d < best) { best = d; loose = sack; }
            }
            if (carried || loose == null) { if (_markerTarget.Value != _pirateBoat.NetworkObject) Marker("Boat", _pirateBoat.NetworkObject); }
            else if (_markerTarget.Value != loose.NetworkObject) Marker("Money sack", loose.NetworkObject);
        }

        /// <summary>A sack that sank, drifted off or was destroyed turns up again on the vault floor.</summary>
        private void RespawnLostSacks(Transform ledge)
        {
            for (int i = _sacks.Count - 1; i >= 0; i--)
            {
                Item sack = _sacks[i];
                bool gone = sack == null || !sack.IsSpawned;
                bool sunk = !gone && !sack.IsHeld && sack.transform.position.y < -6f;
                if (!gone && !sunk) continue;
                if (sunk) Despawn(sack.gameObject);
                _sacks.RemoveAt(i);
                Item again = SpawnItem("MoneySack", ledge.position + ledge.forward * 1.8f + Vector3.up * 0.45f);
                if (again != null) _sacks.Add(again);
            }
        }

        private void ClearSacks()
        {
            foreach (Item sack in _sacks)
                if (sack != null && sack.IsSpawned) Despawn(sack.gameObject);
            _sacks.Clear();
        }

        // ------------------------------------------------------------------ 3.10 back to the hotel

        private IEnumerator BackToTheHotel()
        {
            if (_pirateBoat == null || _pirateLanding == null) yield break;
            // Resumed here (or jumped to): the boat at the night dock with its four sacks, the THIEF by it.
            if (_cargo.Value < GoldSacks)
            {
                EnsureOnIsland3(NightDock);
                BerthPirateBoat();
                SetCargo(GoldSacks);
                ThiefAtTheBoat();
            }
            StoryNpc thief = _thief != null && _thief.IsSpawned ? _thief : null;
            if (thief != null) thief.ServerRide(_pirateBoat, new Vector3(0.65f, 0.86f, 1.4f)); // on the foredeck beside the sacks
            _pirateBoat.ServerSetLocked(false);
            Vector3 stop = _pirateLanding.position + _pirateLanding.forward * 13f;
            SetObjective("Sail the gold back to the hotel island (round its west end to the beach)");
            Marker("Hotel beach", stop + Vector3.up * 2f);
            while (_pirateBoat.Driver == null && !TeamOnIsland2()) yield return new WaitForSeconds(0.3f);
            float onIsland2Since = -1f;
            while (true)
            {
                float toStop = Vector3.Distance(Flat(_pirateBoat.transform.position), Flat(stop));
                // Halfway home, nobody watching the back of the boat: the THIEF and one sack go over the side.
                if (thief != null && toStop < 260f)
                {
                    Remove(thief);
                    if (_thief == thief) _thief = null;
                    thief = null;
                    SetCargo(GoldSacks - 1);
                    Debug.Log("[Story] the THIEF slipped off the pirate boat with a sack");
                }
                if (toStop < 40f) break;
                // The team went over by travel pad: the boat comes too (the hotel's boys bring it round).
                if (_pirateBoat.Driver == null && TeamOnIsland2())
                {
                    if (onIsland2Since < 0f) onIsland2Since = Time.time;
                    if (Time.time - onIsland2Since > 4f)
                    {
                        _pirateBoat.ServerPlace(stop, _pirateLanding.eulerAngles.y);
                        yield return null;
                        continue;
                    }
                }
                else onIsland2Since = -1f;
                yield return new WaitForSeconds(0.3f);
            }
            if (thief != null) { Remove(thief); if (_thief == thief) _thief = null; SetCargo(GoldSacks - 1); }
            NoMarker();
            _pirateBoat.ServerSetLocked(true, "The gold's boat");
            Vehicles.Mooring.Tie(_pirateBoat, HotelBerth, _pirateLanding.eulerAngles.y);
            // Marisol comes down to the beach to count it.
            StoryNpc marisol = _receptionist;
            Vector3 home = marisol != null ? marisol.transform.position : stop;
            float homeYaw = marisol != null ? marisol.transform.eulerAngles.y : 0f;
            Vector3 meet = _pirateLanding.position - _pirateLanding.forward * 1f; // at the water's edge
            SetObjective("Marisol is coming down to count the money");
            if (marisol != null)
            {
                marisol.ServerSetTalkable(false);
                marisol.ServerMoveTo(StoryNpc.OnNavMesh(meet), run: true);
                float t0 = Time.time;
                while (marisol.IsMoving && Time.time - t0 < 25f) yield return new WaitForSeconds(0.3f);
                if ((marisol.transform.position - meet).sqrMagnitude > 6f * 6f) marisol.ServerTeleport(meet, _pirateLanding.eulerAngles.y);
                marisol.ServerFace(null); // (whoever comes over)
                Marker("Marisol", marisol.NetworkObject);
                yield return Reach(marisol.transform.position, 12f);
                SetObjective("Marisol counts the casino's money");
                NoMarker();
                marisol.ServerSetMood(AvatarMood.Happy);
                yield return Say(marisol, "You're back! With the casino's money! Let me count it. One... two... three.");
                marisol.ServerSetMood(AvatarMood.Neutral);
                yield return Say(marisol, "Three? The casino says FOUR sacks went missing.");
            }
            yield return You("There were four. The THIEF was guarding them in the back... Where's the THIEF?");
            if (marisol != null)
            {
                yield return Say(marisol, "Well. Three is three more than the police got back. The hotel says thank you: a finder's fee for the team.");
                if (Economy.Instance != null) Economy.Instance.ServerAdd(FindersFee, "Finder's fee (the casino's gold)", marisol.HeadPosition);
            }
            // Where the fourth sack was: Sandy's tag.
            Vector3 deck = _pirateBoat.transform.TransformPoint(new Vector3(0.3f, 1.2f, 1.6f));
            Item tag = SpawnItem("LostTag", deck);
            if (tag != null)
            {
                SetObjective("Something's on the deck where the fourth sack was");
                Marker("On the deck", tag.NetworkObject);
                float t0 = Time.time;
                while (tag != null && !tag.IsHeld && StoryNpc.NearestPlayer(tag.transform.position, 1.8f) == null && Time.time - t0 < 90f)
                    yield return new WaitForSeconds(0.2f);
                NoMarker();
            }
            TitleObservers("FINDERS KEEPERS.", "Deliver to the island.   (a Lost & Found tag, Sandy's handwriting)");
            yield return new WaitForSeconds(2.5f);
            yield return You("That's Sandy's writing. From the Lost & Found... on the island where it all started.");
            if (marisol != null)
            {
                yield return Say(marisol, "And this was tied to it. A sea chart. There's an island on it I've never seen... circled. In orange.");
                yield return Say(marisol, "Nobody sails out there. Everybody says it's private.");
            }
            yield return You("Finders Keepers. Whoever's island that is... they've got our THIEF. And a sack.");
            SetChapter("End of chapter 3");
            SetObjective("Chapter 3 complete");
            yield return ChapterReport("CHAPTER 3 COMPLETE");
            SetCargo(0); // the hotel has the gold now
            MooredAtHotel(); // (out in deep enough water for the trip in 4.1, not on the sand)
            if (marisol != null) { marisol.ServerMoveTo(home, run: false); StartCoroutine(TurnLater(marisol, homeYaw, 20f)); }
            if (tag != null && tag.IsSpawned && !tag.IsHeld) Despawn(tag.gameObject);
        }

        private static IEnumerator TurnLater(StoryNpc npc, float yaw, float within)
        {
            float t0 = Time.time;
            while (npc != null && npc.IsMoving && Time.time - t0 < within) yield return new WaitForSeconds(0.5f);
            if (npc != null) npc.ServerTeleport(npc.transform.position, yaw, keepExact: true);
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        /// <summary>A lifeguard is ashore on the hotel island, on foot.</summary>
        private static bool TeamOnIsland2()
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && Economy.OnHotelIsland(p.transform.position) && Vehicle.SeatOf(p) == null && Shore.IsAshore(p.transform.position)) return true;
            return false;
        }

        // ------------------------------------------------------------------ the boat's cargo (every machine)

        private void SetCargo(int sacks)
        {
            _cargo.Value = Mathf.Clamp(sacks, 0, GoldSacks);
            CargoObservers(_cargo.Value);
        }

        [ObserversRpc(BufferLast = true)]
        private void CargoObservers(int sacks)
        {
            if (_pirateBoat == null) return;
            if (_cargoRoot == null)
            {
                _cargoRoot = new GameObject("GoldCargo").transform;
                _cargoRoot.SetParent(_pirateBoat.transform, false);
                Item prefab = GameContent.Items != null ? GameContent.Items.Find("MoneySack") : null;
                Transform look = prefab != null ? prefab.transform.Find("Look") : null;
                // Stacked on the foredeck, port side (the THIEF sits starboard): three on the deck, one on top.
                Vector3[] spots = { new(-0.5f, 1.08f, 1.5f), new(-0.5f, 1.08f, 2.25f), new(0.1f, 1.08f, 2.6f), new(-0.45f, 1.5f, 1.9f) };
                for (int i = 0; i < GoldSacks; i++)
                {
                    GameObject copy = look != null ? Instantiate(look.gameObject, _cargoRoot) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    copy.name = "Sack" + i;
                    copy.transform.SetParent(_cargoRoot, false);
                    copy.transform.localPosition = spots[i];
                    copy.transform.localRotation = Quaternion.Euler(0f, 35f * i, i == 3 ? 12f : 0f);
                    foreach (Collider c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
                }
            }
            for (int i = 0; i < _cargoRoot.childCount; i++) _cargoRoot.GetChild(i).gameObject.SetActive(i < sacks);
        }
    }
}
