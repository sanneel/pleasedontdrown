using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Network compatibility string: app version + a stamp the scene builder writes every time the networked
    /// content is regenerated. The editor and a build made from the same generated scene match; anything older
    /// is refused at connect time instead of failing with cryptic scene-object errors.
    /// </summary>
    public static class NetVersion
    {
        private static string _current;

        public static string Current
        {
            get
            {
                if (_current != null) return _current;
                var stamp = Resources.Load<TextAsset>("BuildStamp");
                _current = $"{Application.version}+{(stamp != null ? stamp.text.Trim() : "dev")}";
                return _current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _current = null;
    }
}
