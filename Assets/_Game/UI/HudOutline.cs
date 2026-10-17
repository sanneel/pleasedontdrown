using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// The HUD's text outline (see <see cref="Hud.Label"/>): eight dark copies of the letters round the text, built into
    /// the same mesh, so a label is one draw and is only rebuilt when it changes (IMGUI drew every label nine times a
    /// frame).
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class HudOutline : BaseMeshEffect
    {
        // The same eight directions the IMGUI outline used (screen y runs down there, up here).
        private static readonly Vector2[] Offsets =
        {
            new(-1f, 1f), new(1f, 1f), new(-1f, -1f), new(1f, -1f), new(0f, -1.3f), new(0f, 1f), new(1.2f, 0f), new(-1.2f, 0f)
        };

        private static readonly List<UIVertex> Source = new(), Built = new();

        private float _distance;
        private Color32 _color;

        /// <summary>Changes rebuild the text mesh, so it is only set when it differs.</summary>
        public void Set(float distance, Color color)
        {
            Color32 c = color;
            if (Mathf.Approximately(distance, _distance) && c.r == _color.r && c.g == _color.g && c.b == _color.b && c.a == _color.a) return;
            _distance = distance;
            _color = c;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _distance <= 0f || _color.a == 0) return;
            vh.GetUIVertexStream(Source);
            int n = Source.Count;
            Built.Clear();
            if (Built.Capacity < n * 9) Built.Capacity = n * 9;
            foreach (Vector2 o in Offsets)
            {
                Vector3 shift = o * _distance;
                for (int i = 0; i < n; i++)
                {
                    UIVertex v = Source[i];
                    v.position += shift;
                    v.color = _color; // (one colour: what colour tags say only shows on the letters themselves)
                    Built.Add(v);
                }
            }
            Built.AddRange(Source);
            vh.Clear();
            vh.AddUIVertexTriangleStream(Built);
            Source.Clear();
            Built.Clear();
        }
    }
}
