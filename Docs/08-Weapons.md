# PLEASE DON'T DROWN: guns

> 2026-09-28. Five guns with the handling of How to Fish's guns (feature parity, our own code: nothing copied from
> its decompiled source, models or sounds). Code: `Assets/_Game/Combat/Weapons/`, `UI/WeaponHud.cs`; models are
> greybox, built by `Editor/GameSceneBuilder.Weapons.cs`.

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
