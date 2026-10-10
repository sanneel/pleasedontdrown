using System.Collections.Generic;
using PleaseDontDrown.Items;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// What the bars sell besides the beer: fries, a can of cola and a tropical cocktail (models from
    /// ArtSource/Tools/model_props.py: fries, cola_can, cocktail). Eaten or drunk like the beer: hold Secondary.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static IEnumerable<Object> BuildBarFoodItems()
        {
            PhysicsMaterial paper = GetPhysicsMaterial("Paper", 0.4f, 0.1f, PhysicsMaterialCombine.Average);
            PhysicsMaterial glass = GetPhysicsMaterial("BottleGlass", 0.15f, 0.5f, PhysicsMaterialCombine.Average);
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material clear = GetMaterial("CocktailGlass", new Color(0.8f, 0.92f, 0.95f), smoothness: 0.9f);

            yield return BuildItem("Fries", "Fries", 0.2f, new Vector3(0.22f, -0.27f, 0.5f), new Vector3(-10f, 0f, 0f), 1f, paper, root =>
            {
                Primitive(PrimitiveType.Cube, "Carton", root, new Vector3(0f, -0.02f, 0f), new Vector3(0.1f, 0.18f, 0.06f), red);
                DressProp(root, "fries");
            }, linearDamping: 0.2f, angularDamping: 0.5f, density: 0.4f, waterDrag: 1.4f, configure: go => BarFood(go, food: 0.3f, seconds: 2.2f));

            yield return BuildItem("Cola", "Cola", 0.35f, new Vector3(0.22f, -0.28f, 0.5f), new Vector3(-8f, 0f, 0f), 1f, glass, root =>
            {
                Primitive(PrimitiveType.Cylinder, "Can", root, Vector3.zero, new Vector3(0.08f, 0.1f, 0.08f), red);
                DressProp(root, "cola_can");
            }, linearDamping: 0.1f, angularDamping: 0.4f, density: 0.7f, waterDrag: 1.0f,
                configure: go => BarFood(go, food: 0.1f, seconds: 2.2f, drink: true, burp: 5f, lip: new Vector3(0.02f, 0.1f, 0f)));

            yield return BuildItem("Cocktail", "Cocktail", 0.4f, new Vector3(0.22f, -0.27f, 0.5f), new Vector3(-6f, 0f, 0f), 0.8f, glass, root =>
            {
                Primitive(PrimitiveType.Cylinder, "Glass", root, new Vector3(0f, -0.01f, 0f), new Vector3(0.08f, 0.1f, 0.08f), clear);
                DressProp(root, "cocktail");
            }, linearDamping: 0.1f, angularDamping: 0.4f, density: 0.7f, waterDrag: 1.0f,
                configure: go => BarFood(go, food: 0.15f, seconds: 3f, drink: true, burp: 12f, lip: new Vector3(-0.03f, 0.15f, 0.012f)));
        }

        /// <summary>One-handed, pocketable, eaten or drunk with Secondary; drinks are held like the beer bottle.</summary>
        private static void BarFood(GameObject go, float food, float seconds, bool drink = false, float burp = 0f, Vector3 lip = default)
        {
            Item item = go.GetComponent<Item>();
            SetBool(item, "_pocketable", true);
            SetEnum(item, "_grip", (int)ItemGrip.OneHand);
            AudioSource audio = SpatialAudio(go, 1.5f, 25f);
            var edible = go.AddComponent<Edible>();
            SetRef(edible, "_audio", audio);
            SetField(edible, "_food", p => p.floatValue = food);
            SetField(edible, "_seconds", p => p.floatValue = seconds);
            SetBool(edible, "_drink", drink);
            SetField(edible, "_burpAfter", p => p.floatValue = burp);
            if (drink)
            {
                SetField(edible, "_lip", p => p.vector3Value = lip);
                go.AddComponent<BottleHold>();
            }
            SetRef(go.AddComponent<ImpactSound>(), "_audio", audio);
        }
    }
}
