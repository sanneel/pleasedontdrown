# Tourists: Tripo prompt list

Replaces the Meshy tourists (painted faces too rough to clean). Target 20 women + 20 men: 8 + 8 new models
(25 credits each: Ultra Mesh off, 2K texture, PBR off, Remove Lighting on, Triangle, polycount 30000, H3.1), the
rest re-textures of those (Texture tool, 2K, 10 credits). Approved budget 2026-10-09: ~800 credits.

Files: raw `ArtSource/Tripo/Tourists/raw/<asset_name>_meshopt.glb`, decoded `ArtSource/Tripo/Tourists/<asset_name>.glb`,
then `ArtSource/Tools/prepare_character.py` (it rigs them itself: needs a clean A-pose, arms away from the body).

Every prompt starts with the shared style block, then the character line.

**Style block (all):** Stylised cartoon 3D game character, full body, one person alone, standing straight in an
A-pose: arms straight down and held 30 degrees away from the body, hands open with fingers apart, legs slightly
apart, facing the camera. Adult body proportions like the grown-ups in a modern animated film: normal-sized head
about one seventh of the body height (not chibi, not a big head), long legs, smooth clean surfaces, bright flat
colours. (Try 1 with "Pixar-like proportions with a slightly large head" came out chibi: clean face, head a third
of the height. Rejected: no big heads.) Clean face: smooth even skin, large clearly painted cartoon eyes with white sclera and round irises, neat
eyebrows, small nose, closed smiling mouth. Barefoot, nothing in the hands, no sunglasses, no hat, no bag, no base.

| # | asset_name | Character |
|---|---|---|
| T1 | girl_01_brunette_red | Young woman beach tourist, slim, long wavy brown hair, simple red triangle bikini, light tan skin. |
