using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static class FlamingoRiderReview
    {
        public static void Configure(GameObject root)
        {
            var current = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Meshy/flamingo.glb");
            var source = current != null ? current.GetComponentInChildren<MeshFilter>() : null;
            var model = root.transform.Find("Meshy_flamingo");
            if (model != null && source != null)
            {
                model.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                model.GetComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
            }
            // Sit on the rear of the ring, feet into its opening. Support hands on
            // the ring beside the hips; the neck was beyond a seated arm's reach.
            float seatHeight = SurfaceHeight(root, 0, -.55f) + .12f;
            float leftHeight = SurfaceHeight(root, -.34f, -.54f) + .025f;
            float rightHeight = SurfaceHeight(root, .34f, -.54f) + .025f;
            root.transform.Find("Seat").localPosition = new Vector3(0, seatHeight, -.40f);
            root.transform.Find("GripLeft").localPosition = new Vector3(-.34f, leftHeight, -.54f);
            root.transform.Find("GripRight").localPosition = new Vector3(.34f, rightHeight, -.54f);
            root.transform.Find("GripLeft").localRotation = Quaternion.identity;
            root.transform.Find("GripRight").localRotation = Quaternion.identity;
            var settings = new SerializedObject(root.GetComponent<Vehicle>());
            settings.FindProperty("_straddle").boolValue = false;
            settings.FindProperty("_restHands").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[FlamingoRider] seat {seatHeight:F3}, palms {leftHeight:F3}/{rightHeight:F3}");
        }

        private static float SurfaceHeight(GameObject root, float x, float z)
        {
            float highest = float.NegativeInfinity;
            foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null) continue;
                Vector3[] points = mesh.sharedMesh.vertices;
                int[] triangles = mesh.sharedMesh.triangles;
                Matrix4x4 local = root.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (int i = 0; i < points.Length; i++) points[i] = local.MultiplyPoint3x4(points[i]);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                    float determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(determinant) < 1e-8f) continue;
                    float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / determinant;
                    float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / determinant;
                    if (u >= 0 && v >= 0 && u + v <= 1)
                        highest = Mathf.Max(highest, u * a.y + v * b.y + (1 - u - v) * c.y);
                }
            }
            if (float.IsNegativeInfinity(highest)) throw new System.InvalidOperationException($"No flamingo surface at {x}/{z}");
            return highest;
        }

        public static void ApplyAndCaptureBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            GameObject root = GameObject.Find("Flamingo");
            if (root == null) throw new System.InvalidOperationException("Flamingo missing");
            Configure(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            // Review in the real scene and with the same animator and hand targets as play.
            var rider = new GameObject("Flamingo rider review");
            var seat = root.transform.Find("Seat");
            rider.transform.SetPositionAndRotation(seat.position - Vector3.up * .5f, Quaternion.Euler(0, seat.eulerAngles.y, 0));
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            var rig = rider.AddComponent<AvatarRig>();
            rig.Build(AvatarLook.Lifeguard);
            var animator = rider.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            root.GetComponent<Vehicle>().GetHandlebars(out HandGrip left, out HandGrip right);
            animator.Motion = new AvatarMotion { FacingYaw = seat.eulerAngles.y, Grounded = true, Seated = true, FloatSeat = true,
                Holding = true, TwoHanded = true, GripLeft = left, GripRight = right };
            for (int i = 0; i < 90; i++) animator.Tick(1f / 30f);
            foreach (AvatarRig.Bone bone in new[] { AvatarRig.Bone.Hips, AvatarRig.Bone.ThighL, AvatarRig.Bone.ShinL, AvatarRig.Bone.FootL })
                Debug.Log($"[FlamingoRider] {bone}: {root.transform.InverseTransformPoint(rig[bone].position)}");
            var camera = new GameObject("Rider review camera").AddComponent<Camera>();
            camera.fieldOfView = 48;
            camera.nearClipPlane = .03f;
            Directory.CreateDirectory("Screenshots/Review/FlamingoRider");
            Vector3 aim = root.transform.TransformPoint(new Vector3(0, .8f, -.25f));
            Vector3[] positions = { new(2.6f, 1.8f, -2.6f), new(2.8f, 1.8f, 0), new(0, 2.5f, 2.8f), new(1.7f, 3.4f, -1.8f) };
            for (int i = 0; i < positions.Length; i++)
            {
                camera.transform.position = root.transform.TransformPoint(positions[i]);
                camera.transform.LookAt(aim);
                var target = new RenderTexture(1200, 900, 24) { antiAliasing = 4 };
                var previous = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); texture.Apply();
                File.WriteAllBytes($"Screenshots/Review/FlamingoRider/pose-{i}.png", texture.EncodeToPNG());
                camera.targetTexture = null; RenderTexture.active = previous;
                Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target);
            }
            Object.DestroyImmediate(rider); Object.DestroyImmediate(camera.gameObject);
            Debug.Log("[FlamingoRider] Pose captures complete.");
        }
    }
}
