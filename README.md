# Conveyor Studio

An interactive modular conveyor simulator built in Unity for the VACAC Graduate Software Engineer assessment.

**Source repository:** https://github.com/cloutician/conveyor-studio

Choose from the supplied Generic, Short and Incline conveyors, assemble a continuous line with magnetic snapping, and transport boxes and cans using rigid-body physics. A separate 3D control box provides speed control, synchronisation, Start and a latched emergency stop.

## Run the Windows release

Open `Windows/ConveyorStudio.exe` in the submitted release package. Keep the executable, its `ConveyorStudio_Data` folder and the accompanying DLLs together. Unity is not needed to run the application.

The application opens in a resizable window and starts with an empty, stopped workspace. Maximise it for a larger workspace. Close the window or press **Alt+F4** to quit. The accompanying captioned video demonstrates the main workflow.

## Controls

| Action | Control |
| --- | --- |
| Add Generic / Short / Incline | Library tiles or **1 / 2 / 3** |
| Place a conveyor | Move the preview, then left-click |
| Move an existing conveyor | Left-drag |
| Rotate in 90-degree steps | **Q / E**, **R**, or rotation buttons |
| Cancel placement | **Esc** |
| Select multiple conveyors | **Ctrl-click** |
| Move a selection | Drag any selected conveyor |
| Delete selected conveyors | **Delete** |
| Open controls and focus a conveyor | Double-click |
| Focus selection / reframe the workspace | **F / Home** |
| Orbit / pan / zoom | Right-drag / middle-drag / wheel or trackpad |
| Add products | Box and can picture buttons |

Selected conveyors receive products at their own infeeds. With no selection, each disconnected line receives a product at its upstream end. Occupied infeeds are skipped to prevent overlapping spawns.

## Simulation and interaction

- **Placement:** model silhouettes show selection, invalid overlap and connection readiness. Magnetic capture, release hysteresis and eased settling help conveyors connect without abrupt jumps. An invalid drop smoothly returns to its last valid position. Raised joins extend the support legs to the floor.
- **Groups:** Ctrl-selection supports shared movement, quarter-turn rotation and deletion. Internal transfer rollers remain visible during a move, and exposed group ends can reconnect to an existing line.
- **Transport:** moving belt meshes and powered transfer rollers accompany dynamic box/can motion. Contact friction, gravity, inertia, collision response and rolling resistance determine product movement; products are not attached to paths or teleported between belts.
- **Support loss:** moving or deleting a supporting machine wakes touching product stacks. Unsupported sleeping products resume falling. The floor has tuned friction so fallen boxes settle rather than glide indefinitely.
- **Cleanup:** off-belt products fade after a five-second floor countdown. Touching congestion shares a cleanup deadline, with a two-second quiet period for new box arrivals before fading starts. An active fade is irreversible.
- **Control box:** OFF plus nine speed settings, independent or synchronised speeds, Start and emergency stop. Speed settings can be adjusted while stopped. Its physical controls, dotted display, cursor tilt and screen-edge docking remain independent of world navigation.
- **Camera:** smooth focus/reframe, scene-centred trackpad zoom and floor clearance throughout orbit, pan and framing transitions.

## Open and build the source

1. Install **Unity 6000.6.8f1** with **Windows Build Support**.
2. In Unity Hub, add the **`ConveyorStudio`** folder inside this repository.
3. Open `Assets/Scenes/ConveyorStudio.unity` and press Play.
4. Choose **Tools → Conveyor Studio → Export Windows Release** to create a Windows x64 build. The export includes the studio scene and uses the Mono scripting backend.

Unity restores the dependencies declared in `Packages/manifest.json` and `packages-lock.json`. The repository contains `Assets`, their `.meta` files, `Packages` and `ProjectSettings`; generated caches are intentionally excluded.

The supplied FBX models are stored with **Git LFS** because several exceed GitHub's regular file-size limit. Install Git LFS before cloning, then run `git lfs pull` if the model files have not been downloaded. The submission ZIP contains the complete files and does not require Git LFS.

## Development tools and structure

- Unity 6.6, C#, Universal Render Pipeline 17.6, Input System 1.20.1 and Unity UI 2.6.
- Three.js 0.187 for the separate control-box model and inspector; glTFast 6.21 imports the exported GLB into native Unity materials and a prefab.
- Git for source control. ChatGPT and OpenAI Codex were used for development assistance and iteration; the app icon was created with an image-generation tool.

`ConveyorStudio/Assets/Scripts` contains the runtime application. Placement, group snapping, camera movement, product physics and line/control-box state are separated into focused components. The contact-modification callback receives immutable surface snapshots rather than accessing Unity objects from the physics worker thread.

`ConveyorStudio/Assets/Editor` contains asset preparation, the Windows exporter and isolated verification fixtures, all excluded from the player. `ControlBoxModel` contains the original Three.js model source and export workflow. The `CONVEYOR_*.md` files inside the Unity project describe implementation details and checks.

## Verification and scope

Editor checks exercise placement, silhouette outlines, magnetic settling, reconnection, group editing, camera clearance, product physics, cleanup, all nine conveyor-type transfer pairings and the integrated controls. The final release package includes the collected verification results; the exported executable is also checked separately.

Conveyors rotate in quarter turns and connect **Exit → Entry**. Perpendicular placement is supported, but corner/side-transfer transport is outside this version. Layouts are temporary and reset when the application closes. The product physics uses tuned approximations suited to an interactive demonstration.

## Asset acknowledgements

VACAC supplied the conveyor and product models for this assessment. The separate control-box model, interface and application behaviour were developed for this submission. Third-party notices for Three.js and its typeface are in `ControlBoxModel/THIRD_PARTY_NOTICES.txt` and are included with the player under `StreamingAssets`.
