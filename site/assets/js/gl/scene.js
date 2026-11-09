// The Form, in 3D: a desk at night under one lamp. An A4 application form, the photo prints, a bank statement with
// a padlock on it, three scans and a round rubber stamp, all with real perspective, soft lamp shadows that sharpen
// as things come down, gloss on the prints and lens blur. Every position is a function of the film time T, so
// scrolling back and forth always shows the same frame.

import * as F from "../film.js";
import { createGL, startProgram, programsReady, finishProgram, mesh, grid, lathe, tube, roundedBox, texture, target, msaa, M4, add, scale, norm, sub, cross } from "./engine.js";
import * as P from "./paint.js";
import * as S from "./shaders.js";

const { A4, BOX, NOTE, STAMP_AT, toWorld, centre, A1, A2, J1, J2, J3, E, LOCK, CROP, CROP_UV, rise, lerp, clamp, knock, outCubic, inCubic, outQuint, smooth } = F;

const LAMP = [-340, 600, -330];
const LAMP_AIM = [0, 0, 20];
// The page colour behind the 3D (sRGB), and the desk: a seamless studio backdrop, peach running into pink.
const PAGE_BG = [1.0, 0.961, 0.941]; // #fff5f0
const SWEEP = [[0.95, 0.60, 0.44], [0.90, 0.40, 0.52]];
const SHADOW_RECT = [-360, -360, 720, 720];
const PHOTO = centre(BOX.photo);
const STMT = centre(BOX.stmt);
const DOCC = centre(BOX.docs);
const STAMPW = toWorld(...STAMP_AT);
const SIZE = { p1: [66, 85], p1f: [34.4, 44.2], stmt: [60, 85], stmtf: [31.4, 42.4], scan: [60, 80], scanf: [34, 45.3] };
// the refused prints are flung right off the table (never seen again); the photo comes back from there
const POS = {
  desk: { from: [270, -50], hold1: [26, -62], hold2: [40, 8], hold3: [44, 62], rej1: [270, -100], rej2: [280, -30] },
  phone: { from: [230, -10], hold1: [-50, 20], hold2: [-48, 56], hold3: [-32, 32], rej1: [270, -100], rej2: [280, -30] },
};
const sin = Math.sin, PI = Math.PI;
// the crop: its size, and where its middle sits in the full print (mm from the print's middle; z = down the picture)
const CROP_SIZE = [(CROP_UV[2] - CROP_UV[0]) * SIZE.p1[0], (CROP_UV[3] - CROP_UV[1]) * SIZE.p1[1]];
const CROP_OFF = [((CROP_UV[0] + CROP_UV[2]) / 2 - 0.5) * SIZE.p1[0], ((CROP_UV[1] + CROP_UV[3]) / 2 - 0.5) * SIZE.p1[1]];
/** A local offset turned by a card's yaw (the same turn M4.trs makes). */
const turn = ([x, z], yaw) => [Math.cos(yaw) * x + Math.sin(yaw) * z, -Math.sin(yaw) * x + Math.cos(yaw) * z];

/** The scene's pictures, painted in a background worker where the browser can (no freezes), else here. Either way
 *  get(name) gives { src, meta }: src is plain pixels or a canvas, ready for texture(). paintHere forces this page. */
function painter(photo, p1Aspect, paintHere) {
  const jobs = new Map(P.jobs(photo, p1Aspect));
  const here = (name) => {
    const r = jobs.get(name)();
    const meta = r.canvas ? { ...r } : {};
    delete meta.canvas;
    return { src: r.canvas || r, meta };
  };
  let worker = null;
  const ready = new Map(), waiting = new Map();
  const giveUp = () => {
    // the worker failed: paint whatever is still missing here (same pictures, only not in the background)
    worker?.terminate();
    worker = null;
    for (const [name, ok] of waiting) ok(here(name));
    waiting.clear();
  };
  if (!paintHere && typeof Worker !== "undefined" && typeof OffscreenCanvas !== "undefined" && typeof createImageBitmap !== "undefined") {
    try {
      worker = new Worker(new URL("./paint-worker.js", import.meta.url), { type: "module" });
      worker.onmessage = ({ data }) => {
        if (data.error) { giveUp(); return; }
        const got = { src: { width: data.width, height: data.height, data: data.data }, meta: data.meta };
        if (waiting.has(data.name)) { waiting.get(data.name)(got); waiting.delete(data.name); } else ready.set(data.name, got);
      };
      worker.onerror = (e) => { e.preventDefault?.(); giveUp(); };
      createImageBitmap(photo).then((bmp) => worker?.postMessage({ photo: bmp, p1Aspect }, [bmp]), giveUp);
    } catch { giveUp(); }
  }
  return {
    inWorker: () => !!worker,
    get(name) {
      if (ready.has(name)) { const r = ready.get(name); ready.delete(name); return Promise.resolve(r); }
      if (!worker) return Promise.resolve(here(name));
      // (a worker that goes quiet for 8 s is given up on, and the rest is painted here)
      const late = setTimeout(giveUp, 8000);
      return new Promise((ok) => waiting.set(name, (r) => { clearTimeout(late); ok(r); }));
    },
    done() { worker?.terminate(); worker = null; },
  };
}

export async function createScene(cv, { keep = false, look = "studio", msaa: msaaOverride = null, progress = () => {}, paintHere = false } = {}) {
  const BG = PAGE_BG;
  const gl = createGL(cv, { keep });
  const CEL = look === "cel";
  // Let the loader paint between the heavy steps. A promise that resolves inside requestAnimationFrame would run the
  // next step before that frame is painted (the loader froze at every step); the setTimeout lands after the paint.
  const breathe = () => new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
  // ...and in between, give the loader a frame whenever about one frame's worth of work has piled up
  let since = performance.now();
  const slice = async () => { if (performance.now() - since > 12) { await breathe(); since = performance.now(); } };
  // the shaders compile while the photo downloads (on another thread, where the browser can); starting each one
  // still takes the page a moment, so the loader gets a frame between them
  await breathe();
  since = performance.now();
  const started = [];
  for (const [vs, fs] of [
    [S.MESH_VS, S.MESH_FS], [S.MESH_VS, S.DECAL_FS], [S.SHADOW_VS, S.SHADOW_FS],
    [S.QUAD_VS, S.DOWN_FS], [S.QUAD_VS, S.BLUR_FS], [S.QUAD_VS, S.FINAL_FS],
  ]) { started.push(startProgram(gl, vs, fs)); await slice(); }
  const img = await new Promise((ok, no) => {
    const i = new Image();
    i.onload = () => ok(i);
    i.onerror = () => no(new Error("photo failed to load"));
    i.src = "assets/img/cat.jpg";
  });
  // decode it now, off the main thread, rather than in the middle of painting the print
  await img.decode?.().catch(() => {});
  // the papers start painting in the background now, while the shaders and meshes are made
  const paints = painter(img, SIZE.p1[0] / SIZE.p1[1], paintHere);
  progress(0.34, "Mixing the light");
  await breathe();

  // ---- programs (each check waits only for its own program; the loader gets a frame between them)
  for (let i = 0; i < 120 && !programsReady(gl, started); i++) await breathe();
  const progs = [];
  for (const s of started) { progs.push(finishProgram(gl, s)); await slice(); }
  const [prog, decalProg, shadowProg, downProg, blurProg, finalProg] = progs;
  const emptyVao = gl.createVertexArray();

  // ---- meshes
  const plane = mesh(gl, grid(1, 1));
  const sheetMesh = mesh(gl, grid(36, 48));
  const cardMesh = mesh(gl, grid(10, 14));
  // the round stamp: green-inked rubber face, a turned beech handle
  const wood = CEL ? [0.62, 0.4, 0.22] : [0.36, 0.19, 0.08], rubber = [0.02, 0.07, 0.04];
  const brass = CEL ? [0.92, 0.7, 0.3] : [0.86, 0.64, 0.3], steel = CEL ? [0.78, 0.8, 0.85] : [0.9, 0.9, 0.92];
  const stampMesh = mesh(gl, lathe([
    [0, 23.4, rubber], [2.8, 23.4, wood], [2.8, 25.4, wood], [12.5, 25.4, wood], [14.5, 22.5, wood], [14.5, 8.6, wood],
    [24, 8.2, wood], [30, 7.4, wood], [42, 9.4, wood], [52, 15, wood], [60, 15.4, wood], [66, 12, wood], [69, 0, wood],
  ], 48, "y"));
  const collarMesh = mesh(gl, lathe([[14.3, 10.4, brass], [15.3, 11, brass], [20.8, 11, brass], [21.8, 10.2, brass], [21.8, 7.6, brass]], 48, "y"));
  // a brass padlock lying on its back, shackle pointing at the top of the page
  const lockBody = mesh(gl, roundedBox(24, 9, 19, 3.2, 10, brass));
  const shacklePath = [];
  for (let i = 0; i <= 4; i++) shacklePath.push([-6.4, 0, -6 - i * 2.2]);
  for (let i = 1; i < 16; i++) { const a = PI - (i / 16) * PI; shacklePath.push([Math.cos(a) * 6.4, 0, -14.8 - Math.sin(a) * 6.4]); }
  for (let i = 0; i <= 4; i++) shacklePath.push([6.4, 0, -14.8 + i * 2.2]);
  const shackle = mesh(gl, tube(shacklePath, 1.7, 18, steel));

  progress(0.42, "Printing the form");
  await breathe();

  // ---- textures (one picture at a time, with a frame for the loader whenever one took a while)
  const pic = async (name, opts) => { const { src, meta } = await paints.get(name); const t = texture(gl, src, opts); await slice(); return { ...meta, tex: t }; };
  const form = (await pic("form")).tex;
  progress(0.56, "Laying out the papers");
  await breathe();
  const p1 = (await pic("p1")).tex;
  const stmt = (await pic("stmt")).tex;
  const scans = [];
  for (let i = 0; i < 3; i++) scans.push((await pic(`scan${i}`)).tex);
  const tex = { form, p1, stmt, scans, noise: (await pic("noise", { srgb: false, wrap: true })).tex };
  progress(0.7, "Inking the seal");
  await breathe();
  const notes = {};
  for (const k of Object.keys(P.NOTES)) notes[k] = await pic(`note.${k}`);
  const seal = await pic("seal");
  const stp = await pic("staple");
  paints.done();
  // A plain 1×1 texture for things that sample nothing. Without it, the last frame's picture (which the scene can
  // be drawing into) stays bound, WebGL refuses those draws, and the padlock and the stamp vanish (seen in Safari).
  const blank = (() => {
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
    return t;
  })();

  progress(0.78, "Warming up the lamp");
  await breathe();

  // ---- the lamp: brightness set so the middle of the form is lit at about 0.95
  const lampDir = norm(sub(LAMP_AIM, LAMP));
  const power = (() => {
    const L = norm(LAMP);
    const d = Math.hypot(...LAMP);
    return 0.95 / (L[1] / (1 + (d / 1100) ** 2));
  })();

  // ---- shadow map + dynamic caster buffer
  const shadowMap = target(gl, 1024, 1024);
  const casterVao = gl.createVertexArray();
  const casterBuf = gl.createBuffer();
  gl.bindVertexArray(casterVao);
  gl.bindBuffer(gl.ARRAY_BUFFER, casterBuf);
  const stride = 9 * 4;
  gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);
  gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);
  gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 2, gl.FLOAT, false, stride, 16);
  gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 3, gl.FLOAT, false, stride, 24);
  gl.bindVertexArray(null);
  let casterData = new Float32Array(9 * 6 * 32);

  // ---- render targets (made in resize)
  let RT = null;
  let samples = Math.min(4, gl.getParameter(gl.MAX_SAMPLES) || 0);
  if (msaaOverride !== null) samples = Math.min(samples, msaaOverride);

  function resize(V, force = false) {
    const w = cv.width, h = cv.height;
    if (!force && RT && RT.w === w && RT.h === h) return;
    if (RT) { RT.ms?.free(); RT.sharp.free(); RT.half.free(); RT.tmp.free(); RT.blur.free(); }
    const hw = Math.max(1, w >> 1), hh = Math.max(1, h >> 1);
    // with multisampling the scene draws into its own buffer and is copied out; without it, straight in
    RT = {
      w, h, ms: samples > 1 ? msaa(gl, w, h, samples) : null, sharp: target(gl, w, h, { depth: samples <= 1 }),
      half: target(gl, hw, hh), tmp: target(gl, hw, hh), blur: target(gl, hw, hh),
    };
    void V;
  }
  /** For slow phones: drop the edge smoothing (never the resolution). */
  function lighter(V) {
    if (samples <= 1) return false;
    samples = 0;
    resize(V, true);
    return true;
  }

  // ------------------------------------------------------------------------------------------ the camera ---
  const gaze = { x: 0, y: 0 };
  function camera(T, V) {
    const k = F.camAt(T, V.phone);
    let [cx, cz] = k.c;
    let { el, az } = k;
    const t = V.calm ? 0 : V.clock;
    cx += 1.6 * sin(0.41 * t);
    cz += 1.2 * sin(0.33 * t + 1);
    el += 0.6 * sin(0.27 * t);
    az += 1.0 * sin(0.21 * t + 0.4);
    if (V.mouse && V.live && V.mouse.x >= 0) {
      gaze.x += ((V.mouse.x / V.W - 0.5) - gaze.x) * 0.05;
      gaze.y += ((V.mouse.y / V.H - 0.5) - gaze.y) * 0.05;
    }
    az += gaze.x * 4;
    el -= gaze.y * 2.4;
    // knocks: the refusals, each landing, and the seal (the biggest, so you feel it's done)
    const shake = 1.1 * knock(T - A1.hit, 10, 9) + 1.1 * knock(T - A2.hit, 10, 9) + 1.0 * knock(T - LOCK.hit, 10, 9)
      + 4.2 * knock(T - E.hit, 8, 5.5)
      + 0.35 * (knock(T - J1.place1, 9, 10) + knock(T - J2.place1, 9, 10) + knock(T - J3.place1, 9, 10));
    const side = V.calm ? 0 : 2.2 * knock(T - E.hit - 0.03, 6.5, 6);
    const calm = V.calm ? 0 : 1;
    const fov = ((V.phone ? 33 : 27) * PI) / 180;
    const t2 = Math.tan(fov / 2);
    const top = lerp(V.top, V.topEnd || V.top, rise(T, E.hit, E.gone + 0.6)), bottom = V.H - V.bottomPad;
    const safeH = Math.max(80, bottom - top);
    const aspect = V.W / V.H;
    const ER = (el * PI) / 180, AR = (az * PI) / 180;
    const Dh = (k.h * Math.sin(ER) + 36 * Math.cos(ER)) / (2 * t2 * (safeH / V.H));
    const Dw = k.w / (2 * t2 * aspect * ((V.W - 32) / V.W));
    // the seal's knock also pushes the camera in a touch, like a punch
    const D = Math.max(Dh, Dw) * (1 - 0.018 * Math.max(0, knock(T - E.hit, 4, 5)));
    const tgt = [cx + side, shake * 0.6 * calm, cz + shake * 0.4 * calm];
    const dir = [Math.sin(AR) * Math.cos(ER), Math.sin(ER), Math.cos(AR) * Math.cos(ER)];
    const eye = add(tgt, scale(dir, D));
    const view = M4.lookAt(eye, tgt);
    const cy = top + safeH / 2;
    const proj = M4.persp(fov, aspect, 8, 9000, 0, (2 * cy) / V.H - 1);
    const vp = M4.mul(proj, view);
    const project = (p) => {
      const q = M4.apply(vp, p);
      return [(q[0] * 0.5 + 0.5) * V.W, (0.5 - q[1] * 0.5) * V.H];
    };
    const fwd = norm(sub(tgt, eye));
    const right = norm(cross(fwd, [0, 1, 0]));
    const up = cross(right, fwd);
    return { eye, view, proj, project, up, focus: D, aperture: 3.2 * k.dof };
  }

  // ------------------------------------------------------------------------------------------ the things ---
  const D = (V) => POS[V.phone ? "phone" : "desk"];
  const arc = (a, b, u, l0, l1, peak) => [lerp(a[0], b[0], u), lerp(l0, l1, u) + sin(u * PI) * peak, lerp(a[1], b[1], u)];

  /** A failed try: dragged over the box, slammed, thrown back off the table. */
  function tried(T, A, c, over, rej, spin) {
    if (T < A.over) {
      const u = outCubic((T - A.in0) / (A.over - A.in0));
      [c.x, c.y, c.z] = arc(c.from, over, u, c.l0 ?? 38, 9, 12);
      c.yaw = lerp(c.yaw0 ?? 0.3, 0.04, u); c.roll = -0.16 * sin(u * PI); c.bend = 1.6 * sin(u * PI);
    } else if (T < A.hit) {
      const u = inCubic((T - A.over) / (A.hit - A.over));
      c.x = lerp(over[0], PHOTO[0] + 3, u); c.z = over[1]; c.y = lerp(9, 0.5, u); c.yaw = 0.04; c.bend = -0.6 * u;
    } else if (T < A.back) {
      c.x = PHOTO[0] + 3; c.z = PHOTO[1]; c.y = 0.5; c.yaw = 0.04; c.bend = -0.9 * knock(T - A.hit, 13, 14);
    } else {
      const u = clamp((T - A.back) / (A.fall - A.back));
      const h = outCubic(u);
      c.x = lerp(PHOTO[0] + 3, rej[0], h);
      c.z = lerp(PHOTO[1], rej[1], h) + 10 * sin(u * PI);
      const up = Math.pow(u, 0.75);
      c.y = lerp(0.5, 0.35, u) + 46 * 4 * up * (1 - up);
      const f = Math.pow(1 - u, 1.2);
      c.roll = 0.55 * sin(u * PI * 3.0) * f;
      c.pitch = 0.34 * sin(u * PI * 2.2 + 0.5) * f;
      c.yaw = 0.04 + spin * outCubic(u);
      c.bend = 4.5 * sin(u * PI * 4) * f;
    }
  }

  /** Hovering in the hand: a slow bob while the ring (or the password window) is up. */
  function hover(T, t0, c, hold) {
    [c.x, c.z] = hold;
    c.y = 24 + 0.9 * sin((T - t0) * 2.3);
    c.yaw = 0.06 + 0.012 * sin((T - t0) * 1.7);
    c.bend = 0.7 * sin((T - t0) * 2.0);
  }
  /** Carried into its box and laid down. */
  function placed(T, J, c, hold, box) {
    if (T < J.place1) {
      const u = smooth((T - J.place0) / (J.place1 - J.place0));
      [c.x, c.y, c.z] = arc(hold, box, u, 24, 0.42, 9);
      c.yaw = lerp(0.06, 0, u); c.bend = 1.2 * sin(u * PI);
    } else {
      [c.x, c.z] = box; c.y = 0.42; c.yaw = 0;
      c.bend = 0.55 * knock(T - J.place1, 6, 9);
      c.rest = true;
    }
  }

  /** The DragShift drag: in from `from` to `hold`, hover while the ring is open, shrink, carried into the box. */
  function dragged(T, J, c, from, hold, box, big, small, { l0 = 38, yaw0 = 0.3 } = {}) {
    if (T < J.in1) {
      const u = smooth((T - J.in0) / (J.in1 - J.in0));
      [c.x, c.y, c.z] = arc(from, hold, u, l0, 24, 16);
      c.yaw = lerp(yaw0, 0.06, u); c.roll = 0.14 * sin(u * PI); c.bend = 2.2 * sin(u * PI);
    } else if (T < J.place0) hover(T, J.in1, c, hold);
    else placed(T, J, c, hold, box);
    const e = outQuint((T - J.work0) / (J.work1 - J.work0));
    c.w = lerp(big[0], small[0], e);
    c.h = lerp(big[1], small[1], e);
  }

  /** The statement: tried on its box and refused (it bounces back into your hand), unlocked, then laid in. */
  function refused(T, J, c, from, hold, box) {
    const over = [box[0] + 5, box[1]];
    if (T < J.in1) {
      const u = outCubic((T - J.in0) / (J.in1 - J.in0));
      [c.x, c.y, c.z] = arc(from, over, u, 38, 9, 12);
      c.yaw = lerp(-0.25, 0.03, u); c.roll = 0.12 * sin(u * PI); c.bend = 1.6 * sin(u * PI);
    } else if (T < LOCK.hit) {
      const u = inCubic((T - J.in1) / (LOCK.hit - J.in1));
      c.x = lerp(over[0], box[0] + 3, u); c.z = over[1]; c.y = lerp(9, 0.5, u); c.yaw = 0.03;
    } else if (T < LOCK.back) {
      c.x = box[0] + 3; c.z = box[1]; c.y = 0.5; c.yaw = 0.03; c.bend = -0.9 * knock(T - LOCK.hit, 13, 14);
    } else if (T < LOCK.held) {
      const u = outCubic((T - LOCK.back) / (LOCK.held - LOCK.back));
      [c.x, c.y, c.z] = arc([box[0] + 3, box[1]], hold, u, 0.5, 24, 10);
      c.yaw = lerp(0.03, 0.06, u) + 0.08 * knock(T - LOCK.back, 3, 4);
    } else if (T < J.place0) hover(T, LOCK.held, c, hold);
    else placed(T, J, c, hold, box);
    const e = smooth((T - J.place0) / (J.place1 - J.place0));
    c.w = lerp(SIZE.stmt[0], SIZE.stmtf[0], e);
    c.h = lerp(SIZE.stmt[1], SIZE.stmtf[1], e);
  }

  /** The photo through the opening and the first DragShift job. */
  function photo(T, d) {
    const c = { tex: tex.p1, kind: 2, w: SIZE.p1[0], h: SIZE.p1[1], x: 400, y: 0, z: 0, yaw: 0, pitch: 0, roll: 0, bend: 0, twist: 0, on: false, rest: false, uv: [0, 0, 1, 1] };
    if (T < A1.in0) return c;
    c.on = T < A2.fall + 0.05 || T >= J1.in0;
    const over = [PHOTO[0] + 6, PHOTO[1]];
    if (T < A1.back) { c.from = d.from; tried(T, A1, c, over, d.rej1, 1.1); }
    else if (T < A1.held) {
      const u = outCubic((T - A1.back) / (A1.held - A1.back));
      [c.x, c.y, c.z] = arc([PHOTO[0] + 3, PHOTO[1]], d.hold1, u, 0.5, 24, 10);
      c.yaw = lerp(0.04, 0.06, u) + 0.08 * knock(T - A1.back, 3, 4);
    } else if (T < A2.in0) hover(T, A1.held, c, d.hold1);
    else if (T < J1.in0) { c.from = d.hold1; c.l0 = 24; c.yaw0 = 0.06; tried(T, A2, c, over, d.rej2, -0.8); }
    else dragged(T, J1, c, d.rej2, d.hold1, PHOTO, CROP_SIZE, SIZE.p1f, { l0: 30, yaw0: -0.76 });
    c.full = { x: c.x, z: c.z, yaw: c.yaw, y: c.y };
    if (T >= CROP.cut) {
      c.uv = CROP_UV;
      if (T < J1.in0) {
        // the cut: what's left is the middle of the print, so it moves to where that middle was, with a small snap
        const snap = 1 + 0.045 * Math.exp(-(T - CROP.cut) * 11);
        c.w = CROP_SIZE[0] * snap; c.h = CROP_SIZE[1] * snap;
        if (T < A2.in0) { const [ox, oz] = turn(CROP_OFF, c.yaw); c.x += ox; c.z += oz; }
      }
    }
    return c;
  }
  /** The four trimmed strips: they come off the print at the cut and fall away outwards, spinning. */
  function strips(T, d) {
    const s = T - CROP.cut;
    const base = { yaw: 0.06 + 0.012 * sin((CROP.cut - A1.held) * 1.7), y: 24 + 0.9 * sin((CROP.cut - A1.held) * 2.3) };
    const [W, H] = SIZE.p1;
    const [u0, v0, u1, v1] = CROP_UV;
    const parts = [
      { uv: [0, 0, 1, v0], dir: [0, -1] }, { uv: [0, v1, 1, 1], dir: [0, 1] },
      { uv: [0, v0, u0, v1], dir: [-1, 0] }, { uv: [u1, v0, 1, v1], dir: [1, 0] },
    ];
    const fade = 1 - smooth((s - 0.55) / 0.2);
    return parts.map((p, i) => {
      const lx = ((p.uv[0] + p.uv[2]) / 2 - 0.5) * W, lz = ((p.uv[1] + p.uv[3]) / 2 - 0.5) * H;
      const reach = 70 * s + 60 * s * s;
      const [ox, oz] = turn([lx + p.dir[0] * reach, lz + p.dir[1] * reach + 30 * s], base.yaw);
      return {
        tex: tex.p1, kind: 2, on: fade > 0.01, rest: false, uv: p.uv, twist: 0,
        w: (p.uv[2] - p.uv[0]) * W * fade, h: (p.uv[3] - p.uv[1]) * H * fade,
        x: d.hold1[0] + ox, z: d.hold1[1] + oz, y: Math.max(0.4, base.y + 16 * s - 150 * s * s),
        yaw: base.yaw + (i % 2 ? 1 : -1) * 1.4 * s, roll: p.dir[0] * 3.2 * s + (i === 0 ? -1.5 * s : 0), pitch: p.dir[1] * 2.6 * s,
        bend: 3 * sin(s * 9) * (1 - s),
      };
    });
  }

  const cardModel = (c, jolt) => M4.trs([c.x + (c.rest ? jolt[0] : 0), c.y, c.z + (c.rest ? jolt[1] : 0)], { yaw: c.yaw, pitch: c.pitch, roll: c.roll });

  function things(T, V, cam, jolt) {
    const d = D(V);
    const card = (tx, kind, [w, h]) => ({ tex: tx, kind, w, h, x: 400, y: 0, z: 0, yaw: 0, pitch: 0, roll: 0, bend: 0, twist: 0, on: false, rest: false, frost: 0 });
    const out = {};

    // the photo: thrown back into your hand, cropped, thrown off the table, then (with DragShift) into its box
    const p1 = photo(T, d);
    out.p1 = p1;
    out.strips = T >= CROP.cut && T < CROP.cut + 0.75 ? strips(T, d) : [];

    // the bank statement, frosted while it's locked
    const st = card(tex.stmt, 3, SIZE.stmt);
    if (T >= J2.in0) {
      st.on = true;
      refused(T, J2, st, [d.from[0], d.from[1] + 50], d.hold2, STMT);
      st.frost = 1 - rise(T, LOCK.pop + 0.05, LOCK.clear);
    }
    out.stmt = st;

    // its padlock: rides on the page, the shackle springs open, then it hops off and falls away
    const lk = { on: false };
    if (T >= J2.in0 && T < LOCK.gone) {
      lk.on = true;
      const ride = M4.mul(cardModel(st, jolt), M4.trs([0, 0.3 + 4.5, -st.h * 0.06], { yaw: -0.18 }));
      const open = rise(T, LOCK.pop, LOCK.pop + 0.12);
      const hop = clamp((T - LOCK.pop - 0.1) / (LOCK.gone - LOCK.pop - 0.1));
      let m = ride;
      if (hop > 0) {
        const p = M4.apply(ride, [0, 0, 0]);
        const to = [p[0] + 150 * hop, p[1] + 34 * sin(hop * PI) - 30 * hop * hop, p[2] - 30 * hop];
        m = M4.mul(M4.trs(to, { yaw: -0.18 + 1.6 * hop, pitch: 0.9 * hop, roll: 2.4 * hop }), M4.trs([0, 0, 0]));
      }
      lk.body = m;
      // the shackle slides out 4 mm and swings open about its left leg
      lk.shackle = M4.mul(m, M4.mul(M4.trs([-6.4, 0, -4 * open], { yaw: -1.1 * open }), M4.trs([6.4, 0, 0])));
      lk.y = M4.apply(m, [0, 0, 0])[1] - 4.5;
    }
    out.lock = lk;

    // three scans, fanned, tidied into one stapled PDF
    const docs = card(null, 3, SIZE.scan);
    if (T >= J3.in0) { docs.on = true; dragged(T, J3, docs, [d.from[0], d.from[1] + 90], d.hold3, DOCC, SIZE.scan, SIZE.scanf, { yaw0: 0.22 }); }
    const tidy = rise(T, J3.work0, J3.work0 + 0.6);
    const fan = [[-15, 5, -0.17], [0, -3, 0.02], [15, 4, 0.16]];
    out.scans = [0, 1, 2].map((i) => {
      const [fx, fz, fr] = fan[i];
      const k = 1 - tidy;
      const cs = Math.cos(docs.yaw), sn = Math.sin(docs.yaw);
      return {
        ...docs, tex: tex.scans[i],
        x: docs.x + (fx * cs + fz * sn) * k, z: docs.z + (-fx * sn + fz * cs) * k,
        y: docs.y + i * lerp(0.6, 0.22, tidy), yaw: docs.yaw + fr * k, bend: docs.bend * (0.8 + i * 0.1),
      };
    });
    out.docs = docs;
    out.stapleA = T >= J3.work0 + 0.62 ? 1 : 0;
    out.staple = out.scans[2];

    // the round stamp: comes out of the screen from your side, slams down, goes back the way it came
    const sp = { on: false };
    if (T >= E.t0 && T < E.gone) {
      sp.on = true;
      const at = [STAMPW[0], 0, STAMPW[1]];
      const from = add(add(cam.eye, scale(sub(at, cam.eye), 0.2)), scale(cam.up, -42));
      const away = norm(sub(from, at));
      let pos, axis, squash = 0;
      if (T < E.hit) {
        const u = clamp((T - E.t0) / (E.hit - E.t0));
        const e = Math.pow(u, 2.1);
        pos = add(add(from, scale(sub(at, from), e)), scale(cam.up, 18 * sin(u * PI)));
        const up = 0.45 + 0.55 * smooth(u * 1.15);
        axis = norm(add(scale(away, 1 - up), scale([0, 1, 0], up)));
      } else if (T < E.lift) {
        pos = at; axis = [0, 1, 0];
        squash = 0.06 * Math.exp(-(T - E.hit) * 14);
      } else {
        const v = clamp((T - E.lift) / (E.gone - E.lift));
        const e = Math.pow(v, 1.6);
        pos = add(at, scale(sub(from, at), e));
        axis = norm(add(scale([0, 1, 0], 1 - 0.55 * smooth(v)), scale(away, 0.55 * smooth(v))));
      }
      sp.pos = pos;
      sp.m = M4.mul(M4.mul(M4.trs(pos), M4.alignY(axis, -0.2)), M4.trs([0, 0, 0], {}, [1, 1 - squash, 1]));
    }
    out.stamp = sp;
    return out;
  }

  // ------------------------------------------------------------------------------------------ drawing ------
  let casterN = 0;
  function pushQuad(m, ex, ez, hw, hh, r, soft, str) {
    const need = (casterN + 6) * 9;
    if (need > casterData.length) { const n = new Float32Array(casterData.length * 2); n.set(casterData); casterData = n; }
    const corner = (lx, lz) => {
      const p = M4.apply(m, [lx, 0, lz]);
      const L = norm(sub(LAMP, p));
      const k = p[1] / Math.max(0.2, L[1]);
      return [p[0] - L[0] * k, p[2] - L[2] * k];
    };
    const c = [corner(-ex, -ez), corner(ex, -ez), corner(ex, ez), corner(-ex, ez)];
    const l = [[-ex, -ez], [ex, -ez], [ex, ez], [-ex, ez]];
    for (const i of [0, 1, 2, 0, 2, 3]) {
      casterData.set([c[i][0], c[i][1], l[i][0], l[i][1], hw, hh, r, soft, str], casterN * 9);
      casterN++;
    }
  }
  /** A soft shadow for something whose bottom is h mm above what it lies on. */
  function caster(m, hw, hh, r, h, strength = 1, tableOnly = false) {
    const soft = 0.45 + h * 0.21;
    const sign = tableOnly ? -1 : 1;
    const str = (strength * 0.62) / (1 + h * 0.026);
    const pad = soft * 1.8 + 1;
    pushQuad(m, hw + pad, hh + pad, hw, hh, r, soft, str * sign);
    if (h < 5) pushQuad(m, hw + 1.6, hh + 1.6, hw, hh, r, 0.32, 0.36 * strength * (1 - h / 5) * sign);
  }

  /** mat: { gloss, frost, metal, rough, coat, card: [w, h] } */
  function drawMesh(m, model, size, deform, kind, tx, mat = {}) {
    prog.m4("uModel", model);
    prog.f("uSize", ...size);
    prog.f("uDeform", ...deform);
    prog.i("uKind", kind);
    prog.f("uGloss", mat.gloss || 0);
    prog.f("uFrost", mat.frost || 0);
    prog.f("uMetal", mat.metal || 0);
    prog.f("uRough", mat.rough ?? 0.5);
    prog.f("uCoat", mat.coat || 0);
    prog.f("uCard", ...(mat.card || [size[0], size[2]]));
    prog.f("uUvRect", ...(mat.uv || [0, 0, 1, 1]));
    prog.f("uInflate", 0);
    prog.f("uLine", 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, tx || blank);
    m.draw();
  }
  /** The cel look's ink outline: the same mesh pushed out a little, back faces only, drawn dark. */
  function outline(m, model, cam, V, px = 1.7) {
    if (!CEL) return;
    const at = M4.apply(model, [0, 0, 0]);
    const d = Math.hypot(at[0] - cam.eye[0], at[1] - cam.eye[1], at[2] - cam.eye[2]);
    const mmPerPx = (2 * d * Math.tan(((V.phone ? 33 : 27) * PI) / 360)) / V.H;
    gl.enable(gl.CULL_FACE);
    gl.cullFace(gl.FRONT);
    prog.m4("uModel", model);
    prog.f("uSize", 1, 1, 1);
    prog.f("uDeform", 0, 0, 0, 0);
    prog.f("uInflate", px * mmPerPx);
    prog.f("uLine", 1);
    m.draw();
    prog.f("uInflate", 0);
    prog.f("uLine", 0);
    gl.disable(gl.CULL_FACE);
  }

  function draw(T, V) {
    const cam = camera(T, V);
    const jolt = [0, 0.5 * knock(T - A1.hit, 12, 10) + 0.5 * knock(T - A2.hit, 12, 10) + 0.4 * knock(T - LOCK.hit, 12, 10) + 1.4 * knock(T - E.hit, 10, 7)];
    const flap = 0.5 * knock(T - A1.hit, 6, 6) + 0.5 * knock(T - A2.hit, 6, 6) + 0.4 * knock(T - LOCK.hit, 6, 6) + 1.3 * knock(T - E.hit, 5, 5);
    const O = things(T, V, cam, jolt);
    const sheetM = M4.trs([jolt[0], 0.15, jolt[1]], { yaw: -0.004 });

    // ---- shadows
    casterN = 0;
    caster(sheetM, A4.w / 2, A4.h / 2, 0.5, 1.2, 0.8, true);
    const cards = [O.p1, ...O.strips, O.stmt, ...(O.docs.on ? O.scans : [])].filter((c) => c.on);
    for (const c of cards) caster(cardModel(c, jolt), c.w / 2, c.h / 2, 1.2, Math.max(0, c.y - 0.15), c.kind === 2 ? 1 : 0.9);
    if (O.lock.on) caster(M4.mul(O.lock.body, M4.trs([0, -4.5, -5])), 12, 15, 5, Math.max(0, O.lock.y - O.stmt.y), 0.9);
    const sp = O.stamp;
    if (sp.on) caster(M4.trs([sp.pos[0], sp.pos[1], sp.pos[2]]), 25.4, 25.4, 25.4, Math.max(0, sp.pos[1]), 1.15);
    gl.bindFramebuffer(gl.FRAMEBUFFER, shadowMap.fb);
    gl.viewport(0, 0, 1024, 1024);
    gl.disable(gl.DEPTH_TEST);
    gl.clearColor(1, 1, 1, 1);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.DST_COLOR, gl.ZERO);
    shadowProg.use();
    shadowProg.f("uRect", ...SHADOW_RECT);
    gl.bindVertexArray(casterVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, casterBuf);
    gl.bufferData(gl.ARRAY_BUFFER, casterData.subarray(0, casterN * 9), gl.DYNAMIC_DRAW);
    gl.drawArrays(gl.TRIANGLES, 0, casterN);
    gl.disable(gl.BLEND);

    // ---- the scene, multisampled
    gl.bindFramebuffer(gl.FRAMEBUFFER, (RT.ms || RT.sharp).fb);
    gl.viewport(0, 0, RT.w, RT.h);
    gl.clearColor(BG[0], BG[1], BG[2], 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.depthMask(true);
    prog.use();
    prog.m4("uView", cam.view);
    prog.m4("uProj", cam.proj);
    prog.f("uEye", ...cam.eye);
    prog.f("uLamp", ...LAMP);
    prog.f("uLampDir", ...lampDir);
    prog.f("uPower", power);
    prog.f("uFocus", cam.focus);
    prog.f("uAperture", cam.aperture);
    prog.f("uShadowRect", ...SHADOW_RECT);
    prog.i("uTex", 0); prog.i("uNoise", 1); prog.i("uShadow", 2);
    prog.i("uLook", CEL ? 1 : 0);
    prog.f("uTable", ...SWEEP[0]);
    prog.f("uTable2", ...SWEEP[1]);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, blank);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, tex.noise);
    gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, shadowMap.tex);
    const none = [0, 0, 0, 0];
    const one = [1, 1, 1];

    // nearest things first, so the paper and the table only shade what's still showing
    if (sp.on) {
      drawMesh(stampMesh, sp.m, one, none, 4, null, { rough: 0.55, coat: 0.85 });
      drawMesh(collarMesh, sp.m, one, none, 4, null, { gloss: 1, metal: 1, rough: 0.24 });
      outline(stampMesh, sp.m, cam, V, 2);
      outline(collarMesh, sp.m, cam, V, 2);
    }
    if (O.lock.on) {
      drawMesh(lockBody, O.lock.body, one, none, 6, null, { gloss: 1, metal: 1, rough: 0.3 });
      drawMesh(shackle, O.lock.shackle, one, none, 4, null, { gloss: 1, metal: 1, rough: 0.12 });
      outline(lockBody, O.lock.body, cam, V);
      outline(shackle, O.lock.shackle, cam, V);
    }
    if (O.docs.on && O.stapleA) {
      const s = O.staple;
      const m = M4.mul(cardModel(s, jolt), M4.trs([-s.w * 0.36, 0.25, -s.h * 0.42], { yaw: 0.75 }));
      drawMesh(cardMesh, m, [stp.w * (s.w / SIZE.scan[0]), 1, stp.h], none, 5, stp.tex);
    }
    for (const c of [...cards].sort((a, b) => b.y - a.y)) drawMesh(cardMesh, cardModel(c, jolt), [c.w, 1, c.h], [2, c.bend, c.twist || 0, 0], c.kind, c.tex, { frost: c.frost || 0, uv: c.uv });
    drawMesh(sheetMesh, sheetM, [A4.w, 1, A4.h], [1, 1.1 + flap, 1.5 + flap * 0.6, 2.4 + flap], 1, tex.form);
    drawMesh(plane, M4.trs([0, 0, 0]), [6000, 1, 6000], none, 0, null);

    // ---- ink on the paper: errors, ticks, the seal
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.DST_COLOR, gl.ZERO, gl.ZERO, gl.ONE);
    gl.depthMask(false);
    decalProg.use();
    decalProg.m4("uView", cam.view);
    decalProg.m4("uProj", cam.proj);
    decalProg.i("uTex", 0);
    const decal = (n, [fx, fy], alpha, reveal, inkK = 1) => {
      if (alpha <= 0.002) return;
      const [wx, wz] = toWorld(fx + n.w / 2 - n.pad, fy - 1.0);
      decalProg.m4("uModel", M4.trs([wx + jolt[0], 0.3, wz + jolt[1]], { yaw: -0.004 }));
      decalProg.f("uSize", n.w, 1, n.h);
      decalProg.f("uDeform", 0, 0, 0, 0);
      decalProg.f("uAlpha", alpha);
      decalProg.f("uReveal", reveal);
      decalProg.f("uInk", inkK);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, n.tex);
      plane.draw();
    };
    const typed = (n, t0, dur) => {
      const u = clamp((T - t0) / dur);
      const i = Math.floor(u * n.stops.length);
      return i <= 0 ? 0 : n.stops[Math.min(n.stops.length - 1, i - 1)] + 0.002;
    };
    const fadeOut = (t) => 1 - rise(T, t, t + 0.25);
    const flash = (t) => 1 + 0.25 * Math.exp(-(T - t) * 6) * (0.5 + 0.5 * Math.cos((T - t) * 40));
    // the photo: first error, replaced by the second, replaced by the green tick
    if (T > A1.hit && T < A2.hit + 0.02) decal(notes.err1, NOTE.photo, 1, typed(notes.err1, A1.hit + 0.04, 0.45));
    if (T >= A2.hit + 0.02 && T < J1.place1 + 0.3) decal(notes.err2, NOTE.photo, fadeOut(J1.place1), typed(notes.err2, A2.hit + 0.04, 0.5), 1 / flash(A2.hit));
    if (T > J1.place1 + 0.05) decal(notes.ok1, NOTE.photo, rise(T, J1.place1 + 0.05, J1.place1 + 0.2), typed(notes.ok1, J1.place1 + 0.1, 0.35));
    // the statement: refused for its password, then the tick
    if (T > LOCK.hit && T < J2.place1 + 0.3) decal(notes.errLock, NOTE.stmt, fadeOut(J2.place1), typed(notes.errLock, LOCK.hit + 0.04, 0.55), 1 / flash(LOCK.hit));
    if (T > J2.place1 + 0.05) decal(notes.ok2, NOTE.stmt, rise(T, J2.place1 + 0.05, J2.place1 + 0.2), typed(notes.ok2, J2.place1 + 0.1, 0.35));
    if (T > J3.place1 + 0.05) decal(notes.ok3, NOTE.docs, rise(T, J3.place1 + 0.05, J3.place1 + 0.2), typed(notes.ok3, J3.place1 + 0.1, 0.4));
    if (T >= E.hit) {
      const grow = lerp(0.94, 1, rise(T, E.hit, E.hit + 0.07));
      decalProg.m4("uModel", M4.trs([STAMPW[0] + jolt[0], 0.3, STAMPW[1] + jolt[1]], { yaw: -0.2 }, [grow, 1, grow]));
      decalProg.f("uSize", seal.w, 1, seal.h);
      decalProg.f("uDeform", 0, 0, 0, 0);
      decalProg.f("uAlpha", 0.92);
      decalProg.f("uReveal", 1);
      decalProg.f("uInk", 1);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, seal.tex);
      plane.draw();
    }
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);

    // ---- lens blur and the final picture
    if (RT.ms) RT.ms.resolve(RT.sharp);
    gl.bindVertexArray(emptyVao);
    const pass = (p, dst, srcTex, set) => {
      gl.bindFramebuffer(gl.FRAMEBUFFER, dst ? dst.fb : null);
      gl.viewport(0, 0, dst ? dst.w : RT.w, dst ? dst.h : RT.h);
      p.use();
      p.i("uSrc", 0);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, srcTex);
      set(p);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    };
    pass(downProg, RT.half, RT.sharp.tex, (p) => p.f("uTexel", 1 / RT.w, 1 / RT.h));
    const maxR = 7.5 * (V.dpr / 2) * (V.phone ? 0.8 : 1);
    pass(blurProg, RT.tmp, RT.half.tex, (p) => { p.f("uDir", 1 / RT.half.w, 0); p.f("uMax", maxR); });
    pass(blurProg, RT.blur, RT.tmp.tex, (p) => { p.f("uDir", 0, 1 / RT.half.h); p.f("uMax", maxR); });
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, RT.w, RT.h);
    finalProg.use();
    finalProg.i("uSharp", 0); finalProg.i("uBlur", 1);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, RT.sharp.tex);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, RT.blur.tex);
    finalProg.f("uRes", RT.w, RT.h);
    finalProg.f("uTop", lerp(V.top, V.topEnd || V.top, rise(T, E.hit + 0.3, E.hit + 1.2)));
    finalProg.f("uDpr", V.dpr);
    finalProg.f("uTime", V.clock);
    finalProg.f("uBg", ...BG);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.bindVertexArray(null);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, blank);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, blank);

    return overlay(T, V, cam, O, jolt);
  }

  // ------------------------------------------------------------------------------------------ the DOM layer
  /** Screen rectangle of a card (its four corners, lifted). */
  function rectOf(c, cam, jolt) {
    const m = cardModel(c, jolt);
    const pts = [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([a, b]) => cam.project(M4.apply(m, [(a * c.w) / 2, 0, (b * c.h) / 2])));
    const xs = pts.map((p) => p[0]), ys = pts.map((p) => p[1]);
    return { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
  }
  function overlay(T, V, cam, O, jolt) {
    const out = { rects: {}, keyA: 0, key: "shift" };
    for (const k of ["p1", "stmt", "docs"]) if (O[k].on) out.rects[k] = rectOf(O[k], cam, jolt);
    const grab = (c) => cam.project(M4.apply(cardModel(c, jolt), [c.w * 0.3, 0, -c.h * 0.08]));
    const centreOf = (c) => cam.project(M4.apply(cardModel(c, jolt), [0, 0, 0]));
    // the failed tries: your pointer drags it over and lets go; the form throws it back
    for (const [A, c] of [[A1, O.p1], [A2, O.p1]]) {
      if (T >= A.in0 && T < A.back + 0.35) {
        // after the slam the pointer stays where it let go
        const at = T < A.hit ? grab(c) : grab({ ...c, x: PHOTO[0] + 3, z: PHOTO[1], y: 0.5, yaw: 0.04, pitch: 0, roll: 0, rest: false });
        out.pointer = { x: at[0], y: at[1], a: rise(T, A.in0, A.in0 + 0.15) * (1 - rise(T, A.back, A.back + 0.3)) };
      }
    }
    // the crop frame on the print: its corners, closing in from the edges to the crop; the pointer drags its corner
    if (T >= CROP.in && T < CROP.out) {
      const f = O.p1.full;
      const m = M4.trs([f.x, f.y, f.z], { yaw: f.yaw });
      const [W, H] = SIZE.p1;
      const corners = (r) => [[r[0], r[1]], [r[2], r[1]], [r[2], r[3]], [r[0], r[3]]].map(([u, v]) => cam.project(M4.apply(m, [(u - 0.5) * W, 0.05, (v - 0.5) * H])));
      const k = outCubic((T - CROP.squeeze0) / (CROP.squeeze1 - CROP.squeeze0));
      const r = [0, 1, 2, 3].map((i) => lerp([0, 0, 1, 1][i], CROP_UV[i], k));
      const rect = corners(r);
      out.crop = {
        a: rise(T, CROP.in, CROP.in + 0.2) * (1 - rise(T, CROP.cut + 0.06, CROP.out)),
        print: corners([0, 0, 1, 1]), rect, flash: T >= CROP.cut ? Math.exp(-(T - CROP.cut) * 9) : 0,
        dim: T < CROP.cut ? 1 : 0, // the dimmed edges are the strips: once cut, they fly off in 3D
      };
      const pa = rise(T, CROP.in + 0.05, CROP.in + 0.25) * (1 - rise(T, CROP.cut, CROP.cut + 0.2));
      if (pa > 0.01) out.pointer = { x: rect[2][0] - 4, y: rect[2][1] - 4, a: pa };
    }
    const jobs = [[J1, O.p1, 0], [J2, O.stmt, 1], [J3, O.docs, 2]];
    for (const [J, c, i] of jobs) {
      if (T < J.in0 || T > J.end) continue;
      const at = grab(c);
      let a = rise(T, J.in0, J.in0 + 0.2) * (1 - rise(T, J.place1 + 0.05, J.place1 + 0.35));
      // while the password is typed, the hand is on the keyboard
      if (J === J2) a *= 1 - rise(T, LOCK.pwIn, LOCK.pwIn + 0.15) * (1 - rise(T, LOCK.pop, LOCK.pop + 0.2));
      if (a > 0.01) out.pointer = { x: at[0], y: at[1] + (T > J.place1 ? (T - J.place1) * 40 : 0), a };
      out.keyA = Math.max(out.keyA, rise(T, J.in0, J.in0 + 0.3) * (1 - rise(T, J.done, J.end)));
      if (F.shiftDown(T, J)) out.shift = true;
      const k = V.phone ? 0.78 : 1;
      const spot = keepOnScreen(centreOf(c), k, V);
      if (T >= J.shift && T <= J.let + 0.3) {
        const o = F.RINGS[i];
        const [ax, ay] = F.aimAt(T, J, o.slice, k, o.page || "faves");
        const u = rise(T, J.shift, J.aim);
        out.ring = { ...F.ringAt(T, J, o), x: spot[0], y: spot[1], k };
        out.pointer = { x: lerp(at[0], spot[0], u) + ax, y: lerp(at[1], spot[1], u) + ay, a: 1 };
      }
      if (J === J2) {
        const pw = F.passwordAt(T);
        if (pw.a > 0.001) out.pw = { ...pw, x: spot[0], y: spot[1] };
        if (T >= LOCK.enter - 0.25 && T < LOCK.pwOut + 0.2) { out.key = "return"; out.shift = pw.press && T < LOCK.enter + 0.16; }
      }
    }
    if (T > E.t0) out.pointer = null;
    return out;
  }

  /** Keeps the ring (and, on bigger screens, its tab bar) on screen, under the words and above the key chip. */
  function keepOnScreen([x, y], k, V) {
    const r = 160 * k; // the disc
    const half = V.phone ? r : Math.max(r, 228 * k); // the tab bar is wider than the disc
    const below = V.phone ? r : 210 * k; // and hangs lower
    const minY = Math.max(r + 70, V.top - 8 + r);
    const maxY = V.H - below - (V.phone ? 66 : 84);
    return [Math.min(V.W - half - 8, Math.max(half + 8, x)), Math.max(minY, Math.min(Math.max(minY, maxY), y))];
  }

  /** GPU cost check: draws n frames and waits for the GPU; returns ms per frame. */
  function gpuTime(T, V, n = 30) {
    const px = new Uint8Array(4);
    draw(T, V);
    gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
    const t0 = performance.now();
    for (let i = 0; i < n; i++) draw(T + i * 0.01, V);
    gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
    return (performance.now() - t0) / n;
  }

  return { resize, draw, gpuTime, lighter, gl };
}
