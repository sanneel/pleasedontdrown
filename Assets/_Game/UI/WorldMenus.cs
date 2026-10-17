using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// The menus that open from things in the world (bar seats, shop counters, travel pads) are IMGUI windows, and
    /// there are dozens of those things: with an OnGUI each, every one cost an IMGUI pass a frame even while shut. A
    /// menu registers here while it is open, and this one OnGUI draws it; with nothing open, nothing runs.
    /// </summary>
    public sealed class WorldMenus : MonoBehaviour
    {
        private static readonly List<(Object owner, Action draw)> Open = new();
        private static WorldMenus _host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Open.Clear();
            _host = null;
        }

        /// <summary><paramref name="draw"/> is called from OnGUI (all IMGUI events) until <see cref="Close"/>.</summary>
        public static void Show(Object owner, Action draw)
        {
            Close(owner);
            Open.Add((owner, draw));
            if (_host == null)
            {
                var go = new GameObject("WorldMenus");
                DontDestroyOnLoad(go);
                _host = go.AddComponent<WorldMenus>();
                _host.useGUILayout = false; // fixed boxes: no layout pass
            }
            _host.enabled = true;
        }

        public static void Close(Object owner)
        {
            for (int i = Open.Count - 1; i >= 0; i--)
                if (Open[i].owner == owner) Open.RemoveAt(i);
            if (Open.Count == 0 && _host != null) _host.enabled = false;
        }

        private void OnGUI()
        {
            for (int i = 0; i < Open.Count; i++)
            {
                var (owner, draw) = Open[i];
                if (owner == null) { Open.RemoveAt(i--); continue; }
                draw();
            }
            if (Open.Count == 0) enabled = false;
        }
    }
}
