# Verification — 12 November 2026

Checked in Microsoft Edge using Playwright and software WebGL. Touch checks used emulated native touch input at a tablet viewport; these are not physical iPad hardware tests.

## Source and viewer

- `npm run check` passes JavaScript syntax checks.
- Viewer reaches its ready state with no JavaScript errors or browser warnings after final changes.
- Background mouse drag changes the orbit camera.
- Side and rear views were visually inspected; controls visibly project from the faceplate and the display is recessed inside its bezel opening.
- Inspector hide/show and full-screen entry/exit work.
- Narrow layouts checked at 390 px and 320 px: document width matches the viewport, with no horizontal overflow.
- Front, three-quarter, side and back previews were regenerated for the revised geometry. The red EMERGENCY marking is visibly clear of the yellow collar, the sync assembly is compact and two extended glands project from the right side.
- Dock-right preview and Hide/Show operate on the actual model. Tucking away leaves part of the actual housing visible (about 69 px wide at its widest point in the checked three-quarter desktop view). Clicking the exposed housing slides the box open, without changing the camera, selection, any speed setting, synchronisation or emergency-stop state.
- The separate keyboard-accessible Hide/Show button is placed below the model, leaving the peeking housing available to click. At 390 px and 320 px, the gesture instructions, view caption and button fit without overlap.
- Faceplate-label geometry was compared before and after enlargement: main labels are about 36% taller, dial OFF/numerals about 40–41% taller and the asset mark 25% taller. The larger red emergency label remains clear of the yellow collar.
- The two right-side glands have 96.6 mm centre spacing, compared with 69 mm previously: a 40% increase.

## Actual 3D control interaction

- Clicking the model's green cap starts the line.
- Clicking the red mushroom stops the line and latches the cap down.
- Emergency cap settles at −0.008 m; pressing Start releases it and resumes the line.
- Start cap has −0.003 m press travel and returns to its released pose.
- A small 12° knob gesture keeps its current detent; a 60° gesture advances two detents with the resistance gain applied.
- Turning the model's knob leaves the orbit camera unchanged.
- Clicking the projecting switch grip synchronises all sample machine settings, including while docked.
- Unlinking preserves settings; individual edits affect the selected machine only.
- Speed changes while emergency-stopped are retained without restarting the line.
- Native touch presses operate the model's Start, Stop and link lever.
- Native touch knob drag advances detents without orbiting; touch drag on the background orbits successfully.
- Revised native-touch checks: Start, emergency stop, sync switch and hide/show all work. A 60° touch knob drag changes the setting from 4 to 6 while emergency-stopped and docked, without changing the camera or scrolling the page.
- Peeking-housing revision: native touch on the exposed actual housing reopens the box. The checked tablet view retained about 68 px of housing; the selected speed and latched emergency-stop state were unchanged by reopening.

## Additional machines and display

- The initial examples are Generic A, Short B and Incline C. Adding an Incline creates D with the correct type and display label; adding while synchronised inherits the current speed.
- Added 27 machines in total through the Add machine handler. IDs progress beyond Z to AA with no three-machine cap.
- Speed synchronisation reaches all 27 machines. Unlinking and changing AA while emergency-stopped leaves the other machine settings unchanged.
- The display updates for the selected type and ID, including INCLINE / UNIT D and INCLINE / UNIT AA.
- Exported dot geometry has identical minimum and maximum Z coordinates: zero geometric depth. Display dots use an unlit material; fixed faceplate markings retain physical relief.

## Export

- A GLB was exported while the source viewer was in a non-neutral state and wireframe mode; the exported asset still used the defined neutral pose and plain materials.
- Final `exports/control-box.glb` reloads through Three.js GLTFLoader.
- The four named moving pivots survive the round trip: `SpeedKnob_Pivot`, `LinkLever_Pivot`, `StartCap_Pivot`, `EmergencyCap_Pivot`.
- GLB 2.0, 94 meshes, 16 materials, 30,652 triangles, 2,705,856 bytes.
- No texture images or texture entries are present. Floor, lighting, cameras and measurement helpers are absent from the exported model.
- Both right-side gland groups survive export. The display exports as GENERIC / UNIT A even when a different machine is selected. Both button caps export released; knob and sync lever export in their defined neutral poses.
- The reloaded GLB retains the wider gland spacing and enlarged lettering. The exported emergency label is approximately 8.03 mm high, compared with 5.89 mm previously.
- Export was also checked with the box hidden, emergency stop active, wireframe enabled and 27 sample machines added. Screen-docking helpers and preview-only machine state do not change the neutral asset.
- Exported physical bounds, including projecting controls and both glands: approximately 0.3985 × 0.5300 × 0.1390 m.
- Import and C# integration inside Unity remain a subsequent step; this check verifies the exported glTF asset, not a completed Unity prefab.

Preview images are in `previews/`.
