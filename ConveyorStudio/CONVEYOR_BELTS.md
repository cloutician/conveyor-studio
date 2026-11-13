# Moving conveyor belts

Generic, Short and Incline now have genuinely circulating belt geometry and two rotating roller meshes. In Play mode, the supplied belt's dimensions are used to create a closed rubber band with thickness and small geometric grooves. Its vertices travel across the top, around the ends and underneath. UVs remain attached to the rubber; texture offsets remain fixed.

The rubber mesh has motor-controlled kinematic motion. In the main **ConveyorStudio** scene, products now use dynamic rigid bodies carried by physical belt contact friction. See [CONVEYOR_PHYSICS.md](CONVEYOR_PHYSICS.md). The original SampleScene and generated module preview retain their legacy path-following demo products. Flexible-rubber tension and deformation are not simulated.

## See the result

1. Return to the Unity editor for **ConveyorStudio**.
2. Open **SampleScene** if needed, then press **Play** above the Scene/Game panels.
3. The existing two-conveyor demo runs the geometry and rollers at 0.5 metres per second, with the product following its current conveyor's motor speed.
4. Press **Play** again to return to Edit mode.

All three source model assets and reusable module prefabs are prepared. Their materials and surface components are inherited when the models are instantiated. The original demo contains Generic conveyors; **ConveyorModulesPreview** shows all three types. See `CONVEYOR_MODULES.md` for the prefab paths, connector heights and preview instructions.

During Play, expand **ConveyorStraight / Visual / Conveyor_Long_Section.023**. Its Mesh Filter uses a mesh named `_Circulating`; its **MovingRollers** child contains **FrontRoller_Pivot** and **RearRoller_Pivot**. The roller geometry is generated inside the band because the supplied model does not expose separate roller objects. Original frame and guard geometry stays fixed. Exposed axle caps have an offset fastener that makes their rotation visible.

Select the belt object to adjust **Speed** or **Is Running** in its Conveyor Belt Surface component. Turning Is Running off freezes the vertices and rollers at their current pose. Physical products retain momentum and brake through friction; legacy demo products stop immediately. Speed changes while stopped take effect when Is Running is turned on again. Scene-view Wireframe in Play mode lets you inspect the moving vertices directly.

Materials and texture files are in **Assets / Art / Conveyors**:

- `BeltRibbed.mat`: URP/Lit surface, with base colour and normal maps.
- `BeltEdge.mat`: plain rubber sidewalls.
- `BeltRoller.mat`: metal roller drums and axle caps.
- `BeltRibs.png` and `BeltRibsNormal.png`: small, seamless, repeating textures.

Use **Tools / Conveyor Studio / Apply Ribbed Belts (All Types)** to regenerate the assets and reimport the models.

## Mapping and movement

The original UVs mirror the two halves of the belt. Scrolling those UVs directly would make the visible surface move in opposite directions around the centre. `ConveyorBeltImporter` rebuilds only the blue belt's UVs into one continuous loop, with the seam underneath. Frame and leg geometry/materials are preserved; the FBX source files are not rewritten.

Rib spacing is approximately 75 mm for every type (74.25 mm for Short; 74.49 mm for Generic/Incline). An integer number of repeats closes each loop without an irregular gap. The visible outer-surface path is used to keep the spacing consistent around the bends. The importer enables mesh Read/Write for these three models so the mesh can be prepared during import.

`ConveyorBeltSurface` owns the motor travel, dynamic band mesh and roller pivots. `ConveyorBeltLoopMesh` sweeps the outer visible path around the two rollers; inner vertices preserve the supplied belt thickness. Material coordinates are fixed. Actual geometric grooves move with the band and bend around each end. Each machine owns its dynamic mesh, while material assets are shared.

The roller angle follows the band's outer-surface bend angle, keeping it aligned with the circulating geometry. `Speed` is metres per second. `IsRunning` pauses travel without resetting pose. `ReverseDirection` changes the geometry's local travel direction; the incline prefab uses it to travel uphill. Rib pitch is kept near 75 mm, adjusted slightly so a whole number of repeats closes the outer loop. Surface sections are regenerated in memory each frame; the original FBX and its imported shared mesh are preserved.

`ConveyorProductMover` reads the current section's belt motor. Transfer calculations consume the remaining frame time on each section so different speeds and stopped downstream sections are respected. Its serialized speed is a fallback for conveyors without a motor. The Three.js control-box inspector is still a separate prototype; Unity's controller integration remains to be implemented.

## Verification — 12 November 2026

Unity 6000.6.8f1 compiled and imported the changes. The existing demo was observed in Play mode and returned to Edit mode.

- All three imported reference meshes have 744 vertices and two material slots. Each Play-mode instance creates its own dynamic band, with four geometric samples per rib and duplicated seam vertices.
- Imported top-surface UVs were checked for direction on both halves; the opposite-direction mirroring is removed.
- Solid sidewall triangles are assigned separately from the travelling surface.
- Real Play-mode checks ran on temporary instances of all three supplied model types. Their vertex arrays changed while running, while UV arrays and material texture offsets stayed fixed. Both roller pivots rotated at the expected motor angle. Seam vertices stayed coincident and different conveyors owned independent meshes.
- Stopping froze vertex arrays, travel distance and both roller poses. A speed change while stopped was retained; resuming advanced geometry and rollers again.
- The live demo product stopped with its current motor and resumed when the motor restarted. Temporary test objects were removed and Unity was returned to Edit mode.
- The existing product demo still runs with the prepared surfaces.

Reports are generated in `Library/ConveyorBeltSetupReport.json`, `Library/ConveyorBeltUvReport.json` and `Library/ConveyorBeltGeometryRuntimeReport.json`.

**Tools / Conveyor Studio / Validate Ribbed Belts** checks imported geometry and explicitly arms a one-time runtime probe for the next Play session. That probe creates temporary Generic/Short/Incline instances off camera, exercises their geometry and rollers, briefly tests the demo product's stopped motor, restores the original motor settings and writes the report. Normal Play sessions do not run the probe unless validation has been requested.
