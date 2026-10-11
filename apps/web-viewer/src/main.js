// room.html entry point: reads the embedded room, draws it with three.js, and
// wires the view-and-measure tools (ADR-0013 section 3, task R8).
//
// View and measure only. Nothing here edits the room, saves anything or makes
// a network request: every model arrives base64-encoded inside the page.

import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

import { parseRoomData } from './data.js';
import { objectTransform } from './placement.js';
import { describeMeasurement } from './measure.js';
import { objectDetails, objectSummary, objectTitle, roomDetails } from './objectInfo.js';

// Agreed with the phone's room.glb writer (task R6) and recorded in
// docs/contracts/export-bundle-v1.md: the node holding the ceiling geometry.
const CEILING_NODE = 'ceiling';

const CLICK_SLOP_PX = 5;

const el = (id) => document.getElementById(id);

start().catch((error) => {
  console.error(error);
  showMessage('This room could not be displayed. The file may be damaged; export it again from the phone.');
});

async function start() {
  const parsed = parseRoomData(el('ghostmap-data')?.textContent ?? '');
  if (!parsed.ok) {
    showMessage(parsed.error);
    return;
  }

  if (!webglAvailable()) {
    showMessage('This browser cannot draw 3D graphics (WebGL is turned off or unsupported). Try another browser.');
    return;
  }

  const { scene: snapshot, warnings } = parsed;
  const room = snapshot.room;
  const objects = Array.isArray(room.objects) ? room.objects : [];

  document.title = `${room.name || 'Room'} · GhostMap`;
  el('room-title').textContent = room.name || 'Untitled room';
  fillRows(el('room-details'), roomDetails(snapshot));

  if (warnings.length > 0) {
    const notice = el('warnings');
    notice.hidden = false;
    for (const warning of warnings) {
      const item = document.createElement('li');
      item.textContent = warning;
      notice.append(item);
    }
  }

  // ---- three.js scene --------------------------------------------------

  const viewport = el('viewport');
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  viewport.append(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(cssColor('--canvas'));

  scene.add(new THREE.HemisphereLight(0xffffff, 0x8a8f99, 2.2));
  const sun = new THREE.DirectionalLight(0xffffff, 1.6);
  sun.position.set(4, 10, 6);
  scene.add(sun);

  const camera = new THREE.PerspectiveCamera(50, 1, 0.05, 200);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.12;
  controls.maxPolarAngle = Math.PI * 0.49; // never below the floor (plan 13.1)
  controls.minDistance = 0.3;
  controls.maxDistance = 60;

  const loader = new GLTFLoader();

  const roomModel = (await loader.parseAsync(toArrayBuffer(parsed.roomGlb), '')).scene;
  scene.add(roomModel);

  const objectRoots = new Map();
  const objectLayer = new THREE.Group();
  scene.add(objectLayer);

  for (const object of objects) {
    const bytes = parsed.objectGlbs.get(object.id);
    if (!bytes) {
      continue;
    }

    const model = (await loader.parseAsync(toArrayBuffer(bytes), '')).scene;
    const root = new THREE.Group();
    root.name = `object:${object.id}`;
    root.userData.objectId = object.id;

    const { position, rotationY } = objectTransform(object);
    root.position.set(...position);
    root.rotation.y = rotationY;
    root.add(model);
    objectLayer.add(root);
    objectRoots.set(object.id, root);
  }

  // ---- camera framing --------------------------------------------------

  const roomBox = new THREE.Box3().setFromObject(roomModel);
  if (roomBox.isEmpty()) {
    roomBox.setFromObject(scene);
  }

  function frameRoom() {
    const center = roomBox.getCenter(new THREE.Vector3());
    const size = roomBox.getSize(new THREE.Vector3());
    const reach = Math.max(size.x, size.z, 2);

    controls.target.copy(center);
    // High enough that the near walls do not hide the furniture.
    camera.position.set(center.x + reach * 0.55, center.y + reach * 1.45, center.z + reach * 0.75);
    controls.update();
  }

  function frameObject(id) {
    const root = objectRoots.get(id);
    if (!root) {
      return;
    }
    const box = new THREE.Box3().setFromObject(root);
    const center = box.getCenter(new THREE.Vector3());
    const reach = Math.max(box.getSize(new THREE.Vector3()).length(), 1);

    // Furniture usually stands against a wall, so look from above and from
    // the room's middle; keeping the current direction can put the camera
    // behind that wall.
    const inward = roomBox.getCenter(new THREE.Vector3()).sub(center).setY(0);
    if (inward.lengthSq() < 1e-6) {
      inward.set(0, 0, 1);
    }
    const direction = inward.normalize().multiplyScalar(0.6).add(new THREE.Vector3(0, 1, 0)).normalize();

    controls.target.copy(center);
    camera.position.copy(center).addScaledVector(direction, reach * 1.8);
    controls.update();
  }

  // ---- dollhouse -------------------------------------------------------

  const ceiling = roomModel.getObjectByName(CEILING_NODE);
  const dollhouseButton = el('dollhouse');

  function setDollhouse(on) {
    if (!ceiling) {
      return;
    }
    ceiling.visible = !on;
    dollhouseButton.setAttribute('aria-pressed', String(on));
  }

  if (ceiling) {
    // Open the room from above: a closed box seen from a high corner shows
    // only its roof.
    setDollhouse(true);
    dollhouseButton.addEventListener('click', () => setDollhouse(ceiling.visible));
  } else {
    dollhouseButton.disabled = true;
    dollhouseButton.title = 'This export has no separate ceiling to hide.';
  }

  el('frame').addEventListener('click', frameRoom);

  // ---- selection -------------------------------------------------------

  const byId = new Map(objects.map((object) => [object.id, object]));
  const listButtons = new Map();
  let selectedId = null;
  let outline = null;

  function select(id) {
    if (outline) {
      outline.removeFromParent();
      outline.geometry.dispose();
      outline = null;
    }
    for (const [buttonId, button] of listButtons) {
      button.setAttribute('aria-pressed', String(buttonId === id));
    }

    selectedId = id;
    const panel = el('selection');
    const object = id === null ? undefined : byId.get(id);

    if (!object) {
      panel.hidden = true;
      return;
    }

    el('selection-title').textContent = objectTitle(object);
    fillRows(el('selection-details'), objectDetails(object));
    panel.hidden = false;

    const root = objectRoots.get(id);
    if (root && [object.widthM, object.depthM, object.heightM].every((v) => Number.isFinite(v) && v > 0)) {
      // The object's own box from scene.json, in its local frame: width along
      // local X, depth along local Z, resting on the floor.
      const box = new THREE.BoxGeometry(object.widthM, object.heightM, object.depthM);
      const edges = new THREE.EdgesGeometry(box);
      box.dispose();
      outline = new THREE.LineSegments(
        edges,
        new THREE.LineBasicMaterial({ color: cssColor('--accent'), depthTest: false, transparent: true }),
      );
      outline.renderOrder = 10;
      outline.position.y = object.heightM / 2;
      root.add(outline);
    }
  }

  const list = el('object-list');
  if (objects.length === 0) {
    const empty = document.createElement('li');
    empty.className = 'muted';
    empty.textContent = 'No furniture in this room.';
    list.append(empty);
  }
  for (const object of objects) {
    const item = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'list-item';
    button.textContent = objectSummary(object);
    button.setAttribute('aria-pressed', 'false');
    if (!objectRoots.has(object.id)) {
      button.classList.add('missing');
      button.title = 'No 3D model was exported for this object.';
    }
    button.addEventListener('click', () => {
      setMeasuring(false);
      select(object.id);
      frameObject(object.id);
    });
    item.append(button);
    list.append(item);
    listButtons.set(object.id, button);
  }

  el('selection-close').addEventListener('click', () => select(null));

  // ---- measuring -------------------------------------------------------

  const measureButton = el('measure');
  const measureLayer = new THREE.Group();
  measureLayer.renderOrder = 20;
  scene.add(measureLayer);
  const label = el('measure-label');
  let measuring = false;
  let points = [];

  function clearMeasurement() {
    for (const child of [...measureLayer.children]) {
      child.removeFromParent();
      child.geometry?.dispose();
    }
    points = [];
    label.hidden = true;
  }

  function setMeasuring(on) {
    measuring = on;
    measureButton.setAttribute('aria-pressed', String(on));
    viewport.classList.toggle('measuring', on);
    if (on) {
      clearMeasurement();
      select(null);
      setStatus('Click the first point to measure from. Esc cancels.');
    } else {
      setStatus('');
      if (points.length < 2) {
        clearMeasurement();
      }
    }
  }

  const markerMaterial = new THREE.MeshBasicMaterial({ color: cssColor('--accent'), depthTest: false });
  const lineMaterial = new THREE.LineBasicMaterial({ color: cssColor('--accent'), depthTest: false });

  function addMeasurePoint(point) {
    if (points.length >= 2) {
      clearMeasurement();
    }
    points.push(point);

    const marker = new THREE.Mesh(new THREE.SphereGeometry(0.03, 16, 12), markerMaterial);
    marker.position.copy(point);
    marker.renderOrder = 20;
    measureLayer.add(marker);

    if (points.length === 1) {
      setStatus('Now click the second point.');
      return;
    }

    const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), lineMaterial);
    line.renderOrder = 20;
    measureLayer.add(line);

    const result = describeMeasurement(points[0].toArray(), points[1].toArray());
    label.innerHTML = '';
    label.append(row('3D', result.direct), row('Along floor', result.horizontal));
    label.hidden = false;
    setStatus(`Distance ${result.direct}, along the floor ${result.horizontal}. Click again for a new measurement.`);
  }

  measureButton.addEventListener('click', () => setMeasuring(!measuring));

  // ---- pointer ---------------------------------------------------------

  const raycaster = new THREE.Raycaster();
  const pointer = new THREE.Vector2();
  let downAt = null;

  renderer.domElement.addEventListener('pointerdown', (event) => {
    downAt = { x: event.clientX, y: event.clientY };
  });

  renderer.domElement.addEventListener('pointerup', (event) => {
    if (!downAt || Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y) > CLICK_SLOP_PX) {
      downAt = null;
      return; // that was an orbit or pan, not a click
    }
    downAt = null;

    const rect = renderer.domElement.getBoundingClientRect();
    pointer.set(((event.clientX - rect.left) / rect.width) * 2 - 1, -((event.clientY - rect.top) / rect.height) * 2 + 1);
    raycaster.setFromCamera(pointer, camera);

    const hit = raycaster
      .intersectObjects([roomModel, objectLayer], true)
      .find((candidate) => isShown(candidate.object) && candidate.object.isMesh);

    if (measuring) {
      if (hit) {
        addMeasurePoint(hit.point.clone());
      }
      return;
    }

    select(hit ? owningObjectId(hit.object) : null);
  });

  window.addEventListener('keydown', (event) => {
    if (event.target instanceof HTMLInputElement || event.metaKey || event.ctrlKey || event.altKey) {
      return;
    }
    switch (event.key) {
      case 'Escape':
        if (measuring) {
          setMeasuring(false);
          clearMeasurement();
        } else {
          select(null);
        }
        break;
      case 'f':
      case 'F':
        frameRoom();
        break;
      case 'd':
      case 'D':
        if (ceiling) {
          setDollhouse(ceiling.visible);
        }
        break;
      case 'm':
      case 'M':
        setMeasuring(!measuring);
        break;
      default:
        return;
    }
    event.preventDefault();
  });

  // ---- render loop -----------------------------------------------------

  function resize() {
    const { clientWidth: width, clientHeight: height } = viewport;
    renderer.setSize(width, height, false);
    camera.aspect = width / Math.max(height, 1);
    camera.updateProjectionMatrix();
  }

  new ResizeObserver(resize).observe(viewport);
  resize();
  frameRoom();

  const midpoint = new THREE.Vector3();
  renderer.setAnimationLoop(() => {
    controls.update();
    renderer.render(scene, camera);

    if (!label.hidden && points.length === 2) {
      midpoint.copy(points[0]).add(points[1]).multiplyScalar(0.5).project(camera);
      const behind = midpoint.z > 1;
      label.style.visibility = behind ? 'hidden' : 'visible';
      label.style.left = `${((midpoint.x + 1) / 2) * viewport.clientWidth}px`;
      label.style.top = `${((1 - midpoint.y) / 2) * viewport.clientHeight}px`;
    }
  });

  document.body.classList.add('ready');
}

// ---- helpers -------------------------------------------------------------

function owningObjectId(object) {
  for (let node = object; node; node = node.parent) {
    if (typeof node.userData?.objectId === 'string') {
      return node.userData.objectId;
    }
  }
  return null;
}

/** Raycasting ignores visibility, so a hidden ceiling would still catch clicks. */
function isShown(object) {
  for (let node = object; node; node = node.parent) {
    if (!node.visible) {
      return false;
    }
  }
  return true;
}

function toArrayBuffer(bytes) {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
}

function fillRows(container, rows) {
  container.innerHTML = '';
  for (const [name, value] of rows) {
    container.append(row(name, value));
  }
}

function row(name, value) {
  const wrapper = document.createElement('div');
  const term = document.createElement('dt');
  const detail = document.createElement('dd');
  term.textContent = name;
  detail.textContent = value;
  wrapper.append(term, detail);
  return wrapper;
}

function setStatus(text) {
  const status = el('status');
  status.textContent = text;
  status.hidden = text === '';
}

function showMessage(text) {
  const message = el('message');
  message.querySelector('p').textContent = text;
  message.hidden = false;
  document.body.classList.add('failed');
}

function cssColor(name) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return new THREE.Color(value || '#888888');
}

function webglAvailable() {
  try {
    const canvas = document.createElement('canvas');
    return Boolean(canvas.getContext('webgl2') || canvas.getContext('webgl'));
  } catch {
    return false;
  }
}
