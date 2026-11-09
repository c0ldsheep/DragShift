// A small WebGL2 rasteriser for one table-top scene (no libraries). Meshes are drawn with real perspective into a
// multisampled buffer, then blurred where they're out of focus. Everything here is plain helpers: context,
// programs, meshes (grids, lathes, boxes), textures, framebuffers and a few matrix functions.

export function createGL(canvas, { keep = false } = {}) {
  const gl = canvas.getContext("webgl2", {
    alpha: false, antialias: false, depth: false, stencil: false, premultipliedAlpha: false,
    preserveDrawingBuffer: keep, powerPreference: "high-performance",
  });
  if (!gl) throw new Error("WebGL2 is off");
  return gl;
}

export function program(gl, vs, fs) {
  return finishProgram(gl, startProgram(gl, vs, fs));
}

/** Starts compiling and linking without waiting for the result: asking for it is what makes the page wait. With
 *  KHR_parallel_shader_compile the browser does the work on another thread meanwhile (see programsReady). */
export function startProgram(gl, vs, fs) {
  const mk = (type, src) => {
    const s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    return { s, src };
  };
  const p = gl.createProgram();
  const parts = [mk(gl.VERTEX_SHADER, vs), mk(gl.FRAGMENT_SHADER, fs)];
  for (const { s } of parts) gl.attachShader(p, s);
  gl.bindAttribLocation(p, 0, "aPos");
  gl.bindAttribLocation(p, 1, "aNor");
  gl.bindAttribLocation(p, 2, "aUv");
  gl.bindAttribLocation(p, 3, "aCol");
  gl.linkProgram(p);
  return { p, parts };
}

/** True once every started program has finished linking (always true without the extension: then the first
 *  status check simply waits). */
export function programsReady(gl, started) {
  const ext = gl.getExtension("KHR_parallel_shader_compile");
  return !ext || started.every(({ p }) => gl.getProgramParameter(p, ext.COMPLETION_STATUS_KHR));
}

/** Checks a started program (compile errors come with numbered source lines) and wraps it. */
export function finishProgram(gl, { p, parts }) {
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
    for (const { s, src } of parts) {
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
        const lines = src.split("\n").map((l, i) => `${i + 1}: ${l}`).join("\n");
        throw new Error(`${gl.getShaderInfoLog(s)}\n${lines}`);
      }
    }
    throw new Error(gl.getProgramInfoLog(p));
  }
  const loc = new Map();
  const U = (n) => { if (!loc.has(n)) loc.set(n, gl.getUniformLocation(p, n)); return loc.get(n); };
  return {
    p,
    use() { gl.useProgram(p); },
    f(n, ...v) { const l = U(n); if (l) gl[`uniform${v.length}f`](l, ...v); },
    fv(n, size, arr) { const l = U(n); if (l) gl[`uniform${size}fv`](l, arr); },
    m4(n, m) { const l = U(n); if (l) gl.uniformMatrix4fv(l, false, m); },
    i(n, v) { const l = U(n); if (l) gl.uniform1i(l, v); },
  };
}

/** Uploads a mesh: { pos, nor, uv, col, idx } (flat arrays). */
export function mesh(gl, { pos, nor, uv, col, idx }) {
  const vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  const buf = (data, loc, size) => {
    if (!data) { gl.disableVertexAttribArray(loc); gl.vertexAttrib4f(loc, 1, 1, 1, 1); return; }
    const b = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, b);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(data), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, size, gl.FLOAT, false, 0, 0);
  };
  buf(pos, 0, 3);
  buf(nor, 1, 3);
  buf(uv, 2, 2);
  buf(col, 3, 3);
  const ib = gl.createBuffer();
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib);
  const big = pos.length / 3 > 65535;
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, big ? new Uint32Array(idx) : new Uint16Array(idx), gl.STATIC_DRAW);
  gl.bindVertexArray(null);
  const n = idx.length;
  const type = big ? gl.UNSIGNED_INT : gl.UNSIGNED_SHORT;
  return { draw() { gl.bindVertexArray(vao); gl.drawElements(gl.TRIANGLES, n, type, 0); } };
}

/** A flat grid on the xz plane, -0.5..0.5 both ways, facing up; uv (0,0) at the far-left corner (-x, -z). */
export function grid(nx, nz) {
  const pos = [], nor = [], uv = [], idx = [];
  for (let j = 0; j <= nz; j++)
    for (let i = 0; i <= nx; i++) {
      const u = i / nx, v = j / nz;
      pos.push(u - 0.5, 0, v - 0.5);
      nor.push(0, 1, 0);
      uv.push(u, v);
    }
  for (let j = 0; j < nz; j++)
    for (let i = 0; i < nx; i++) {
      const a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
      idx.push(a, c, b, b, c, d);
    }
  return { pos, nor, uv, idx };
}

/**
 * A turned shape around the x axis (a pen, a stamp handle). profile: [[x, r, [r,g,b]], ...] from one end to the
 * other. Ends are closed with flat caps.
 */
export function lathe(profile, seg = 28, axis = "x") {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  const put = (x, r, a, nx, nr, c, v) => {
    const ca = Math.cos(a), sa = Math.sin(a);
    const p = [x, r * ca, r * sa];
    const n = [nx, nr * ca, nr * sa];
    if (axis === "y") { pos.push(p[1], p[0], p[2]); nor.push(n[1], n[0], n[2]); }
    else { pos.push(...p); nor.push(...n); }
    uv.push(a / (Math.PI * 2), v);
    col.push(...c);
  };
  // side rings: each profile point twice (hard edges between bands), normals from the neighbouring slope
  const rings = [];
  for (let k = 0; k < profile.length - 1; k++) {
    const [x0, r0, c0] = profile[k];
    const [x1, r1] = profile[k + 1];
    const dx = x1 - x0, dr = r1 - r0;
    const l = Math.hypot(dx, dr) || 1;
    const nx = -dr / l, nr = dx / l;
    const base = pos.length / 3;
    for (let s = 0; s <= seg; s++) {
      const a = (s / seg) * Math.PI * 2;
      put(x0, r0, a, nx, nr, c0, k / profile.length);
      put(x1, r1, a, nx, nr, c0, (k + 1) / profile.length);
    }
    for (let s = 0; s < seg; s++) {
      const a = base + s * 2;
      idx.push(a, a + 2, a + 1, a + 1, a + 2, a + 3);
    }
    rings.push(base);
  }
  // caps
  for (const end of [0, profile.length - 1]) {
    const [x, r, c] = profile[end];
    if (r < 0.01) continue;
    const sgn = end === 0 ? -1 : 1;
    const centre = pos.length / 3;
    put(x, 0, 0, sgn, 0, c, 0);
    for (let s = 0; s <= seg; s++) put(x, r, (s / seg) * Math.PI * 2, sgn, 0, c, 0);
    for (let s = 0; s < seg; s++) {
      if (sgn > 0) idx.push(centre, centre + 1 + s, centre + 2 + s);
      else idx.push(centre, centre + 2 + s, centre + 1 + s);
    }
  }
  // swapping x and y mirrors the shape, so the triangles must turn the other way to face out
  if (axis === "y") for (let k = 0; k < idx.length; k += 3) [idx[k + 1], idx[k + 2]] = [idx[k + 2], idx[k + 1]];
  return { pos, nor, uv, col, idx };
}

/** A round tube along a path of [x, y, z] points (a padlock's shackle). Open ends are fine: they sit in the body. */
export function tube(path, r, seg = 14, c = [1, 1, 1]) {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  const N = path.length;
  for (let i = 0; i < N; i++) {
    const p = path[i];
    const a = path[Math.max(0, i - 1)], b = path[Math.min(N - 1, i + 1)];
    const t = norm(sub(b, a));
    const ref = Math.abs(t[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
    const u = norm(cross(t, ref));
    const v = cross(t, u);
    for (let s = 0; s <= seg; s++) {
      const ang = (s / seg) * Math.PI * 2;
      const n = [u[0] * Math.cos(ang) + v[0] * Math.sin(ang), u[1] * Math.cos(ang) + v[1] * Math.sin(ang), u[2] * Math.cos(ang) + v[2] * Math.sin(ang)];
      pos.push(p[0] + n[0] * r, p[1] + n[1] * r, p[2] + n[2] * r);
      nor.push(...n);
      uv.push(s / seg, i / (N - 1));
      col.push(...c);
    }
  }
  for (let i = 0; i < N - 1; i++)
    for (let s = 0; s < seg; s++) {
      const a = i * (seg + 1) + s, b = a + seg + 1;
      idx.push(a, a + 1, b, a + 1, b + 1, b);
    }
  return { pos, nor, uv, col, idx };
}

/** A box w × h × d with every edge rounded by r (smooth normals), centred on the origin. n: grid per face. */
export function roundedBox(w, h, d, r, n = 10, c = [1, 1, 1]) {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  const half = [w / 2, h / 2, d / 2];
  const inner = half.map((v) => Math.max(0, v - r));
  const faces = [
    [[1, 0, 0], [0, 0, -1], [0, 1, 0]], [[-1, 0, 0], [0, 0, 1], [0, 1, 0]],
    [[0, 1, 0], [1, 0, 0], [0, 0, -1]], [[0, -1, 0], [1, 0, 0], [0, 0, 1]],
    [[0, 0, 1], [1, 0, 0], [0, 1, 0]], [[0, 0, -1], [-1, 0, 0], [0, 1, 0]],
  ];
  for (const [f, u, v] of faces) {
    const base = pos.length / 3;
    for (let j = 0; j <= n; j++)
      for (let i = 0; i <= n; i++) {
        const a = (i / n) * 2 - 1, b = (j / n) * 2 - 1;
        const s = [0, 1, 2].map((k) => (f[k] + a * u[k] + b * v[k]) * half[k]);
        const q = s.map((x, k) => Math.max(-inner[k], Math.min(inner[k], x)));
        const dn = norm(sub(s, q));
        pos.push(q[0] + dn[0] * r, q[1] + dn[1] * r, q[2] + dn[2] * r);
        nor.push(...dn);
        uv.push(i / n, j / n);
        col.push(...c);
      }
    for (let j = 0; j < n; j++)
      for (let i = 0; i < n; i++) {
        const p0 = base + j * (n + 1) + i;
        idx.push(p0, p0 + 1, p0 + n + 1, p0 + 1, p0 + n + 2, p0 + n + 1);
      }
  }
  return { pos, nor, uv, col, idx };
}

/** A box with bevel-free faces, -0.5..0.5, per-face normals, one colour. */
export function box(c = [1, 1, 1]) {
  const pos = [], nor = [], uv = [], col = [], idx = [];
  const faces = [
    [[1, 0, 0], [0, 0, -1], [0, 1, 0]], [[-1, 0, 0], [0, 0, 1], [0, 1, 0]],
    [[0, 1, 0], [1, 0, 0], [0, 0, -1]], [[0, -1, 0], [1, 0, 0], [0, 0, 1]],
    [[0, 0, 1], [1, 0, 0], [0, 1, 0]], [[0, 0, -1], [-1, 0, 0], [0, 1, 0]],
  ];
  for (const [n, u, v] of faces) {
    const base = pos.length / 3;
    for (const [a, b] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
      pos.push(0.5 * (n[0] + a * u[0] + b * v[0]), 0.5 * (n[1] + a * u[1] + b * v[1]), 0.5 * (n[2] + a * u[2] + b * v[2]));
      nor.push(...n);
      uv.push((a + 1) / 2, (b + 1) / 2);
      col.push(...c);
    }
    idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
  }
  return { pos, nor, uv, col, idx };
}

/** A texture from a canvas or image. srgb: sample as linear light. */
export function texture(gl, src, { srgb = true, mips = true, wrap = false } = {}) {
  const t = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, t);
  gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  // a canvas, or plain pixels { width, height, data } painted in the worker (same straight-alpha values)
  if (src.data) gl.texImage2D(gl.TEXTURE_2D, 0, srgb ? gl.SRGB8_ALPHA8 : gl.RGBA8, src.width, src.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, src.data);
  else gl.texImage2D(gl.TEXTURE_2D, 0, srgb ? gl.SRGB8_ALPHA8 : gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, src);
  if (mips) gl.generateMipmap(gl.TEXTURE_2D);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, mips ? gl.LINEAR_MIPMAP_LINEAR : gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  const w = wrap ? gl.REPEAT : gl.CLAMP_TO_EDGE;
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, w);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, w);
  const ext = gl.getExtension("EXT_texture_filter_anisotropic");
  if (ext && mips) gl.texParameterf(gl.TEXTURE_2D, ext.TEXTURE_MAX_ANISOTROPY_EXT, Math.min(8, gl.getParameter(ext.MAX_TEXTURE_MAX_ANISOTROPY_EXT)));
  return t;
}

/** A colour target the size w × h (a texture you can sample afterwards). */
export function target(gl, w, h, { depth = false } = {}) {
  const tex = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  const fb = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
  let rb = null;
  if (depth) {
    rb = gl.createRenderbuffer();
    gl.bindRenderbuffer(gl.RENDERBUFFER, rb);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, w, h);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, rb);
  }
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  return { tex, fb, w, h, free() { gl.deleteTexture(tex); gl.deleteFramebuffer(fb); if (rb) gl.deleteRenderbuffer(rb); } };
}

/** A multisampled colour + depth buffer (smooth edges); resolve() copies it into a plain target. */
export function msaa(gl, w, h, samples) {
  const fb = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
  const c = gl.createRenderbuffer();
  gl.bindRenderbuffer(gl.RENDERBUFFER, c);
  gl.renderbufferStorageMultisample(gl.RENDERBUFFER, samples, gl.RGBA8, w, h);
  gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, c);
  const d = gl.createRenderbuffer();
  gl.bindRenderbuffer(gl.RENDERBUFFER, d);
  gl.renderbufferStorageMultisample(gl.RENDERBUFFER, samples, gl.DEPTH_COMPONENT24, w, h);
  gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, d);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  return {
    fb, w, h,
    resolve(dst) {
      gl.bindFramebuffer(gl.READ_FRAMEBUFFER, fb);
      gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, dst.fb);
      gl.blitFramebuffer(0, 0, w, h, 0, 0, w, h, gl.COLOR_BUFFER_BIT, gl.NEAREST);
      gl.bindFramebuffer(gl.READ_FRAMEBUFFER, null);
      gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
    },
    free() { gl.deleteFramebuffer(fb); gl.deleteRenderbuffer(c); gl.deleteRenderbuffer(d); },
  };
}

// ------------------------------------------------------------------------------------------- matrices -------

export const M4 = {
  ident() { const m = new Float32Array(16); m[0] = m[5] = m[10] = m[15] = 1; return m; },
  mul(a, b) {
    const o = new Float32Array(16);
    for (let c = 0; c < 4; c++)
      for (let r = 0; r < 4; r++) {
        let s = 0;
        for (let k = 0; k < 4; k++) s += a[k * 4 + r] * b[c * 4 + k];
        o[c * 4 + r] = s;
      }
    return o;
  },
  /** Perspective with a lens shift (sx, sy in clip units) so the subject can sit below the words. */
  persp(fovY, aspect, near, far, sx = 0, sy = 0) {
    const f = 1 / Math.tan(fovY / 2);
    const m = new Float32Array(16);
    m[0] = f / aspect; m[5] = f;
    m[8] = sx; m[9] = sy;
    m[10] = (far + near) / (near - far); m[11] = -1;
    m[14] = (2 * far * near) / (near - far);
    return m;
  },
  lookAt(eye, at, up = [0, 1, 0]) {
    const z = norm(sub(eye, at));
    const x = norm(cross(up, z));
    const y = cross(z, x);
    const m = new Float32Array(16);
    m[0] = x[0]; m[4] = x[1]; m[8] = x[2];
    m[1] = y[0]; m[5] = y[1]; m[9] = y[2];
    m[2] = z[0]; m[6] = z[1]; m[10] = z[2];
    m[12] = -dot(x, eye); m[13] = -dot(y, eye); m[14] = -dot(z, eye); m[15] = 1;
    return m;
  },
  /** Translate, then rotate (yaw about y, then pitch about x, then roll about z), then scale. */
  trs([tx, ty, tz], { yaw = 0, pitch = 0, roll = 0 } = {}, [sx, sy, sz] = [1, 1, 1]) {
    const cy = Math.cos(yaw), syw = Math.sin(yaw);
    const cp = Math.cos(pitch), sp = Math.sin(pitch);
    const cr = Math.cos(roll), sr = Math.sin(roll);
    // R = Ry * Rx * Rz
    const Ry = [cy, 0, -syw, 0, 1, 0, syw, 0, cy];
    const Rx = [1, 0, 0, 0, cp, sp, 0, -sp, cp];
    const Rz = [cr, sr, 0, -sr, cr, 0, 0, 0, 1];
    const m3 = (a, b) => {
      const o = new Array(9);
      for (let c = 0; c < 3; c++) for (let r = 0; r < 3; r++) o[c * 3 + r] = a[r] * b[c * 3] + a[3 + r] * b[c * 3 + 1] + a[6 + r] * b[c * 3 + 2];
      return o;
    };
    const R = m3(m3(Ry, Rx), Rz);
    const m = new Float32Array(16);
    m[0] = R[0] * sx; m[1] = R[1] * sx; m[2] = R[2] * sx;
    m[4] = R[3] * sy; m[5] = R[4] * sy; m[6] = R[5] * sy;
    m[8] = R[6] * sz; m[9] = R[7] * sz; m[10] = R[8] * sz;
    m[12] = tx; m[13] = ty; m[14] = tz; m[15] = 1;
    return m;
  },
  /** A rotation whose y axis points along `a` (unit), yaw turned about that axis first. */
  alignY(a, yaw = 0) {
    const y = norm(a);
    const ref = Math.abs(y[2]) < 0.95 ? [0, 0, 1] : [1, 0, 0];
    const x = norm(cross(y, ref));
    const z = cross(x, y);
    const R = new Float32Array(16);
    R[0] = x[0]; R[1] = x[1]; R[2] = x[2];
    R[4] = y[0]; R[5] = y[1]; R[6] = y[2];
    R[8] = z[0]; R[9] = z[1]; R[10] = z[2];
    R[15] = 1;
    return M4.mul(R, M4.trs([0, 0, 0], { yaw }));
  },
  apply(m, [x, y, z]) {
    const w = m[3] * x + m[7] * y + m[11] * z + m[15];
    return [(m[0] * x + m[4] * y + m[8] * z + m[12]) / w, (m[1] * x + m[5] * y + m[9] * z + m[13]) / w, (m[2] * x + m[6] * y + m[10] * z + m[14]) / w];
  },
};
export const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
export const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
export const scale = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
export const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
export const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
export const norm = (v) => { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; };
