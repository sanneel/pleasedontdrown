# PLEASE DON'T DROWN: guns

> 2026-09-28. Five guns with the handling of How to Fish's guns (feature parity, our own code: nothing copied from
> its decompiled source, models or sounds). Code: `Assets/_Game/Combat/Weapons/`, `UI/WeaponHud.cs`; models are
> fitted GLB bodies with baked surface details, assembled by `Editor/GameSceneBuilder.Weapons.cs`.

## Visual detail pass (1 October 2026)

All five gun prefabs include an additional finish pass:

* Pistol: front slide serrations, diamond grip checks, frame pins and small steel highlights.
* SMG: receiver panels, vents, grip grooves, selector mark and ribbed magazines.
* Shotgun: receiver fasteners, restrained walnut grain, stock checks and metal collars on the moving pump.
* Rifle: grip checks, curved magazine flutes, receiver fasteners and a selector mark.
* Sniper: receiver plates, fore-end vents, grip grooves and rings around its integrated scope.
* Attachments: suppressor collars and dark muzzle recesses, brake ports, scope rings and turret marks,
  reflex-sight fittings, magazine floorplates and witness marks.

`Editor/WeaponArtPolish.cs` samples the actual imported body surfaces to fit the small details. Geometry is
combined by material and moving part into assets in `Art/Weapons/Details`. Pump and magazine details are
children of their original moving/swappable parts. There are no new runtime scripts or colliders.

**PLEASE DON'T DROWN > Polish gun prefabs** updates only the five existing gun prefabs. Full scene rebuilds
also include this pass. The focused command checks that the weapon and item settings and collider counts
remain unchanged. Batch entry: `PleaseDontDrown.Editor.WeaponArtPolish.ApplyBatch`.
Review renders are written to `Screenshots/GunPolish/After` (both sides and first person).

## Smooth finish and grip correction (2 October 2026)

`WeaponSurfaceFinish` bakes smooth normals across coincident imported vertices while keeping sharp
edges above 65 degrees. The pistol, rifle and sniper use a consistent satin material instead of the
source model's triangle-by-triangle paint tones. Original GLBs are preserved; the fitted copies live
in `Art/Weapons/Surfaces`. Each bake starts from the original import, so rerunning it is repeatable.

The sniper's irregular integrated scope is replaced with a round optic, two receiver mounts, inset
lenses, adjustment knobs and ridged rings. Added receiver ports, frame pins, stock grooves and small
selector markings complement the earlier details. Colored skins preserve this contrasting hardware.

First-person hands use the existing rounded hand builder with smooth shading and slimmer fingers.
The pistol palm sits lower and its thumb curl is reduced to clear the back of the slide. Other item
grips and third-person hand geometry retain their existing settings.
The long guns also use a more open right thumb so its tip stays outside the receiver.

Run `PleaseDontDrown.Editor.WeaponSurfaceFinish.BuildBatch` to update the five prefabs, render the
review views and build the saved scene. `skin <index>` previews a finish on the locally held item
without changing its saved preference; index 5 is Bubblegum.

Verified in the Windows player: all five guns equipped, aimed, inspected, fired and reloaded, with
matching host/client ammo counts. The Bubblegum pistol was checked at hip, aim and inspect poses.
After the final thumb adjustment, all four long guns were checked again during inspection and after
reloading; the visible thumb intersections were removed. Captures are in
`Builds/Win64/Screenshots/GunGrips` (`Smooth_Pistol_pink*` and `Final_*`). Logs:
`Logs/weapon-thumb-final-build.log`, `Logs/weapon-smooth-runtime.log`, `Logs/weapon-thumb-runtime.log`.

## Support thumb correction (2 October 2026)

The support thumb previously curled upright beside the barrel, exaggerating its visible length.
`HandPose.ThumbSwing` now rotates it at the base to sit along the fore-end. The four two-handed
gun grips use a 65-degree swing; the value blends with the rest of the pose during animation.
Thumb geometry and other item poses retain their existing dimensions and defaults.

Verified in the visible Windows player on the shotgun, SMG, rifle and sniper, including shotgun
inspection and return from firing/reloading. Build: `Logs/weapon-thumb-build.log`; runtime:
`Logs/weapon-support-thumb-runtime.log`. Final-frame captures: `Builds/Win64/Screenshots/GunGrips/Thumb_*.png`.

## Non-glowing weapon finishes (2 October 2026)

All opaque gun materials now use `PleaseDontDrown/Weapon`. Its soft directional shading stays within
0..1 linear brightness, with no emission, HDR reflection probes or unbounded specular peaks. The
factory finish, colored skins, metal details and inactive attachments use this same treatment.
World lighting and bloom settings are unchanged. Chrome and gold retain their colors with subdued
highlights. Non-weapon items retain their original skin shader.

`WeaponSurfaceFinish.BuildBatch` checks all opaque material slots before building. Validated 228 slots
across the five prefabs. Runtime checks cycled all nine skins on all five guns, looked toward the
ground and sky, and fired/reloaded each gun. Factory, chrome and Bubblegum were also checked in the
visible player window on every gun: 15 final-frame captures confirmed readable guns without glow.
Evidence: `Logs/weapon-noglow-build.log`, `Logs/weapon-noglow-runtime.log`, and
`Logs/weapon-noglow-display.log`; screenshots are `Builds/Win64/Screenshots/GunGrips/Display_*.png`.

For visual approval, use `frameshot <name>` in a visible game window. It waits until the end of the
frame and captures the displayed image, including post-processing and HUD. Hidden-window frame
captures can be black. `viewshot` submits an HDR camera render request for diagnostics, excludes
the screen-overlay HUD, and its off-screen color output is not a substitute for displayed frames.

## Hand fitting (1 October 2026)

Each gun now has its own right palm position, grip angle, trigger-finger bend and support-hand pose.
The index finger has separate joint bends and a lift angle; its knuckle stays attached to the palm.
These extra pose values blend through reloads and other gestures and default to zero on existing non-gun items.
The shotgun's support grip is parented to the pump so the hand follows the firing cycle; reload waypoints
compensate for the pump's movement to avoid applying the stroke twice.

`Editor/WeaponGripAuthoring.cs` contains the fitted values. They are applied by the scene builder after the
gun's hold position is calculated, keeping the existing framing. `WeaponGripAuthoring.SaveBatch` updates
only the five gun prefabs and renders hand close-ups into `Screenshots/GunGrips/After`.
`WeaponGripReview.BuildSavedGameBatch` builds the saved level without regenerating it.

For live checks, `viewshot <name>` renders the running player's camera to
`Builds/Win64/Screenshots/GunGrips/<name>.png`, including the live hands and weapon motion.
It works while the window is hidden; screen-overlay HUD elements are excluded.

Verified in the Windows development build on 1 October: all five guns were equipped, aimed,
inspected, fired once and reloaded. Ammo changed 10/9/10 (pistol), 30/29/30 (SMG and rifle),
2/1/2 (shotgun), and 5/4/5 (sniper), with client and host counts matching. The run saved 30
live camera captures covering hip, aim, inspect, fire, reload and ready states. The build
and runtime logs are `Logs/grip-final-build.log` and `Logs/grip-runtime-final.log`.
The sniper's screen-overlay scope mask is not included in these camera-only captures.

## Controls
| Key | What |
|---|---|
| LMB | Shoot (hold for the SMG and rifle) |
| RMB | Aim down the sights (zoom; scopes fill the screen) |
| R | Reload (also automatic when the magazine is empty) |
| F | Inspect: turn the gun over, card with damage, magazine, fire rate and parts |
| hold G | Throw the gun, tap G to drop it, 1-4 / wheel to switch |

## The guns
| Gun | Fire | Damage | Magazine | Notes |
|---|---|---|---|---|
| Pistol ($250) | semi, 0.16 s | 34 | 12 / 18 | 3 hits for a pirate (100 HP) |
| SMG ($600) | full auto, 800/min | 16 | 30 / 45 | sprays from the hip |
| Shotgun ($700) | pump, 0.8 s | 8 × 14 | 6 / 9 | knocks you back, pellets fly 60 m |
| Assault Rifle ($900) | full auto, 600/min | 25 | 30 / 40 | accurate aimed |
| Sniper Rifle ($1200) | bolt, 1.2 s | 100 | 5 / 8 | 8x scope, bullets drop over distance |

Health is on a 100 scale now: a punch does 25 (robber 75 / 125, pirates and the shark 100).

## Parts (bought at reception while holding the gun)
Sights (iron, red dot, 2x/4x/8x scopes), barrel (suppressor: quiet, small flash, a little less recoil; compensator /
choke / muzzle brake: much less climb), laser sight (beam and dot, tighter hip fire), extended magazine, and two
bullet upgrades per gun (more damage). Every part is already modelled on the gun and switched on when fitted; the
choice is synced as a few bytes.

## How it works
* **Holding.** Guns are rigid in view (`Item._rigidInHand`): no physics lag. The gun's pose (`Weapon : IHoldPose`)
  adds everything: aim (the sight's eye point lines up with the screen centre), sprint pose, raise when taken out,
  reload tilt, inspect turn, pulling back from walls, mouse sway, and recoil springs.
* **Recoil.** The view climbs per shot and stays (like How to Fish); the gun kicks back/up/twists on a
  spring-damper and settles (stiffer when aiming). Barrel parts scale both.
* **Shots are projectiles** (`ProjectileSystem`): plain data stepped at the physics rate with sphere casts, gravity,
  water splash, max range per gun; drawn as instanced tracers that start at the muzzle and blend onto the true path
  (bullets leave from the eye, so they go where the crosshair is). Impacts: dust/splinter puffs by surface, bullet
  holes on static things, thud; muzzle flash + light, ejected casings/shells, slide/pump/bolt movement.
* **Network.** The shooter fires instantly and sends (tick, origin, direction, seed, spread) to the host. The host
  checks holder, fire rate and its own ammo count, then passes the shot to everyone, who fly their own copy (same
  seed = same pellets) and fast-forward it by the tick difference. Only the shooter's copy reports hits; the host
  checks them (hit budget per shot, range) and applies damage (`IDamageable`). Hitting players knocks them back;
  loose things and people lying about get pushed.
* **Ammo** is the host's number (synced); the holder predicts it. Reload refills at 85% of the motion.

## Tests
Console: `spawn Pistol|SMG|Shotgun|Rifle|Sniper`, `grab <name>`, `fire [n]`, `aim 0|1`, `reload`, `inspect`, `gun`
(state), `fit <part>` (host, free), `shotlog 1` (where every bullet ends), `aimat <npc>`, `robber`.
Checked 2026-09-28: pistol drops the robber in 3 hits (host), client SMG hits confirmed by the host, ammo in sync
(12 → 9 → reload → 12), shotgun 8 pellets, sights line up, scopes, red dot, laser, reload/inspect poses.

## Meshy prompts for the real models (later)
Same style rules as 06/07 (no text, no logos). Side view, pointing right; Claude re-points them forward, splits
the moving part (slide / pump / bolt) and places the hand grips, eye points and muzzles.
```
Chunky cartoon toy-like pistol, dark grey slide and black grip, simple shapes, side view pointing right. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. No text, no logos.
```
```
Chunky cartoon compact submachine gun, black metal body, short barrel, straight magazine in front of the pistol grip, folding wire stock, side view pointing right. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. No text, no logos.
```
```
Chunky cartoon pump-action shotgun, dark metal barrel and receiver, warm brown wooden stock and pump grip, side view pointing right. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. No text, no logos.
```
```
Chunky cartoon assault rifle, sand-tan body with black handguard and curved magazine, carry rail on top, side view pointing right. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. No text, no logos.
```
```
Chunky cartoon bolt-action sniper rifle, long dark barrel, brown wooden stock, big scope on top, side view pointing right. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. No text, no logos.
```
Parts (one model each): a small red dot reflex sight; a rifle scope; a cylindrical black suppressor; a stubby
compensator with side vents; a small black tactical laser module; a long straight magazine; a long curved magazine.
