using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Console <c>overlaps [seconds]</c>: after every frame is drawn, measures how deep each character's hand (palm,
    /// fingertips, wrist) and forearm sit inside every other character's body, and prints the worst cases, grouped by
    /// what the character was doing (CPR, carrying, a punch...). For finding hands that go into other people.
    /// </summary>
    public class OverlapWatch : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("OverlapWatch") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<OverlapWatch>();
        }

        private void OnEnable() =>
            DevCommands.Register("overlaps", "[seconds]", "Measure hands going into other characters for a while, then print the worst (tests).", args =>
            {
                float seconds = args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 5f;
                StopAllCoroutines();
                StartCoroutine(Watch(seconds));
            }, owner: this);

        private void OnDisable() => DevCommands.Unregister("overlaps", this);

        private struct Worst
        {
            public float Depth;
            public int Frames, Bad;
            public string Where;
            public float Apart; // the two bodies' hips apart (flat) at the worst moment
            public float Torso; // the deepest into the other's body, head and legs (not their arms)
            public float At;    // seconds into the watch of the worst moment
            public Vector3 Point;
        }

        private static IEnumerator Watch(float seconds)
        {
            var worst = new Dictionary<string, Worst>();
            float end = Time.time + seconds;
            var wait = new WaitForEndOfFrame();
            int frames = 0;
            while (Time.time < end)
            {
                yield return wait;
                frames++;
                IReadOnlyList<AvatarRig> rigs = BodySpace.Rigs;
                for (int a = 0; a < rigs.Count; a++)
                {
                    AvatarRig me = rigs[a];
                    if (me == null || !me.IsBuilt || me.Renderer == null || !me.Renderer.enabled) continue;
                    me.TryGetComponent(out AvatarAnimator anim);
                    for (int side = 0; side < 2; side++)
                    {
                        bool right = side == 1;
                        HandBones hand = me.Hand(right);
                        Transform handBone = me[right ? AvatarRig.Bone.HandR : AvatarRig.Bone.HandL];
                        Transform fore = me[right ? AvatarRig.Bone.ForearmR : AvatarRig.Bone.ForearmL];
                        if (hand == null || handBone == null || fore == null) continue;
                        float s = me.Scale;
                        var points = new List<(string name, Vector3 at, float radius)>
                        {
                            ("palm", handBone.TransformPoint(hand.PalmCenter), 0.03f * s),
                            ("wrist", handBone.position, 0.025f * s),
                            ("forearm", Vector3.Lerp(fore.position, handBone.position, 0.5f), 0.025f * s)
                        };
                        for (int f = 0; f < HandBones.Fingers; f++) points.Add((f == 0 ? "thumb" : "finger" + f, hand.Tip(f), 0.012f * s));
                        for (int b = 0; b < rigs.Count; b++)
                        {
                            AvatarRig other = rigs[b];
                            if (b == a || other == null || !other.IsBuilt || other.Renderer == null || !other.Renderer.enabled) continue;
                            if (anim != null && ReferenceEquals(anim.IgnoreBody, other)) continue;
                            Transform hips = other[AvatarRig.Bone.Hips];
                            if (hips == null || (hips.position - handBone.position).sqrMagnitude > 2.5f * 2.5f) continue;
                            float deepest = 0f, torso = 0f;
                            string where = null;
                            foreach (var (name, at, radius) in points)
                            {
                                float d = BodySpace.DepthIn(other, at, radius) - radius * 0.5f; // (touching the skin is fine)
                                if (d > deepest) { deepest = d; where = name; }
                                torso = Mathf.Max(torso, BodySpace.DepthIn(other, at, radius, false) - radius * 0.5f);
                            }
                            string doing = Doing(anim);
                            string key = $"{Who(me)} {(right ? "R" : "L")} hand in {Who(other)} [{doing}]";
                            worst.TryGetValue(key, out Worst w);
                            w.Frames++;
                            if (deepest > 0.01f) w.Bad++;
                            w.Torso = Mathf.Max(w.Torso, torso);
                            if (deepest > w.Depth)
                            {
                                w.Depth = deepest;
                                w.Where = where;
                                w.At = Time.time - (end - seconds);
                                w.Point = handBone.position;
                                Transform myHips = me[AvatarRig.Bone.Hips];
                                w.Apart = myHips != null ? Vector2.Distance(new Vector2(myHips.position.x, myHips.position.z), new Vector2(hips.position.x, hips.position.z)) : -1f;
                            }
                            worst[key] = w;
                        }
                    }
                }
            }
            var report = worst.Where(kv => kv.Value.Depth > 0.01f).OrderByDescending(kv => kv.Value.Depth).ToList();
            Debug.Log($"[Overlap] {frames} frames, {report.Count} hand/body pairs went in more than 1 cm");
            foreach (var (key, w) in report.Take(40).Select(kv => (kv.Key, kv.Value)))
                Debug.Log($"[Overlap] {key}: {w.Depth * 100f:0.0} cm deep ({w.Where}), in {w.Bad} of {w.Frames} frames, bodies {w.Apart * 100f:0} cm apart; into the body itself (not an arm) {w.Torso * 100f:0.0} cm; worst at {w.At:0.0} s, {w.Point:F1}");
            DevCommands.Print($"overlaps: {report.Count} bad pairs (see the log)");
        }

        private static string Who(AvatarRig rig)
        {
            Transform t = rig.transform;
            while (t.parent != null && t.GetComponent<Player.PlayerHub>() == null && t.GetComponent<Story.StoryNpc>() == null && t.GetComponent<Rescue.VictimBody>() == null) t = t.parent;
            return t.name;
        }

        private static string Doing(AvatarAnimator anim)
        {
            if (anim == null) return "?";
            AvatarMotion m = anim.Motion;
            var parts = new List<string>();
            if (m.Cpr) parts.Add("cpr");
            if (m.CarryingPerson) parts.Add("carrying");
            else if (m.Holding) parts.Add(m.TwoHanded ? "holding 2h" : "holding");
            if (m.Seated) parts.Add("seated");
            if (m.Pose != AvatarPose.Normal) parts.Add(m.Pose.ToString());
            if (m.Velocity.sqrMagnitude > 0.25f) parts.Add("walking");
            AvatarGesture g = anim.CurrentGesture;
            if (g != AvatarGesture.None) parts.Add(g.ToString());
            return parts.Count == 0 ? "standing" : string.Join(",", parts);
        }
    }
}
