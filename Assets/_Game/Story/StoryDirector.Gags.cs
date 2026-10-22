using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The funny side of the story. Every island has just three rescues, and each one is somebody with their own silly
    /// reason to be out there: what they yell instead of "help!", what the guide says about it, and what they say once
    /// they're saved (which also depends on how: pumped, slapped, zapped or just pulled out). A little changes from one
    /// playthrough to the next (who it is, which gag from a short list, where they are), the story itself never does.
    /// Island 1 also has a false alarm: somebody screaming for help where the water is knee-deep.
    /// </summary>
    public partial class StoryDirector
    {
        private sealed class Gag
        {
            public string Id;
            public int Figure = -1;     // -1 either, 0 man, 1 woman
            public string Shouts;       // what they yell, '|' between lines
            public string Spotted;      // the guide's comment once they're out there ({name}, {he}, {him}, {his}, {himself})
            public string[] After;      // what they say once they're saved
        }

        private static readonly Gag[] Island1Gags =
        {
            new()
            {
                Id = "hotdogs", Figure = 0,
                Shouts = "CRAMP! CRAMP!|I ATE THREE HOT DOGS!|MOM WAS RIGHT!|SHE SAID WAIT 30 MINUTES!",
                Spotted = "{name} ate three hot dogs and ran straight into the sea! I SAW {him}!",
                After = new[] { "Thanks, lifeguard. ...Is the hot dog stand still open?" }
            },
            new()
            {
                Id = "influencer",
                Shouts = "CHAT, I'M DROWNING!|LIKE AND SUBSCRIBE!|THIS IS GREAT CONTENT!|DON'T FORGET TO FOLLOW!",
                Spotted = "Is {name} FILMING {himself} drowning?!",
                After = new[] { "Did you get my good side? ...Wait. Where's my PHONE?!" }
            },
            new()
            {
                Id = "flamingo", Figure = 1,
                Shouts = "PRINCESS POPPED!|MY FLAMINGO!|SAVE PRINCESS!|PRINCESS, NOOO!",
                Spotted = "{name}'s inflatable flamingo just popped! {He} LOVED that flamingo!",
                After = new[] { "Princess is gone, isn't she? ...I'm signing up for swimming lessons." }
            },
            new()
            {
                Id = "olympian", Figure = 0,
                Shouts = "I DON'T NEED HELP!|I SWAM IN THE OLYMPICS!|I'M FINE! I'M FINE!|OK, MAYBE A LITTLE HELP!",
                Spotted = "{name} tells everyone {he} swam in the Olympics. ...Go and get {him}.",
                After = new[] { "Not a word about this. To ANYONE." }
            },
            new()
            {
                Id = "waver",
                Shouts = "HI!|HELLO, BEACH!|HI THERE!|NO WAIT, HELP!",
                Spotted = "Is {name} waving or drowning? ...Drowning! Go, go, go!",
                After = new[] { "I WAS waving. And then I wasn't." }
            },
            new()
            {
                Id = "sunscreen", Figure = 1,
                Shouts = "TOO MUCH SUNSCREEN!|I'M SO SLIPPERY!|SPF 100!|I CAN'T HOLD ON TO ANYTHING!",
                Spotted = "{name} used a whole bottle of sunscreen. Don't let {him} slip out of your hands!",
                After = new[] { "SPF 100. I regret nothing. Look, not even pink!" }
            },
        };

        private static readonly Gag[] Island2Gags =
        {
            new()
            {
                Id = "complainer",
                Shouts = "DO YOU KNOW WHO I AM?!|I'M A GOLD MEMBER!|I PAID FOR A SEA VIEW, NOT THIS!|GET ME THE MANAGER!",
                Spotted = "That's {name}. {He} has complained about everything since check-in. Please don't let {him} drown.",
                After = new[] { "I'm leaving a one-star review. For the ocean." }
            },
            new()
            {
                Id = "mojito",
                Shouts = "SAVE THE MOJITO!|NOT A DROP SPILLED!|NOT THE DRINK!|HOLD MY DRINK! HOLD IT!",
                Spotted = "{name} swam out with a cocktail. Guests, please. No. Cocktails. In. The. Sea.",
                After = new[] { "The mojito made it. That's all that matters." }
            },
            new()
            {
                Id = "ring", Figure = 1,
                Shouts = "MY RING!|I DROPPED MY RING!|HE'S GOING TO KILL ME!|IT WAS HIS GRANDMA'S!",
                Spotted = "{name} dived for {his} wedding ring. On {his} honeymoon. Romantic. Stupid, but romantic.",
                After = new[] { "The ring's still down there, isn't it? ...Don't tell my husband." }
            },
            new()
            {
                Id = "snorkel", Figure = 0,
                Shouts = "MY SNORKEL'S FULL!|BLUB! BLUB!|WHO PUT WATER IN THE SNORKEL?!|A FISH LOOKED AT ME!",
                Spotted = "{name} is snorkelling... upside down. Lifeguard, please.",
                After = new[] { "I saw a fish. It looked at me like I was the stupid one." }
            },
            new()
            {
                Id = "influencer",
                Shouts = "CHAT, I'M DROWNING!|LIKE AND SUBSCRIBE!|THIS IS GREAT CONTENT!|DON'T FORGET TO FOLLOW!",
                Spotted = "Is {name} FILMING {himself} drowning?!",
                After = new[] { "Did you get my good side? ...Wait. Where's my PHONE?!" }
            },
        };

        // The first thing they say when they come round, by how they were brought back.
        private static readonly string[] PumpedLines =
        {
            "(cough) My ribs! ...You saved my life, didn't you?",
            "(cough) Was that CPR? ...I think I need another round.",
            "(cough) You push like a jackhammer. Thank you?"
        };
        private static readonly string[] SlappedLines =
        {
            "OW! Who SLAPPED me?!",
            "(cough) I think you knocked a tooth loose. Thanks, though!",
            "(cough) My face... my FACE... ...thank you."
        };
        private static readonly string[] ZappedLines =
        {
            "AAAH! I SAW A LIGHT! ...Oh. It's the sun.",
            "Is my hair standing up? It feels like my hair is standing up.",
            "I can taste colours! Is that normal?"
        };
        private static readonly string[] PulledOutLines =
        {
            "(cough) I swallowed half the sea!",
            "(cough) Thank you! THANK YOU! ...Which way is the beach?",
            "(cough) I'm alive! I'm ALIVE! ...Can I go back in?"
        };

        private sealed class FalseAlarmGag
        {
            public string Shouts;   // what they yell from the shallows, '|' between lines
            public string Arrive;   // when a lifeguard gets there
            public string You;      // the lifeguard
            public string Comeback; // them again
            public string Sandy;    // Sandy, from her hut
        }

        private static readonly FalseAlarmGag[] FalseAlarms =
        {
            new()
            {
                Shouts = "HELP! I'M DROWNING!|SOMEBODY! ANYBODY!|I CAN'T FEEL THE BOTTOM!",
                Arrive = "Help! I'm drown... (looks down) ...oh.",
                You = "You're standing up.",
                Comeback = "In my defence, it's VERY wet.",
                Sandy = "(shouting) Every summer! EVERY single summer!"
            },
            new()
            {
                Shouts = "SOMETHING'S GOT MY TOE!|IT'S EATING ME!|TELL MY MOTHER I LOVE HER!",
                Arrive = "It's got me! It's huge! It's...",
                You = "It's a crab. It's the size of a coin.",
                Comeback = "It's a very STRONG coin.",
                Sandy = "(shouting) Leave Gerald alone! That crab's been here longer than I have!"
            },
            new()
            {
                Shouts = "SOMETHING TOUCHED MY LEG!|IT'S GOT ME!|SHAAARK!",
                Arrive = "There's something in the water! It touched me! It's...",
                You = "Seaweed. That's seaweed.",
                Comeback = "Seaweed with INTENT.",
                Sandy = "(shouting) It's the sea, dear! Things live in it!"
            },
        };

        private readonly HashSet<string> _usedGags = new();

        /// <summary>A gag from the pool that suits a man (0), a woman (1) or either (-1), not one used already this run.</summary>
        private Gag PickGag(Gag[] pool, int figure)
        {
            var fits = new List<Gag>();
            foreach (Gag g in pool)
                if ((g.Figure < 0 || figure < 0 || g.Figure == figure) && !_usedGags.Contains(g.Id)) fits.Add(g);
            if (fits.Count == 0)
                foreach (Gag g in pool)
                    if (g.Figure < 0 || figure < 0 || g.Figure == figure) fits.Add(g);
            Gag pick = fits[Random.Range(0, fits.Count)];
            _usedGags.Add(pick.Id);
            return pick;
        }

        private static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];

        private static string Fill(string text, VictimBrain v)
        {
            bool f = v.IsFemale;
            return text.Replace("{name}", v.Name).Replace("{He}", f ? "She" : "He").Replace("{he}", f ? "she" : "he")
                .Replace("{himself}", f ? "herself" : "himself").Replace("{him}", f ? "her" : "him").Replace("{his}", f ? "her" : "his");
        }

        private static Color VoiceColor(VictimBrain v) => v.IsFemale ? new Color(1f, 0.8f, 0.9f) : new Color(0.8f, 0.9f, 1f);

        /// <summary>The guide's comment on somebody in trouble, once nobody else is talking (and only while they still are in trouble).</summary>
        private IEnumerator Comment(StoryNpc guide, string text, VictimBrain about)
        {
            yield return new WaitForSeconds(2.5f);
            float until = Time.time + 8f;
            while (DialogueService.Current.HasValue && Time.time < until) yield return null;
            if (about == null || !about.IsSpawned || !about.State.NeedsHelp()) yield break;
            yield return Say(guide, Fill(text, about));
        }

        /// <summary>What a saved tourist says: how it felt to be brought back, then their own punchline.</summary>
        private IEnumerator Reaction(VictimBrain v, Gag gag)
        {
            if (v == null) yield break;
            _howRescued.TryGetValue(v, out VictimEvent how);
            _howRescued.Remove(v);
            _heroOf.TryGetValue(v, out PlayerHub hero);
            _heroOf.Remove(v);
            string name = v.Name;
            Color color = VoiceColor(v);
            string[] first = how switch
            {
                VictimEvent.Revived => v.IsFemale ? PumpedLines : SlappedLines,
                VictimEvent.Zapped => ZappedLines,
                _ => PulledOutLines
            };
            // On island 1 (the hut is there), not the hotel's or Skull Cove's beach.
            bool onIsland1 = (v.transform.position - BeachPointFrom(v.transform.position, _island1)).sqrMagnitude < 120f * 120f;
            yield return new WaitForSeconds(1.2f);
            yield return Say(name, Pick(first), color);
            if (gag?.After is { Length: > 0 }) yield return Say(name, Pick(gag.After), color);
            // Brought back with CPR on island 1: she has other plans for her hero.
            if (how == VictimEvent.Revived && v != null && v.IsSpawned && v.IsFemale && onIsland1 && hero != null && LoveHut.Instance != null)
                yield return LoveHutScene(v, hero);
        }

        private static readonly string[] HutInvites =
        {
            "My hero... Come with me to the toilets. I want to thank you. PROPERLY.",
            "You saved my life. Come, come, I have to show you something. In the toilets.",
            "Those lips... I mean, that CPR! Quick, to the toilets!"
        };
        private static readonly string[] HutGoodbyes = { "Call me!", "Best. Rescue. EVER.", "Same time tomorrow? I'll drown at three." };

        /// <summary>
        /// The beach hut gag: she gets up, takes her hero by the hand and walks them into the hut. The door shuts, the
        /// hut wobbles and squeaks (nothing is shown), Sandy pretends she saw nothing, and out they come.
        /// </summary>
        private IEnumerator LoveHutScene(VictimBrain v, PlayerHub hero)
        {
            LoveHut hut = LoveHut.Instance;
            string name = v.Name;
            AvatarLook look = v.Look;
            Color voice = VoiceColor(v);
            Vector3 at = v.transform.position;
            // Up she gets a step away from her hero (who is kneeling right at her side): standing on top of them she
            // turned round and round trying to face them.
            Vector3 fromHero = at - hero.transform.position;
            fromHero.y = 0f;
            if (fromHero.magnitude < 1f)
                at = StoryNpc.OnNavMesh(hero.transform.position + (fromHero.sqrMagnitude > 1e-4f ? fromHero.normalized : -hero.transform.forward) * 1f, 1.5f);
            Vector3 door = hut.Outside.position;
            Vector3 face = door - at;
            face.y = 0f;
            Despawn(v.gameObject); // up she gets: the floppy tourist becomes a walking one
            StoryNpc her = SpawnNpc(name, NpcRole.Guest, look, at, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face).eulerAngles.y : 0f);
            her.ServerSetMood(AvatarMood.Happy);
            her.ServerFace(hero.transform.position);
            yield return Say(her, Pick(HutInvites));
            her.ServerSetTalkable(true, "Accept her invitation (optional)");
            float invitationEnds = Time.time + 12f;
            _talks.RemoveAll(t => t.npc == her);
            while (hero != null && her != null && Time.time < invitationEnds && !_talks.Exists(t => t.npc == her && t.by == hero))
                yield return null;
            bool accepted = hero != null && her != null && _talks.Exists(t => t.npc == her && t.by == hero);
            _talks.RemoveAll(t => t.npc == her);
            if (her != null) her.ServerSetTalkable(false);
            if (!accepted)
            {
                if (her != null) { her.ServerShout("Duty calls? Fair enough. Thanks, lifeguard!", false); StartCoroutine(RemoveLater(her, 12f)); }
                yield break;
            }
            Debug.Log($"[Story] {name} leads {hero.DisplayName} to the toilets");

            // Hand in hand after the rescued tourist's invitation is accepted.
            hut.ServerLead(hero, her);
            her.ServerFace(null);
            her.ServerWalkWith(hero.GetComponent<Rigidbody>());
            yield return WalkToHut(her, door, hut.Outside.eulerAngles.y);
            if (her != null) her.ServerWalkWith(null);
            if (her == null || hero == null)
            {
                if (hero != null) hut.ServerLead(hero, null);
                yield break;
            }
            her.ServerStop();
            hut.ServerDoor(true);
            yield return new WaitForSeconds(0.9f);
            hut.ServerLead(hero, null);
            her.ServerTeleport(hut.Inside.position, hut.Inside.eulerAngles.y, keepExact: true);
            hut.ServerPut(hero, hut.Inside.position + hut.Inside.right * 0.6f, hut.Inside.position + Vector3.up * 1.4f);
            hut.ServerDark(hero, true);
            yield return new WaitForSeconds(0.7f);
            hut.ServerDoor(false);

            // Meanwhile, outside...
            const float seconds = 10f;
            hut.ServerRockAndRoll(seconds);
            yield return new WaitForSeconds(3.5f);
            StartCoroutine(Say(_sandy, "(shouting) I didn't see anything! I didn't see ANYTHING!"));
            yield return new WaitForSeconds(seconds - 3.5f);

            // Out they come.
            hut.ServerDoor(true);
            yield return new WaitForSeconds(0.6f);
            Vector3 front = hut.Outside.position;
            Vector3 away = -hut.Outside.forward;
            if (hero != null)
            {
                hut.ServerPut(hero, front + hut.Outside.right * 0.7f, front + away * 4f + Vector3.up * 1.5f);
                hut.ServerDark(hero, false);
            }
            if (her != null)
            {
                her.ServerTeleport(front - hut.Outside.right * 0.6f, Quaternion.LookRotation(away).eulerAngles.y);
                yield return new WaitForSeconds(0.8f);
                yield return Say(name, Pick(HutGoodbyes), voice);
                if (Economy.Instance != null) Economy.Instance.ServerAdd(25, "a tip from " + name, her.transform.position);
                her.ServerMoveTo(front - hut.Outside.right * 18f); // off along the beach, humming (not into the sea)
                StartCoroutine(RemoveLater(her, 25f));
            }
            yield return new WaitForSeconds(1f);
            hut.ServerDoor(false);
        }

        /// <summary>
        /// To the hut door, whatever is in the way: a route that ends early is set off again, and if she still makes
        /// no headway she is put at the door (the hand-held lifeguard is pulled after her). The lifeguard can't move
        /// meanwhile, so this never takes long.
        /// </summary>
        private IEnumerator WalkToHut(StoryNpc her, Vector3 door, float doorYaw)
        {
            const float arrive = 1.6f, headway = 0.3f, patience = 4f, limit = 20f;
            float giveUp = Time.time + limit;
            float best = float.MaxValue, bestAt = Time.time;
            int retries = 0;
            her.ServerMoveTo(door, 1.7f, water: true); // revived in the shallows: the first steps are through water
            while (her != null)
            {
                Vector3 to = door - her.transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance <= arrive) yield break;
                if (distance < best - headway)
                {
                    best = distance;
                    bestAt = Time.time;
                }
                string why = Time.time > giveUp ? "took too long"
                    : Time.time - bestAt > patience ? "no headway"
                    : !her.IsMoving && retries >= 2 ? "no way through"
                    : null;
                if (why != null)
                {
                    Debug.Log($"[Story] {her.Name} put at the hut door ({why}, {distance:F1} m away)");
                    her.ServerTeleport(door, doorYaw);
                    yield break;
                }
                if (!her.IsMoving)
                {
                    retries++;
                    her.ServerMoveTo(door, 1.7f, water: true);
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        // ------------------------------------------------------------------ island 1: the false alarm

        private IEnumerator FalseAlarm()
        {
            _island = _island1;
            FalseAlarmGag gag = FalseAlarms[Random.Range(0, FalseAlarms.Length)];
            // Somebody who was wading in the shallows (lent by the crowd), or a stranger standing there if nobody is.
            Vector3 shallows = ShallowSpot(_island1);
            BeachCrowd crowd = BeachCrowd.Nearest(shallows);
            StoryNpc panicker = crowd != null ? crowd.Borrow(BeachCrowd.Activity.Wade, shallows, 120f) : null;
            BeachCrowd lender = panicker != null ? crowd : null;
            if (panicker == null)
                panicker = SpawnNpc(VictimBrain.RandomName(false), NpcRole.Bystander, GuestLook(Random.Range(1, 99999), 0), shallows, 0f);
            panicker.ServerStop();
            if (Shore.WaterDepthAt(panicker.transform.position + Vector3.up * 2f) < 0.25f)
            {
                // Lent while on dry sand: wade in first, the joke needs the water.
                panicker.ServerMoveTo(shallows, 2.2f, water: true);
                float until = Time.time + 15f;
                while (panicker.IsMoving && Time.time < until) yield return new WaitForSeconds(0.3f);
                panicker.ServerStop();
            }
            Vector3 beach =BeachPointFrom(panicker.transform.position, _island1) + new Vector3(_island1.Shoreward.x, 0f, _island1.Shoreward.z).normalized * 6f;
            panicker.ServerFace(beach);
            panicker.ServerSetPose(AvatarPose.Scared);
            panicker.ServerSetMood(AvatarMood.Scared);
            if (RescueService.Instance != null)
            {
                RescueService.Instance.ServerRingBell("story");
                RescueService.Instance.ServerAnnounce($"<color=#ffd060><b>HELP!</b></color> {panicker.Name} is screaming in the shallows!");
            }
            SetObjective($"{panicker.Name} is screaming for help in the shallows!");
            Marker("HELP!", panicker.NetworkObject);
            Debug.Log($"[Story] false alarm: {panicker.Name} at {panicker.transform.position:F1}");

            string[] shouts = gag.Shouts.Split('|');
            int n = 0;
            float nextShout = 0f;
            _talks.Clear();
            panicker.ServerSetTalkable(true, $"Calm {panicker.Name} down");
            float giveUp = Time.time + 75f; // nobody came: they calm down by themselves
            while (StoryNpc.NearestPlayer(panicker.transform.position, 3.5f) == null && !_talks.Exists(t => t.npc == panicker) && Time.time < giveUp)
            {
                if (Time.time >= nextShout)
                {
                    nextShout = Time.time + 2.4f;
                    panicker.ServerShout(shouts[n++ % shouts.Length], true);
                    panicker.ServerGesture(AvatarGesture.Wave, beach);
                }
                yield return new WaitForSeconds(0.2f);
            }
            panicker.ServerSetTalkable(false);
            _talks.Clear();
            NoMarker();
            SetObjective("Keep the tourists alive out there");
            if (Time.time >= giveUp)
            {
                panicker.ServerSetPose(AvatarPose.Normal);
                panicker.ServerSetMood(AvatarMood.Neutral);
                if (lender != null) lender.Return(panicker);
                else Remove(panicker);
                yield break;
            }
            PlayerHub lifeguard = StoryNpc.NearestPlayer(panicker.transform.position, 60f);
            if (lifeguard != null) panicker.ServerFace(lifeguard.transform.position);
            yield return Say(panicker, gag.Arrive);
            panicker.ServerSetPose(AvatarPose.Normal);
            panicker.ServerSetMood(AvatarMood.Neutral);
            yield return You(gag.You);
            yield return Say(panicker, gag.Comeback);
            yield return Say(_sandy, gag.Sandy);
            // Off they go, as if nothing happened.
            if (lender != null) lender.Return(panicker);
            else
            {
                panicker.ServerMoveTo(beach);
                StartCoroutine(RemoveLater(panicker, 20f));
            }
        }

        /// <summary>A spot in front of the beach where the water is about knee-deep.</summary>
        private static Vector3 ShallowSpot(IslandSetup island)
        {
            var sea = new Vector3((island.SeaX.x + island.SeaX.y) * 0.5f + Random.Range(-8f, 8f), 0f, (island.SeaZ.x + island.SeaZ.y) * 0.5f);
            Vector3 seaward = -new Vector3(island.Shoreward.x, 0f, island.Shoreward.z).normalized;
            Vector3 p = BeachPointFrom(sea, island);
            for (int i = 0; i < 30 && Shore.WaterDepthAt(p + Vector3.up * 2f) < 0.55f; i++) p += seaward * 0.4f;
            float ground = Shore.GroundHeightAt(p + Vector3.up * 5f);
            if (!float.IsNaN(ground)) p.y = ground;
            return p;
        }
    }
}
