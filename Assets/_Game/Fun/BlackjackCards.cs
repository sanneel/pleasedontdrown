using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// Playing cards and chips laid on a table (the casino's blackjack, <see cref="BlackjackTable"/>): a card is a flat
    /// quad showing its cell of Resources/Cards/cards.png (ArtSource/Tools/make_cards.py: 13 columns A..K by suit
    /// rows, the back in the last row) with Resources/Cards/Card.mat; a chip a little cylinder in its colour.
    /// </summary>
    public static class BlackjackCards
    {
        public const float Width = 0.13f, Height = 0.182f; // twice real size (6 x 9 cm): read across the table in first person
        public const byte Back = 52;
        private const int Columns = 13, Rows = 5;

        private static readonly Dictionary<int, Mesh> Meshes = new();
        private static Material _material;
        private static Mesh _chipMesh;
        private static readonly Dictionary<int, Material> ChipMaterials = new();

        private static Material Material => _material != null ? _material : _material = Resources.Load<Material>("Cards/Card");

        /// <summary>A card lying face up (or its back, <see cref="Back"/>), its top toward the transform's +z.</summary>
        public static GameObject Make(byte card, Transform parent)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(card);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static void Show(GameObject card, byte id) => card.GetComponent<MeshFilter>().sharedMesh = MeshFor(id);

        private static Mesh MeshFor(byte card)
        {
            if (Meshes.TryGetValue(card, out Mesh mesh) && mesh != null) return mesh;
            int column = card == Back ? 0 : card % 13, row = card == Back ? 4 : card / 13 % 4;
            // (Cell (column, row) from the atlas's top left; UVs from its bottom left.)
            float u0 = column / (float)Columns, u1 = (column + 1) / (float)Columns;
            float v1 = 1f - row / (float)Rows, v0 = 1f - (row + 1) / (float)Rows;
            float w = Width * 0.5f, h = Height * 0.5f;
            mesh = new Mesh { name = "Card" + card };
            mesh.vertices = new[] { new Vector3(-w, 0f, -h), new Vector3(w, 0f, -h), new Vector3(w, 0f, h), new Vector3(-w, 0f, h) };
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            Meshes[card] = mesh;
            return mesh;
        }

        /// <summary>One casino chip (red 5, blue 10, green 25, black 100 ...) lying flat, 4 cm across.</summary>
        public static GameObject Chip(int value, Transform parent)
        {
            var go = new GameObject("Chip");
            go.transform.SetParent(parent, false);
            if (_chipMesh == null)
            {
                var prim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _chipMesh = prim.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(prim);
            }
            go.AddComponent<MeshFilter>().sharedMesh = _chipMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ChipMaterial(value);
            go.transform.localScale = new Vector3(0.042f, 0.004f, 0.042f); // (the cylinder is 2 m tall: 8 mm)
            return go;
        }

        private static Material ChipMaterial(int value)
        {
            if (ChipMaterials.TryGetValue(value, out Material m) && m != null) return m;
            Color colour = value >= 100 ? new Color(0.08f, 0.08f, 0.1f) : value >= 25 ? new Color(0.1f, 0.5f, 0.25f)
                : value >= 10 ? new Color(0.12f, 0.3f, 0.75f) : new Color(0.8f, 0.12f, 0.12f);
            m = new Material(Material) { name = "Chip" + value, mainTexture = null, color = colour };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            ChipMaterials[value] = m;
            return m;
        }
    }
}
