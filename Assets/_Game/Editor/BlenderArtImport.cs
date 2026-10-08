using System;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    public static class BlenderArtImport
    {
        [MenuItem("PLEASE DON'T DROWN/Import Blender face and gun polish")]
        public static void ApplyBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                MeshyCharacters.BakeTourists();
                foreach (string kind in new[] { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper" })
                {
                    string path = $"Assets/_Game/Items/Prefabs/{kind}.prefab";
                    GameObject gun = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        string handling = EditorJsonUtility.ToJson(gun.GetComponent<Weapon>());
                        string item = EditorJsonUtility.ToJson(gun.GetComponent<Item>());
                        int colliders = gun.GetComponentsInChildren<Collider>(true).Length;
                        WeaponSurfaceFinish.Apply(gun.transform, kind);
                        foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>(true))
                            if (filter.sharedMesh == null) throw new InvalidOperationException($"{kind}: missing mesh on {filter.name}");
                        if (handling != EditorJsonUtility.ToJson(gun.GetComponent<Weapon>()) ||
                            item != EditorJsonUtility.ToJson(gun.GetComponent<Item>()) ||
                            colliders != gun.GetComponentsInChildren<Collider>(true).Length)
                            throw new InvalidOperationException($"{kind}: art import changed gameplay or physics");
                        PrefabUtility.SaveAsPrefabAsset(gun, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(gun); }
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[BlenderPolish] Tourist bodies and five gun surfaces imported; gameplay and colliders preserved.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
    }
}
