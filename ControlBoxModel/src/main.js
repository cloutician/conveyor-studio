import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { FontLoader } from 'three/addons/loaders/FontLoader.js';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { buildControlBox } from './model.js';

const $ = selector => document.querySelector(selector);
const viewport = $('#viewport');
const status = $('#status');
const state = { selected: 'a', levels: { a: 4, b: 4, c: 4 }, types: { a: 'generic', b: 'short', c: 'incline' }, linked: false, running: false, emergency: false };
const presentation = { docked: false, hidden: false };
let nextMachineIndex = 3;
const machineLabel = id => `${state.types[id][0].toUpperCase()}${state.types[id].slice(1)} · ${id.toUpperCase()}`;
const selectedLevel = () => state.levels[state.selected];
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
const report = message => { if (status.textContent !== message) status.textContent = message; };

try {
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.15;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFShadowMap;
  renderer.shadowMap.autoUpdate = false;
  renderer.shadowMap.needsUpdate = true;
  renderer.domElement.setAttribute('aria-label', '3D control box. Drag the background to orbit. Use the inspector controls for keyboard operation.');
  viewport.appendChild(renderer.domElement);
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0xdeded6);
  const pmrem = new THREE.PMREMGenerator(renderer);
  const room = new RoomEnvironment();
  const environment = pmrem.fromScene(room, 0.04);
  scene.environment = environment.texture;
  scene.environmentIntensity = 0.4;
  room.dispose();
  pmrem.dispose();
  const camera = new THREE.PerspectiveCamera(35, 1, 0.01, 15);
  camera.position.set(0.45, 0.2, 1);
  const orbit = new OrbitControls(camera, renderer.domElement);
  orbit.target.set(0, -0.025, -0.015);
  orbit.enableDamping = !reducedMotion;
  orbit.dampingFactor = 0.12;
  orbit.minDistance = 0.37;
  orbit.maxDistance = 3;
  orbit.maxPolarAngle = Math.PI * 0.94;
  const key = new THREE.DirectionalLight(0xfff6e8, 3.1);
  key.position.set(-0.75, 1.3, 1.1);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  key.shadow.camera.left = key.shadow.camera.bottom = -0.65;
  key.shadow.camera.right = key.shadow.camera.top = 0.65;
  key.shadow.camera.near = 0.15;
  key.shadow.camera.far = 4;
  key.shadow.normalBias = 0.0004;
  key.shadow.bias = -0.00001;
  scene.add(key);
  const fill = new THREE.DirectionalLight(0xe5f0ec, 1.1);
  fill.position.set(0.9, 0.3, -0.3);
  scene.add(fill, new THREE.HemisphereLight(0xf7f4e8, 0x8d9e92, 1.4));
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(20, 20), new THREE.ShadowMaterial({ color: 0x34483c, opacity: 0.19 }));
  floor.name = 'StudioFloor_NotExported';
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -0.311;
  floor.receiveShadow = true;
  scene.add(floor);

  const font = await new FontLoader().loadAsync('/assets/helvetiker_regular.typeface.json');
  const model = buildControlBox(font);
  const dockMount = new THREE.Group();
  dockMount.name = 'ScreenDock_NotExported';
  dockMount.add(model.root, model.dimensions);
  scene.add(dockMount);
  const restingBounds = new THREE.Box3().setFromObject(model.root);
  const boxCorners = [];
  for (const x of [restingBounds.min.x, restingBounds.max.x]) for (const y of [restingBounds.min.y, restingBounds.max.y]) for (const z of [restingBounds.min.z, restingBounds.max.z]) boxCorners.push(new THREE.Vector3(x, y, z));
  const enclosureBounds = new THREE.Box3().setFromObject(model.root.getObjectByName('Enclosure'));
  const enclosureCorners = [];
  for (const x of [enclosureBounds.min.x, enclosureBounds.max.x]) for (const y of [enclosureBounds.min.y, enclosureBounds.max.y]) for (const z of [enclosureBounds.min.z, enclosureBounds.max.z]) enclosureCorners.push(new THREE.Vector3(x, y, z));
  const dockMotion = { value: 0, velocity: 0 };
  const targets = { dial: 0, lever: 0, start: 0, stop: 0 };
  const motion = { dial: { value: 0, velocity: 0 }, lever: { value: 0, velocity: 0 }, start: { value: 0, velocity: 0 }, stop: { value: 0, velocity: 0 } };
  let grab = null, pressed = null, hover = null, cameraGoal = null, currentView = 'perspective', fitDistance = 1, lastTime = 0;
  let releaseStartAt = 0;
  const raycaster = new THREE.Raycaster();
  const mouse = new THREE.Vector2();
  const panelPlane = new THREE.Plane(new THREE.Vector3(0, 0, 1), -0.04);

  function refreshMachines() {
    $('#machine').replaceChildren(...Object.keys(state.levels).map(id => new Option(machineLabel(id), id)));
  }
  function machineId(index) {
    let id = '';
    for (let value = index + 1; value > 0; value = Math.floor((value - 1) / 26)) id = String.fromCharCode(97 + (value - 1) % 26) + id;
    return id;
  }
  function addMachine(type) {
    if (!['generic', 'short', 'incline'].includes(type)) return;
    const id = machineId(nextMachineIndex++);
    state.levels[id] = state.linked ? selectedLevel() : 4;
    state.types[id] = type;
    state.selected = id;
    refreshMachines();
    sync();
    report(`${machineLabel(id)} added · ${Object.keys(state.levels).length} machines`);
  }
  function sync() {
    targets.dial = THREE.MathUtils.degToRad(135 - selectedLevel() * 30);
    targets.lever = THREE.MathUtils.degToRad(state.linked ? -28 : 28);
    targets.stop = state.emergency ? -0.008 : 0;
    model.setDisplay(state.selected, state.types[state.selected]);
    model.setLighting(state.running && !state.emergency, state.linked);
    $('#machine').value = state.selected;
    $('#speed').value = selectedLevel();
    $('#speed-output').textContent = selectedLevel() === 0 ? 'OFF' : `${selectedLevel()} / 9`;
    $('#speed').setAttribute('aria-valuetext', selectedLevel() === 0 ? 'Off' : `Level ${selectedLevel()} of 9${state.running ? '' : '; setting saved until Start'}`);
    $('#link').checked = state.linked;
    const line = $('#line-state');
    line.dataset.state = state.emergency ? 'emergency' : state.running ? 'running' : 'ready';
    line.replaceChildren();
    const lamp = document.createElement('i');
    lamp.setAttribute('aria-hidden', 'true');
    line.append(lamp, document.createTextNode(state.emergency ? 'Emergency stop' : state.running ? 'Running' : 'Ready'));
    $('#machine-count').textContent = `${Object.keys(state.levels).length} machines`;
    $('#mesh-stats').textContent = `${(model.statistics().triangles / 1000).toFixed(0)}k triangles`;
  }
  function setLevel(value) {
    const next = THREE.MathUtils.clamp(Math.round(value), 0, 9);
    if (selectedLevel() === next) return;
    state.levels[state.selected] = next;
    if (state.linked) for (const id of Object.keys(state.levels)) state.levels[id] = next;
    sync();
    report(`${state.linked ? 'All conveyors' : machineLabel(state.selected)} · ${next === 0 ? 'OFF' : `speed level ${next}`}${state.running ? '' : ' · saved until Start'}`);
  }
  function toggleLink() {
    state.linked = !state.linked;
    if (state.linked) for (const id of Object.keys(state.levels)) state.levels[id] = selectedLevel();
    sync();
    report(state.linked ? 'Speeds synchronised · the switch and indicator lamp are on' : 'Individual speed control · sync switch off');
  }
  function startLine() {
    state.running = true;
    state.emergency = false;
    targets.start = -0.003;
    releaseStartAt = performance.now() + 120;
    sync();
    report(`Line started · ${machineLabel(state.selected)} at ${selectedLevel() === 0 ? 'OFF' : `level ${selectedLevel()}`}`);
  }
  function stopLine() {
    state.running = false;
    state.emergency = true;
    targets.start = 0;
    releaseStartAt = 0;
    sync();
    report('Emergency stop latched · press Start to release and resume');
  }
  const actions = { start: startLine, stop: stopLine, link: toggleLink, reopen: () => setBoxHidden(false) };

  function setRay(event) {
    const bounds = renderer.domElement.getBoundingClientRect();
    mouse.set((event.clientX - bounds.left) / bounds.width * 2 - 1, -(event.clientY - bounds.top) / bounds.height * 2 + 1);
    raycaster.setFromCamera(mouse, camera);
  }
  function controlAt(event) {
    setRay(event);
    const hit = raycaster.intersectObject(model.root, true)[0];
    if (presentation.hidden) return hit ? 'reopen' : null;
    let item = hit?.object;
    while (item && item !== model.root) { if (item.userData.control) return item.userData.control; item = item.parent; }
    return null;
  }
  function dialAngle(event) {
    setRay(event);
    model.root.updateWorldMatrix(true, false);
    panelPlane.setFromNormalAndCoplanarPoint(new THREE.Vector3(0, 0, 1).transformDirection(model.root.matrixWorld), model.root.localToWorld(new THREE.Vector3(0, 0, 0.04)));
    const hit = raycaster.ray.intersectPlane(panelPlane, new THREE.Vector3());
    if (!hit) return null;
    model.root.worldToLocal(hit);
    return Math.atan2(hit.x - model.dialCenter.x, hit.y - model.dialCenter.y);
  }
  function endInteraction(event, cancelled = false) {
    if (!grab || event.pointerId !== grab.pointerId) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const control = grab.control;
    const activate = !cancelled && control !== 'speed' && controlAt(event) === control;
    grab = null;
    pressed = null;
    orbit.enabled = true;
    if (renderer.domElement.hasPointerCapture(event.pointerId)) renderer.domElement.releasePointerCapture(event.pointerId);
    if (activate) actions[control]();
    else { targets.start = 0; targets.stop = state.emergency ? -0.008 : 0; sync(); }
    renderer.domElement.style.cursor = 'grab';
  }
  renderer.domElement.addEventListener('pointerdown', event => {
    if (event.button !== 0 || grab) return;
    const control = controlAt(event);
    if (!control) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    cameraGoal = null;
    orbit.enabled = false;
    grab = { control, pointerId: event.pointerId, previous: dialAngle(event), raw: -135 + selectedLevel() * 30 };
    pressed = control;
    if (control === 'start') targets.start = -0.003;
    if (control === 'stop') targets.stop = -0.008;
    renderer.domElement.setPointerCapture(event.pointerId);
    renderer.domElement.style.cursor = control === 'speed' ? 'grabbing' : 'pointer';
  }, { capture: true });
  renderer.domElement.addEventListener('pointermove', event => {
    if (grab) {
      if (event.pointerId !== grab.pointerId) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      if (grab.control === 'speed') {
        const angle = dialAngle(event);
        if (angle === null || grab.previous === null) return;
        let delta = THREE.MathUtils.radToDeg(angle - grab.previous);
        if (delta > 180) delta -= 360;
        if (delta < -180) delta += 360;
        grab.previous = angle;
        grab.raw = THREE.MathUtils.clamp(grab.raw + delta * 0.85, -135, 135);
        let next = selectedLevel();
        while (next < 9 && grab.raw - (-135 + next * 30) > 18) next++;
        while (next > 0 && grab.raw - (-135 + next * 30) < -18) next--;
        setLevel(next);
        const flex = Math.tanh((grab.raw - (-135 + selectedLevel() * 30)) / 10) * 4;
        targets.dial = THREE.MathUtils.degToRad(135 - selectedLevel() * 30 - flex);
      }
      return;
    }
    const control = controlAt(event);
    renderer.domElement.style.cursor = control ? (control === 'speed' ? 'grab' : 'pointer') : 'grab';
    if (control !== hover) {
      hover = control;
      if (control === 'speed') report('Speed knob · drag around its centre to engage the next detent');
      else if (control === 'link') report('Sync switch · press the projecting lever or its mounting nut');
      else if (control === 'start') report('Start · press the raised green cap');
      else if (control === 'stop') report('Emergency stop · press the mushroom cap');
      else if (control === 'reopen') report('Control box · click the exposed housing to slide it open');
    }
  }, { capture: true });
  renderer.domElement.addEventListener('pointerup', event => endInteraction(event), { capture: true });
  renderer.domElement.addEventListener('pointercancel', event => endInteraction(event, true), { capture: true });
  renderer.domElement.addEventListener('lostpointercapture', event => { if (grab?.pointerId === event.pointerId) endInteraction(event, true); });
  renderer.domElement.addEventListener('contextmenu', event => event.preventDefault());
  for (const type of ['touchstart', 'touchmove']) renderer.domElement.addEventListener(type, event => { if (event.cancelable) event.preventDefault(); event.stopPropagation(); }, { passive: false });
  addEventListener('keydown', event => {
    if (event.key === 'Escape' && grab) { const pointerId = grab.pointerId; endInteraction({ pointerId, preventDefault() {}, stopImmediatePropagation() {} }, true); }
  });
  orbit.addEventListener('start', () => { cameraGoal = null; currentView = null; updateViewButtons(); });

  const directions = { perspective: new THREE.Vector3(0.46, 0.21, 0.94), front: new THREE.Vector3(0, 0.035, 1), side: new THREE.Vector3(1, 0.09, 0.1), back: new THREE.Vector3(0.35, 0.13, -1) };
  function updateViewButtons() { document.querySelectorAll('[data-view]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.view === currentView))); }
  function setView(view, instant = false) {
    currentView = view;
    orbit.target.set(0, -0.025, -0.015);
    const position = directions[view].clone().normalize().multiplyScalar(fitDistance).add(orbit.target);
    if (instant || reducedMotion) { camera.position.copy(position); camera.lookAt(orbit.target); orbit.update(); cameraGoal = null; }
    else cameraGoal = position;
    updateViewButtons();
  }
  function resize() {
    const bounds = viewport.getBoundingClientRect();
    const previousFit = fitDistance;
    camera.aspect = bounds.width / Math.max(bounds.height, 1);
    fitDistance = Math.max(0.61, 0.46 / camera.aspect) / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2))) * 1.12;
    if (previousFit > 0 && !grab) camera.position.sub(orbit.target).multiplyScalar(fitDistance / previousFit).add(orbit.target);
    camera.updateProjectionMatrix();
    renderer.setSize(Math.max(1, bounds.width), Math.max(1, bounds.height), false);
    if (cameraGoal && currentView) cameraGoal.copy(directions[currentView]).normalize().multiplyScalar(fitDistance).add(orbit.target);
  }
  new ResizeObserver(resize).observe(viewport);
  document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => setView(button.dataset.view)));
  function updateDock() {
    $('#dock-toggle').setAttribute('aria-pressed', String(presentation.docked));
    $('#box-toggle').setAttribute('aria-expanded', String(!presentation.hidden));
    $('#box-toggle').setAttribute('aria-label', presentation.hidden ? 'Show control box' : 'Hide control box');
    $('#box-toggle-label').textContent = presentation.hidden ? 'Show box' : 'Hide box';
    $('#box-toggle-arrow').textContent = presentation.hidden ? '‹' : '›';
    floor.visible = !presentation.docked;
    renderer.shadowMap.needsUpdate = true;
  }
  function setBoxHidden(hidden) {
    if (grab) endInteraction({ pointerId: grab.pointerId, preventDefault() {}, stopImmediatePropagation() {} }, true);
    presentation.hidden = hidden;
    presentation.docked = true;
    updateDock();
    report(hidden ? 'Control box tucked away · click the visible housing to reopen' : 'Control box open · settings retained');
  }
  $('#dock-toggle').addEventListener('click', () => {
    presentation.docked = !presentation.docked;
    presentation.hidden = false;
    updateDock();
    report(presentation.docked ? 'Dock preview · control box follows the right screen edge' : 'Inspection view · orbit the control box');
  });
  $('#box-toggle').addEventListener('click', () => setBoxHidden(!presentation.hidden));
  $('#inspector-toggle').addEventListener('click', () => {
    const hidden = $('.studio').classList.toggle('inspector-hidden');
    $('#inspector-toggle').setAttribute('aria-expanded', String(!hidden));
  });
  $('#fullscreen').addEventListener('click', async () => {
    try { if (document.fullscreenElement) await document.exitFullscreen(); else await $('.studio').requestFullscreen(); }
    catch { report('Use your browser’s full-screen control to expand the inspector'); }
  });
  document.addEventListener('fullscreenchange', () => { $('#fullscreen').textContent = document.fullscreenElement ? 'Exit full screen' : 'Full screen'; resize(); });
  $('#machine').addEventListener('change', event => { state.selected = event.target.value; sync(); report(`${machineLabel(state.selected)} selected · its speed setting is preserved`); });
  $('#add-machine').addEventListener('click', () => addMachine($('#add-type').value));
  $('#speed').addEventListener('input', event => setLevel(Number(event.target.value)));
  $('#link').addEventListener('change', toggleLink);
  $('#start').addEventListener('click', startLine);
  $('#stop').addEventListener('click', stopLine);
  $('#wireframe').addEventListener('change', event => {
    model.root.traverse(item => { if (item.isMesh) { for (const material of Array.isArray(item.material) ? item.material : [item.material]) material.wireframe = event.target.checked; } });
    renderer.shadowMap.needsUpdate = true;
    $('#view-caption').textContent = event.target.checked ? 'Wireframe · inspect the actual mesh' : 'Solid geometry · no textures';
  });
  $('#dimensions').addEventListener('change', event => { model.dimensions.visible = event.target.checked; });

  async function exportAsset(download = true) {
    const fresh = buildControlBox(font);
    fresh.knob.rotation.z = THREE.MathUtils.degToRad(15);
    fresh.lever.rotation.x = THREE.MathUtils.degToRad(28);
    fresh.setLighting(false, false);
    fresh.root.userData.defaultState = { selected: 'a', levels: { a: 4, b: 4, c: 4 }, types: { a: 'generic', b: 'short', c: 'incline' }, linked: false, running: false, emergency: false };
    fresh.root.userData.machineCountLimit = null;
    fresh.root.updateMatrixWorld(true);
    try {
      const data = await new GLTFExporter().parseAsync(fresh.root, { binary: true, trs: true, onlyVisible: true });
      if (download) {
        const url = URL.createObjectURL(new Blob([data], { type: 'model/gltf-binary' }));
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'control-box.glb';
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 1500);
      }
      return data;
    } finally {
      fresh.root.traverse(item => { if (item.isMesh) item.geometry.dispose(); });
      fresh.dimensions.traverse(item => { if (item.geometry) item.geometry.dispose(); });
      Object.values(fresh.mats).forEach(material => material.dispose());
    }
  }
  $('#export').addEventListener('click', async () => {
    $('#export').disabled = true;
    report('Exporting separate parts and pivots…');
    try { const data = await exportAsset(); report(`GLB exported · ${(data.byteLength / 1024 / 1024).toFixed(1)} MB · neutral pose · no textures`); }
    catch (error) { console.error(error); report(`Export failed: ${error.message}`); }
    finally { $('#export').disabled = false; }
  });

  function spring(part, target, dt, stiffness = 340) {
    if (reducedMotion) { part.value = target; part.velocity = 0; return; }
    part.velocity += ((target - part.value) * stiffness - part.velocity * 28) * dt;
    part.value += part.velocity * dt;
  }
  refreshMachines();
  sync();
  motion.dial.value = targets.dial;
  motion.lever.value = targets.lever;
  resize();
  setView('perspective', true);
  $('#loading').hidden = true;
  report('Model ready · orbit the box or operate a control');
  document.body.dataset.ready = 'true';
  renderer.domElement.style.cursor = 'grab';
  function animate(time) {
    const dt = lastTime ? Math.min((time - lastTime) / 1000, 0.25) : 0;
    lastTime = time;
    if (releaseStartAt && time >= releaseStartAt && pressed !== 'start') { targets.start = 0; releaseStartAt = 0; }
    const steps = Math.max(1, Math.ceil(dt / 0.012));
    for (let i = 0; i < steps; i++) {
      spring(motion.dial, targets.dial, dt / steps);
      spring(motion.lever, targets.lever, dt / steps);
      spring(motion.start, targets.start, dt / steps, 520);
      spring(motion.stop, targets.stop, dt / steps, 390);
    }
    if (Math.abs(motion.dial.velocity) > 0.0001 || Math.abs(motion.lever.velocity) > 0.0001 || Math.abs(motion.start.velocity) > 0.00001 || Math.abs(motion.stop.velocity) > 0.00001) renderer.shadowMap.needsUpdate = true;
    model.knob.rotation.z = motion.dial.value;
    model.lever.rotation.x = motion.lever.value;
    model.start.position.z = motion.start.value;
    model.stop.position.z = motion.stop.value;
    if (cameraGoal) {
      camera.position.lerp(cameraGoal, 1 - Math.exp(-dt * 10));
      camera.lookAt(orbit.target);
      if (camera.position.distanceTo(cameraGoal) < 0.0005) { camera.position.copy(cameraGoal); cameraGoal = null; orbit.update(); }
    } else if (orbit.enabled) orbit.update();
    let dockTarget = 0;
    if (presentation.docked) {
      camera.updateMatrixWorld();
      const width = viewport.getBoundingClientRect().width;
      const edge = presentation.hidden ? 1 - 2 * Math.min(72, width * 0.22) / width : 0.9;
      const forward = camera.getWorldDirection(new THREE.Vector3());
      const projectionScale = Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)) * camera.aspect;
      const offsets = (presentation.hidden ? enclosureCorners : boxCorners).map(point => {
        const depth = point.clone().sub(camera.position).dot(forward);
        return (edge - point.clone().project(camera).x) * depth * projectionScale;
      });
      dockTarget = presentation.hidden ? Math.max(...offsets) : Math.min(...offsets);
    }
    if (grab) dockMotion.velocity = 0;
    else for (let i = 0; i < steps; i++) spring(dockMotion, dockTarget, dt / steps, 280);
    if (Math.abs(dockMotion.velocity) > 0.0001) renderer.shadowMap.needsUpdate = true;
    dockMount.position.setFromMatrixColumn(camera.matrixWorld, 0).multiplyScalar(dockMotion.value);
    if (!document.hidden) renderer.render(scene, camera);
    requestAnimationFrame(animate);
  }
  requestAnimationFrame(animate);
  document.addEventListener('visibilitychange', () => { lastTime = 0; });
  window.controlBoxInspector = Object.freeze({
    getState: () => structuredClone(state), getPresentation: () => ({ ...presentation, offsetMetres: dockMotion.value }), asset: model.root, camera, renderer, setView, exportAsset,
    getControlPoint(control) {
      const object = { speed: model.knob, link: model.lever, start: model.start, stop: model.stop }[control];
      const point = object.localToWorld(new THREE.Vector3(0, control === 'speed' ? 0.025 : 0, control === 'speed' ? 0.034 : control === 'link' ? 0.032 : control === 'start' ? 0.022 : 0.034));
      const screen = point.clone().project(camera), bounds = renderer.domElement.getBoundingClientRect();
      return { x: bounds.left + (screen.x + 1) * bounds.width / 2, y: bounds.top + (1 - screen.y) * bounds.height / 2, world: point.toArray() };
    },
  });
} catch (error) {
  console.error(error);
  $('#loading').textContent = 'The 3D viewer could not start. Open this page in a browser with WebGL enabled.';
  report(error.message);
}
