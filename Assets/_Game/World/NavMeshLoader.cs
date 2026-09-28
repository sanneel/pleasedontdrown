using UnityEngine;
using UnityEngine.AI;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Adds the navigation meshes the scene builder baked (one per island) so story characters walk around walls,
    /// palms and counters and through doorways instead of through them.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NavMeshLoader : MonoBehaviour
    {
        [SerializeField] private NavMeshData[] _data = System.Array.Empty<NavMeshData>();

        private NavMeshDataInstance[] _instances = System.Array.Empty<NavMeshDataInstance>();

        private void OnEnable()
        {
            _instances = new NavMeshDataInstance[_data.Length];
            for (int i = 0; i < _data.Length; i++)
                if (_data[i] != null) _instances[i] = NavMesh.AddNavMeshData(_data[i]);
        }

        private void OnDisable()
        {
            foreach (NavMeshDataInstance instance in _instances)
                if (instance.valid) instance.Remove();
            _instances = System.Array.Empty<NavMeshDataInstance>();
        }
    }
}
