using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>Every generated body by id (Resources/AvatarBodies, written by the scene builder).</summary>
    public class AvatarBodyLibrary : ScriptableObject
    {
        public const string ResourcePath = "AvatarBodies";

        public AvatarBody[] Bodies = new AvatarBody[0];

        private static AvatarBodyLibrary _instance;
        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _loaded = false;
        }

        /// <summary>The body with this id, or null (then the code-built body is used).</summary>
        public static AvatarBody Get(int id)
        {
            if (id == 0) return null;
            if (!_loaded)
            {
                _instance = Resources.Load<AvatarBodyLibrary>(ResourcePath);
                _loaded = _instance != null;
            }
            if (_instance == null) return null;
            foreach (AvatarBody body in _instance.Bodies)
                if (body != null && body.Id == id && body.Mesh != null) return body;
            return null;
        }
    }
}
