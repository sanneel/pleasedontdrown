# PLEASE DON'T DROWN: the dev island

> 2026-09-28. A sandbox ~110 m west of island 1 (centre -230, -60) to try every feature without playing the story.
> Code: `Assets/_Game/Dev/` (DevIsland, TargetDummy, DevButton, TeleportPad); built by `Editor/GameSceneBuilder.DevIsland.cs`.

## Getting there
* The purple **teleport pad** on island 1, west of the spawn (sign "TO DEV ISLAND"), E to use. The pad next to the
  arrival spot on the dev island goes back.
* Console: `devisland` and `home`. The console opens with **`**, **F1** or **F2**; it works on a Georgian keyboard
  layout too (letters are typed as the Latin letter on the same key).
* Or ride over: the dev dock has a jet ski that needs no key.

## What's there
| Place | What |
|---|---|
| Gun table (firing line) | Pistol, SMG, shotgun, rifle, sniper. Taken ones come back after a few seconds. |
| Armory counter | Every gun free; holding a gun, every part free (sights, scopes, suppressor, compensator, laser, magazine, rounds). |
| Shooting range (west) | Plywood targets at 10, 25, 50 m, one sliding at 30 m, and 100 / 150 m on platforms in the sea. 100 HP each: damage numbers per hit, they fall over and pop back up. |
| Items row | Every item (coconut, life ring, crate, cooler, beach ball, defibrillator, lost things, baggie, jet ski keys), restocked. |
| Model gallery (south) | Every generated character on a pedestal: Sandy, Sandy (boss), the four Meshy tourists, and the code-built lifeguard for comparison. New Meshy characters get added here. |
| Rescue tests (east beach) | Buttons: drowning woman / man, silent drowning, unconscious woman / man on the sand (CPR), no pulse (defibrillator), shark, robber, +$1000, clear the tourists. |

Console for tests: `devtest <DrowningWoman|DrowningMan|SilentWoman|CprWoman|CprMan|Flatline|Robber|Shark|Money|ClearTourists>`.

## Checked 2026-09-28
Headless: gallery set up (7 characters), all 17 shelf items stocked, pistol drops the 10 m target in 3 hits,
every button works (victims spawn, the shark bites, the robber runs). Windowed: pad on island 1, sea round the island,
the Meshy models on their pedestals, a drowning and an unconscious Meshy tourist, the range through the rifle scope.
