# Product physics

Open **Assets/Scenes/ConveyorStudio.unity**, press **Play** and place conveyors.
The box/can picture buttons feed the selected belt, including a belt partway along
a line. Multiple selected belts each receive one product. With no selection, the
buttons feed the first belt of every disconnected line. Blocked infeeds are skipped
while clear targets receive one product each. Use the 3D control box to start, stop
or adjust the selected machines.

## Physical behaviour

- Products have gravity, unconstrained rotation, mass, centre of mass and shape-based
  inertia. Off-centre impacts cause rotation; collisions transfer momentum.
- Boxes slide, tip and stack. Cans stand on flat ends, tip and roll on their sides.
  Static and sliding friction, low restitution and contact-only rolling resistance
  dissipate energy. Small air damping avoids a heavy artificial drag effect.
- Floor contact has stronger grip and zero restitution. Substantial box impacts
  absorb some horizontal and angular momentum through dissipative impulses, as an
  approximation of cardboard cushioning. Additional angular resistance acts only
  while a box contacts the floor, reducing continued tumbling and scooting. It does
  not lock position or rotation. **Floor Angular Resistance** is editable on boxes.
- The motor supplies tangential surface velocity at belt contacts using Unity's
  contact solver. Friction supplies the actual product force and torque; products
  are never locked to a path, orientation or belt speed. They can slip under load.
- Spawning represents an infeed delivering a product at the current belt speed.
  This sets its initial velocity once. All later motion comes from physics.
- Stopping a motor preserves inertia; rubber friction brakes the products. Restarting
  wakes resting products. A stopped downstream belt can cause physical accumulation.
- Open belt ends allow products to fall onto the floor. Removing a machine leaves
  its products alive. The first contact with the tiled floor starts a **5 second**
  cleanup timer, followed by an eased **0.8 second fade** before the entire product
  is removed. Each product uses temporary material copies during its fade, preserving
  other products and the original material assets. Bouncing or continued
  floor contact does not reset the timer. Touching off-belt products form a shared
  cleanup batch, including sideways congestion and vertical stacks. A new box
  joining a batch gives it at least **2 quiet seconds** before fading. An arrival
  that already touched the floor merges its entire batch too; the shared deadline
  retains the later of the existing countdowns. Every member uses the same fade
  progress and removal time, even if the pile subsequently separates. Recontacts
  within that same batch do not extend the countdown. Once fading begins, its
  deadline is committed and opacity only decreases. A later arrival cannot revive
  older products or inherit their active fade; it joins a new cleanup batch when
  it reaches the floor. Products still touching conveyors
  cannot join a ground pile; separate, non-touching products keep independent
  countdowns. A stack supported by a conveyor has no cleanup timer until it reaches
  the floor. Floor contact is
  identified by the ground's **Tile** physics material.
  Dragging a machine temporarily disables its solids so moving
  a kinematic machine cannot sweep products away violently; unsupported products fall.
  Picking up a machine wakes nearby products and every touching stack layer before
  its collision surfaces are disabled. Moved, disabled and destroyed conveyor
  contacts are discarded. Sleeping products retain actual upward-contact poses;
  if those supports disappear or move, a short sweep of the real product shape
  checks for replacement support and wakes unsupported products automatically.
  Removing a box also wakes surviving products above it. Supported resting products
  retain normal physics sleeping; gravity, momentum and rotation remain unconstrained.
- Connected ports receive powered transfer rollers spanning the rounded-end gap.
  Transfer speed is the slower of the two motors, and stops if either motor stops.
  The short transfer deck has a smooth collision surface over the visible rollers.
  It sits 1 mm below the belt tangent surfaces, avoiding a raised vertical edge
  that catches a box's lower corner. The next belt's rounded nose provides the
  exit lead-in. Rebuilding a join also wakes any resting product at that join.
- Frame, guard and support geometry also collide. Extended support collision meshes
  are rebuilt when placement finishes. Picking volumes remain separate triggers.

## Editable presets

| Product | Supplied visual dimensions | Mass preset | Collision shape |
| --- | --- | --- | --- |
| Box | 263 × 269 × 528 mm | 2 kg | Fitted box |
| Can | 60 × 155 mm | 0.27 kg | Smooth cylindrical side contact with inset flat-ended hull |

The can uses a capsule for smooth rolling contact and a 64-sided hull, radially inset
by 10%, for flat-end and shoulder contact. This avoids the severe artificial rolling
energy loss measured with a faceted cylinder alone. It approximates the shoulders;
it is not an exact scan of the can. Its inertia is that of a uniformly filled cylinder.

Prefabs: **Assets/Prefabs/PhysicalBox.prefab** and **PhysicalCan.prefab**. Edit the
Rigidbody mass/inertia and **Physics Product → Rolling Resistance** as needed. Masses
are plausible starting assumptions, not measurements of the supplied assets.

Physics materials are in **Assets/Resources/Physics/**:

| Material | Static friction | Sliding friction | Restitution |
| --- | --- | --- | --- |
| Cardboard | 0.60 | 0.45 | 0.04 |
| Can | 0.45 | 0.32 | 0.08 |
| Rubber | 0.85 | 0.70 | 0.015 |
| Steel | 0.45 | 0.30 | 0.04 |
| Tile | 0.90 | 0.80 | 0 |

Friction combines by average except the floor, which uses **Maximum** so a lower
product coefficient cannot weaken its grip. Restitution uses the lower value.
These are tunable presets. Floor grout is visual texture detail over a flat
physical floor. The generated box preset uses floor angular resistance of 12/s;
substantial impacts absorb 65% of tangential and 80% of angular momentum once.

The project runs physics at **120 Hz**, with **16 position / 8 velocity solver
iterations**, Temporal Gauss-Seidel, interpolation and continuous collision detection.
These improve small-object stability at a higher CPU cost. Belt collision hulls are
cooked at metre scale independently of the original FBX's small units.

## Verification and maintenance

**Tools → Conveyor Studio → Build Physical Products** regenerates the physical
prefabs, collision hull and material presets and updates the runtime catalogue.
It resets manual edits to these generated presets. It uses a preview scene and
preserves the user's open scene. The original ProductBox prefab and legacy demo
path follower remain available for the source scenes.

**Tools → Conveyor Studio → Verify Product Physics** runs isolated 120 Hz simulations
immediately in Play mode, or arms the next Play session. `Library/ConveyorPhysicsRuntimeReport.json` records 69 checks:
free fall; unconstrained bodies; floor collision and settling; momentum conservation;
off-centre rotation; tile friction; can rolling and resistance; belt acceleration,
braking and restart; open-end falls; transfers; incline travel; box/can stacking;
support removal; impacts between different product types; and touchdown travel and
settling after actual Generic/Short/Incline belt falls at 0.5, 1 and 2 m/s. Sleeping
support regressions cover box/can release during held drags on all three belt types,
stale contact removal, deleting a stopped belt under a complete stack, deleting a
supporting box, preserving unrelated sleepers and recovering unsupported sleeping
boxes/cans. Build also arms this
verification for the next Play session. Verification fixtures are temporary and
excluded from player builds.

**Tools → Conveyor Studio → Verify Stack Cleanup** runs immediately in Play mode,
or arms the next Play session. It uses an isolated physics scene and the real game
clock. `Library/ConveyorCleanupRuntimeReport.json` records checks covering a
three-box stack, a later can joining its countdown, identical fade opacity, complete
stack removal, independent side-by-side cleanup, conveyor exclusion and unchanged
source materials, plus repeated fresh-box grace periods, conveyor-contact exclusion,
same-batch recontacts, sideways congestion, floor-first batch merges, irreversible
fading after late arrivals and independent cleanup for newcomers. The temporary fixture removes itself after
roughly eleven seconds.

**Tools → Conveyor Studio → Verify All Product Transfers** checks boxes and cans
through all nine ordered Generic/Short/Incline pairings in an isolated physics scene.
`Library/ConveyorTransferRuntimeReport.json` records the 18 transfer outcomes and a
snapshot of the running workspace's motor and connector state. It requires a box
to travel well beyond the join and a can's full footprint to clear it; a narrow can
may still tip later on a steep incline under its unconstrained physical simulation.

This is a detailed real-time rigid-body simulation. Cardboard crushing, metal dents,
liquid sloshing and flexible-belt deformation require different simulation models
and are outside this implementation.

Unity API references: [contact target velocity](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/modifiablecontactpair/settargetvelocity)
and [contact modification events](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/physics/contactmodifyevent).
