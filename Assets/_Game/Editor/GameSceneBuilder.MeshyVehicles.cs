using System.IO;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Meshy models for the jet ski, the pirates' boat and the shark. Each GLB is prepared by
    /// ArtSource/Tools/prepare_prop.py (nose on +Z, real length, lowest point at y 0), so the numbers here are
    /// measured on the prepared model. Without the GLB the greybox stays as it is.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string MeshyVehicleDir = "Assets/_Game/Art/Meshy";

        private static GameObject LoadMeshyModel(string asset)
        {
            string path = $"{MeshyVehicleDir}/{asset}.glb";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null && File.Exists(path)) throw new InvalidDataException($"GLB import failed: {path}");
            return prefab;
        }

        /// <summary>Greybox children lose their looks (and all but the kept colliders), then the model goes in.</summary>
        private static GameObject SwapGreybox(Transform root, GameObject prefab, Vector3 modelPos, params string[] keep)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponent<MeshRenderer>() == null) continue; // handling points
                if (System.Array.IndexOf(keep, child.name) >= 0)
                {
                    Object.DestroyImmediate(child.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(child.GetComponent<MeshFilter>());
                }
                else Object.DestroyImmediate(child.gameObject);
            }
            var model = Object.Instantiate(prefab, root, false);
            model.name = "Meshy_" + prefab.name;
            model.transform.localPosition = modelPos;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            return model;
        }

        private static void MovePoint(Transform root, string name, Vector3 position)
        {
            Transform t = root.Find(name);
            if (t != null) t.localPosition = position;
        }

        private static void ColliderBox(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void FitBox(Transform root, string name, Vector3 centre, Vector3 size)
        {
            Transform t = root.Find(name);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale = size;
        }

        /// <summary>The jet ski's model (3 m). Returns false when the GLB isn't there.</summary>
        private static bool DressJetSki(GameObject root)
        {
            GameObject prefab = LoadMeshyModel("jetski");
            if (prefab == null) return false;
            SwapGreybox(root.transform, prefab, Vector3.zero, "Hull");
            FitBox(root.transform, "Hull", new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.6f, 2.9f));
            MovePoint(root.transform, "Seat", new Vector3(0f, 0.88f, -0.3f));
            MovePoint(root.transform, "GripLeft", new Vector3(-0.4f, 1f, 0.38f));
            MovePoint(root.transform, "GripRight", new Vector3(0.4f, 1f, 0.38f));
            MovePoint(root.transform, "Thrust", new Vector3(0f, 0.05f, -1.4f));
            return true;
        }

        /// <summary>
        /// The pirates' black RIB (7 m): tube and console colliders, the helm behind the console, a pirate flag on the
        /// stern arch, and four Ride spots (fore deck and stern well) the story puts the pirates on.
        /// Returns false when the GLB isn't there.
        /// </summary>
        private static bool DressPirateBoat(GameObject root)
        {
            GameObject prefab = LoadMeshyModel("pirate_boat");
            if (prefab == null) return false;
            Transform t = root.transform;
            SwapGreybox(t, prefab, Vector3.zero, "Hull");
            // Hull = the walkable floor inside the tubes.
            FitBox(t, "Hull", new Vector3(0f, 0.31f, 0.1f), new Vector3(2.2f, 0.62f, 6.4f));
            TagSurface(t.Find("Hull").gameObject, SurfaceKind.Wood);
            foreach (float side in new[] { -1f, 1f })
                ColliderBox(t, "Tube", new Vector3(1.12f * side, 0.42f, -0.1f), new Vector3(0.5f, 0.84f, 6.6f));
            ColliderBox(t, "Console", new Vector3(0f, 1.1f, 0.1f), new Vector3(0.9f, 1.3f, 0.5f));

            MovePoint(t, "Seat", new Vector3(0f, 1.4f, -0.45f));
            MovePoint(t, "GripLeft", new Vector3(-0.18f, 1.5f, -0.12f));
            MovePoint(t, "GripRight", new Vector3(0.18f, 1.5f, -0.12f));
            MovePoint(t, "Thrust", new Vector3(0f, 0.1f, -3.4f));

            Material black = GetMaterial("PhoneBlack", new Color(0.08f, 0.08f, 0.1f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material metal = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));
            Primitive(PrimitiveType.Cylinder, "FlagPole", t, new Vector3(0f, 2.6f, -1.95f), new Vector3(0.05f, 0.65f, 0.05f), metal, keepCollider: false);
            // The flag streams back off the pole; the skull shows on both sides.
            Primitive(PrimitiveType.Cube, "Flag", t, new Vector3(0f, 3f, -2.4f), new Vector3(0.02f, 0.55f, 0.85f), black, keepCollider: false);
            Primitive(PrimitiveType.Sphere, "Skull", t, new Vector3(0f, 3.03f, -2.4f), new Vector3(0.035f, 0.2f, 0.2f), white, keepCollider: false);

            Vector3[] rides = { new(-0.35f, 0.66f, 2.35f), new(0.35f, 0.66f, 2.35f), new(-0.45f, 0.4f, -2.35f), new(0.45f, 0.4f, -2.35f) };
            for (int i = 0; i < rides.Length; i++)
            {
                var spot = new GameObject("Ride" + i).transform;
                spot.SetParent(t, false);
                spot.localPosition = rides[i];
            }
            return true;
        }

        /// <summary>The shark's model (3 m): the body centred on the shark's origin, the tail on its own wagging pivot.</summary>
        private static void DressShark(GameObject shark, Transform tailPivot)
        {
            GameObject prefab = LoadMeshyModel("shark");
            if (prefab == null) return;
            const float bodyCentre = 0.66f; // model height of the torso's middle
            GameObject model = SwapGreybox(shark.transform, prefab, new Vector3(0f, -bodyCentre, 0f), "Body");
            for (int i = tailPivot.childCount - 1; i >= 0; i--) Object.DestroyImmediate(tailPivot.GetChild(i).gameObject);
            tailPivot.localPosition = new Vector3(0f, 0.501f - bodyCentre, -0.721f); // the cut prepare_prop.py made
            Transform tail = model.transform.Find("Tail");
            if (tail == null) throw new InvalidDataException("shark.glb has no Tail node (run prepare_prop.py with --split-tail)");
            tail.SetParent(tailPivot, true);
        }
    }
}
