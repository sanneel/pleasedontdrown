using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
namespace PleaseDontDrown.Editor
{
    public static class MeshyArtReview
    {
        public static void CaptureBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Meshy");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.fieldOfView = 55f;
            Capture(camera, new Vector3(-9, 5, 0), new Vector3(0, 2, 10), "station");
            Capture(camera, new Vector3(5, 4, -5), new Vector3(12, 2.5f, 4), "tower");
            var tourist = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab"));
            tourist.transform.position = new Vector3(0, 1.13f, 19);
            Capture(camera, new Vector3(0, 1.5f, 22.5f), new Vector3(0, 1, 19), "tourist-rest");
            Object.DestroyImmediate(tourist);
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Player/Prefabs/Player.prefab"));
            player.transform.position = new Vector3(0, 0, 19);
            Capture(camera, new Vector3(0, 1.5f, 22.5f), new Vector3(0, 1, 19), "lifeguard");
            Object.DestroyImmediate(player);
            Debug.Log("[Meshy] Visual captures complete.");
        }
        private static void Capture(Camera camera, Vector3 position, Vector3 target, string name)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            var rt = new RenderTexture(1000, 750, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(1000,750,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1000,750),0,0);
            texture.Apply();
            File.WriteAllBytes($"Screenshots/Meshy/{name}.jpg", texture.EncodeToJPG(85));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
