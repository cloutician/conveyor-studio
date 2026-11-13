# Runtime placement studio

Open **Assets/Scenes/ConveyorStudio.unity**, then press **Play**. Use the Game tab.
If the interface is cropped, set the Game view resolution to **16:9 Aspect**.
Right-click the **Game** tab and choose **Maximize** for a larger workspace.
For a sharp preview, turn off **Low Resolution Aspect Ratios** in its resolution menu.
The header uses the approved Conveyor Studio icon, with **FOR VACAC** below the
title and Reframe/library controls at the right.

1. Click a **Generic**, **Short**, or **Incline** image tile in the narrow library.
   **1, 2, 3** (including the number pad) start placing those models, even if a
   machine is already selected. Ctrl/Alt combinations retain their other meanings.
2. Move the preview into the workspace. Its silhouette border is green when valid
   and red when blocked. The model retains its normal materials and lighting.
3. Use **Q/E**, **R**, or the visible **−90°/+90°** buttons to rotate.
4. Click a valid position to place. **Esc** cancels a new preview.
5. Drag any placed machine to move it. An invalid release returns it smoothly to
   its last valid position over roughly 0.26 seconds.
   **Ctrl-click** machines to add or remove them from a selection. Every selected
   machine is outlined. A plain click narrows the selection on release; dragging
   preserves the group. Drag any selected member to move the entire arrangement;
   offsets, elevated heights and internal joins are retained. Q/E rotates the group
   in quarter turns. Internal connectors remain visible during movement, with
   their physics paused. Exposed group ends use the same magnetic capture,
   hysteresis, green readiness and gentle settling as individual machines, including
   fitting both sides of a gap. Collisions or below-floor drops return the whole group together.
6. The **box/can picture buttons** add one product to each selected belt, including
   a belt partway along a line. Click empty space to clear the selection and feed
   the first belt of every disconnected line instead. Occupied infeeds are skipped
   while clear targets continue to receive products.
   Press **Delete** to remove all selected machines together. The compact sidebar contains
   only the three image tiles, two product buttons and two rotation buttons;
   the machine list and selection-details panel have been removed.
   **Double-click / double-tap a belt** to focus the camera smoothly and slide out
   its 3D control box. Single clicks continue to select; dragging continues to move.
7. Right-drag orbits the camera, middle-drag pans, and the wheel or two-finger
   trackpad scroll zooms around the middle of the visible conveyors, regardless
   of where the pointer sits in the world viewport.
   A held right/middle drag continues across the control box, other interface
   elements and viewport edges until release. A right-drag can begin over the
   control box; it cannot accidentally operate its buttons or dial.
   **F** focuses the selection; **Home** / **Reframe** fits the current line.
   Reframe uses the visible model geometry, including raised supports, and fits it
   to the available viewport with comfortable margins while preserving the viewing
   angle. A depth-aware stand-off prevents an oversized foreground roller at low
   angles; the overview limits near-to-far perspective exaggeration.
   Reframe eases position and distance over 0.3 seconds, retaining the current
   viewing angle. Manual orbit, pan or zoom immediately takes over the transition.
   Zoom preserves fractional trackpad input, with a short smooth response and
   immediate direction reversal. Movement is proportional to viewing distance:
   close inspection stays precise, while an overview covers ground quickly.
   The centre stays fixed during each gesture; off-screen machines do not pull
   it away, and an empty panned view retains its existing centre.
   Close range scales to an individual conveyor's width (typically about 0.5 m
   from the focus); the overview limit is 2.4 times the current line's fitted
   viewing distance. Clearance checks stop zoom before solid surfaces and keep
   the camera above the floor. A shared floor boundary also protects orbit, pan,
   focus and every frame of Reframe, including large layouts whose fitted pivot
   lies underground. The camera stays at least 12 cm above the floor, with extra
   clearance when needed to keep the entire near clipping plane above it. An
   inherited underground view is repaired when the camera reloads. Orbit can
   still lower to 5° above horizontal.
8. The vertical **HIDE** tab beside the library slides it smoothly off the left
   edge. The tab remains visible as **SHOW**. Each complete word is rotated 90°;
   the camera viewport expands and contracts with the same animation.
   The library equipment stays vertically centred, and the tab cannot place or
   select a conveyor behind itself.

The library, camera and editing controls are runtime components, included when
the scene is exported as a player. The scene starts empty. Play-mode edits are
temporary; layout save/load is not implemented in this phase.

## Placement rules

- Highlighting is a screen-space silhouette of the complete assembled machine,
  including its belt, frame, rollers and feet. Only a three-pixel outside border
  is coloured; the model is not tinted. There is no rectangular floor footprint
  or separate outline around each mesh part. The contour follows the camera.

- Yaw is limited to **0°, 90°, 180°, 270°**. Roots stay upright, with unit scale.
- The floor is **world Y = 0**. Pointer placement stays on that plane. A valid
  elevated connection can lift a machine, extending its existing support shafts
  to keep its feet on the ground. Source meshes and prefabs are not modified.
- The existing 400 × 400 m ground plane has a collision mesh and now uses regular
  grey **600 mm square floor tiles with 3 mm grout**. Colour, bevel normals,
  smoothness and occlusion maps give the tiles a subtle satin surface. The joints
  are material detail; the physical floor remains flat. The material retains its
  original asset GUID (`Assets/Art/Studio/FloorGrid.mat`) so existing scenes keep
  their references. Rebuilding the studio preserves the tiled finish.
- Moving, rotating and snapping all use the same validation. A snap requiring a
  negative root height is rejected, and its visible preview stays above ground.
- Separate oriented bounds for the model parts reject overlaps. These are
  conservative placement volumes, rather than a detailed CAD interference test.
- At a precisely aligned Entry/Exit pair only, a local **85 mm mating zone**
  accommodates the incline's projecting end hardware and its rotated bounds.
  This allowance never applies to supports or unconnected side-by-side units.
- A faint guide appears within **0.31 m horizontally**. Magnetic capture starts
  within **0.21 m**, after a brief 25 ms intent delay; fast passes are ignored.
  The machine eases into the connection without overshoot. A captured connector
  stays stable until the pointer moves **0.26 m** away, avoiding boundary jitter.
  Measuring horizontally lets a floor-level preview find an elevated output.
- The guide fades and grows gently at the target port. The model follows the
  pointer freely outside the magnetic field. A release near a connection smoothly
  completes the remaining travel; products transfer only once the ports align.
- Returning a unit into a matching gap checks both Entry and Exit together. Both
  stationary neighbours receive green outlines and port guides when the destination
  fits. Releasing restores both connections without moving the neighbouring units
  or changing the elevated downstream supports. Mismatched heights cannot advertise
  both ends as ready; a valid single-end connection remains available.
- Picking up a connected machine temporarily ignores its former connectors while
  moving away. Moving clear or deliberately returning towards the old join re-arms
  the magnet, so reconnection does not require a large detour.
  A quick pull-away and release finishes at the requested free position.
- Actual connections are **Exit → Entry**, with coincident markers. Perpendicular
  layouts are allowed when clear, but no side transfer or corner transport is
  implied. A 90° transfer requires a separate junction and routing feature.
- Moving a unit disconnects its current neighbours. Committing or returning
  reconnects aligned free ports without moving the neighbours. Chains cannot cycle.
- Returning and settling units reserve their destination. Other placement remains
  available while these short animations finish.
- Names remain stable after removal. Labels continue A…Z, AA…; there is no fixed
  three-machine limit. Performance still depends on the number of visible models.

## Transport and control box

Belts retain their motor-driven moving meshes. **Add box** and **Add can** now
spawn independent dynamic rigid bodies. Belt contact friction carries them; gravity,
collisions, inertia and material friction determine their subsequent motion. Products
can tip, roll, collide, stack and fall off open ends onto the tiled floor. They remain
alive when their supporting machine is removed. Infeed clearance prevents spawning
one product inside another. Connected ends receive visible powered transfer rollers
to bridge the supplied models' rounded-end gap.

Editing temporarily pauses that unit's motor and disables its solid colliders, then
restores them after placement or the smooth invalid-position return. Existing products
are not attached to a dragged machine and can fall when their support moves away.
See [CONVEYOR_PHYSICS.md](CONVEYOR_PHYSICS.md) for presets, tuning and verification.

The Three.js model is now imported as the separate Unity prefab
`Assets/Resources/ControlBox/ControlBox.prefab`. Its projecting knob, lever, caps,
raised lettering, flat dot-matrix screen and twin side glands retain their geometry.
The screen shows the selected type and stable unit ID. A separate lit camera keeps
the box fixed to the right edge; world navigation cannot move it. Reframe and
double-tap focus allow room beside the open box.
For multiple selections it shows the count and stable letters, for example
**3 MACHINES / A, B, C**. Longer lists page every two seconds to stay legible.
F focuses a selected group; a plain double-tap selects and focuses that one belt.
The screen presentation is 20% smaller than the initial integration. Both side
fittings show part of their shafts, while their terminal faces stay beyond the
right screen edge even during the cursor-driven counter-tilt. The open box sits
another six reference pixels toward the screen edge. Cursor movement has four
times the earlier horizontal travel and nearly four times the vertical travel,
with a faster eased response. The resting view is neutral and straight-on, with
no fixed horizontal or vertical tilt.
Cursor movement can sweep through face-on past that resting angle, making the
relief and housing depth much more apparent. The tilt
eases back on pointer exit and holds still while a control is being operated.

The scene starts stopped. Press the physical green **Start** cap to run the line.
Press the red mushroom to latch an emergency stop: all motors stop and the green
lamp goes off. Start releases the latch and applies the current saved settings.
Products continue to obey gravity and inertia during an emergency stop.
An empty workspace reads **NO MACHINES**. Start refuses an empty line with a brief
**ADD MACHINES / TO START** prompt, then restores the display. Removing the final
machine stops the line, so machines added later wait for Start again. The eighth
machine earns a temporary **EIGHT BELTS / VERY SUBTLE** message.

Drag the physical dial across its ten positions, **OFF and 1–9**. A firmer resistant
flex precedes each detent: lower drag gain and a wider breakaway threshold require
a deliberate pull, with up to 6.55 degrees of elastic movement. The resistance
is blended 15% back toward the earlier, lighter feel. The pointer
snaps promptly onto the marking when released. Each level
adds 0.2 m/s; the default saved level is 4 (0.8 m/s). Settings remain adjustable
while stopped. The **SYNC SPEEDS** lever applies the selected setting to every
placed conveyor. New belts inherit that setting while synchronised; switching back
to independent control retains each belt's current setting. There is no machine cap.
With Sync off, the dial applies to every selected machine. Clearing the selection
clears its controls; the line retains its independent saved settings.
Sync requires at least two machines. Otherwise its lever briefly flips and springs
back to OFF, its lamp stays off, and **NEED TWO / TO SYNC** temporarily replaces the
unit display. Removing machines down to one also releases sync.

Use the narrow chevron to tuck the actual housing off-screen. Part of it remains
visible; clicking that exposed housing slides it back without pressing a control.
The chevron appears only while the box is open; the tucked housing has no arrow tab.
Its open screen position stays steady during cursor tilt; it does not inherit the
small surface shifts used to keep the side fitting ends cropped.
Hovering the tucked edge gently reveals another 16 reference pixels, while the
panel remains tucked; clicking opens it fully. Hiding and reopening translate
the camera sideways to reclaim or reserve space while preserving the current
zoom and viewing angle. Double-tapping a machine remains the explicit focus action.
Selection, speed settings and the emergency latch survive hiding. Pointer input on
the panel cannot manipulate a belt or zoom the scene behind it. Editing pauses
only the moved belt; a global emergency stop also updates its remembered motor
state, preventing a returning belt from restarting itself.

The model uses native URP materials with the original scalar finishes. Its camera
uses the dedicated forward `ControlBox_Renderer`, lit by its studio lights. Camera
shadows and post-processing are disabled. This keeps world-scale screen-space ambient occlusion
from adding broad halos around the small dial and button assemblies. The main scene
retains its renderer and ambient occlusion. Both quality pipelines register the
presentation renderer, and the camera selects it by name after creation or reload.
Decorative
surface textures and sound effects have not been added in this integration.

## Project assets and verification

- Application prefab: `Assets/Prefabs/ConveyorStudioApplication.prefab`
- Scene: `Assets/Scenes/ConveyorStudio.unity` (first enabled scene in Build Settings)
- Catalogue: `Assets/Data/ConveyorCatalog.asset`
- Runtime UI previews/materials: `Assets/Art/Studio/`
- Floor tile maps: `Assets/Art/Studio/FloorTiles/`
- Regenerate tiles: **Tools → Conveyor Studio → Apply Regular Floor Tiles**.
- Rebuild: **Tools → Conveyor Studio → Build Placement Studio**
- Verify: **Tools → Conveyor Studio → Verify Placement on Next Play**, then Play.
  This uses a temporary workspace and restores the editing scene on stopping.
- Report: `Library/ConveyorPlacementRuntimeReport.json`
- Silhouette check: **Tools → Conveyor Studio → Verify Silhouette Outlines**.
  Its pixel checks cover all three models at six camera angles in green and red,
  verifying unchanged model interiors/materials and a border confined to the model.
  Report: `Library/ConveyorSilhouetteRuntimeReport.json`; renders are under
  `Library/ConveyorSilhouetteViews/`.
- Snap motion check: **Tools → Conveyor Studio → Verify Soft Snapping**, then Play.
  This exercises the live controller's eased motion, hover delay, boundary jitter,
  fast passes, 30/60 fps behaviour, smooth release, destination reservation and
  free detachment. Report: `Library/ConveyorSnapRuntimeReport.json`.
- Gap and camera check: **Tools → Conveyor Studio → Verify Gap Reconnection and Reframe**.
  It exercises two inclines followed by an elevated Short, small pull-away/reversal,
  both green readiness outlines, complete reconnection, incompatible heights and
  comfortable framing in wide and narrow viewports. An additional sweep covers
  all three modules and a raised line at five elevations (including 5° and 10°),
  four headings and two viewport shapes, checking perspective, margins, clearance
  and repeated Reframe stability. Report: `Library/ConveyorGapRuntimeReport.json`.
  Low-angle previews: `Library/ConveyorReframeViews/`.
  These checks can run in the existing Play session using isolated temporary scenes.
- Trackpad zoom check: **Tools → Conveyor Studio → Verify Trackpad Zoom**.
  It checks fractional gestures, platform scroll scaling, 30/60/120 fps response,
  immediate reversals, visible-model centring, pointer independence, scene-sized
  limits, overscroll, long frames,
  solid-surface clearance and low-angle floor clearance in an isolated workspace.
  Report: `Library/ConveyorZoomRuntimeReport.json`.
- Control box: **Tools → Conveyor Studio → Verify Integrated Control Box**.
  This checks the real imported controls' hit areas, physical dial drag, global
  start/stop, sync, saved speeds, new machines, emergency during an edit, selection,
  deletion, docking and camera space in an isolated temporary scene.
  Report: `Library/ConveyorControlBoxRuntimeReport.json`.
- Group workflow: **Tools → Conveyor Studio → Verify Group Workflow**.
  This checks retained internal transfer rollers, elevated group magnetism into a
  two-sided gap, single-click versus drag selection, numbered catalogue commands,
  floor-safe group joins, selected-belt product delivery and automatic delivery at
  each disconnected line's head when the selection is clear.
  Report: `Library/ConveyorWorkflowRuntimeReport.json`.
- Interface artwork lives in `Assets/Resources/Studio/`: the approved app icon and
  transparent previews of the actual box/can models. The editor's preview renderer
  produces those product pictures without altering the running scene.
  The app icon's rounded navy tile has transparent exterior corners.
- Library, header and status text use TextMesh Pro signed-distance-field rendering
  with pixel-aligned canvas geometry. The bundled font atlas, embedded fallback font
  and shaders under `Assets/TextMesh Pro/` are included in Windows builds.
- **Build Six-Belt Control Demo** creates a connected sample line in an empty Play
  workspace and opens the controls for Generic A. It preserves any existing layout.

The editor builder creates and closes its own additive scene. It does not save or
discard changes in another open scene. Verification code lives under `Assets/Editor`
and is excluded from player builds.
