using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The open shift: the game's loop on every island, and it never runs out (host). Tourists keep getting into trouble
    /// in the sea wherever the lifeguards are, each with a silly reason to be out there (StoryDirector.Gags); you fish
    /// them out, bring them round, and then throw, cannon or otherwise launch them (see <see cref="Fun.TrickShots"/>).
    /// On island 1 Sandy shouts what to do the first time each step of a rescue comes up, and now and then someone
    /// screams for help in knee-deep water.
    /// </summary>
    public partial class StoryDirector
    {
        private const float FreePlayNearSea = 140f;   // somebody must be this close to an island's sea for a tourist to turn up there
        private const float FreePlayFirstWait = 10f;
        private const int FreePlayMaxInTrouble = 3;
        private const float FalseAlarmChance = 0.12f;

        private bool _freePlay;
        private Coroutine _freePlayLoop, _hints, _falseAlarm;
        private readonly Dictionary<VictimBrain, Gag> _gagOf = new();
        private IslandSetup _island3;

        /// <summary>True while the open shift is running.</summary>
        public bool IsFreePlay => _freePlay;

        /// <summary>Skull Cove's sea: off the east beach by the pirates' dock. Island 2's rules.</summary>
        private IslandSetup Island3Setup => _island3 ??= new IslandSetup
        {
            Name = "Skull Cove",
            SeaX = new Vector2(124f, 150f),
            SeaZ = new Vector2(-935f, -872f),
            Shoreward = Vector3.left,
            Profile = _island2.Profile,
            LandArea = new Rect(80f, -940f, 30f, 70f)
        };

        private void BeginFreePlay()
        {
            if (_freePlay || !IsServerInitialized) return;
            _freePlay = true;
            _running = true;
            _beat.Value = "shift";
            NoMarker();
            SetChapter("On shift");
            SetObjective("Keep the tourists alive out there");
            SetRentalAccess(true);
            SandySits();
            Debug.Log("[Story] open shift running");
            _freePlayLoop = StartCoroutine(FreePlayLoop());
            _hints = StartCoroutine(GuideHints());
        }

        private void StopFreePlay()
        {
            if (_freePlayLoop != null) StopCoroutine(_freePlayLoop);
            if (_hints != null) StopCoroutine(_hints);
            if (_falseAlarm != null) StopCoroutine(_falseAlarm);
            _freePlayLoop = _hints = _falseAlarm = null;
            EndMission();
            _freePlay = false;
            _running = false;
            _beat.Value = string.Empty;
            CleanupActors();
            SetChapter(string.Empty);
            SetObjective("Free play (shift off)");
            NoMarker();
        }

        private IEnumerator FreePlayLoop()
        {
            yield return new WaitForSeconds(FreePlayFirstWait);
            while (_freePlay && IsServerInitialized)
            {
                // More lifeguards, more tourists; never so many that a rescue gets dull.
                int players = Mathf.Max(1, PlayerHub.All.Count);
                yield return new WaitForSeconds(Random.Range(16f, 32f) / Mathf.Sqrt(players));
                if (!_freePlay || !IsServerInitialized) yield break;
                _waveTourists.RemoveWhere(v => v == null);
                if (RescueService.Instance == null || RescueService.InTroubleCount >= Mathf.Min(FreePlayMaxInTrouble, 1 + players)) continue;
                IslandSetup island = FreePlayIsland();
                if (island == null) continue;
                _island = island;
                if (island == _island1 && _falseAlarm == null && Random.value < FalseAlarmChance)
                {
                    _falseAlarm = StartCoroutine(FalseAlarmOnce()); // (alongside: the sea doesn't wait for it)
                    continue;
                }
                int figure = Random.value < 0.5f ? 0 : 1;
                bool silent = Random.value < 0.15f;
                TouristProfile profile = Profile(island, figure, silent);
                Gag gag = null;
                if (!silent)
                {
                    gag = PickGag(island == _island1 ? Island1Gags : Island2Gags, figure);
                    profile.Shouts = gag.Shouts;
                    if (gag.Figure >= 0) profile.Figure = gag.Figure;
                }
                VictimBrain v = SpawnStoryTourist(island, profile);
                if (v == null || gag == null) continue;
                _gagOf[v] = gag;
                StoryNpc guide = island == _island1 ? _sandy : island == _island2 ? _receptionist : null;
                if (guide != null && !string.IsNullOrEmpty(gag.Spotted))
                    StartCoroutine(Comment(guide, (guide == _sandy ? "(shouting) " : "(over the speaker) ") + gag.Spotted, v));
            }
        }

        private IEnumerator FalseAlarmOnce()
        {
            yield return FalseAlarm();
            _falseAlarm = null;
        }

        private Gag GagOf(VictimBrain v)
        {
            if (v == null || !_gagOf.TryGetValue(v, out Gag gag)) return null;
            _gagOf.Remove(v);
            return gag;
        }

        /// <summary>The island whose sea has a lifeguard nearby (the closest one if several), or null if nobody is anywhere near.</summary>
        private IslandSetup FreePlayIsland()
        {
            IslandSetup best = null;
            float bestSq = FreePlayNearSea * FreePlayNearSea;
            foreach (IslandSetup island in new[] { _island1, _island2, Island3Setup })
            {
                if (island == null) continue;
                var centre = new Vector3((island.SeaX.x + island.SeaX.y) * 0.5f, 0f, (island.SeaZ.x + island.SeaZ.y) * 0.5f);
                foreach (PlayerHub p in PlayerHub.All)
                {
                    if (p == null) continue;
                    Vector3 d = p.transform.position - centre;
                    d.y = 0f;
                    if (d.sqrMagnitude < bestSq)
                    {
                        bestSq = d.sqrMagnitude;
                        best = island;
                    }
                }
            }
            return best;
        }

        /// <summary>Sandy is the guide: the first time each step of a rescue comes up on island 1, she shouts what to do.</summary>
        private IEnumerator GuideHints()
        {
            bool spotted = false, towing = false, cpr = false, punch = false;
            while (_sandy != null && !(spotted && towing && cpr && punch))
            {
                foreach (VictimBrain v in VictimBrain.All)
                {
                    if (v == null || (v.transform.position - _sandy.transform.position).sqrMagnitude > 120f * 120f) continue;
                    if (!spotted && v.State.IsStruggling() && !v.Item.IsHeld)
                    {
                        spotted = true;
                        StartCoroutine(Say(_sandy, $"(shouting) Look, out there! {v.Name}'s in trouble! Swim out, grab {(v.IsFemale ? "her" : "him")} with E and bring {(v.IsFemale ? "her" : "him")} back!"));
                    }
                    else if (!towing && v.Item.IsHeld && v.State.IsStruggling())
                    {
                        towing = true;
                        StartCoroutine(Say(_sandy, "(shouting) That's it! Keep the head up and swim back to the beach!"));
                    }
                    else if (!cpr && v.State == VictimState.Unconscious && v.IsAshore && !v.Item.IsHeld)
                    {
                        cpr = true;
                        StartCoroutine(Say(_sandy, $"(shouting) {v.Name} isn't breathing! Kneel down and push on the chest: right mouse, again and again!"));
                    }
                    else if (!punch && v.State == VictimState.Unconscious && v.NextCprStep == CprStep.Punch)
                    {
                        punch = true;
                        StartCoroutine(Say(_sandy, "(shouting) Men are tougher. Slap him awake! Left mouse, right across the face!"));
                    }
                }
                yield return new WaitForSeconds(0.3f);
            }
        }
    }
}
