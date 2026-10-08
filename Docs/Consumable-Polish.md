# Consumable and swoosh polish

Beer and coconut are the current consumables. The beer retains the DROWN Lager beach branding, with a smoother amber bottle, blue label trim, a gold open lip and small condensation drops. Its existing size, grip and drinking lip position are retained. The coconut has a fibrous shell and an open white flesh bowl; its existing gameplay collider is retained.

`ActionFoley` generates three original variations per action: a crisp bite with a quiet chewing tail, several liquid bubbles with a soft swallow, and layered air movement for punches, throws and knife swings. Clips last 0.20–0.38 seconds and use the shared DC removal, edge fades and peak headroom. Each action cycles its variants. Eating and drinking use the existing bite timing and spatial audio source. The holder hears an immediate local sound; the observer callback skips that holder to avoid doubling it when item ownership belongs to the server. Bite requests validate the current holder.

Regenerate just these models with `model_props.py -- <output directory> --only beer_bottle,coconut`. Unity's `GameSceneBuilder.PolishConsumablesBatch` updates the prefabs and scene copies, captures the actual Unity materials and exports audio samples. `BuildConsumablePolishBatch` also builds the saved game. Review outputs are under `Screenshots/Review/Consumables` and `Screenshots/Review/Audio` (local, ignored by Git).

Verification: inspected the Unity item render; all fifteen effect variations passed finite-sample, non-silence, peak and edge checks. Hearing the final balance through gameplay speakers and multiplayer timing remain listening checks.
