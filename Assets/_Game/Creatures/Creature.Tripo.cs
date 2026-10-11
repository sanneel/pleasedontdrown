using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// The Tripo bodies (Resources/CreatureModels, made by ArtSource/Tools/tripo_prepare_creatures.py) over the greybox:
    /// the greybox keeps its hit zones and pivots but stops drawing; the model's parts (Head, Jaw, Tail) hang on the
    /// greybox pivots, so the same posing moves them. The parrot keeps its greybox. Without the model files the
    /// greybox shows, as before.
    /// </summary>
    public partial class Creature
    {
        private static readonly Dictionary<CreatureKind, GameObject> TripoBodies = new();
        private bool _tripo;
        private Transform _shadesAt;

        private static GameObject TripoBody(CreatureKind kind)
        {
            if (TripoBodies.TryGetValue(kind, out GameObject prefab)) return prefab;
            string file = kind switch
            {
                CreatureKind.Monkey => "monkey",
                CreatureKind.Alligator => "alligator",
                CreatureKind.OldSnapper => "old_snapper",
                CreatureKind.Kleptopus => "kleptopus",
                _ => null
            };
            prefab = file != null ? Resources.Load<GameObject>("CreatureModels/" + file) : null;
            TripoBodies[kind] = prefab;
            return prefab;
        }

        private void DressTripo(CreatureKind kind)
        {
            _tripo = false;
            GameObject prefab = TripoBody(kind);
            if (prefab == null) return;
            foreach (MeshRenderer r in _model.GetComponentsInChildren<MeshRenderer>(true))
                if (_hat == null || !r.transform.IsChildOf(_hat)) r.enabled = false; // the monkey's stolen hat still shows
            Transform body = Instantiate(prefab, _model, false).transform;
            body.name = "Tripo";
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.allowOcclusionWhenDynamic = false;
            }
            _tripo = true;

            switch (kind)
            {
                case CreatureKind.Alligator:
                    Hang(body, "Head", _model.Find("Head"));
                    Hang(body, "Jaw", _jaw);
                    Hang(body, "Tail", _tail);
                    break;
                case CreatureKind.OldSnapper:
                    Hang(body, "Head", _neck != null ? _neck.Find("HeadPivot") : null);
                    Hang(body, "Jaw", _jaw);
                    Hang(body, "Tail", _tail);
                    break;
                case CreatureKind.Monkey:
                    Transform spot = Find(body, "HatSpot");
                    if (spot != null && _hat != null) _hat.position = spot.position + Vector3.up * 0.02f;
                    break;
                case CreatureKind.Kleptopus:
                    // Its own eight tentacles are the idle arms: only the two working (shootable) arms stay drawn.
                    for (int a = 2; a < (_arms?.Length ?? 0); a++)
                        foreach (Transform seg in _arms[a]) seg.GetComponent<MeshRenderer>().enabled = false;
                    // The two working arms: one skin like the model's tentacles (no greybox stripes), slimmer (AnimateArms).
                    if (_paints.Length > (int)CreaturePaint.Purple)
                        for (int a = 0; a < Mathf.Min(2, _arms?.Length ?? 0); a++)
                            foreach (Transform seg in _arms[a]) seg.GetComponent<MeshRenderer>().sharedMaterial = _paints[(int)CreaturePaint.Purple];
                    _shadesAt = Find(body, "Eyes");
                    if (_glasses != null && _shadesAt != null)
                    {
                        _glasses.position = _shadesAt.position;
                        _glasses.localScale = new Vector3(1.5f, 0.32f, 0.08f);
                    }
                    break;
            }
        }

        /// <summary>Hangs the model's part on the greybox pivot: the pivot moves to the part's hinge (its children stay put).</summary>
        private static void Hang(Transform body, string part, Transform pivot)
        {
            Transform p = Find(body, part);
            if (p == null || pivot == null) return;
            var children = new List<(Transform t, Vector3 at, Quaternion rot)>();
            foreach (Transform c in pivot) children.Add((c, c.position, c.rotation));
            pivot.position = p.position;
            foreach ((Transform t, Vector3 at, Quaternion rot) in children) t.SetPositionAndRotation(at, rot);
            p.SetParent(pivot, true);
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
