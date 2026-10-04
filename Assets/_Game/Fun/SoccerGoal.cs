using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The beach soccer goal: kick (punch, walk into) or throw the beach ball over the goal line between the posts
    /// and under the bar. Host: a sweep of each beach ball's path through the goal mouth's plane (like the hoop).
    /// </summary>
    public class SoccerGoal : NetworkBehaviour
    {
        public const string BallName = "Beach Ball";

        [SerializeField] private Transform _mouth;     // middle of the goal line on the ground; forward = out of the goal
        [SerializeField] private float _halfWidth = 1.5f, _height = 1.6f;
        [SerializeField] private AudioSource _audio;

        private readonly System.Collections.Generic.Dictionary<Item, float> _lastSide = new();
        private float _cooldown;

        private void FixedUpdate()
        {
            if (!IsServerInitialized || _mouth == null) return;
            foreach (Item ball in Item.All)
            {
                if (ball == null || ball.DisplayName != BallName || ball.IsHeld) continue;
                Vector3 local = _mouth.InverseTransformPoint(ball.transform.position);
                float side = local.z;
                if (_lastSide.TryGetValue(ball, out float before) && before > 0f && side <= 0f && Time.time > _cooldown &&
                    Mathf.Abs(local.x) < _halfWidth - 0.1f && local.y < _height - 0.1f)
                {
                    _cooldown = Time.time + 2f;
                    PlayerHub kicker = ball.LastHolder;
                    GoalObservers(kicker != null ? kicker.DisplayName : "");
                }
                _lastSide[ball] = side;
            }
        }

        [ObserversRpc]
        private void GoalObservers(string who)
        {
            FloatingText.Spawn(_mouth.position + Vector3.up * 2.2f, "GOOOOAL!", new Color(0.4f, 1f, 0.5f), 1.6f, 2.2f);
            PlayerHud.ShowToast(string.IsNullOrEmpty(who) ? "GOAL!" : $"GOAL! <b>{who}</b> scores!", 2.5f);
            if (_audio != null)
            {
                _audio.PlayOneShot(FunSounds.Cheer, 1f);
                _audio.PlayOneShot(FunSounds.Swish, 0.6f);
            }
        }
    }
}
