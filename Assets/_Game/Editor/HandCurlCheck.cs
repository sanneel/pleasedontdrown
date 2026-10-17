using System;
using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// "Does a fist close?" Builds the players' lifeguard, puts the right hand in each finger pose and measures, from
    /// the skinned mesh itself, where every digit's tip ends up in the hand's own frame (x: toward the back of the hand,
    /// -x the palm; y: up the arm; z: the thumb's side). A closing digit's tip must come back up toward the knuckles and
    /// over to the palm side. Fails when a fist leaves a digit out straight.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.HandCurlCheck.RunBatch -quit</c>
    /// </summary>
    public static class HandCurlCheck
    {
        public static void RunBatch()
        {
            int failures;
            try { failures = Run(); }
            catch (Exception e)
            {
                Debug.LogError($"[HandCurl] FAILED to run: {e}");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log(failures == 0 ? "[HandCurl] ALL PASS" : $"[HandCurl] {failures} FAILED");
            if (failures > 0) EditorApplication.Exit(1);
        }

        private static readonly string[] Names = { "thumb", "index", "middle", "ring", "pinky" };

        private static int Run()
        {
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            string[] cli = Environment.GetCommandLineArgs();
            int tuckAt = Array.IndexOf(cli, "-thumbtuck");
            if (tuckAt >= 0 && tuckAt + 1 < cli.Length) AvatarRig.ThumbTuckDegrees = float.Parse(cli[tuckAt + 1], System.Globalization.CultureInfo.InvariantCulture);
            var go = new GameObject("HandCurl");
            try
            {
                var rig = go.AddComponent<AvatarRig>();
                rig.Build(AvatarLook.Lifeguard);
                SkinnedMeshRenderer renderer = rig.Renderer;
                Mesh mesh = renderer.sharedMesh;
                Transform[] bones = renderer.bones;
                BoneWeight[] weights = mesh.boneWeights;
                Transform hand = rig[AvatarRig.Bone.HandR];
                HandBones fingers = rig.RightHand;
                AvatarAnimator animator = null;
                if (cli.Contains("-animated"))
                {
                    animator = go.AddComponent<AvatarAnimator>();
                    animator.Rig = rig;
                }
                {
                    // Standing at rest (arms down, facing +z): which way the hand's own axes point in the world.
                    var probe = go.AddComponent<AvatarAnimator>();
                    probe.Rig = rig;
                    probe.Motion = new AvatarMotion { FacingYaw = 0f, Grounded = true };
                    for (int i = 0; i < 30; i++) { AvatarAnimator.TimeOverride = 50f + i / 30f; probe.Tick(1f / 30f); }
                    AvatarAnimator.TimeOverride = null;
                    Debug.Log($"[HandCurl] at rest the right hand's +x points {hand.right:F2}, -y (fingers) {(-hand.up):F2}, +z (thumb side) {hand.forward:F2} (world: +x right, +z forward)");
                    // Where the hook digit (the one with skin highest up the hand, toward the wrist) sits: on +z or -z.
                    renderer.BakeMesh(new Mesh(), true);
                    Object.DestroyImmediate(probe);
                }
                // Each digit's tip: the vertices mostly on its last bone.
                var tips = new List<int>[HandBones.Fingers];
                for (int f = 0; f < HandBones.Fingers; f++)
                {
                    Transform last = fingers.Bones[HandBones.BoneIndex(f, HandBones.Segments - 1)];
                    int index = Array.IndexOf(bones, last);
                    tips[f] = Enumerable.Range(0, weights.Length).Where(i => Dominant(weights[i]) == index).ToList();
                }
                var poses = new (string name, HandPose pose)[] { ("flat", HandPose.Flat), ("relaxed", HandPose.Relaxed), ("fist", HandPose.Fist) };
                var at = new Dictionary<string, Vector3[]>();
                var baked = new Mesh();
                foreach (var (name, pose) in poses)
                {
                    if (animator != null)
                    {
                        // As the game does it: both hands holding something out in front in this finger pose.
                        Vector3 chest = go.transform.position + Vector3.up * 1.2f;
                        animator.Motion = new AvatarMotion
                        {
                            FacingYaw = 0f, Grounded = true, Holding = true, TwoHanded = true,
                            GripLeft = new HandGrip(chest + Vector3.forward * 0.35f - Vector3.right * 0.12f, Vector3.forward, Vector3.right, pose),
                            GripRight = new HandGrip(chest + Vector3.forward * 0.35f + Vector3.right * 0.12f, Vector3.forward, -Vector3.right, pose)
                        };
                        for (int i = 0; i < 60; i++)
                        {
                            AvatarAnimator.TimeOverride = 100f + i / 30f;
                            animator.Tick(1f / 30f);
                        }
                        AvatarAnimator.TimeOverride = null;
                    }
                    else
                    {
                        rig.ResetPose(fingers: false);
                        fingers.Pose(pose);
                        rig.LeftHand.Pose(pose);
                    }
                    renderer.BakeMesh(baked, true);
                    Vector3[] v = baked.vertices;
                    var row = new Vector3[HandBones.Fingers];
                    for (int f = 0; f < HandBones.Fingers; f++)
                    {
                        if (tips[f].Count == 0) { row[f] = Vector3.positiveInfinity; continue; }
                        Vector3 sum = Vector3.zero;
                        foreach (int i in tips[f]) sum += hand.InverseTransformPoint(renderer.transform.TransformPoint(v[i]));
                        row[f] = sum / tips[f].Count;
                    }
                    at[name] = row;
                    Debug.Log($"[HandCurl] {name,-8} " + string.Join("  ", Enumerable.Range(0, HandBones.Fingers).Select(f =>
                        $"{Names[f]} {(tips[f].Count == 0 ? "no skin" : $"({row[f].x * 100f:0},{row[f].y * 100f:0},{row[f].z * 100f:0}) cm")}")));
                }
                if (animator != null)
                {
                    // Mid-punch (the strike poses the punching hand as a fist): where are the right hand's tips?
                    animator.Motion = new AvatarMotion { FacingYaw = 0f, Grounded = true };
                    for (int i = 0; i < 30; i++) { AvatarAnimator.TimeOverride = 200f + i / 30f; animator.Tick(1f / 30f); }
                    animator.Play(AvatarGesture.Punch);
                    for (int i = 0; i < 6; i++) { AvatarAnimator.TimeOverride = 201f + i / 30f; animator.Tick(1f / 30f); }
                    AvatarAnimator.TimeOverride = null;
                    renderer.BakeMesh(baked, true);
                    Vector3[] pv = baked.vertices;
                    Debug.Log("[HandCurl] punching " + string.Join("  ", Enumerable.Range(0, HandBones.Fingers).Where(f => tips[f].Count > 0).Select(f =>
                    {
                        Vector3 sum = Vector3.zero;
                        foreach (int i in tips[f]) sum += hand.InverseTransformPoint(renderer.transform.TransformPoint(pv[i]));
                        Vector3 c = sum / tips[f].Count;
                        return $"{Names[f]} ({c.x * 100f:0},{c.y * 100f:0},{c.z * 100f:0}) cm";
                    })));
                }
                int failures = 0;
                for (int f = 0; f < HandBones.Fingers; f++)
                {
                    if (tips[f].Count == 0) continue;
                    Vector3 flat = at["flat"][f], fist = at["fist"][f];
                    float rise = fist.y - flat.y;          // came back up toward the knuckles
                    float palmward = flat.x - fist.x;      // over to the palm side (right hand: palm on -x)
                    float across = flat.z - fist.z;        // the thumb: in across the fingers (toward -z)
                    // Fingers: the tip back up at least 3 cm and not out on the back of the hand (curled all the way
                    // round it ends against the palm, level with where it started). The thumb: folded in over them.
                    bool ok = f == 0 ? rise > 0.02f && palmward > 0.015f && across > 0.025f : rise > 0.03f && palmward > -0.005f;
                    Debug.Log($"[HandCurl] {(ok ? "PASS" : "FAIL")} {Names[f]}: tip comes up {rise * 100f:0.0} cm, {palmward * 100f:0.0} cm to the palm side, {across * 100f:0.0} cm in across the hand");
                    if (!ok) failures++;
                }
                Object.DestroyImmediate(baked);
                return failures;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static int Dominant(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { bone = w.boneIndex1; best = w.weight1; }
            if (w.weight2 > best) { bone = w.boneIndex2; best = w.weight2; }
            if (w.weight3 > best) bone = w.boneIndex3;
            return bone;
        }
    }
}
