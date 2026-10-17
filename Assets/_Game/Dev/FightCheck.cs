using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Two-player and character test helper: console <c>meet [distance] [head] [name]</c> steps you up square in front
    /// of the nearest other lifeguard (or the story character whose name contains <c>name</c>) and looks at their chest
    /// (or head), so automated runs can talk to, punch, revive and film each other. (<c>goto</c> puts you on top of
    /// them, where nothing is in front of you to talk to or hit.)
    /// </summary>
    public static class FightCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register() =>
            DevCommands.Register("meet", "[distance] [head] [name]", "Step up in front of the nearest other lifeguard, or a named character, and look at them (tests).", args =>
            {
                PlayerHub me = PlayerHub.Local;
                if (me == null || me.Motor == null) throw new System.InvalidOperationException("no local player");
                float distance = 1.1f;
                bool head = false;
                string name = null;
                foreach (string a in args)
                {
                    if (float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) distance = d;
                    else if (a == "head") head = true;
                    else name = name == null ? a : name + " " + a;
                }

                Transform target = null;
                Vector3 facing = Vector3.forward, look = Vector3.zero;
                string shown = null;
                if (name != null)
                {
                    float best = float.MaxValue;
                    foreach (StoryNpc npc in StoryNpc.All)
                    {
                        if (npc == null || npc.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        float d = (npc.transform.position - me.transform.position).sqrMagnitude;
                        if (d >= best) continue;
                        best = d;
                        target = npc.transform;
                        facing = Quaternion.Euler(0f, npc.DrawnYaw, 0f) * Vector3.forward;
                        look = npc.HeadPosition - Vector3.up * (head ? 0.02f : 0.4f);
                        shown = npc.Name;
                    }
                }
                else
                {
                    float best = float.MaxValue;
                    foreach (PlayerHub p in PlayerHub.All)
                    {
                        if (p == null || p == me) continue;
                        float d = (p.transform.position - me.transform.position).sqrMagnitude;
                        if (d >= best) continue;
                        best = d;
                        target = p.transform;
                        facing = p.Head.forward;
                        look = p.Head.position - Vector3.up * (head ? 0.05f : 0.45f);
                        shown = p.DisplayName;
                    }
                }
                if (target == null) throw new System.InvalidOperationException(name != null ? $"no character called '{name}'" : "no other lifeguard");
                facing = Vector3.ProjectOnPlane(facing, Vector3.up);
                if (facing.sqrMagnitude < 1e-4f) facing = target.forward;
                me.Motor.Teleport(target.position + facing.normalized * distance);
                me.Look.LookAt(look);
                DevCommands.Print($"meeting {shown} at {distance:F1} m");
            }, cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterPitch() =>
            DevCommands.Register("pitch", "<degrees down>", "Look level (0) or that far down, keeping the heading (tests).", args =>
            {
                PlayerHub me = PlayerHub.Local;
                if (me == null || me.Look == null || me.Head == null) throw new System.InvalidOperationException("no local player");
                float down = args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                Vector3 flat = Vector3.ProjectOnPlane(me.Head.forward, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = me.transform.forward;
                me.Look.LookAt(me.Head.position + flat.normalized * 10f - Vector3.up * (10f * Mathf.Tan(down * Mathf.Deg2Rad)));
            }, cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterHeld() =>
            DevCommands.Register("heldinfo", "", "Where the held item is drawn and whether its renderers are on (tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                Items.Item held = me != null && me.Hands != null ? me.Hands.HeldItem : null;
                if (held == null) throw new System.InvalidOperationException("holding nothing");
                DevCommands.Print($"{held.DisplayName} at {held.transform.position:F2}, head {me.Head.position:F2}, feet {me.transform.position:F2}, active {held.gameObject.activeInHierarchy}, layer {held.gameObject.layer}");
                var kind = me.Hands.GetGrip(out Avatars.HandGrip left, out Avatars.HandGrip right);
                Avatars.AvatarRig rig = me.GetComponentInChildren<Avatars.AvatarRig>();
                if (rig != null)
                {
                    float reach = rig.UpperArmLength + rig.ForearmLength + rig.HandLength * 0.5f;
                    Vector3 sl = rig[Avatars.AvatarRig.Bone.UpperArmL].position, sr = rig[Avatars.AvatarRig.Bone.UpperArmR].position;
                    Vector3 hl = rig[Avatars.AvatarRig.Bone.HandL].position, hr = rig[Avatars.AvatarRig.Bone.HandR].position;
                    DevCommands.Print($"grip {kind}: reach {reach:F2}; L target {left.Point:F2} ({Vector3.Distance(sl, left.Point):F2} from shoulder), hand at {hl:F2} ({Vector3.Distance(hl, left.Point):F2} off); " +
                                      $"R target {right.Point:F2} ({Vector3.Distance(sr, right.Point):F2}), hand at {hr:F2} ({Vector3.Distance(hr, right.Point):F2} off)");
                }
                if (held.TryGetComponent(out Rescue.VictimBody victim))
                {
                    Avatars.AvatarRig v = victim.GetComponentInChildren<Avatars.AvatarRig>();
                    if (v != null)
                        foreach (var b in new[] { Avatars.AvatarRig.Bone.Hips, Avatars.AvatarRig.Bone.Spine, Avatars.AvatarRig.Bone.Chest, Avatars.AvatarRig.Bone.Head, Avatars.AvatarRig.Bone.ShinL })
                            DevCommands.Print($"  tourist {b} at {v[b].position:F2}");
                    return;
                }
                foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
                    DevCommands.Print($"  {r.name}: enabled {r.enabled}, off {r.forceRenderingOff}, active {r.gameObject.activeInHierarchy}, shadows {r.shadowCastingMode}, bounds {r.bounds.center:F2} size {r.bounds.size:F2}");
            }, cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterHand() =>
            DevCommands.Register("handinfo", "", "The local lifeguard's right hand: body, finger bones and how bent they are now (tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                Avatars.AvatarRig rig = me != null ? me.GetComponentInChildren<Avatars.AvatarRig>() : null;
                if (rig == null || rig.RightHand == null) throw new System.InvalidOperationException("no rig");
                Avatars.HandBones h = rig.RightHand;
                var bends = new System.Text.StringBuilder();
                for (int f = 0; f < Avatars.HandBones.Fingers; f++)
                {
                    Transform b0 = h.Bones[Avatars.HandBones.BoneIndex(f, 0)], b1 = h.Bones[Avatars.HandBones.BoneIndex(f, 1)];
                    bends.Append($" f{f}: knuckle {Quaternion.Angle(Quaternion.identity, b1.localRotation):0} deg, base at {b0.localPosition * 100f:F0} cm;");
                }
                DevCommands.Print($"rig {rig.name}, look body {rig.Look.Body}, renderer {(rig.Renderer != null ? rig.Renderer.sharedMesh.name : "none")}, bones {(rig.Renderer != null ? rig.Renderer.bones.Length : 0)}:{bends}");
                // Where the skin of each digit's tip really is (the mesh as drawn this frame), in the hand's frame, cm.
                SkinnedMeshRenderer r = rig.Renderer;
                if (r != null && r.sharedMesh != null && r.sharedMesh.isReadable)
                {
                    var baked = new Mesh();
                    r.BakeMesh(baked, true);
                    Vector3[] v = baked.vertices;
                    BoneWeight[] w = r.sharedMesh.boneWeights;
                    Transform[] bones = r.bones;
                    Transform hand = rig[Avatars.AvatarRig.Bone.HandR];
                    var tips = new System.Text.StringBuilder();
                    int stray = 0;
                    for (int f = 0; f < Avatars.HandBones.Fingers; f++)
                    {
                        int index = System.Array.IndexOf(bones, h.Bones[Avatars.HandBones.BoneIndex(f, 2)]);
                        Vector3 sum = Vector3.zero;
                        int n = 0;
                        for (int i = 0; i < w.Length; i++)
                            if (w[i].boneIndex0 == index && w[i].weight0 > 0.5f) { sum += hand.InverseTransformPoint(r.transform.TransformPoint(v[i])); n++; }
                        tips.Append(n == 0 ? $" f{f}: no skin;" : $" f{f}: {sum / n * 100f:F0};");
                    }
                    for (int i = 0; i < bones.Length; i++) if (bones[i] == null) stray++;
                    DevCommands.Print($"tips (hand frame, cm):{tips} quality skin weights {QualitySettings.skinWeights}, null bones {stray}, renderer quality {r.quality}");
                    Object.Destroy(baked);
                }
                else DevCommands.Print("mesh not readable: can't measure the skin");
            }, cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterFists() =>
            DevCommands.Register("fists", "[0|1]", "Close everybody's hands into fists (checking the fingers' skin).", args =>
                Avatars.AvatarAnimator.DebugFists = args.Length == 0 || args[0] != "0", cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterHide() =>
            DevCommands.Register("hidetourists", "[0|1]", "Hide the tourists' bodies (to see the arms holding one; tests).", args =>
            {
                bool hide = args.Length == 0 || args[0] != "0";
                foreach (Rescue.VictimBody v in Object.FindObjectsByType<Rescue.VictimBody>(FindObjectsSortMode.None))
                    foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hide;
            }, cheat: true);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSay() =>
            DevCommands.Register("npcsay", "<name> <text>", "Make a story character say a line (talk animation tests). Host only.", args =>
            {
                if (args.Length < 2) throw new System.ArgumentException("usage: npcsay <name> <text>");
                if (DialogueService.Instance == null) throw new System.InvalidOperationException("no dialogue service (host only)");
                foreach (StoryNpc npc in StoryNpc.All)
                {
                    if (npc == null || npc.Name.IndexOf(args[0], System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    DialogueService.Instance.ServerSay(npc.Name, string.Join(" ", args, 1, args.Length - 1), npc);
                    return;
                }
                throw new System.InvalidOperationException($"no character called '{args[0]}'");
            }, cheat: true);
    }
}
