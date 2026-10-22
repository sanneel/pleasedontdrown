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
        private static readonly Dictionary<string, GameObject> TripoBodies = new();
        /// <summary>The jungle's monkeys come in three looks (the same size and moves): the capuchin, a blue monkey and a
        /// big blue ape, picked by the creature's network id so every machine shows the same one.</summary>
        private static readonly string[] MonkeyModels = { "monkey", "monkey_blue", "monkey_ape" };
        private bool _tripo;
        private Transform _shadesAt;

        private GameObject TripoBody(CreatureKind kind)
        {
            string file = kind switch
            {
                CreatureKind.Monkey => MonkeyModels[Mathf.Abs(ObjectId) % MonkeyModels.Length],
                CreatureKind.Alligator => "alligator",
                CreatureKind.OldSnapper => "old_snapper",
                CreatureKind.Kleptopus => "kleptopus",
                _ => null
            };
            if (file == null) return null;
            if (TripoBodies.TryGetValue(file, out GameObject prefab)) return prefab;
            prefab = Resources.Load<GameObject>("CreatureModels/" + file);
            if (prefab == null && kind == CreatureKind.Monkey) prefab = Resources.Load<GameObject>("CreatureModels/monkey");
            TripoBodies[file] = prefab;
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
                {
                    Transform headPivot = _neck != null ? _neck.Find("HeadPivot") : null;
                    Hang(body, "Head", headPivot);
                    Hang(body, "Jaw", _jaw);
                    Hang(body, "Tail", _tail);
                    // The neck's skin is weighted between the body and this bone at the head's hinge: on the head pivot it
                    // stretches out of the shell with the head (it used to come away from the neck, a gap behind its head).
                    Transform tip = Find(body, "NeckTip");
                    if (tip != null && headPivot != null) tip.SetParent(headPivot, true);
                    break;
                }
                case CreatureKind.Monkey:
                    Transform spot = Find(body, "HatSpot");
                    if (spot != null && _hat != null) _hat.position = spot.position + Vector3.up * 0.02f;
                    break;
                case CreatureKind.Kleptopus:
                    // Its own eight tentacles are the idle arms: only the two working (shootable) arms stay drawn.
                    // The two working arms are smooth tentacles out of its waist (Creature.Tentacles), slimmer (AnimateArms).
                    for (int a = 2; a < (_arms?.Length ?? 0); a++) ShowTentacle(a, false);
                    // Their skin like the model's: a lighter, glossy purple (the greybox paint is flat and dark beside it).
                    if (_tentacleDrawn != null && (int)CreaturePaint.Purple < _paints.Length)
                    {
                        var skin = new Material(_paints[(int)CreaturePaint.Purple]) { name = "KleptopusTentacle" };
                        skin.SetColor("_BaseColor", new Color(0.52f, 0.3f, 0.66f));
                        skin.SetFloat("_Smoothness", 0.55f);
                        for (int a = 0; a < Mathf.Min(2, _tentacleDrawn.Length); a++) _tentacleDrawn[a].sharedMaterial = skin;
                    }
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
