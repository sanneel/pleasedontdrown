using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>Scene entry point to shared content assets (catalogs). One per game scene.</summary>
    public class GameContent : MonoBehaviour
    {
        [SerializeField] private ItemCatalog _items;
        [SerializeField] private Material _avatarMaterial;
        [Tooltip("Signs' text material; its texture follows the (dynamic) built-in font's atlas.")]
        [SerializeField] private Material _worldTextMaterial;

        private Font _font;

        public static GameContent Instance { get; private set; }
        public static ItemCatalog Items => Instance != null ? Instance._items : null;

        private void Awake()
        {
            Instance = this;
            AvatarRig.SharedMaterial = _avatarMaterial; // for characters made at runtime (preview, first-person arms)
            if (_worldTextMaterial != null)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _worldTextMaterial.mainTexture = _font.material.mainTexture;
                Font.textureRebuilt += OnFontRebuilt;
            }
        }

        private void OnFontRebuilt(Font font)
        {
            if (font == _font && _worldTextMaterial != null) _worldTextMaterial.mainTexture = font.material.mainTexture;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (Instance == this) Instance = null;
        }
    }
}
