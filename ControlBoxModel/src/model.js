import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { TextGeometry } from 'three/addons/geometries/TextGeometry.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

const mm = value => value / 1000;

export function buildControlBox(font) {
  const root = new THREE.Group();
  root.name = 'ControlBox';
  root.userData = { asset: 'CB-01', units: 'metres', housingDimensions: [0.34, 0.53, 0.08], frontAxis: '+Z', untextured: true };
  const material = (name, color, roughness = 0.56, metalness = 0.12) => {
    const value = new THREE.MeshStandardMaterial({ color, roughness, metalness });
    value.name = name;
    return value;
  };
  const mats = {
    housing: material('Housing_Plain', 0x71817e, 0.55, 0.22),
    plate: material('Faceplate_Plain', 0xc7cdc2, 0.64, 0.06),
    rear: material('BackCover_Plain', 0x62726f, 0.6, 0.16),
    fitting: material('Fittings_Plain', 0xa5b0aa, 0.34, 0.48),
    graphite: material('Knob_Plain', 0x303c3b, 0.57, 0.04),
    cap: material('KnobCap_Plain', 0x3d4b47, 0.5, 0.05),
    dark: material('Recess_Plain', 0x152a28, 0.66, 0.03),
    ink: material('RaisedMarkings_Plain', 0x344c46, 0.62, 0.02),
    warning: material('EmergencyMarking_Red', 0xa72d29, 0.65, 0.02),
    pale: material('ButtonLettering_Plain', 0xe7e9d5, 0.62, 0.02),
    pointer: material('KnobPointer_Plain', 0xddc586, 0.58, 0.06),
    green: material('StartCap_Plain', 0x426e50, 0.4, 0.04),
    red: material('EmergencyCap_Plain', 0xb7473d, 0.42, 0.03),
    yellow: material('EmergencyCollar_Plain', 0xd9b45c, 0.59, 0.03),
    led: new THREE.MeshBasicMaterial({ name: 'DisplayDots_Unlit', color: 0x80d7b2, toneMapped: false }),
    lamp: material('LinkLamp_Plain', 0x375a47, 0.34, 0.03),
  };

  function mesh(geometry, surface, name, parent = root, position = [0, 0, 0]) {
    const item = new THREE.Mesh(geometry, surface);
    item.name = name;
    item.position.set(...position);
    item.castShadow = true;
    item.receiveShadow = true;
    parent.add(item);
    return item;
  }
  function rounded(w, h, d, radius, surface, name, parent, position) {
    return mesh(new RoundedBoxGeometry(w, h, d, 2, radius), surface, name, parent, position);
  }
  function cylinder(radius, depth, surface, name, parent, position, segments = 48, topRadius = radius) {
    const geometry = new THREE.CylinderGeometry(topRadius, radius, depth, segments);
    geometry.rotateX(Math.PI / 2);
    return mesh(geometry, surface, name, parent, position);
  }
  function ring(outer, inner, depth, surface, name, parent, position, segments = 64) {
    const shape = new THREE.Shape();
    shape.absarc(0, 0, outer, 0, Math.PI * 2, false);
    const hole = new THREE.Path();
    hole.absarc(0, 0, inner, 0, Math.PI * 2, true);
    shape.holes.push(hole);
    const geometry = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: false, curveSegments: segments / 4, steps: 1 });
    geometry.translate(0, 0, -depth / 2);
    return mesh(geometry, surface, name, parent, position);
  }
  function roundedPath(w, h, r) {
    const shape = new THREE.Shape();
    const x = -w / 2, y = -h / 2;
    shape.moveTo(x + r, y);
    shape.lineTo(x + w - r, y);
    shape.quadraticCurveTo(x + w, y, x + w, y + r);
    shape.lineTo(x + w, y + h - r);
    shape.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    shape.lineTo(x + r, y + h);
    shape.quadraticCurveTo(x, y + h, x, y + h - r);
    shape.lineTo(x, y + r);
    shape.quadraticCurveTo(x, y, x + r, y);
    return shape;
  }
  function frame(w, h, innerW, innerH, depth, surface, name, parent, position) {
    const shape = roundedPath(w, h, mm(7));
    const hole = new THREE.Path(roundedPath(innerW, innerH, mm(3)).getPoints(6).reverse());
    shape.holes.push(hole);
    const geometry = new THREE.ExtrudeGeometry(shape, { depth, steps: 1, curveSegments: 5, bevelEnabled: true, bevelSize: mm(0.6), bevelThickness: mm(0.6), bevelSegments: 2 });
    geometry.translate(0, 0, -depth / 2);
    return mesh(geometry, surface, name, parent, position);
  }
  function lettering(text, size, position, parent = root, surface = mats.ink, name = text) {
    const geometry = new TextGeometry(text, { font, size, depth: mm(0.45), curveSegments: 3, bevelEnabled: false });
    geometry.computeBoundingBox();
    const box = geometry.boundingBox;
    geometry.translate(-(box.max.x + box.min.x) / 2, -(box.max.y + box.min.y) / 2, 0);
    return mesh(geometry, surface, name.replaceAll(' ', '_'), parent, position);
  }
  function screw(x, y, z, parent = root, backwards = false) {
    const screwRoot = new THREE.Group();
    screwRoot.name = 'Fastener';
    screwRoot.position.set(x, y, z);
    if (backwards) screwRoot.rotation.y = Math.PI;
    parent.add(screwRoot);
    cylinder(mm(3.7), mm(2.8), mats.fitting, 'Head', screwRoot, [0, 0, 0], 16);
    rounded(mm(4.6), mm(0.65), mm(0.25), mm(0.1), mats.dark, 'Slot', screwRoot, [0, 0, mm(1.45)]);
    rounded(mm(0.65), mm(4.6), mm(0.25), mm(0.1), mats.dark, 'CrossSlot', screwRoot, [0, 0, mm(1.45)]);
  }

  const housing = new THREE.Group();
  housing.name = 'Housing';
  root.add(housing);
  rounded(mm(340), mm(530), mm(80), mm(12), mats.housing, 'Enclosure', housing, [0, 0, -mm(40)]);
  frame(mm(325), mm(514), mm(307), mm(494), mm(5), mats.fitting, 'RaisedFrontRim', housing, [0, 0, mm(2)]);
  rounded(mm(307), mm(494), mm(4), mm(5), mats.plate, 'RecessedFaceplate', housing, [0, 0, mm(0.5)]);
  rounded(mm(310), mm(495), mm(7), mm(8), mats.rear, 'RearCover', housing, [0, 0, -mm(82)]);
  for (const x of [-mm(142), mm(142)]) for (const y of [-mm(231), mm(231)]) screw(x, y, mm(4.2), housing);
  for (const x of [-mm(130), mm(130)]) for (const y of [-mm(210), mm(210)]) screw(x, y, -mm(87), housing, true);
  for (const y of [-mm(125), mm(125)]) {
    rounded(mm(8), mm(46), mm(23), mm(3), mats.housing, 'SideHinge', housing, [-mm(172), y, -mm(27)]);
    const pin = new THREE.CylinderGeometry(mm(2.5), mm(2.5), mm(51), 12);
    mesh(pin, mats.fitting, 'HingePin', housing, [-mm(176), y, -mm(24)]);
  }
  const glandCentreY = -mm(172.5), glandSpacing = mm(69) * 1.4;
  for (const [index, y] of [glandCentreY + glandSpacing / 2, glandCentreY - glandSpacing / 2].entries()) {
    const gland = new THREE.Group();
    gland.name = index === 0 ? 'CableGland_Upper' : 'CableGland_Lower';
    gland.position.set(mm(170), y, -mm(41));
    gland.rotation.y = Math.PI / 2;
    gland.userData = { axis: '+X', projectionMetres: 0.05 };
    housing.add(gland);
    cylinder(mm(16), mm(8), mats.graphite, 'GlandNut', gland, [0, 0, 0], 6);
    cylinder(mm(10), mm(42), mats.graphite, 'GlandBody', gland, [0, 0, mm(22)], 24);
    for (let i = 0; i < 7; i++) ring(mm(11.5), mm(8), mm(1.7), mats.cap, `GlandRib_${i}`, gland, [0, 0, mm(7 + i * 5)], 24);
    ring(mm(10.5), mm(6), mm(6), mats.graphite, 'GlandOutlet', gland, [0, 0, mm(47)], 32);
    cylinder(mm(6), mm(1), mats.dark, 'CableRecess', gland, [0, 0, mm(44)], 24);
  }
  lettering('CB-01', mm(7.5), [0, -mm(235), mm(3.1)], root, mats.ink, 'AssetMark');

  const display = new THREE.Group();
  display.name = 'MachineDisplay';
  display.position.set(0, mm(184), 0);
  root.add(display);
  frame(mm(282), mm(79), mm(257), mm(53), mm(10), mats.graphite, 'DisplayBezel', display, [0, 0, mm(9)]);
  rounded(mm(258), mm(54), mm(2), mm(3), mats.dark, 'RecessedDisplay', display, [0, 0, mm(7)]);
  let dots = null;
  const glyphs = {
    A:['01110','10001','10001','11111','10001','10001','10001'], B:['11110','10001','10001','11110','10001','10001','11110'],
    C:['01111','10000','10000','10000','10000','10000','01111'], D:['11110','10001','10001','10001','10001','10001','11110'],
    E:['11111','10000','10000','11110','10000','10000','11111'], F:['11111','10000','10000','11110','10000','10000','10000'],
    G:['01110','10001','10000','10111','10001','10001','01110'], H:['10001','10001','10001','11111','10001','10001','10001'],
    I:['11111','00100','00100','00100','00100','00100','11111'], J:['00111','00010','00010','00010','10010','10010','01100'],
    K:['10001','10010','10100','11000','10100','10010','10001'], L:['10000','10000','10000','10000','10000','10000','11111'],
    M:['10001','11011','10101','10101','10001','10001','10001'], N:['10001','11001','11001','10101','10011','10011','10001'],
    O:['01110','10001','10001','10001','10001','10001','01110'], P:['11110','10001','10001','11110','10000','10000','10000'],
    Q:['01110','10001','10001','10001','10101','10010','01101'], R:['11110','10001','10001','11110','10100','10010','10001'],
    S:['01111','10000','10000','01110','00001','00001','11110'], T:['11111','00100','00100','00100','00100','00100','00100'],
    U:['10001','10001','10001','10001','10001','10001','01110'], V:['10001','10001','10001','10001','10001','01010','00100'],
    W:['10001','10001','10001','10101','10101','10101','01010'], X:['10001','10001','01010','00100','01010','10001','10001'],
    Y:['10001','10001','01010','00100','00100','00100','00100'], Z:['11111','00001','00010','00100','01000','10000','11111'],
    ' ':['00000','00000','00000','00000','00000','00000','00000'],
  };
  function setDisplay(letter, type = 'generic') {
    const lines = [type.toUpperCase(), `UNIT ${letter.toUpperCase()}`];
    const text = lines.join('\n');
    if (dots?.userData.text === text) return;
    if (dots) { display.remove(dots); dots.geometry.dispose(); }
    const pieces = [], dot = new THREE.CircleGeometry(mm(0.9), 10);
    lines.forEach((line, lineIndex) => {
      const cell = Math.min(mm(3), mm(237) / (line.length * 6 - 1));
      const startX = -((line.length - 1) * 6 + 4) * cell / 2;
      [...line].forEach((char, index) => (glyphs[char] || glyphs[' ']).forEach((row, y) => [...row].forEach((bit, x) => {
        if (bit === '1') pieces.push(dot.clone().translate(startX + (index * 6 + x) * cell, (lineIndex === 0 ? mm(11.5) : -mm(11.5)) + (3 - y) * cell, mm(8.3)));
      })));
    });
    dots = mesh(mergeGeometries(pieces), mats.led, 'FlatDotMatrix', display);
    dots.castShadow = dots.receiveShadow = false;
    dots.userData.text = text;
    dots.userData.surface = 'planar display pixels';
    pieces.forEach(item => item.dispose());
    dot.dispose();
  }
  setDisplay('a');

  const dialCenter = new THREE.Vector3(0, mm(27), mm(5));
  lettering('BELT SPEED', mm(9.5), [0, mm(127), mm(3.1)]);
  ring(mm(58), mm(47), mm(5), mats.fitting, 'SpeedDialBezel', root, dialCenter.toArray());
  const tickPieces = [];
  for (let level = 0; level < 10; level++) {
    const angle = THREE.MathUtils.degToRad(-135 + level * 30), radius = mm(72);
    const tick = new THREE.BoxGeometry(mm(1.6), mm(6), mm(1));
    tick.rotateZ(-angle).translate(Math.sin(angle) * radius, dialCenter.y + Math.cos(angle) * radius, mm(3.1));
    tickPieces.push(tick);
    lettering(level === 0 ? 'OFF' : String(level), level === 0 ? mm(8) : mm(10.4), [Math.sin(angle) * mm(88), dialCenter.y + Math.cos(angle) * mm(88), mm(3.1)], root, mats.ink, `SpeedMark_${level}`);
  }
  mesh(mergeGeometries(tickPieces), mats.ink, 'RaisedDetentMarks');
  tickPieces.forEach(item => item.dispose());
  const knob = new THREE.Group();
  knob.name = 'SpeedKnob_Pivot';
  knob.position.copy(dialCenter);
  knob.userData = { control: 'speed', axis: 'localZ', detents: 10, angleDegrees: [-135, 135] };
  root.add(knob);
  cylinder(mm(46), mm(25), mats.graphite, 'KnobCore', knob, [0, 0, mm(16)]);
  cylinder(mm(43), mm(9), mats.cap, 'KnobFace', knob, [0, 0, mm(29)], 64, mm(41));
  const gripPieces = [];
  for (let i = 0; i < 48; i++) {
    const angle = i / 48 * Math.PI * 2;
    const tooth = new RoundedBoxGeometry(mm(2.2), mm(4), mm(22), 1, mm(0.6));
    tooth.rotateZ(-angle).translate(Math.sin(angle) * mm(46.1), Math.cos(angle) * mm(46.1), mm(16));
    gripPieces.push(tooth);
  }
  mesh(mergeGeometries(gripPieces), mats.graphite, 'RaisedKnurledGrip', knob);
  gripPieces.forEach(item => item.dispose());
  rounded(mm(3), mm(18), mm(1.8), mm(0.65), mats.pointer, 'RaisedPointer', knob, [0, mm(24), mm(34)]);

  const link = new THREE.Group();
  link.name = 'LinkSwitch';
  link.position.set(-mm(19), -mm(105), mm(5));
  link.userData.control = 'link';
  root.add(link);
  ring(mm(19), mm(8), mm(4), mats.fitting, 'SwitchWasher', link, [0, 0, 0], 40);
  cylinder(mm(11.5), mm(5), mats.fitting, 'SwitchHexNut', link, [0, 0, mm(3)], 6);
  const lever = new THREE.Group();
  lever.name = 'LinkLever_Pivot';
  lever.position.z = mm(6);
  lever.userData = { control: 'link', axis: 'localX', angleDegrees: [-28, 28] };
  link.add(lever);
  cylinder(mm(3.4), mm(30), mats.fitting, 'LeverShaft', lever, [0, 0, mm(15)], 16);
  rounded(mm(10), mm(10), mm(20), mm(4.5), mats.cap, 'LeverGrip', lever, [0, 0, mm(32)]);
  lettering('ON', mm(7.5), [-mm(55), -mm(87), mm(3.1)]);
  lettering('OFF', mm(7.5), [-mm(55), -mm(123), mm(3.1)]);
  lettering('SYNC SPEEDS', mm(8.7), [0, -mm(67), mm(3.1)]);
  ring(mm(5), mm(3.7), mm(3), mats.fitting, 'LinkLampBezel', root, [mm(27), -mm(105), mm(4)]);
  const lamp = mesh(new THREE.SphereGeometry(mm(3.8), 16, 12), mats.lamp, 'LinkLampLens', root, [mm(27), -mm(105), mm(5)]);

  function makeButton(id, x, emergency) {
    const assembly = new THREE.Group();
    assembly.name = emergency ? 'EmergencyStop' : 'StartButton';
    assembly.position.set(x, -mm(193), mm(5));
    assembly.userData.control = id;
    root.add(assembly);
    ring(emergency ? mm(41) : mm(31), emergency ? mm(23) : mm(22), mm(5), emergency ? mats.yellow : mats.fitting, 'RaisedCollar', assembly, [0, 0, 0]);
    cylinder(mm(22), mm(9), mats.graphite, 'ButtonSocket', assembly, [0, 0, mm(3)]);
    const cap = new THREE.Group();
    cap.name = emergency ? 'EmergencyCap_Pivot' : 'StartCap_Pivot';
    cap.userData = { control: id, axis: 'localZ', pressTravelMetres: emergency ? 0.008 : 0.003 };
    assembly.add(cap);
    const profile = emergency
      ? [[0,0],[.019,0],[.020,.009],[.029,.012],[.031,.016],[.031,.022],[.029,.026],[.024,.028],[0,.028]]
      : [[0,0],[.021,0],[.023,.003],[.023,.013],[.021,.016],[0,.016]];
    const head = new THREE.LatheGeometry(profile.map(([r,z]) => new THREE.Vector2(r,z)), 64);
    head.rotateX(Math.PI / 2);
    mesh(head, emergency ? mats.red : mats.green, 'ButtonCap', cap, [0, 0, mm(5)]);
    lettering(emergency ? 'STOP' : 'START', emergency ? mm(9.3) : mm(8.5), [0, 0, emergency ? mm(33.2) : mm(21.2)], cap, mats.pale, emergency ? 'StopLettering' : 'StartLettering');
    lettering(emergency ? 'EMERGENCY' : 'START', mm(7.5), [x, emergency ? -mm(133) : -mm(152), mm(3.1)], root, emergency ? mats.warning : mats.ink);
    return cap;
  }
  const start = makeButton('start', -mm(76), false);
  const stop = makeButton('stop', mm(76), true);

  const dimensions = new THREE.Group();
  dimensions.name = 'InspectionDimensions_NotExported';
  dimensions.visible = false;
  const dimensionMaterial = new THREE.LineBasicMaterial({ color: 0x5f776c });
  function measure(points) { dimensions.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points.map(p => new THREE.Vector3(...p))), dimensionMaterial)); }
  measure([[-.17,-.333,0],[.17,-.333,0]]);
  measure([[-.17,-.327,0],[-.17,-.339,0]]); measure([[.17,-.327,0],[.17,-.339,0]]);
  measure([[-.222,-.265,0],[-.222,.265,0]]);
  measure([[-.216,-.265,0],[-.228,-.265,0]]); measure([[-.216,.265,0],[-.228,.265,0]]);
  lettering('340 mm', mm(8), [0,-.353,0], dimensions, mats.ink, 'WidthLabel');
  const heightLabel = lettering('530 mm', mm(8), [-.242,0,0], dimensions, mats.ink, 'HeightLabel');
  heightLabel.rotation.z = Math.PI / 2;

  function setLighting(running, linked) {
    mats.green.color.set(running ? 0x65b979 : 0x426e50);
    mats.green.emissive.set(running ? 0x275d35 : 0x000000);
    mats.green.emissiveIntensity = running ? 0.22 : 0;
    mats.lamp.color.set(linked ? 0x72cf91 : 0x375a47);
    mats.lamp.emissive.set(linked ? 0x45a969 : 0x000000);
    mats.lamp.emissiveIntensity = linked ? 0.5 : 0;
  }
  function statistics() {
    let meshes = 0, triangles = 0;
    root.traverse(item => { if (item.isMesh) { meshes++; triangles += (item.geometry.index?.count || item.geometry.attributes.position.count) / 3; } });
    return { meshes, triangles: Math.round(triangles) };
  }
  return { root, knob, lever, start, stop, lamp, dimensions, mats, dialCenter, setDisplay, setLighting, statistics };
}
