using System.Collections.Generic;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>`itemdebug` toggles floating labels over every item: who simulates it, who holds it, speed.</summary>
    public class ItemDebugView : MonoBehaviour
    {
        private readonly Dictionary<Item, TextMesh> _labels = new();
        private bool _visible;

        private void OnEnable() =>
            DevCommands.Register("itemdebug", "", "Toggle item authority labels.", _ =>
            {
                _visible = !_visible;
                if (!_visible) Clear();
                DevCommands.Print($"item labels {(_visible ? "ON" : "OFF")}");
            }, owner: this);

        private void OnDisable()
        {
            DevCommands.Unregister("itemdebug", this);
            Clear();
        }

        private void LateUpdate()
        {
            if (!_visible) return;
            Camera cam = Camera.main;

            foreach (Item item in Item.All)
            {
                if (!_labels.TryGetValue(item, out TextMesh label) || label == null)
                    _labels[item] = label = CreateLabel();
                Rigidbody body = item.Sync.Body;
                label.text = $"{item.DisplayName}\nsim: {item.Sync.AuthorityLabel}" +
                             (item.IsHeld ? $"\nheld: {item.Holder.DisplayName}" : "") +
                             $"\n{body.linearVelocity.magnitude:F1} m/s{(body.isKinematic ? " (follow)" : "")}";
                label.color = item.Sync.IsSimulator ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.8f, 0.4f);
                label.transform.position = item.transform.position + Vector3.up * 0.9f;
                if (cam != null) label.transform.rotation = cam.transform.rotation;
            }

            // Remove labels of despawned items.
            var stale = new List<Item>();
            foreach (var pair in _labels)
                if (pair.Key == null) stale.Add(pair.Key);
            foreach (Item dead in stale)
            {
                if (_labels[dead] != null) Destroy(_labels[dead].gameObject);
                _labels.Remove(dead);
            }
        }

        private static TextMesh CreateLabel()
        {
            var go = new GameObject("ItemDebugLabel");
            var mesh = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mesh.fontSize = 48;
            mesh.characterSize = 0.03f;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            return mesh;
        }

        private void Clear()
        {
            foreach (TextMesh label in _labels.Values)
                if (label != null) Destroy(label.gameObject);
            _labels.Clear();
        }
    }
}
