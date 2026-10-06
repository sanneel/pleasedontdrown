using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Look;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Filming the game for trailers and clips: a movie camera that takes over from the player's eyes and frames
    /// the action (fixed, following, chasing, circling), and a recorder that writes what it sees to numbered JPGs
    /// at a steady frame rate, for Tools/showreel.py to cut into a video. Works in a background (-batchmode) copy of
    /// the game, so a scripted host can film other copies acting the scenes out (but a background copy has no sound:
    /// only a windowed one also records it, to a WAV).
    /// Console:
    ///   cam fixed x y z  tx ty tz [fov]            a still shot
    ///   cam look &lt;target&gt; dx dy dz [fov]          stand off the target (world offset) and keep it in frame
    ///   cam chase &lt;target&gt; back up side [fov]     ride along behind/beside it (offset turns with it)
    ///   cam rel &lt;target&gt; &lt;from&gt; right up ahead [fov]  stand off the target, "ahead" being away from the other
    ///                                               one (e.g. across the tourist from the lifeguard doing CPR)
    ///   cam follow &lt;target&gt; dx dy dz [fov]        locked onto it from a world offset (fast things: a lifeguard
    ///                                               shot out of the cannon)
    ///   cam orbit &lt;target&gt; radius height deg/s [fov] [start deg]
    ///   cam drift dx dy dz                          slide the camera along (m/s) in fixed/look shots
    ///   cam off                                     back to the player's eyes
    ///   record &lt;name&gt; [width height fps] / record stop
    ///   costar &lt;n&gt;                                 a different goofy face and colours for this player
    /// Targets: p1..p9 (player by join order: the host is p0), victim / victimf / victimm, or part of the name of
    /// a vehicle, item or scene object; add .head / .chest / .mouth / .feet for a part of a body. A tourist lying
    /// down faces the way her head points.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after everything has moved this frame
    public class Showreel : MonoBehaviour
    {
        private enum Mode { Off, Fixed, Look, Chase, Follow, Orbit }

        private static Showreel _instance;
        private Camera _camera;
        private RenderTexture _dummy;
        private Camera _hidden;
        private AudioListener _hiddenEars;
        private Mode _mode;
        private Func<Vector3> _target;
        private Func<float> _targetYaw;
        private Vector3 _position, _offset, _drift, _look;
        private float _orbitAngle, _orbitSpeed, _radius, _height;
        private float _lastYaw;
        private bool _cut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            DevCommands.Register("cam", "fixed|look|chase|orbit|drift|off ...", "Movie camera for filming (see Dev/Showreel.cs).", args => Get().CamCommand(args), cheat: true);
            DevCommands.Register("record", "<name> [width height fps] | stop", "Record the view to Recordings/<name> (JPG frames + WAV).", args => Get().RecordCommand(args), cheat: true);
            DevCommands.Register("costar", "<n>", "Wear goofy look number n (so the players in a clip don't all look alike).", args =>
            {
                PlayerHub me = PlayerHub.Local;
                if (me == null) throw new InvalidOperationException("no local player");
                int n = Mathf.RoundToInt(DevCommands.ParseFloat(args, 0));
                AvatarLook look = AvatarLook.Lifeguard;
                look.HeadSize = (byte)(n % 3 == 2 ? AvatarLook.SmallHead : 0);
                look.Belly = (byte)((n * 3 + 1) % 4);
                look.Nose = (byte)((n + 2) % 4);
                look.Eyes = (byte)(n % 4);
                look.Teeth = (byte)((n * 2 + 1) % 4);
                look.Skin = (byte)((n * 3 + 2) % AvatarLook.SkinTones.Length);
                look.BottomColor = (byte)((n * 5) % AvatarLook.ClothColors.Length);
                me.SetLook(look);
            }, cheat: true);
        }

        private static Showreel Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Showreel");
            DontDestroyOnLoad(go);
            return _instance = go.AddComponent<Showreel>();
        }

        // ------------------------------------------------------------------ the camera

        private void CamCommand(string[] args)
        {
            if (args.Length == 0) throw new ArgumentException("which shot?");
            float F(int i) => DevCommands.ParseFloat(args, i);
            float Fov(int i) => args.Length > i ? F(i) : 55f;
            switch (args[0].ToLowerInvariant())
            {
                case "off":
                    Release();
                    return;
                case "drift":
                    _drift = new Vector3(F(1), F(2), F(3));
                    return;
                case "fixed":
                    Take();
                    _mode = Mode.Fixed;
                    _position = new Vector3(F(1), F(2), F(3));
                    Vector3 at = new Vector3(F(4), F(5), F(6));
                    _target = () => at;
                    _camera.fieldOfView = Fov(7);
                    break;
                case "look":
                    Resolve(args[1]);
                    Take();
                    _mode = Mode.Look;
                    _position = _target() + new Vector3(F(2), F(3), F(4));
                    _camera.fieldOfView = Fov(5);
                    break;
                case "chase":
                    Resolve(args[1]);
                    Take();
                    _mode = Mode.Chase;
                    _offset = new Vector3(F(4), F(3), -F(2));
                    _camera.fieldOfView = Fov(5);
                    break;
                case "follow":
                    Resolve(args[1]);
                    Take();
                    _mode = Mode.Follow;
                    _offset = new Vector3(F(2), F(3), F(4));
                    _camera.fieldOfView = Fov(5);
                    break;
                case "rel":
                    Resolve(args[2]);
                    Vector3 from = _target();
                    Resolve(args[1]);
                    Vector3 toward = _target() - from; // facing: from the second one to the first
                    float facing = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                    Take();
                    _mode = Mode.Look;
                    _position = _target() + Quaternion.Euler(0f, facing, 0f) * new Vector3(F(3), F(4), F(5));
                    _camera.fieldOfView = Fov(6);
                    break;
                case "orbit":
                    Resolve(args[1]);
                    Take();
                    _mode = Mode.Orbit;
                    _radius = F(2);
                    _height = F(3);
                    _orbitSpeed = F(4);
                    _camera.fieldOfView = Fov(5);
                    _orbitAngle = args.Length > 6 ? F(6) : 0f;
                    break;
                default:
                    throw new ArgumentException($"unknown shot '{args[0]}'");
            }
            _drift = Vector3.zero;
            _cut = true; // a new shot is a cut, not a swoop
        }

        /// <summary>Our camera becomes the main one (the water, floating words and listeners all follow Camera.main).</summary>
        private void Take()
        {
            if (_camera == null)
            {
                var go = new GameObject("ShowreelCamera");
                go.transform.SetParent(transform, false);
                _camera = go.AddComponent<Camera>();
                _camera.nearClipPlane = 0.05f;
                _camera.farClipPlane = 2000f;
                _dummy = new RenderTexture(16, 16, 24); // (it renders for real into the recorder's target)
                _camera.targetTexture = _dummy;
                go.AddComponent<AudioListener>().enabled = false;
            }
            Camera player = PlayerHub.Local != null && PlayerHub.Local.Look != null ? PlayerHub.Local.Look.Camera : null;
            if (player != null && player != _camera)
            {
                _hidden = player;
                player.enabled = false;
                player.tag = "Untagged";
                if (player.TryGetComponent(out _hiddenEars)) _hiddenEars.enabled = false;
            }
            _camera.gameObject.tag = "MainCamera";
            _camera.enabled = true;
            _camera.GetComponent<AudioListener>().enabled = true;
            LookDirector.Prepare(_camera);
        }

        private void Release()
        {
            _mode = Mode.Off;
            if (_camera != null)
            {
                _camera.enabled = false;
                _camera.gameObject.tag = "Untagged";
                _camera.GetComponent<AudioListener>().enabled = false;
            }
            foreach (GameObject tag in _hiddenTags)
                if (tag != null) tag.SetActive(true);
            _hiddenTags.Clear();
            if (_hidden != null)
            {
                _hidden.enabled = true;
                _hidden.tag = "MainCamera";
                if (_hiddenEars != null) _hiddenEars.enabled = true;
            }
        }

        private void LateUpdate()
        {
            if (_mode != Mode.Off && _camera != null && _target != null)
            {
                float dt = Time.unscaledDeltaTime;
                Vector3 target;
                float yaw = 0f;
                try
                {
                    target = _target();
                    if (_targetYaw != null) yaw = _lastYaw = _targetYaw();
                }
                catch (Exception) // (the target went away, e.g. a revived tourist walking off: hold the shot)
                {
                    target = _look;
                    yaw = _lastYaw;
                }
                switch (_mode)
                {
                    case Mode.Fixed:
                    case Mode.Look:
                        _position += _drift * dt;
                        break;
                    case Mode.Chase:
                        Vector3 want = target + Quaternion.Euler(0f, yaw, 0f) * _offset;
                        _position = _cut ? want : Vector3.Lerp(_position, want, 1f - Mathf.Exp(-dt * 6f));
                        break;
                    case Mode.Follow:
                        _position = target + _offset;
                        break;
                    case Mode.Orbit:
                        _orbitAngle += _orbitSpeed * dt;
                        float a = _orbitAngle * Mathf.Deg2Rad;
                        _position = target + new Vector3(Mathf.Sin(a) * _radius, _height, Mathf.Cos(a) * _radius);
                        break;
                }
                // Keep the camera out of the sand and the sea.
                float floor = WaterLevelOrGround(_position) + 0.25f;
                if (_position.y < floor) _position.y = floor;
                _look = _cut || _mode == Mode.Follow ? target : Vector3.Lerp(_look, target, 1f - Mathf.Exp(-dt * 7f));
                _camera.transform.position = _position;
                if ((_look - _position).sqrMagnitude > 1e-4f) _camera.transform.rotation = Quaternion.LookRotation(_look - _position);
                _cut = false;
                HideNameTags();
            }
            if (_recording) Capture();
        }

        private readonly List<GameObject> _hiddenTags = new();

        /// <summary>No "User" floating over every head in a clip (the lifeguards' name tags).</summary>
        private void HideNameTags()
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null)
                    foreach (TextMesh tag in p.GetComponentsInChildren<TextMesh>())
                    {
                        tag.gameObject.SetActive(false);
                        _hiddenTags.Add(tag.gameObject);
                    }
        }

        private static float WaterLevelOrGround(Vector3 p)
        {
            float level = World.Water.WaterSurface.Exists ? World.Water.WaterSurface.HeightAt(p) : float.MinValue;
            float ground = Shore.GroundHeightAt(p + Vector3.up * 30f);
            return float.IsNaN(ground) ? level : Mathf.Max(level, ground);
        }

        // ------------------------------------------------------------------ targets

        private void Resolve(string spec)
        {
            string name = spec, part = "";
            int dot = spec.LastIndexOf('.');
            if (dot > 0)
            {
                name = spec.Substring(0, dot);
                part = spec.Substring(dot + 1).ToLowerInvariant();
            }
            name = name.Replace('_', ' ');
            _targetYaw = null;

            if (name.Length >= 2 && name[0] == 'p' && int.TryParse(name.Substring(1), out int id))
            {
                PlayerHub P()
                {
                    foreach (PlayerHub p in PlayerHub.All)
                        if (p != null && p.OwnerId == id) return p;
                    throw new InvalidOperationException($"no player {id}");
                }
                P();
                _target = part switch
                {
                    "head" => () => P().Head.position,
                    "feet" => () => P().transform.position,
                    _ => () => P().transform.position + Vector3.up * 0.9f
                };
                _targetYaw = () =>
                {
                    Vector3 f = P().Head.forward; // (the way they look: the body itself doesn't always turn)
                    return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                };
                return;
            }
            if (name.StartsWith("victim", StringComparison.OrdinalIgnoreCase))
            {
                int figure = name.Length == 7 ? name[6] == 'f' ? 1 : name[6] == 'm' ? 0 : -1 : -1; // victimf / victimm
                VictimBrain V()
                {
                    foreach (VictimBrain v in VictimBrain.All)
                        if (v != null && (figure < 0 || v.IsFemale == (figure == 1))) return v;
                    throw new InvalidOperationException("no such tourist");
                }
                V();
                _target = part switch
                {
                    "head" => () => V().Body.HeadPosition,
                    "mouth" => () => V().Body.MouthPoint,
                    "feet" => () => V().transform.position,
                    _ => () => V().Body.ChestPoint
                };
                _targetYaw = () =>
                {
                    Vector3 head = V().transform.up; // (lying down, the body's up is where the head points)
                    if (Mathf.Abs(head.y) > 0.7f) return V().transform.eulerAngles.y;
                    return Mathf.Atan2(head.x, head.z) * Mathf.Rad2Deg;
                };
                return;
            }
            Transform t = FindNamed(name);
            if (t == null) throw new InvalidOperationException($"nothing called '{name}'");
            float lift = part == "feet" ? 0f : part == "head" ? 1.6f : 0.6f;
            _target = () => t.position + Vector3.up * lift;
            _targetYaw = () => t.eulerAngles.y;
        }

        private static Transform FindNamed(string name)
        {
            foreach (Vehicle v in Vehicle.All)
                if (v != null && v.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return v.transform;
            foreach (Item i in Item.All)
                if (i != null && i.DisplayName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return i.transform;
            string squashed = name.Replace(" ", "");
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.IndexOf(squashed, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        // ------------------------------------------------------------------ the recorder

        private bool _recording;
        private string _folder;
        private int _width = 1080, _height2 = 1920, _frame, _pending;
        private float _fps = 30f, _start, _next;
        private RenderTexture _target2;
        private StreamWriter _index;
        private readonly List<float> _audio = new();
        private bool _audioOn;
        private int _channels;

        private void RecordCommand(string[] args)
        {
            if (args.Length == 0) throw new ArgumentException("record <name> | stop");
            if (args[0] == "stop")
            {
                Stop();
                return;
            }
            if (_recording) Stop();
            if (_mode == Mode.Off) throw new InvalidOperationException("set up a shot first (cam ...)");
            string name = args[0];
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            _folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", name));
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
            Directory.CreateDirectory(_folder);
            if (args.Length >= 3)
            {
                _width = Mathf.RoundToInt(DevCommands.ParseFloat(args, 1));
                _height2 = Mathf.RoundToInt(DevCommands.ParseFloat(args, 2));
            }
            if (args.Length >= 4) _fps = DevCommands.ParseFloat(args, 3);
            _target2 = new RenderTexture(_width, _height2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            _target2.Create();
            _index = new StreamWriter(Path.Combine(_folder, "frames.txt"));
            _frame = 0;
            _start = Time.unscaledTime;
            _next = _start;
            _audio.Clear();
            _channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            try { _audioOn = !Application.isBatchMode && AudioRenderer.Start(); }
            catch (Exception e) { _audioOn = false; Debug.LogWarning("[Showreel] no sound capture: " + e.Message); }
            _recording = true;
            Debug.Log($"[Showreel] recording {_width}x{_height2} at {_fps} fps to {_folder} (sound {(_audioOn ? "on" : "off")})");
        }

        private void Capture()
        {
            if (_audioOn)
            {
                int samples = AudioRenderer.GetSampleCountForCaptureFrame();
                if (samples > 0)
                {
                    using var buffer = new NativeArray<float>(samples * _channels, Allocator.Temp);
                    AudioRenderer.Render(buffer);
                    foreach (float s in buffer) _audio.Add(s);
                }
            }
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = Mathf.Max(_next + 1f / _fps, now - 0.5f / _fps);
            if (_camera == null || !_camera.enabled) return;

            RenderPipeline.SubmitRenderRequest(_camera, new UniversalRenderPipeline.SingleCameraRequest { destination = _target2 });
            int frame = _frame++;
            _index.WriteLine($"{frame} {(now - _start).ToString("F4", CultureInfo.InvariantCulture)}");
            string path = Path.Combine(_folder, $"f{frame:D5}.jpg");
            uint w = (uint)_width, h = (uint)_height2;
            Interlocked.Increment(ref _pending);
            AsyncGPUReadback.Request(_target2, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    Interlocked.Decrement(ref _pending);
                    return;
                }
                byte[] pixels = request.GetData<byte>().ToArray();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, w, h, 0, 92)); }
                    finally { Interlocked.Decrement(ref _pending); }
                });
            });
        }

        private void Stop()
        {
            if (!_recording) return;
            _recording = false;
            AsyncGPUReadback.WaitAllRequests();
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Volatile.Read(ref _pending) > 0 && Time.realtimeSinceStartup < deadline) Thread.Sleep(10);
            _index.Close();
            if (_audioOn)
            {
                AudioRenderer.Stop();
                WriteWav(Path.Combine(_folder, "sound.wav"), _audio, _channels, AudioSettings.outputSampleRate);
            }
            Debug.Log($"[Showreel] recorded {_frame} frames ({Time.unscaledTime - _start:F1} s, {_audio.Count / Mathf.Max(1, _channels)} sound samples) to {_folder}");
            _target2.Release();
            Destroy(_target2);
        }

        private void OnApplicationQuit() => Stop();

        private static void WriteWav(string path, List<float> samples, int channels, int rate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples.Count * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(bytes);
            foreach (float s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
