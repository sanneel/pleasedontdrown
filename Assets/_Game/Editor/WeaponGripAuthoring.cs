using System;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Per-gun grips fitted to our hand rig and current meshes, in metres in gun space.</summary>
    public static class WeaponGripAuthoring
    {
        public static void Apply(Transform gun, string kind)
        {
            Item item = gun.GetComponent<Item>();
            Vector3 right; float slant, wrap, index, lift;
            Vector3 left = Vector3.zero;
            switch (kind)
            {
                case "Pistol": right = new Vector3(.029f,-.073f,-.103f); slant = 12; wrap = 15; index = .27f; lift = 20; break;
                case "SMG": right = new Vector3(.026f,-.068f,-.045f); slant = 0; wrap = 25; index = .40f; lift = 5; left = new Vector3(-.024f,-.018f,.145f); break;
                case "Shotgun": right = new Vector3(.025f,-.069f,-.083f); slant = 0; wrap = 25; index = .40f; lift = 8; left = new Vector3(-.024f,-.027f,.265f); break;
                case "Rifle": right = new Vector3(.026f,-.073f,-.092f); slant = 8; wrap = 25; index = .28f; lift = 18; left = new Vector3(-.024f,-.009f,.25f); break;
                case "Sniper": right = new Vector3(.026f,-.089f,-.083f); slant = 5; wrap = 25; index = .30f; lift = 20; left = new Vector3(-.025f,-.032f,.44f); break;
                default: return;
            }
            Quaternion orientation = Quaternion.Euler(slant,0,0) * Quaternion.LookRotation(Vector3.forward, Vector3.right);
            Vector3 pivot = right - orientation * Vector3.up * .016f;
            Quaternion turn = Quaternion.AngleAxis(wrap, orientation * Vector3.left);
            item.GripRight.localPosition = pivot + turn * (right - pivot);
            item.GripRight.localRotation = turn * orientation;
            if (item.GripLeft != null)
            {
                Transform support = item.GripLeft;
                support.SetParent(gun, false);
                support.localPosition = left;
                support.localRotation = Quaternion.LookRotation(new Vector3(1,0,.28f).normalized, Vector3.down);
                if (kind == "Shotgun") support.SetParent(gun.Find("Pump"), true);
            }
            var so = new SerializedObject(item);
            SetPose(so.FindProperty("_gripPose"), new HandPose(.74f,kind == "Pistol" ? .30f : .18f,0) { Index = index, Middle = .70f, Ring = .76f, Pinky = .80f,
                IndexLift = lift, IndexBend = new Vector3(-12,28,0) });
            so.FindProperty("_ownLeftPose").boolValue = true;
            SetPose(so.FindProperty("_gripPoseLeft"), new HandPose(.52f,.68f,.04f) { Index = .46f, Middle = .50f, Ring = .54f, Pinky = .59f, ThumbSwing = 65f });
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPose(SerializedProperty p, HandPose pose)
        {
            p.FindPropertyRelative("Thumb").floatValue = pose.Thumb;
            p.FindPropertyRelative("Index").floatValue = pose.Index;
            p.FindPropertyRelative("Middle").floatValue = pose.Middle;
            p.FindPropertyRelative("Ring").floatValue = pose.Ring;
            p.FindPropertyRelative("Pinky").floatValue = pose.Pinky;
            p.FindPropertyRelative("Spread").floatValue = pose.Spread;
            p.FindPropertyRelative("IndexLift").floatValue = pose.IndexLift;
            p.FindPropertyRelative("IndexBend").vector3Value = pose.IndexBend;
            p.FindPropertyRelative("ThumbSwing").floatValue = pose.ThumbSwing;
        }

        public static void SaveBatch()
        {
            foreach (string kind in new[] { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper" })
            {
                string path = $"Assets/_Game/Items/Prefabs/{kind}.prefab";
                GameObject gun = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Apply(gun.transform, kind);
                    PrefabUtility.SaveAsPrefabAsset(gun, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(gun); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[GripReview] Per-gun finger poses saved.");
            WeaponGripReview.CaptureHands();
        }

        public static void SaveAndBuildBatch()
        {
            SaveBatch();
            WeaponGripReview.BuildSavedGameBatch();
        }
    }
}
