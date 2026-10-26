# AppA
A remote application for VRWS: VRWS_World1's AppA (a walled 100 m square with houses, a grabbable cube and spheres) as its own world, made from EmptyWorld.

VRWS starts it with Start App (Remote Apps entry "AppA": objects on port 5061, its portal on 5062) and opens a portal into it. The player comes out at the exit portal in the middle of AppA's floor.

- `Assets/AppA.unity`: the scene. EmptyWorld's, with `Assets/AppA/AppA.prefab` in place of its floor. AppACamera is kept but its Camera is off: in VRWS it draws AppA into a window, here it would draw over the player's view. ObjectApp (on AppA) is off, as it is in VRWS.
- Like EmptyWorld: the exit portal (`RemotePortalServer`, port from `-portalPort`), ToOther and FromOther for links to other applications (VRWS's Remote Links), ObjectReceiver and serial.cs to build objects that come through, and the Hand layer for touch grab.
- Build to `Build/AppA.exe`.

Uses the shared VRPortalToolkit package in `E:\Bruce\VRPortalToolkit` (referenced as `"com.vrws.vrportaltoolkit": "file:../../VRPortalToolkit"` in `Packages/manifest.json`), the same toolkit as VRWS_V02. A fresh clone needs the `VRPortalToolkit` folder beside it.
