# Conveyor prefabs

The three reusable module types are ready. Use these prefabs from **Assets / Prefabs**, rather than placing the raw FBX models:

| Type | Prefab | Travel length | Input height | Output height |
| --- | --- | ---: | ---: | ---: |
| Generic | `ConveyorStraight.prefab` | 2.222 m | 0.547 m | 0.547 m |
| Short | `ConveyorShort.prefab` | 1.583 m | 0.547 m | 0.547 m |
| Incline | `ConveyorIncline.prefab` | 2.221 m | 0.547 m | 1.339 m |

Lengths are measured between Entry and Exit. Heights assume a clean prefab root at world Y = 0. The incline rises approximately **20.90°** toward its Exit.

## Inspect and try them

1. Open **Assets / Scenes / ConveyorModulesPreview** in Unity. If Unity asks about your current scene's unsaved changes, save the changes you want to keep before switching.
2. The scene contains Generic, Short and Incline side by side, each with a demo product.
3. Press **Play** to see their circulating meshes, rollers and product paths. The incline's belt and product travel uphill.
4. Press **Play** again to return to Edit mode.

Double-click either new prefab in the Project panel to inspect it in isolation. Use the back arrow in the Hierarchy to return to your scene. Its clean root is position zero, rotation zero and scale one. The supplied model's import scale and orientation are contained inside `Visual`.

## Module conventions

- Forward travel is root +Z. Entry is at the negative-Z end and Exit at the positive-Z end.
- Each root has a `ConveyorSection`, a module-type identifier, `Visual`, `Entry` and `Exit`.
- Entry/Exit positions follow the top transport plane. Their orientation frames stay horizontal, so snapping an incline does not rotate its legs off vertical.
- The moving belt component, ribbed materials and roller generation are inherited from the supplied FBX.
- The incline reverses its source motor direction after orienting the model, ensuring that the actual mesh circulates uphill along the same route as products.
- Source geometry is grounded using transformed vertices. Foot heights were checked in all four footprint quadrants, within 0.15 mm of the floor.

The supplied incline's original input was 26.88 mm below the flat modules. Its visual assembly is raised by that amount and its existing lower leg shafts are extended downward, leaving the original foot pads on the floor. This derived leg mesh is stored in `Assets/Art/Conveyors/InclineLegsStandard.asset`; the original FBX is preserved.

The incline's elevated output is a different working level. A flat conveyor connected after it will need approximately 0.792 m of additional support height. Preparing elevated horizontal supports belongs to the placement system; these prefabs do not make raised conveyors' legs reach the floor automatically.

## Corrected bend spacing

The moving band's path is now measured along its **outer surface**, including both bends. Previously, spacing was measured around the inner contact surface, which spread the outside ribs farther apart around the rollers. Roller rotation now follows the same bend angle as the circulating geometry.

Measurements across two animation phases on each type found approximately 74.25–74.49 mm on straight runs and 73.71–73.94 mm at groove centres around the bends. The roughly 0.73% difference comes from the grooves' 0.8 mm inset. The former large stretch around the ends is removed. Perspective can still foreshorten visible spacing in a camera view.

## Rebuild and verification

**Tools / Conveyor Studio / Build Short and Incline Prefabs** regenerates the two prefab assets, the derived incline leg mesh and the module preview scene. Treat the preview scene as generated. The existing SampleScene and its unsaved changes are not saved or replaced by this command.

Checked in Unity 6000.6.8f1 on 12 November 2026:

- Saved prefabs were instantiated again to verify clean roots, grounded feet, connector references, module types, slope and motor direction.
- All nine ordered type combinations were snapped using the real connection code. Connector positions agree within 0.000001 m, and machine roots remain upright.
- Generic/Short to Incline connects at floor level after the infeed-height adjustment.
- A real Play-mode product transferred from Short to Incline, climbed, stopped with its motor, and resumed uphill at the adjusted speed.
- Actual incline mesh movement was checked against the uphill transport direction.
- All three types passed the moving-mesh, fixed-UV, rotating-roller and closed-seam checks after the bend-spacing correction.

Reports are in `Library/ConveyorModulePrefabReport.json`, `Library/ConveyorModuleRuntimeReport.json`, `Library/ConveyorRibSpacingReport.json` and `Library/ConveyorBeltGeometryRuntimeReport.json`.

Building explicitly arms the next Play session's temporary verification probes. They clean up their test objects afterward. Normal later Play sessions do not run those probes unless the relevant validation/build command is requested again.
