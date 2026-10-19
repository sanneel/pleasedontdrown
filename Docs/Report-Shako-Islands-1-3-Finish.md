# Report: islands 1-3 finished off (Shako's Claude, 2026-10-19)

Asked for by Shako: finish what islands 1-3 still lack, use Tripo mainly on island 2, leave island 4 for now, and pull
what is new on GitHub first. Pictures: `Docs/Island-2-Resort-2026-10-19/`. Island 2's list and its state:
`Docs/19-Island-2-What-It-Lacks.md` ("Status 2026-10-19").

Commits on `shako-dev`: `1b8f72c` (merge of sancho-dev: the FK rival body, the claw, police, clips), `1b0a6e6`,
`bf765fc`, `4961c43`, `a4a46bc`.

## What was added

**Island 2 (the Grand Coral)**: one batch, `GameSceneBuilder.Island2ResortBatch`, builds it all under
`Environment/Island2Resort` (run again any time, it replaces its own objects):

- Four over-water bungalows on stilts off the south shore, on a railed boardwalk (piles down to the seabed).
- Six cabana daybeds on the south beach.
- The drivable golf cart wears a Tripo golf cart (the greybox is hidden); a second cart is parked in the pool's bay.
- A helipad with the resort helicopter on the lawn (the hotel's roofs are railed terraces: no room up there).
- A second motor yacht moored on the marina pier's other side.
- A tennis court on the lawn inside the cart road.
- A swim-up bar at the pool's east end: a counter in the water with stools under it and a ledge to stand on, a Tripo
  tiki back bar with bottles under a Tripo thatched palapa (to look at and swim to; no orders).
- **Bellhop Benny** at the hotel door: a new Tripo body (30, a young man in the hotel's teal and gold), faces and
  blinking like the others.

**Island 3 (Skull Cove)**: fishing nets drying on two racks and a rope fence along the east beach
(`DressPirateBeach` in `GameSceneBuilder.Island3Dress.cs`).

**Island 1**: nothing added (its one open item, the wide tower, is under "Not done").

## Tripo

10 models, all kept first try, **300 credits** (390 left): overwater_bungalow, beach_cabana, golf_cart,
resort_helicopter, motor_yacht, hotel_bellhop, fishing_net_rack, rope_fence, swimup_palapa, tiki_back_bar. Prompts:
`.claude/skills/pdd-tripo/prompts/island2-resort.md`; raw and decoded files in `ArtSource/Tripo/Island2/`.

## A fix in Sandro's file (please look)

`Avatar/AvatarClips.cs`: the humanoid avatar's skeleton root now sits at zero. Before, an NPC standing on a raised
floor (the hotel porch, 1.8 m up) had their body drawn 1.8 m under their feet: Bellhop Benny was only a floating
name. ClipCheck, KissCheck and GunHoldCheck: ALL PASS after it.

## Tests

- Story autoplay 1.1 → 3.3 in the dev build: **PASS**, 0 errors (one warning: the jet ski was boarded directly).
- Checked live: the bungalows stand in the sea, both yachts float, the Tripo golf cart drives, the helipad, Benny at
  the door, the swim-up bar (swim right up to it).
- `DevPlayerBuild.Build`: OK.

## Not done

- The waitress carrying a tray: needs an NPC arm pose (the animation code is Sandro's).
- Island 1's wide lifeguard tower: its door logic is risky to change; left as is.
- Mini-golf (the tennis court went there instead).
- Island 4: paused, as asked.

## For Sandro

- `Game.unity` changed (island 2's new objects, island 3's beach). Rebuild with `Island2ResortBatch` and
  `Island3DressBatch` after a scene merge rather than merging the scene by hand.
