# Island 2: what an expensive resort island still lacks (B14)

> For the team to read and pick from **before anything is built** (Docs/18, B14). Written 2026-10-12 after B8-B13.
> Tick or cross items and say which come first; nothing here is being built yet.

## What island 2 has now
The Grand Coral hotel (lobby, reception shop, infirmary, casino with slots/blackjack/roulette), the Coral Bar, the
Coral Arms gun shop, a sunken pool with loungers, a golf cart road (U round the back, two parking bays, no cart yet),
the dock, the north beach with towels and parasols. Island size 180 x 110 m.

## What it lacks (most noticeable first)

| # | Missing | Why it matters | Size of job |
|---|---|---|---|
| 1 | **Trees and plants.** No palms, hedges or flowers anywhere (the 2026-10-09 decor cleanup took them all) | The island reads as a sand plate with buildings on it; nothing says "tropical luxury" | Medium: palms along the road and the beach, flower beds by the hotel front, hedges round the pool |
| 2 | **Night lighting.** No lamp posts, path lights or lit windows | The gala happens at night; right now it would be dark sand | Small: lamp posts along the cart road, uplights on the hotel, pool lights |
| 3 | **A grand arrival.** The dock is a plain plank walkway; no forecourt fountain, no welcome sign, no valet | First impression of the "expensive" island | Medium: stone pier with lamps, a fountain on the forecourt, a "GRAND CORAL" arch |
| 4 | **People who work there.** Only the receptionist and a barista | A resort full of staff feels expensive: bellboys, waiters with trays, a pool attendant, a valet, a security guard | Needs A (NPC behaviour) + Tripo characters (C12) |
| 5 | **Guests using the place.** Nobody swims in the pool, sits on the loungers, plays the slots | Empty luxury looks abandoned | Needs A (crowd behaviour); scene work: seats/slots marked as spots |
| 6 | **Marina with yachts.** The yacht to island 3 has no berth; the pirate boat stands in | The story's yacht keys need a yacht and a place it lives | Medium: a second pier with 2-3 moored yachts (Tripo yacht exists in `ArtSource/Tripo/island3/`) |
| 7 | **Over-water bungalows / villas** | The classic picture of an expensive island | Large: 4-6 bungalows on stilts off the east or south shore, walkway out |
| 8 | **Spa / beach cabanas** | Cabanas with curtains by the beach; a spa hut | Small-medium (the old resort cabana model `resort_cabana.glb` still exists) |
| 9 | **Sport: tennis court or mini-golf** | Golf carts with no golf; a putting green or mini-golf behind the hotel would explain them | Small-medium |
| 10 | **Helipad on the hotel roof** | Cheap, very "rich", visible from far | Small |
| 11 | **Cushion-ride dock** (story: inflatable cushion towed by a jet ski) | The activity is planned but has no start point; model `inflatable_cushion.glb` is ready | Small: a little jetty with the cushion + a sign |
| 12 | **Swim-up bar / pool bar** | The pool is sunken (water 1.85 m below the deck); a pool bar with steps would make it lively | Medium |
| 13 | **Wayfinding signs** | Gun shop, casino, bar, pool, carts: small gold-on-teal signposts at the road | Small |
| 14 | **Gala decorations** (for the gala night) | Champagne tower and chandeliers are in the casino; outside: red carpet, string lights, fireworks | Small-medium, only switched on for the gala |

## Suggested order (if the team agrees)
1, 2, 13 (cheap, change the look the most) → 11, 6 (needed by the story) → 3, 8, 10 → the rest.
Items 4 and 5 need part A first.

## Status 2026-10-19 (Shako's Claude)

| # | State |
|---|---|
| 1, 2, 3, 11, 13, 14 | Done in the overnight island 2 work (palms, lamps, the arch and fountain, the cushion ride, signs, the gala) |
| 4 | Partly: Marisol, the attendant, the waitress, and now **Bellhop Benny** at the door (Tripo body 30, a man in the hotel's teal and gold) |
| 5 | Partly: slot players and lounger guests (overnight) |
| 6 | Done: the marina with the hotel's yacht, and now a **second motor yacht** moored on the pier's other side |
| 7 | **Done: four over-water bungalows** on stilts off the south shore, on a railed boardwalk (`GameSceneBuilder.Island2ResortBatch`) |
| 8 | **Done: six cabana daybeds** on the south beach |
| 9 | **Done: a tennis court** on the lawn inside the cart road (no mini-golf) |
| 10 | **Done, on the lawn instead of the roof**: a helipad with the resort helicopter (the hotel's roofs are railed terraces) |
| 12 | Not done: the swim-up pool bar |
| A9 | The drivable golf cart wears the **Tripo golf cart**; a second one is parked in the pool's bay |

Pictures: `Docs/Island-2-Resort-2026-10-19/`. Tripo list: `.claude/skills/pdd-tripo/prompts/island2-resort.md`.
