using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// "Does every body walk like the others?" Each generated body goes through the same little routine the beach
    /// bots do (stand, turn on the spot, set off, a sharp bend in the path, stop) and the check measures, every frame,
    /// what reads as broken: knees buckling while upright, the legs crossing, a foot sliding while it should be
    /// planted, the hips sinking, the body tipping over. A body is compared with the sunburnt dad, who walks right.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.WalkCheck.RunBatch -quit</c>
    /// </summary>
    public static class WalkCheck
    {
        private static readonly (byte body, string name)[] Bodies =
        {
            (AvatarLook.Bodies.TouristBuddy, "dad"), (AvatarLook.Bodies.Sandy, "sandy"), (AvatarLook.Bodies.Lola, "lola"),
            (AvatarLook.Bodies.GirlRed, "girlred"), (AvatarLook.Bodies.GirlBlonde, "girlblonde"),
        };

        public static void RunBatch()
        {
            int failures;
            try { failures = Run(); }
            catch (Exception e)
            {
                Debug.LogError($"[WalkCheck] FAILED to run: {e}");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log(failures == 0 ? "[WalkCheck] ALL PASS" : $"[WalkCheck] {failures} FAILED");
            if (failures > 0) EditorApplication.Exit(1);
        }

        private sealed class Worst
        {
            public float Knee = 1f, Cross = 1f, Slide, Sink, Tilt;
            public string KneeAt = "", CrossAt = "", SlideAt = "", SinkAt = "", TiltAt = "";
        }

        private static int Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 20f;
            Physics.SyncTransforms();
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            int failures = 0;
            var report = new StringBuilder();
            for (int b = 0; b < Bodies.Length; b++)
            {
                Worst w = Walk(Bodies[b].body, new Vector3(b * 6f, 0f, 0f));
                string line = $"[WalkCheck] {Bodies[b].name,-10} knee straight {w.Knee:F2} ({w.KneeAt}), feet apart {w.Cross * 100f:F1} cm ({w.CrossAt}), " +
                              $"planted foot slides {w.Slide * 100f:F1} cm ({w.SlideAt}), hips sink {w.Sink * 100f:F1} cm ({w.SinkAt}), tilt {w.Tilt:F0} deg ({w.TiltAt})";
                bool bad = w.Knee < 0.8f || w.Cross < 0.02f || w.Slide > 0.06f || w.Sink > 0.12f || w.Tilt > 25f;
                if (bad) { failures++; line += "  <-- FAIL"; }
                Debug.Log(line);
                report.AppendLine(line);
            }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/walk-check.txt", report.ToString());
            return failures;
        }

        /// <summary>The bots' routine, at 30 frames a second, measuring as it goes.</summary>
        private static Worst Walk(byte body, Vector3 start)
        {
            var go = new GameObject("WalkCheck_" + body);
            go.transform.position = start;
            var rig = go.AddComponent<AvatarRig>();
            rig.Build(new AvatarLook { Body = body, Figure = (byte)(AvatarLook.Bodies.IsFeminine(body) ? 1 : 0) });
            var animator = go.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            var w = new Worst();
            const float dt = 1f / 30f;
            float facing = 0f;
            Vector3 position = start;
            var planted = new Dictionary<Bone, Vector3>();
            float restHip = rig.HipHeight;
            for (int frame = 0; frame < 30 * 9; frame++)
            {
                float t = frame * dt;
                Vector3 velocity = Vector3.zero;
                float want;
                string phase;
                if (t < 1f) { want = 0f; phase = "standing"; }
                else if (t < 2.5f) { want = 120f; phase = "turning on the spot"; }
                else if (t < 5f) { want = 120f; velocity = Quaternion.Euler(0f, 120f, 0f) * Vector3.forward * 1.2f; phase = "walking"; }
                else if (t < 7f) { want = 210f; velocity = Quaternion.Euler(0f, 210f, 0f) * Vector3.forward * 1.2f; phase = "sharp bend"; }
                else { want = 210f; phase = "stopping"; }
                // The way StoryNpc turns the drawn body: quickly toward where it walks.
                facing = Mathf.LerpAngle(facing, want, 1f - Mathf.Exp(-(velocity.sqrMagnitude > 0.09f ? 18f : 6f) * dt));
                position += velocity * dt;
                go.transform.position = position;
                animator.Motion = new AvatarMotion { Velocity = velocity, FacingYaw = facing, Grounded = true };
                AvatarAnimator.TimeOverride = 200f + t;
                animator.Tick(dt);
                if (t < 0.5f) continue; // settling

                string at = $"{phase} t={t.ToString("F2", CultureInfo.InvariantCulture)}";
                Transform root = go.transform;
                foreach (var (thigh, shin, foot) in new[] { (Bone.ThighL, Bone.ShinL, Bone.FootL), (Bone.ThighR, Bone.ShinR, Bone.FootR) })
                {
                    float straight = Vector3.Distance(rig[thigh].position, rig[foot].position) / (rig.ThighLength + rig.ShinLength);
                    if (straight < w.Knee) { w.Knee = straight; w.KneeAt = at; }
                    // Planted: on the ground and (nearly) still in the world from one frame to the next.
                    Vector3 p = rig[foot].position;
                    bool down = p.y - start.y < rig.AnkleHeight + 0.015f;
                    if (down && planted.TryGetValue(foot, out Vector3 last))
                    {
                        float slide = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(last.x, 0f, last.z));
                        if (slide > w.Slide) { w.Slide = slide; w.SlideAt = at; }
                    }
                    if (down) planted[foot] = p; else planted.Remove(foot);
                }
                // Feet apart sideways in the body's own frame (negative = crossed over).
                float apart = root.InverseTransformPoint(rig[Bone.FootR].position).x - root.InverseTransformPoint(rig[Bone.FootL].position).x;
                if (apart < w.Cross) { w.Cross = apart; w.CrossAt = at; }
                float sink = restHip - (rig[Bone.Hips].position.y - start.y);
                if (sink > w.Sink) { w.Sink = sink; w.SinkAt = at; }
                float tilt = Vector3.Angle(rig[Bone.Neck].position - rig[Bone.Hips].position, Vector3.up);
                if (tilt > w.Tilt) { w.Tilt = tilt; w.TiltAt = at; }
            }
            AvatarAnimator.TimeOverride = null;
            Object.DestroyImmediate(go);
            return w;
        }
    }
}
