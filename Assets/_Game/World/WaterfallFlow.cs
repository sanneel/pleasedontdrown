using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Scrolls a waterfall sheet's streak texture downward. Uses a property block, so the shared material asset is
    /// never touched (in the editor's play mode a material offset would be saved into the .mat).
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WaterfallFlow : MonoBehaviour
    {
        [Tooltip("Texture repeats per second.")]
        [SerializeField] private float _speed = 1.4f;
        [SerializeField] private Vector2 _tiling = new(4f, 3f);

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            _block.SetVector(BaseMapSt, new Vector4(_tiling.x, _tiling.y, 0f, Time.time * _speed % 1f));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
