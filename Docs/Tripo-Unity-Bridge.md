# Tripo Unity Bridge

Installed official Tripo Bridge 1.0.14 as an embedded Unity package in `Packages/com.tripo3d.unitybridge`. The manifest points to the local package, so installation is portable with the project.

Source: https://tripo-public.tripo3d.ai/plugins/unity-bridge/Tripo3d_Unity_Bridge-latest.zip

Downloaded ZIP SHA-256: `B2B5C84FC780A621B9B985F670DAB5391304B62EBBA82CA5260C9AAD19844532`.

Verified in Unity 6000.3.25f1: package compilation, URP detection, and service startup on localhost `127.0.0.1:60610`. `TripoBridgeSetup.OpenAndVerify` opens the official panel and verifies its service; diagnostic log is `Logs/tripo-bridge-setup.log`.

## Connect

1. Keep Unity open in edit mode with **Tools → Tripo Bridge** open and the service running.
2. Open https://studio.tripo3d.ai/ in Chrome or Edge and sign in.
3. Click **DCC Bridge**, enable **Unity**, then select a model and use **Export → Send to Unity**.
4. Focus Unity after sending. Incoming models are saved under `Assets/TripoModels`.

The bridge is editor-only. Installed dependency resolution uses the project's existing JSON module. Real account connection and a model transfer have not yet been verified.

## Local package adjustment

Disabled both automatic deletion calls in `Editor/StartupCleanup.cs`. The upstream package deletes old directories under `Assets/ImportedModels`, and its temporary-cache cleanup selects folders solely by a 36-character name length. Project assets and unrelated caches should remain intact. No import or connection protocol code was changed. Preserve this adjustment when updating the package.

Official instructions: https://www.tripo3d.ai/blog/tripo-dcc-bridge-for-unity
