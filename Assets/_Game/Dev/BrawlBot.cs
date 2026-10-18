using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Fight tuning helper: console <c>brawlbot [seconds] [mash|timed|smart]</c> fights for the local lifeguard like a
    /// player would. It keeps looking at the nearest fighter on its feet and punches whoever is in reach: <c>mash</c>
    /// clicks as fast as the fists allow (the button-masher), <c>timed</c> jabs twice then hooks (jab, jab, hook),
    /// pausing between, <c>smart</c> reads the fighter (host only): steps back out of a blow that's coming, hooks into a
    /// raised guard now and then, and steps in with a flurry when the guard is down after their swing. The host log gets one
    /// <c>[BrawlBot]</c> line at the end: how long it lasted, punches thrown, how often the lifeguard went down and how
    /// many fighters were beaten.
    /// </summary>
    public sealed class BrawlBot : MonoBehaviour
    {
        private static BrawlBot _running;
        private float _until, _started, _next, _nextStep, _dodgedAt = -10f;
        private string _style = "mash";
        private int _punches, _downs, _step;
        private bool _wasDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register() =>
            DevCommands.Register("brawlbot", "[seconds] [mash|timed]", "Fight like a player for a while (nearest fighter, punches in reach; tests).", args =>
            {
                if (_running != null) Destroy(_running.gameObject);
                var bot = new GameObject("BrawlBot").AddComponent<BrawlBot>();
                bot._started = Time.time;
                bot._until = Time.time + (args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 30f);
                bot._style = args.Length > 1 ? args[1] : "mash";
                _running = bot;
            }, cheat: true);

        private void Update()
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || !me.TryGetComponent(out PlayerCombat combat)) return;
            if (combat.IsDowned != _wasDown)
            {
                _wasDown = combat.IsDowned;
                if (_wasDown) _downs++;
            }
            StoryNpc foe = null;
            int standing = 0, beaten = 0;
            float best = float.MaxValue;
            foreach (StoryNpc npc in StoryNpc.All)
            {
                if (npc == null || npc.MaxHealth <= 0) continue;
                if (npc.IsDefeated) { beaten++; continue; }
                if (!npc.IsFighting) continue;
                standing++;
                if (npc.Pose != Avatars.AvatarPose.Normal) continue;
                float d = (npc.transform.position - me.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    foe = npc;
                }
            }
            if (Time.time >= _until || (standing == 0 && Time.time - _started > 3f))
            {
                Debug.Log($"[BrawlBot] {_style}: {Time.time - _started:F1} s, {_punches} punches, " +
                          $"went down {_downs}x, beaten {beaten}, still standing {standing}");
                Destroy(gameObject);
                return;
            }
            if (foe == null || combat.IsDowned || me.Look == null) return;
            me.Look.LookAt(foe.HeadPosition - Vector3.up * 0.35f);
            if (_style == "smart" && foe.Stance == Avatars.FightStance.WindUp && best < 2.3f * 2.3f)
            {
                // A blow is coming: step back out of it.
                if (Time.time - _dodgedAt > 0.6f)
                {
                    _dodgedAt = Time.time;
                    DevCommands.Execute("strafe 0.35 back");
                }
                return;
            }
            if (Time.time - _dodgedAt < 0.4f) return; // (still stepping back)
            if (best > 1.6f * 1.6f)
            {
                // Out of reach: go after them (as a player runs at the ones waiting their turn).
                if (Time.time >= _nextStep)
                {
                    _nextStep = Time.time + 0.1f;
                    DevCommands.Execute("walk 0.15");
                }
                return;
            }
            if (Time.time < _next) return;
            if (_style == "smart")
            {
                switch (foe.Stance)
                {
                    case Avatars.FightStance.Guard:
                        combat.PressPunch(hook: true); // a hook gets through a guard more often than a jab
                        _next = Time.time + 0.8f;
                        break;
                    default:
                        combat.PressPunch(hook: _step == 2); // open: jab, jab, hook
                        _next = Time.time + 0.15f;
                        _step = (_step + 1) % 3;
                        break;
                }
            }
            else if (_style == "timed")
            {
                // jab, jab, hook, then a breath
                combat.PressPunch(hook: _step == 2);
                _next = Time.time + (_step == 2 ? 0.9f : 0.3f);
                _step = (_step + 1) % 3;
            }
            else
            {
                combat.PressPunch();
                _next = Time.time + 0.12f; // a fast clicker (the fists still set the real pace)
            }
            _punches++;
        }

        private void OnDestroy()
        {
            if (_running == this) _running = null;
        }
    }
}
