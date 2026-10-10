using System.Collections;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The open shift: what the game turns into when the story is over (host). The core loop is the same on every
    /// island and never runs out: tourists keep getting into trouble in the sea, you fish them out, and then you
    /// throw, cannon or otherwise launch them back (see <see cref="Fun.TrickShots"/> for the pay). Saved with the
    /// beat "end", so loading a finished game goes straight here instead of replaying the story.
    /// </summary>
    public partial class StoryDirector
    {
        private const float FreePlayNearSea = 140f;   // somebody must be this close to an island's sea for a tourist to turn up there
        private const float FreePlayFirstWait = 10f;
        private const int FreePlayMaxInTrouble = 3;

        private bool _freePlay;

        /// <summary>True once the story is done and the endless shift is running.</summary>
        public bool IsFreePlay => _freePlay;

        private void BeginFreePlay()
        {
            if (_freePlay || !IsServerInitialized) return;
            _freePlay = true;
            _running = true;
            _beat.Value = "end";
            ClearWaits();
            NoMarker();
            SetChapter("Open shift");
            SetObjective("Open shift: fish tourists out of the sea, then trickshot them back in");
            // Everything the story locked is open now.
            SetRentalAccess(true);
            if (_reception != null) _reception.ServerSetAvailable(true);
            if (_pirateBoat != null) _pirateBoat.ServerSetLocked(false);
            if (_sandy != null) SandySits();
            Debug.Log("[Story] open shift (free play) running");
            StartCoroutine(FreePlayLoop());
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
                int figure = Random.value < 0.5f ? 0 : 1;
                bool silent = Random.value < 0.15f;
                SpawnStoryTourist(island, Profile(island, figure, silent));
            }
        }

        /// <summary>The island whose sea has a lifeguard nearby (the closest one if several), or null if nobody is anywhere near.</summary>
        private IslandSetup FreePlayIsland()
        {
            IslandSetup best = null;
            float bestSq = FreePlayNearSea * FreePlayNearSea;
            foreach (IslandSetup island in new[] { _island1, _island2 })
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
    }
}
