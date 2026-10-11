// Converts a scene.json position into three.js (glTF) coordinates.
//
// scene.json is in Ghost space: Unity axes, left-handed, metres, +Y up
// (docs/contracts/scene-schema-v1.md). The .glb files and three.js are glTF:
// right-handed, +Y up. GlbExporter converts by negating Z
// (apps/viewer/.../Runtime/Export/GlbExporter.cs, MeshBuffers.AddBox).
//
// room.glb is already in room position. Each objects/<id>.glb is centred on the
// origin with no node transform (GlbExporter.Build writes only "mesh" and
// "name"), so the viewer places it from scene.json:
//
//   position   = (center.x, center.y, -center.z)
//   rotation.y = -yawDeg * PI / 180
//
// Why -yaw: the Unity Viewer applies Quaternion.Euler(0, yawDeg, 0)
// (apps/viewer/.../Rendering/FurnitureFactory.cs, Create). Writing that as a
// matrix R and the Z mirror as M = diag(1, 1, -1), a glTF vertex is placed at
// M (c + R l) = M c + (M R M)(M l). M R(t) M has the same form as R(-t), and
// three.js rotation.y uses that same form, so the angle is negated.

/** @returns {{ position: [number, number, number], rotationY: number }} */
export function objectTransform(object) {
  const center = object.center ?? { x: 0, y: 0, z: 0 };
  const yawDeg = Number.isFinite(object.yawDeg) ? object.yawDeg : 0;

  return {
    position: [num(center.x), num(center.y), -num(center.z)],
    rotationY: (-yawDeg * Math.PI) / 180,
  };
}

/** A Ghost-space point (scene.json) in three.js coordinates. */
export function ghostToThree(point) {
  return [num(point.x), num(point.y), -num(point.z)];
}

function num(value) {
  return Number.isFinite(value) ? value : 0;
}
