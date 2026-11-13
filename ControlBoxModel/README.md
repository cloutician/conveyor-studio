# Control Box — Three.js model and inspector

A standalone, locally served geometry inspector for the conveyor control box. No texture maps, build step or external service is required. The one runtime dependency is Three.js, pinned in `package-lock.json`.

## Run

```sh
npm install
npm start
```

Open **http://127.0.0.1:5173** on this computer. The server listens on loopback only. To choose another port, set `CONTROL_BOX_PORT` before starting it.

## Inspect and operate

- Drag the background to orbit. Scroll/pinch to zoom; right-drag or use two fingers to pan.
- Use Front, Three-quarter, Side and Back for fixed views.
- Drag around the physical speed knob. It flexes against each detent, then settles into one of ten positions: OFF, 1–9.
- Click/tap the projecting **SYNC SPEEDS** lever or its washer to synchronise speeds. Its nearby indicator lens illuminates.
- Press the green cap to start. The red mushroom latches down and extinguishes the green cap; Start releases the latch and resumes.
- The inspector starts with Generic A, Short B and Incline C. Add more machines of any of these types with **Add machine**; there is no fixed machine-count cap. IDs continue A–Z, AA, AB and so on. Types and individual identities are separate.
- Each machine remembers its own setting while unsynchronised. A machine added while synchronised inherits the selected speed. Controls remain adjustable while stopped.
- The flat dot-matrix display shows the selected type and unit ID on two lines.
- **Dock right** previews screen-edge placement. **Hide box** smoothly tucks most of the box off the right edge, leaving a narrow strip of the actual housing visible. Click/tap that exposed housing to slide it open; the small **Show box** button also provides keyboard access. Clicking the tucked-away housing only reopens the box, so it cannot accidentally activate a machine control. Reopening retains selection, speed settings, synchronisation and emergency-stop state. Dock motion pauses while operating a physical control.
- The small inspector fields provide keyboard-accessible equivalents of the physical controls.
- Wireframe shows the mesh. Dimensions are inspection helpers, excluded from the model export.

The inspector exercises the control-box behaviour. The Unity implementation now lives in the neighbouring `ConveyorStudio` project; this viewer remains an independent asset inspector.

## Geometry and scale

The enclosure body is **0.34 × 0.53 × 0.08 metres**. Hinges, the two right-side cable glands and raised controls extend beyond those body dimensions. Each gland projects 50 mm from the right side. Front faces +Z and up is +Y in the Three.js source.

The housing has a bevelled body, raised front rim, inset faceplate, true opening around the recessed display, separate rear cover, screw heads, side hinges and two extended ribbed cable glands stacked on the right. The glands have 96.6 mm centre-to-centre spacing, 40% more than the previous 69 mm spacing. The red EMERGENCY marking is raised above the yellow collar. The sync label, switch and lamp form a compact assembly below the dial.

The main faceplate labels are approximately 36% larger than the first revised model; dial numerals and OFF are approximately 40% larger. The small asset mark is 25% larger, and button-cap lettering has also been enlarged. The digital display retains its flat dot-matrix treatment.

The knob grip teeth, detent marks, pointer, fixed lettering, switch shaft, button collars and button caps have physical relief. Display pixels are coplanar circular meshes with zero depth, recessed inside the bezel; they use an unlit material so the lettering stays crisp under inspection lighting. Materials use solid colours and scalar properties; no surface texture maps are used.

Key movable nodes:

| Node | Local motion | Rest/export pose |
| --- | --- | --- |
| `SpeedKnob_Pivot` | Z rotation; levels 0–9 | Level 4, +15° rotation |
| `LinkLever_Pivot` | X rotation, ±28° | OFF, +28° |
| `StartCap_Pivot` | Z translation, 0 to −0.003 m | Released, 0 |
| `EmergencyCap_Pivot` | Z translation, 0 to −0.008 m | Released, 0 |

Control metadata is also stored in node `userData` and exported as glTF extras. Unity's glTF importer handles coordinate-system conversion; verify the resulting prefab orientation and rotation directions before connecting scripts.

The existing `LinkSwitch`, `LinkLever_Pivot` and lamp node names are retained for compatibility; the visible label and inspector now say **SYNC SPEEDS**.

## Export to Unity

**Export GLB** creates a self-contained model with the hierarchy and materials in a neutral pose: GENERIC / UNIT A displayed, speed level 4, sync off, both caps released. It excludes the viewer camera, studio lighting, floor, screen-docking mount and measurement helpers. The export pose is independent of the current inspector state, including whether the box is hidden or wireframe is on.

The prepared export is in `exports/control-box.glb`. The Unity project now uses Unity glTFast 6.21.0 to import it and creates `Assets/Resources/ControlBox/ControlBox.prefab` with native URP materials. C# implements the dynamic dot-matrix display, physical control hit areas, animations, screen-edge presentation and live conveyor state. Double-tapping a placed belt focuses the camera and opens its controls. See `../ConveyorStudio/CONVEYOR_PLACEMENT.md` for operation and verification. The JavaScript preview logic is not embedded in the model file; the inspector remains useful for geometry review.

## Source

- `src/model.js`: procedural geometry, names, pivots and plain materials.
- `src/main.js`: viewer, pointer interaction, behaviour, camera and export.
- `src/style.css`: lightweight inspector layout.
- `server.mjs`: local static server using Node's standard library.
- `assets/helvetiker_regular.typeface.json`: outline data used to generate actual text meshes.

Run `npm run check` for JavaScript syntax checks. Browser interaction and GLB round-trip checks are described in `VERIFICATION.md`.

## Credits

Three.js r187 is MIT licensed. Helvetiker typeface data is from the Three.js examples; copyright MAGENTA Ltd, 2004, distributed under the permission notice embedded in that file. Full notices are included in `THIRD_PARTY_NOTICES.txt`. Generated lettering remains subject to the font's notice. Include the notices with redistribution of the model and source.
