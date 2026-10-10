using System.Collections.Generic;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The pay for the other half of the loop (host): fish a tourist out of the sea, then throw them back for a bonus.
    /// Watches every tourist someone throws and every lifeguard who sails through the air (thrown by a friend, fired
    /// from the cannon, flung off a vehicle) and, when they land, pays the team by distance, height and splash.
    /// Nothing here knows about islands or the story, so the same shot pays on every island, in the story or after it.
    /// A tourist you keep re-throwing pays less each time, so the sea is not an infinite money machine.
    /// </summary>
    public class TrickShots : MonoBehaviour
    {
        private const float TouristSpeed = 7f, PlayerSpeed = 9f;   // m/s measured from movement: fast enough that somebody must have launched it
        private const float MinDistance = 12f, LandedSpeed = 2.5f, MaxAir = 25f, TeleportJump = 6f;
        private const int MaxPay = 150;

        private class Flight
        {
            public Vector3 Start;
            public float StartTime, Peak;
        }

        private class Track
        {
            public Vector3 Last;
            public Flight Flight;
        }

        private readonly Dictionary<Component, Track> _tracks = new();
        private readonly Dictionary<VictimBrain, (int shots, float last)> _repeats = new();
        private float _nextClean;
        private bool _serverActive;

        public static TrickShots Instance { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts/stops the host's tracker, clearing any flights from the previous session.</summary>
        public void SetServerActive(bool active)
        {
            _serverActive = active;
            _tracks.Clear();
            _repeats.Clear();
            _nextClean = 0f;
        }

        private void Update()
        {
            if (!_serverActive || StoryDirector.Instance == null || !StoryDirector.Instance.IsServerInitialized) return;
            float dt = Time.deltaTime;
            if (dt <= 0f || TsunamiState.Active) { _tracks.Clear(); return; } // a wave carrying people off is not a trick
            foreach (VictimBrain v in VictimBrain.All)
            {
                if (v == null || v.Item == null) continue;
                // Only a throw counts: someone let go of them in the last moments.
                bool thrown = Time.time - v.Item.ReleasedAt < 1.5f;
                Step(v, v.transform, v.Item.IsHeld, thrown, v.Item.LastHolder, v.Name, v, TouristSpeed, dt);
            }
            foreach (PlayerHub p in PlayerHub.All)
            {
                if (p == null) continue;
                bool seated = p.Motor != null && p.Motor.Seat != null;
                Step(p, p.transform, seated, true, null, p.DisplayName, null, PlayerSpeed, dt);
            }
            if (Time.time > _nextClean)
            {
                _nextClean = Time.time + 30f;
                var gone = new List<Component>();
                foreach (Component key in _tracks.Keys)
                    if (key == null) gone.Add(key);
                foreach (Component key in gone) _tracks.Remove(key);
                var forgotten = new List<VictimBrain>();
                foreach (var repeat in _repeats)
                    if (repeat.Key == null || Time.time - repeat.Value.last > 300f) forgotten.Add(repeat.Key);
                foreach (VictimBrain victim in forgotten) _repeats.Remove(victim);
            }
        }

        private void Step(Component key, Transform body, bool blocked, bool launchable, PlayerHub thrower, string name, VictimBrain victim, float startSpeed, float dt)
        {
            if (!_tracks.TryGetValue(key, out Track track)) _tracks[key] = track = new Track { Last = body.position };
            Vector3 pos = body.position;
            Vector3 move = pos - track.Last;
            track.Last = pos;
            if (move.magnitude > TeleportJump)
            {
                track.Flight = null; // snapped somewhere (the cannon tucking someone in the barrel): not a flight
                return;
            }
            Vector3 velocity = move / dt;
            Flight f = track.Flight;
            if (f == null)
            {
                if (blocked || !launchable || velocity.magnitude < startSpeed || new Vector2(velocity.x, velocity.z).magnitude < 4f) return;
                track.Flight = new Flight { Start = pos - move, StartTime = Time.time, Peak = pos.y };
                return;
            }
            if (blocked)
            {
                track.Flight = null; // caught in someone's arms, or got on a vehicle
                return;
            }
            f.Peak = Mathf.Max(f.Peak, pos.y);
            float age = Time.time - f.StartTime;
            bool inWater = age > 0.3f && Shore.WaterDepthAt(pos) > 0.6f;
            if (age < 0.4f || (!inWater && velocity.magnitude >= LandedSpeed && age < MaxAir)) return;
            track.Flight = null;
            Land(f, pos, inWater, age, thrower, name, victim);
        }

        private void Land(Flight f, Vector3 at, bool inWater, float air, PlayerHub thrower, string name, VictimBrain victim)
        {
            Vector3 flat = at - f.Start;
            flat.y = 0f;
            float distance = flat.magnitude;
            float height = Mathf.Max(0f, f.Peak - f.Start.y);
            if (distance < MinDistance) return;

            bool splash = inWater && Shore.WaterDepthAt(f.Start) < Shore.StandDepth; // from the beach into the sea
            float raw = distance * 1.2f + height * 2f + (splash ? 20f : 0f);
            float share = 1f;
            if (victim != null)
            {
                _repeats.TryGetValue(victim, out var seen);
                int shots = Time.time - seen.last > 300f ? 0 : seen.shots;
                _repeats[victim] = (shots + 1, Time.time);
                share = Mathf.Max(0.3f, 1f / (1f + 0.35f * shots));
            }
            int pay = Mathf.Clamp(Mathf.RoundToInt(raw * share), 5, MaxPay);
            string tier = distance >= 45f ? "ORBITAL!" : distance >= 30f ? "TRICKSHOT!" : distance >= 20f ? "LONG SHOT!" : "NICE THROW!";
            string who = victim != null
                ? $"{(thrower != null ? thrower.DisplayName : "Somebody")} sent {name} flying"
                : $"{name} flew";
            string text = $"<color=#ffd060><b>{tier}</b></color> {who} {distance:F0} m" +
                          $"{(height >= 6f ? $", {height:F0} m up" : "")}{(splash ? " into the sea" : "")}! +${pay}";
            Debug.Log($"[Trickshot] {who}: {distance:F1} m, {height:F1} m up, {air:F1} s, splash {splash}, +${pay}");
            if (Economy.Instance != null) Economy.Instance.ServerAdd(pay, "trickshot", at + Vector3.up * 1.2f);
            if (RescueService.Instance != null)
            {
                RescueService.Instance.ServerAnnounce(text);
                RescueService.Instance.ServerCallout(at, $"+${pay}", pay >= 60);
            }
        }
    }
}
