using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Finders Keepers, island 4 (GameSceneBuilder.Island4): its places ("Environment/Island4/Story/...") and its sea.
    /// Rescues there go by island 2's rules (the same tourists, the same clock) and pay as island 2 does (Economy's hotel
    /// rate covers z -150 .. -580, where island 4 lies too).
    /// </summary>
    public partial class StoryDirector
    {
        private const string Island4Story = "Environment/Island4/Story/";
        private IslandSetup _island4;

        private static Transform Island4Point(string name)
        {
            GameObject go = GameObject.Find(Island4Story + name);
            if (go == null) Debug.LogWarning($"[Story] island 4 has no '{name}' (run GameSceneBuilder.Island4Batch)");
            return go != null ? go.transform : null;
        }

        /// <summary>The island 4 landing (the west jetty's land end).</summary>
        private static Vector3 Island4Arrival => GameObject.Find("Environment/Island4/Island4Arrival") is { } a ? a.transform.position : new Vector3(471f, 1.4f, -380f);

        /// <summary>
        /// Island 4's sea: past the lagoon's drop-off off the south-west beach, where a tourist out of their depth is
        /// in trouble (the shelf before it is knee-deep). Island 2's tourist rules.
        /// </summary>
        private IslandSetup Island4Setup => _island4 ??= new IslandSetup
        {
            Name = "Finders Keepers",
            SeaX = new Vector2(412f, 424f), // (s -48 .. -36: just past the drop-off, 2-6 m deep)
            SeaZ = new Vector2(-490f, -420f),
            Shoreward = Vector3.right,
            Profile = _island2.Profile,
            LandArea = new Rect(470f, -500f, 140f, 180f),
            Arrival = GameObject.Find("Environment/Island4/Island4Arrival")?.transform
        };
    }
}
