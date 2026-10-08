using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>A host-timed, bounded surge. The same field drives rendering, swimming and floating bodies.</summary>
    public sealed class TsunamiState : NetworkBehaviour
    {
        public const float Duration = 65f;
        private readonly SyncVar<double> _started = new(-1d);
        public static TsunamiState Instance { get; private set; }
        public static double Clock => InstanceFinder.TimeManager != null &&
            (InstanceFinder.IsServerStarted || InstanceFinder.IsClientStarted)
            ? InstanceFinder.TimeManager.TicksToTime(InstanceFinder.TimeManager.Tick) +
              InstanceFinder.TimeManager.GetTickPercentAsDouble() * InstanceFinder.TimeManager.TickDelta
            : Time.timeAsDouble;
        public static float Age => Instance != null && Instance._started.Value >= 0d
            ? (float)(Clock - Instance._started.Value) : -1f;
        public static bool Active => Age >= 0f && Age < Duration;
        // x centre, half width, southern bound, northern bound. The station interior stays dry.
        public static Vector4 Region => new(10f, 76f, -125f, 27f);
        // Moving crest z, crest height, flood height, drawdown.
        public static Vector4 Field
        {
            get
            {
                float t = Age;
                if (t < 0f || t >= Duration) return new Vector4(-200f, 0f, 0f, 0f);
                float crest = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 8f) / 5f)) *
                              (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 30f) / 8f)));
                float flood = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 20f) / 8f)) *
                              (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 40f) / 25f)));
                float drawdown = Mathf.Sin(Mathf.Clamp01(t / 16f) * Mathf.PI) * 0.65f;
                return new Vector4(-110f + Mathf.Max(0f, t - 8f) * 6.2f, crest * 5.4f, flood * 1.8f, drawdown);
            }
        }

        // MUST match SurgeHeight in Ocean.shader. Added AFTER ordinary shore-wave damping.
        public static float HeightAt(float x, float z)
        {
            if (!Active) return 0f;
            Vector4 r = Region, f = Field;
            float side = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.y * 0.75f, r.y, Mathf.Abs(x - r.x)));
            float coast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.z, r.z + 16f, z)) *
                          (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.w - 14f, r.w, z)));
            float d = (z - f.x) / 9f;
            float crest = Mathf.Exp(-d * d) * f.y;
            float behind = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(f.x - 8f, f.x + 5f, z));
            return side * coast * (crest + behind * f.z - f.w);
        }

        public static Vector3 CurrentAt(Vector3 p)
        {
            float h = Mathf.Max(0f, HeightAt(p.x, p.z));
            return Active ? Vector3.forward * Mathf.Clamp(h * (Age < 35f ? 0.32f : -0.18f), -0.5f, 1.5f) : Vector3.zero;
        }

        private AudioSource _roar;
        private AudioClip _clip;
        private void Awake() => Instance = this;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
        [Server] public void ServerBegin() { _started.Value = Clock; Debug.Log("[Tsunami] warning; surge reaches beach in 26 seconds"); }
        [Server] public void ServerStop() => _started.Value = -1d;

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("tsunami", "[start|stop]", "Surge state or test the incoming tsunami.", args =>
            {
                if (args.Length > 0) TestServer(args[0] == "start");
                DevCommands.Print($"tsunami age {Age:F1}, height at dock {HeightAt(30f, -8f):F2}");
            }, cheat: true, owner: this);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            _roar = gameObject.AddComponent<AudioSource>();
            _roar.playOnAwake = false;
            _roar.loop = true;
            _roar.spatialBlend = 0f;
            const int rate = 22050;
            float[] samples = new float[rate * 4];
            var random = new System.Random(716);
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                low = Mathf.Lerp(low, noise, 0.04f);
                float edge = Mathf.Min(1f, Mathf.Min(i, samples.Length - 1 - i) / 220f);
                samples[i] = (low * 2.7f + noise * 0.11f + Mathf.Sin(i * 2f * Mathf.PI * 42f / rate) * 0.055f) * edge;
            }
            _clip = AudioClip.Create("Incoming surf roar", samples.Length, 1, rate, false);
            _clip.SetData(samples, 0);
            _roar.clip = _clip;
        }

        [ServerRpc(RequireOwnership = false)] private void TestServer(bool start)
        {
            if (!DevCommands.CheatsAllowed) return;
            if (start) ServerBegin(); else ServerStop();
        }

        private void Update()
        {
            if (_roar == null) return;
            Camera cam = Camera.main;
            float distance = cam == null ? 1000f : Vector2.Distance(new Vector2(cam.transform.position.x, cam.transform.position.z), new Vector2(10f, -5f));
            float loud = Active ? Mathf.Clamp01(Field.y / 5.4f + Field.z * 0.15f) * Mathf.Clamp01(1f - distance / 220f) * 0.7f : 0f;
            _roar.volume = Mathf.MoveTowards(_roar.volume, loud, Time.deltaTime * 0.4f);
            if (loud > 0f && !_roar.isPlaying) _roar.Play();
            if (_roar.volume <= 0f && _roar.isPlaying) _roar.Stop();
        }
        public override void OnStopClient() { DevCommands.Unregister("tsunami", this); base.OnStopClient(); }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_clip != null) Destroy(_clip);
        }
    }
}
