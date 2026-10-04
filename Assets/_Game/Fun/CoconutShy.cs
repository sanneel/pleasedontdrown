using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The coconut shy: three coconuts on posts, a few balls on the counter. Throw and knock them off. Host: puts
    /// coconuts on the posts, watches anything thrown that flies past one (knocking it off: thrown things are
    /// simulated by the thrower, the coconuts by the host, so the host gives the knock), counts who knocked what,
    /// and sets the coconuts back up a little after the last one falls.
    /// </summary>
    public class CoconutShy : NetworkBehaviour
    {
        [SerializeField] private Transform[] _posts = new Transform[0]; // the top of each post
        [SerializeField] private AudioSource _audio;

        private Item[] _coconuts = new Item[0];
        private bool[] _down = new bool[0];
        private float _resetAt = float.PositiveInfinity;
        private readonly Dictionary<string, int> _round = new();

        public override void OnStartServer()
        {
            base.OnStartServer();
            _coconuts = new Item[_posts.Length];
            _down = new bool[_posts.Length];
            _resetAt = Time.time + 2f;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized) return;
            if (Time.time > _resetAt)
            {
                _resetAt = float.PositiveInfinity;
                SetUp();
                return;
            }
            int downNow = 0;
            for (int i = 0; i < _posts.Length; i++)
            {
                Item coconut = _coconuts[i];
                if (coconut == null || !coconut.IsSpawned)
                {
                    _down[i] = true;
                    downNow++;
                    continue;
                }
                Vector3 c = coconut.transform.position;
                if (!_down[i])
                {
                    // Something thrown flying past it at speed knocks it off.
                    foreach (Item thing in Item.All)
                    {
                        if (thing == null || thing == coconut || thing.IsHeld || thing.LastHolder == null || Time.time - thing.ReleasedAt > 3f) continue;
                        if ((thing.transform.position - c).sqrMagnitude > 0.33f * 0.33f) continue;
                        Rigidbody body = coconut.Sync.Body;
                        if (!body.isKinematic)
                        {
                            Vector3 push = c - thing.ReleasedFrom; // away from whoever threw it
                            push.y = 0f;
                            body.AddForce(push.normalized * 4f + Vector3.up * 1.5f, ForceMode.VelocityChange);
                            body.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
                        }
                        Knocked(i, thing.LastHolder.DisplayName);
                        break;
                    }
                    // Or it simply isn't on its post any more (pushed, punched, picked up).
                    if (!_down[i] && (coconut.IsHeld || (c - _posts[i].position).sqrMagnitude > 0.35f * 0.35f)) Knocked(i, coconut.IsHeld ? coconut.Holder.DisplayName : "");
                }
                if (_down[i]) downNow++;
            }
            if (downNow == _posts.Length && float.IsPositiveInfinity(_resetAt)) _resetAt = Time.time + 4f;
        }

        [Server]
        private void Knocked(int i, string who)
        {
            if (_down[i]) return;
            _down[i] = true;
            int count = 0;
            if (!string.IsNullOrEmpty(who))
            {
                _round.TryGetValue(who, out count);
                _round[who] = ++count;
            }
            bool all = System.Array.TrueForAll(_down, d => d);
            Debug.Log($"[CoconutShy] {(string.IsNullOrEmpty(who) ? "somebody" : who)} knocks coconut {i + 1}{(all ? " (all down)" : "")}");
            KnockObservers(_posts[i].position, who, count, all);
        }

        [ObserversRpc]
        private void KnockObservers(Vector3 at, string who, int count, bool all)
        {
            FloatingText.Spawn(at + Vector3.up * 0.4f, all ? "ALL DOWN! YOU WIN A COCONUT!" : "THUNK!", all ? new Color(1f, 0.85f, 0.3f) : Color.white, all ? 1.3f : 1f, 1.8f);
            if (_audio != null)
            {
                _audio.PlayOneShot(ProceduralAudio.Bonk, 1f);
                if (all) _audio.PlayOneShot(FunSounds.Cheer, 0.9f);
            }
            if (all && !string.IsNullOrEmpty(who)) PlayerHud.ShowToast($"<b>{who}</b> knocked them all down! The coconut is yours.", 2.5f);
        }

        /// <summary>Host: fresh coconuts on every post (old ones lying about are cleared away).</summary>
        [Server]
        private void SetUp()
        {
            Item prefab = GameContent.Items != null ? GameContent.Items.Find("Coconut") : null;
            if (prefab == null) return;
            for (int i = 0; i < _posts.Length; i++)
            {
                Item old = _coconuts[i];
                if (old != null && old.IsSpawned && !old.IsHeld) Despawn(old.gameObject);
                Item fresh = Instantiate(prefab, _posts[i].position + Vector3.up * 0.02f, Quaternion.identity);
                Spawn(fresh.gameObject);
                _coconuts[i] = fresh;
                _down[i] = false;
            }
            _round.Clear();
        }
    }
}
