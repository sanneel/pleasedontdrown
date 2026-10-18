using System;
using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// The motion clips against the hand-made animation, filmed in the running game: the same body twice on a sand floor
    /// of its own high over the sea (nothing in the way), walking the same route at the same speed (stand, walk, run,
    /// walk, stand), each filmed
    /// from the side by its own camera. Frames go to Recordings/clipdemo/a (hand-made) and b (clips), one every 1/30 s
    /// of game time, for a side-by-side video. Console: <c>clipdemo [body] </c> (default 16, Lola).
    /// </summary>
    [DefaultExecutionOrder(300)] // after the bodies are posed (AvatarAnimator 50, AvatarRig 190)
    public sealed class ClipDemo : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            DevCommands.Register("clipdemo", "[body] [quit]", "Film the motion clips beside the hand-made animation (Recordings/clipdemo); quit: close the game when done.", args =>
            {
                byte body = args.Length > 0 ? (byte)Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : AvatarLook.Bodies.Lola;
                if (!AvatarClips.Available) throw new InvalidOperationException("no clips in Resources/Mixamo");
                var demo = new GameObject("ClipDemo").AddComponent<ClipDemo>();
                demo._quitAfter = args.Length > 1 && args[1] == "quit";
                demo.Begin(body);
            }, cheat: true);
        }

        // The route: (until, m/s).
        private static readonly (float until, float speed)[] Route = { (2f, 0f), (6f, 1.4f), (10f, 5.5f), (13f, 1.4f), (15.5f, 0f) };
        private static readonly Vector3 Origin = new(600f, 400f, 0f), Ahead = Vector3.forward; // a floor of its own, over open sea
        // The two lanes: side by side 40 m apart, the second 25 m further on (out of the first one's picture; both are
        // filmed from the same side, so they face the same way on the screen).
        private const float Apart = 40f, Lead = 25f;

        private AvatarAnimator _handMade, _clips;
        private GameObject _floor;
        private Camera _cameraA, _cameraB;
        private RenderTexture _target;
        private Texture2D _read;
        private string _folder;
        private float _t, _speed, _travelled;
        private int _frame;
        private bool _quitAfter;

        private void Begin(byte body)
        {
            _folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "clipdemo"));
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
            Directory.CreateDirectory(Path.Combine(_folder, "a"));
            Directory.CreateDirectory(Path.Combine(_folder, "b"));
            // The floor: a big sand-coloured plane (it takes their shadows).
            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor.name = "ClipDemo floor";
            _floor.transform.position = Origin + Ahead * 60f;
            _floor.transform.localScale = new Vector3(20f, 1f, 20f);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var sand = new Material(lit) { name = "ClipDemo sand" };
                sand.SetColor("_BaseColor", new Color(0.93f, 0.84f, 0.64f));
                sand.SetFloat("_Smoothness", 0.1f);
                _floor.GetComponent<MeshRenderer>().sharedMaterial = sand;
            }
            Physics.SyncTransforms();
            Vector3 across = Vector3.Cross(Vector3.up, Ahead);
            _handMade = Make("ClipDemo hand-made", body, Lane(false, 0f), false);
            _clips = Make("ClipDemo clips", body, Lane(true, 0f), true);
            _cameraA = MakeCamera("ClipDemo camera A");
            _cameraB = MakeCamera("ClipDemo camera B");
            _target = new RenderTexture(960, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _read = new Texture2D(960, 1080, TextureFormat.RGB24, false);
            Time.captureFramerate = 30; // one frame = 1/30 s of game time, however long the rendering takes
            Debug.Log($"[ClipDemo] filming body {body} to {_folder}");
        }

        private static AvatarAnimator Make(string name, byte body, Vector3 at, bool clips)
        {
            var go = new GameObject(name);
            go.transform.position = Ground(at);
            var rig = go.AddComponent<AvatarRig>();
            rig.Build(new AvatarLook { Body = body, Figure = (byte)(AvatarLook.Bodies.IsFeminine(body) ? 1 : 0) });
            var animator = go.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            animator.UseClips = clips;
            return animator;
        }

        private static Vector3 Lane(bool second, float travelled) =>
            Origin + Vector3.Cross(Vector3.up, Ahead) * (second ? Apart * 0.5f : -Apart * 0.5f) + Ahead * (travelled + (second ? Lead : 0f));

        private static Camera MakeCamera(string name)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1500f;
            camera.enabled = false; // rendered by hand into the frame
            return camera;
        }

        private static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }

        private void Update()
        {
            if (_handMade == null) return;
            float dt = Time.deltaTime;
            _t += dt;
            float target = 0f;
            foreach (var leg in Route)
                if (_t < leg.until) { target = leg.speed; break; }
            _speed = Mathf.MoveTowards(_speed, target, dt * 4f);
            _travelled += _speed * dt;
            float yaw = Quaternion.LookRotation(Ahead).eulerAngles.y;
            foreach (AvatarAnimator a in new[] { _handMade, _clips })
            {
                a.transform.position = Ground(Lane(a == _clips, _travelled));
                a.Motion = new AvatarMotion { Velocity = Ahead * _speed, FacingYaw = yaw, Grounded = true, Sprinting = _speed > 3f };
            }
        }

        private void LateUpdate()
        {
            if (_handMade == null) return;
            Film(_cameraA, _handMade, "a", -1f);
            Film(_cameraB, _clips, "b", -1f);
            _frame++;
            if (_t >= Route[^1].until) Finish();
        }

        private void Film(Camera camera, AvatarAnimator who, string folder, float side)
        {
            // From the side (the same side for both), a little ahead, following.
            Vector3 mid = who.transform.position + Vector3.up * 0.9f;
            Vector3 outward = Vector3.Cross(Vector3.up, Ahead) * side;
            camera.transform.position = mid + outward * 4.6f + Ahead * 2f + Vector3.up * 0.4f;
            camera.transform.LookAt(mid + Ahead * 0.2f);
            camera.targetTexture = _target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = _target;
            _read.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            _read.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(_folder, folder, $"f{_frame:0000}.jpg"), _read.EncodeToJPG(90));
        }

        private void Finish()
        {
            Time.captureFramerate = 0;
            Debug.Log($"[ClipDemo] done: {_frame} frames in {_folder}");
            Destroy(_handMade.gameObject);
            Destroy(_clips.gameObject);
            Destroy(_cameraA.gameObject);
            Destroy(_cameraB.gameObject);
            Destroy(_floor);
            _target.Release();
            Destroy(_target);
            Destroy(_read);
            Destroy(gameObject);
            _handMade = null;
            if (_quitAfter) Application.Quit();
        }
    }
}
