using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Weapon skins (How to Fish's [Z/C]): the holder flicks through finishes for the item in their hands, everyone
    /// sees it (the host keeps the choice), and your last pick for that kind of item is remembered and put on the
    /// next one you pick up. Skin 0 is the item as modelled; the others repaint every surface except glass and
    /// effects (lenses, flashes, lasers).
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class ItemSkin : NetworkBehaviour
    {
        private readonly struct Finish
        {
            public readonly string Name;
            public readonly Color Color;
            public readonly float Metallic, Smoothness;
            public readonly Color[] Camo; // blotches over the base colour, or none

            public Finish(string name, Color color, float metallic, float smoothness, params Color[] camo)
            {
                Name = name;
                Color = color;
                Metallic = metallic;
                Smoothness = smoothness;
                Camo = camo;
            }
        }

        private static readonly Finish[] Finishes =
        {
            new("Factory", Color.white, 0f, 0f),
            new("Chrome", new Color(0.86f, 0.88f, 0.92f), 1f, 0.9f),
            new("Gold", new Color(1f, 0.74f, 0.28f), 1f, 0.82f),
            new("Crimson", new Color(0.72f, 0.06f, 0.09f), 0.35f, 0.62f),
            new("Midnight", new Color(0.07f, 0.08f, 0.13f), 0.6f, 0.72f),
            new("Bubblegum", new Color(1f, 0.5f, 0.74f), 0.1f, 0.55f),
            new("Jungle", new Color(0.33f, 0.4f, 0.22f), 0f, 0.25f,
                new Color(0.2f, 0.25f, 0.13f), new Color(0.47f, 0.4f, 0.26f), new Color(0.12f, 0.12f, 0.1f)),
            new("Arctic", new Color(0.9f, 0.93f, 0.96f), 0f, 0.3f,
                new Color(0.66f, 0.72f, 0.8f), new Color(0.45f, 0.5f, 0.58f), new Color(0.98f, 0.98f, 1f)),
            new("Tiger", new Color(0.95f, 0.55f, 0.12f), 0.1f, 0.45f, new Color(0.08f, 0.06f, 0.05f)),
        };

        public static int Count => Finishes.Length;

        private readonly SyncVar<byte> _skin = new SyncVar<byte>();

        private static readonly Dictionary<int, Material> _materials = new();
        private Item _item;
        private Renderer[] _renderers;
        private Material[][] _original;
        private bool _wasMine;
        private float _nextRequest;

        public int Skin => _skin.Value;
        public string SkinName => Finishes[Mathf.Clamp(_skin.Value, 0, Finishes.Length - 1)].Name;

        /// <summary>The skinnable item in the local player's hands, if any (for the HUD prompt).</summary>
        public static ItemSkin LocalHeld { get; private set; }

        private void Awake()
        {
            _item = GetComponent<Item>();
            var list = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (Skinnable(r)) list.Add(r);
            _renderers = list.ToArray();
            _original = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _original[i] = _renderers[i].sharedMaterials;
            _skin.OnChange += (_, next, _) => Apply(next);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply(_skin.Value);
        }

        private static bool Skinnable(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return false;
            string n = r.name.ToLowerInvariant();
            if (n.Contains("flash") || n.Contains("laser") || n.Contains("glass") || n.Contains("lens") || n.Contains("beam")) return false;
            foreach (Material m in r.sharedMaterials)
                if (m != null && (m.renderQueue >= 3000 || m.name.ToLowerInvariant().Contains("glass"))) return false;
            return true;
        }

        private void OnDisable()
        {
            if (LocalHeld == this) LocalHeld = null;
        }

        private void Update()
        {
            PlayerHub holder = _item.Holder;
            bool mine = holder != null && holder == PlayerHub.Local && !_item.IsStowed;
            if (mine) LocalHeld = this;
            else if (LocalHeld == this) LocalHeld = null;

            // Picked up: your favourite finish for this kind of thing goes on (if it's still factory).
            if (mine && !_wasMine && _skin.Value == 0)
            {
                int favourite = PlayerPrefs.GetInt(PrefKey, 0);
                if (favourite > 0 && favourite < Finishes.Length) Request(favourite, announce: false);
            }
            _wasMine = mine;

            if (!mine || !GameInput.GameplayActive || !_item.IsConfirmedHolder(holder)) return;
            int step = GameInput.SkinNext.WasPressedThisFrame() ? 1 : GameInput.SkinPrev.WasPressedThisFrame() ? -1 : 0;
            if (step == 0 || Time.time < _nextRequest) return;
            _nextRequest = Time.time + 0.12f;
            int next = (_skin.Value + step + Finishes.Length) % Finishes.Length;
            PlayerPrefs.SetInt(PrefKey, next);
            Request(next, announce: true);
        }

        private string PrefKey => "pdd.skin." + _item.DisplayName;

        private void Request(int skin, bool announce)
        {
            Apply(skin); // show it now; the host confirms
            SetSkinServer((byte)skin);
            if (announce)
            {
                BeachAudio.PlayLocal(BeachAudio.Equip, 0.35f);
                PlayerHud.ShowToast($"{Finishes[skin].Name}  ({skin + 1}/{Finishes.Length})", 1.2f);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetSkinServer(byte skin, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (holder == null || holder.Owner != caller || skin >= Finishes.Length) return;
            _skin.Value = skin;
        }

        private void Apply(int skin)
        {
            skin = Mathf.Clamp(skin, 0, Finishes.Length - 1);
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                if (skin == 0)
                {
                    r.sharedMaterials = _original[i];
                    continue;
                }
                var mats = new Material[_original[i].Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = MaterialFor(skin);
                r.sharedMaterials = mats;
            }
        }

        private static Material MaterialFor(int skin)
        {
            if (_materials.TryGetValue(skin, out Material mat) && mat != null) return mat;
            Finish f = Finishes[skin];
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader) { name = "Skin_" + f.Name };
            mat.SetColor("_BaseColor", f.Camo.Length > 0 ? Color.white : f.Color);
            mat.SetFloat("_Metallic", f.Metallic);
            mat.SetFloat("_Smoothness", f.Smoothness);
            if (f.Camo.Length > 0) mat.SetTexture("_BaseMap", CamoTexture(f, skin));
            _materials[skin] = mat;
            return mat;
        }

        /// <summary>Soft-edged blotches (or stripes for Tiger) painted from value noise, tiling.</summary>
        private static Texture2D CamoTexture(Finish f, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Camo_" + f.Name, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size];
            float ox = seed * 17.3f, oy = seed * 5.1f;
            bool stripes = f.Name == "Tiger";
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                Color c = f.Color;
                if (stripes)
                {
                    float wobble = Tile(u, v, 4f, ox) * 0.35f;
                    float band = Mathf.Sin((u * 5f + wobble) * Mathf.PI * 2f);
                    if (band > 0.55f) c = f.Camo[0];
                }
                else
                {
                    for (int k = 0; k < f.Camo.Length; k++)
                    {
                        float n = Tile(u, v, 3f + k, ox + k * 31.7f + oy);
                        if (n > 0.58f) c = f.Camo[k];
                    }
                }
                pixels[y * size + x] = c;
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Tileable smooth noise (0..1): Perlin sampled on a torus.</summary>
        private static float Tile(float u, float v, float scale, float offset)
        {
            float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
            float r = scale / (Mathf.PI * 2f);
            float nx = Mathf.Cos(a) * r + offset, ny = Mathf.Sin(a) * r + offset;
            float nz = Mathf.Cos(b) * r + offset * 0.5f, nw = Mathf.Sin(b) * r;
            return (Mathf.PerlinNoise(nx + nz, ny + nw) + Mathf.PerlinNoise(nx - nw + 3.1f, ny + nz + 7.7f)) * 0.5f;
        }
    }
}
