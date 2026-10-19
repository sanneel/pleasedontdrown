using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        /// <summary>
        /// -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.MiloBoatBatch: brings the saved scene up to the story
        /// where the crew buys Milo's boat (1.9) and jet skis run on fuel, without building the whole scene again.
        /// Milo's four rental jet skis (and their berths and bay numbers) go; his boat moors at the first berth and the
        /// story points at it; a fuel pump stands on his dock, the hotel dock and the dev dock; the lifeguards' own jet
        /// skis get their tanks; the kiosk's sign reads MILO'S BOAT. Re-runnable: a second run finds the rest done and
        /// builds the boat again from BuildMiloBoat.
        /// </summary>
        public static void MiloBoatBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var log = new List<string>();
            Transform env = GameObject.Find("Environment").transform;
            StoryDirector director = Object.FindFirstObjectByType<StoryDirector>(FindObjectsInactive.Include);
            if (director == null) throw new System.InvalidOperationException("No StoryDirector in the scene");
            Transform dock = env.Find("MilosRentalDock") ?? throw new System.InvalidOperationException("No MilosRentalDock");

            // The berth: RentalBerth1 if it's there (the story's points root is its parent), else beside Milo's dock.
            Transform firstBerth = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == "RentalBerth1");
            Transform story = firstBerth != null ? firstBerth.parent : director.transform;

            // Milo's old fleet: the four rental skis, their berths 2-4, the bay numbers painted on the cross dock.
            int removed = 0;
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null) continue;
                if (t.name.StartsWith("RentalJetSki") || (t.parent == dock && t.name.StartsWith("BayNumber")))
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            }
            foreach (Transform t in story.Cast<Transform>().ToArray())
                if (t.name is "RentalBerth2" or "RentalBerth3" or "RentalBerth4")
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            log.Add($"old rental fleet: {removed} objects removed");

            // Milo's boat at the first berth (where the cloud builder puts it: 32, -21), with its mooring.
            Vehicle boat = env.Find("MilosBoat") is { } existing ? existing.GetComponent<Vehicle>() : null;
            if (boat != null)
            {
                // Built fresh every run, so changes to BuildMiloBoat reach the scene (the first helm had no seat and
                // its wheel 0.8 m ahead: the driver squatted on the deck, folded over).
                Object.DestroyImmediate(boat.gameObject);
                boat = null;
                log.Add("Milo's boat removed, to build again");
            }
            Vector3 at = OnWater(new Vector3(32f, 0f, -21f));
            if (firstBerth == null) firstBerth = Point(story, "RentalBerth1", at, 180f);
            else firstBerth.SetPositionAndRotation(at, Quaternion.Euler(0f, 180f, 0f));
            if (boat == null)
            {
                boat = BuildMiloBoat(env, at, 180f);
                boat.name = "MilosBoat";
                var mooring = boat.gameObject.AddComponent<RentalMooring>();
                SetRef(mooring, "_vehicle", boat);
                SetRef(mooring, "_berth", firstBerth);
                log.Add("Milo's boat built at the first berth");
            }
            else log.Add("Milo's boat already there");
            SetRefs(director, "_rentalFleet", new Object[] { boat });
            SetRefs(director, "_rentalBerths", new Object[] { firstBerth });
            SetRef(director, "_jetSki", boat); // (the crew's ride: parked at the hotel dock when a save resumes there)

            // Fuel pumps: Milo's cross dock (where the builder puts it) and every dock BuildDock makes.
            void Pump(Transform onDock, Vector3 local)
            {
                if (onDock == null) { log.Add("no dock for a pump"); return; }
                if (onDock.Find("FuelPump") != null) { log.Add($"{onDock.name}: pump already there"); return; }
                BuildFuelPump(onDock, local);
                log.Add($"{onDock.name}: fuel pump");
            }
            Pump(dock, new Vector3(-10.5f, 0.36f, -11f));
            Pump(env.Find("HotelDock"), new Vector3(0.8f, 0.3f, 0f));
            Pump(env.Find("DevDock"), new Vector3(0.8f, 0.3f, 0f));

            // The lifeguards' own jet skis (by the banana, at the cushion jetty) run on fuel, as the builder now makes them.
            int tanks = 0;
            foreach (Vehicle v in Object.FindObjectsByType<Vehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(v);
                if (so.FindProperty("_displayName")?.stringValue != "Lifeguard Jet Ski") continue;
                so.FindProperty("_fuelCapacity").floatValue = 100f;
                so.FindProperty("_fuelBurn").floatValue = 1.5f;
                so.ApplyModifiedPropertiesWithoutUndo();
                tanks++;
            }
            log.Add($"lifeguard jet skis given a 100 l tank: {tanks}");

            // The kiosk's sign.
            Transform kiosk = env.Find("RentalKiosk");
            if (kiosk != null)
            {
                if (kiosk.Find("RentalSign")?.GetComponent<TextMesh>() is { } sign) sign.text = "MILO'S BOAT";
                if (kiosk.Find("RentalNotice")?.GetComponent<TextMesh>() is { } notice) notice.text = "ONE BOAT. ONE CREW.";
                log.Add("kiosk sign: MILO'S BOAT");
            }
            else log.Add("no rental kiosk (Team B's island 1 pass replaced it): no sign to change");

            AssignSceneIds(scene, force: false); // the boat and the pumps are networked scene objects; old ones keep their ids
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/milo-boat.txt", log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MiloBoat] PASS\n" + string.Join("\n", log));
        }
    }
}
