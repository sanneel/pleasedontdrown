# Revive and hut escort review

Reviewed Claude commit `9f6c224` and applied its three targeted changes locally, preserving the existing optional invitation. Kept righting strength 170 and torso height target 1.24. Added the same upright blend to the leg stepping drive: the original patch gated forward motion but still cycled the leg joints while getting up.

## Verified

- `RevivePhysicsReview.RunBatch` simulates the actual tourist Rigidbody, limbs and joint/controller methods on flat ground. Four generated tourist looks, each starting sideways and upside down, stood upright in 0.40–0.74 seconds. Final torso up was 1.000 and walking speed about 1.33 m/s. Coupled torso/limb gravity settled the chest at 1.177 m despite the 1.24 spring target; these values did not produce a runaway lift or unstable lean.
- The real player prefab collider blocks the NPC's normal sweep, is ignored when registered as the escort partner, and a solid wall remains blocking.
- The offline host uses the real `WalkToHut` coroutine and player follow: blocked motion falls back at about 4.05 seconds; repeated early route endings fall back after two retries at about 0.76 seconds; slow continuing progress reaches the total deadline at about 20.15 seconds. The player followed to within 1.01–1.10 m. Timers are checked periodically, so the stated 4/20 seconds include a small scheduling margin.
- The shallow-water case walked to the door's arrival radius in 12.61 seconds, without using the teleport fallback; the following player remained 1.39 m behind the leader.

Run the offline escort cases with `-pdd-host-offline -pdd-nostory -pdd-noinput -pdd-hut-review`. The last case starts in 0.34 m of water on the actual island. Logs: `Logs/revive-physics-review.log`, `Logs/hut-runtime-review.log`. The saved-scene Windows build is in `Builds/Win64`.

These are focused controller, collision and escort checks, not a complete manual playthrough of every CPR/defibrillator animation, terrain slope or multiplayer connection. How stiff the get-up feels remains a visual gameplay judgement.
