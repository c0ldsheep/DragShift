// The scene's shaders. Lighting is one warm desk lamp (a soft spot from the top left) plus a cool night fill;
// colours are worked in linear light and written out tone-mapped. Every surface also writes how far it is from
// the focus (alpha), which the post passes turn into lens blur.

export const MESH_VS = `#version 300 es
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNor;
layout(location = 2) in vec2 aUv;
layout(location = 3) in vec3 aCol;
uniform mat4 uModel, uView, uProj;
uniform vec3 uSize;
uniform vec4 uDeform; // x: 0 none, 1 sheet curl, 2 card bend; y, z, w: amounts in mm
uniform float uInflate; // the outline hull: push out along the normal (mm)
out vec3 vW;
out vec3 vN;
out vec2 vUv;
out vec3 vCol;
out vec3 vObj;
out float vDepth;

float hf(vec2 p) {
  if (uDeform.x < 1.5) {
    vec2 h = uSize.xz * 0.5;
    float cx = pow(max(0.0, 1.0 - (h.x - abs(p.x)) / 13.0), 2.0);
    float cz = pow(max(0.0, 1.0 - (h.y - abs(p.y)) / 17.0), 2.0);
    return uDeform.y * cx * (p.x > 0.0 ? 1.0 : 0.55) + uDeform.z * cz * (p.y > 0.0 ? 0.6 : 1.0)
         + uDeform.w * cx * cz * (p.x > 0.0 ? 1.0 : 0.45);
  }
  vec2 q = p / (uSize.xz * 0.5);
  return uDeform.y * q.y * q.y + uDeform.z * q.x * q.y + uDeform.w * q.x * q.x;
}

void main() {
  vec3 p = aPos * uSize;
  vec3 n = aNor;
  if (uDeform.x > 0.5) {
    float e = 0.6;
    float y0 = hf(p.xz);
    float yx = hf(p.xz + vec2(e, 0.0));
    float yz = hf(p.xz + vec2(0.0, e));
    p.y += y0;
    n = normalize(vec3(-(yx - y0) / e, 1.0, -(yz - y0) / e));
  }
  vObj = p;
  p += n * uInflate;
  vec4 w = uModel * vec4(p, 1.0);
  vW = w.xyz;
  vN = normalize(mat3(uModel) * n);
  vUv = aUv;
  vCol = aCol;
  vec4 v = uView * w;
  vDepth = -v.z;
  gl_Position = uProj * v;
}`;

export const MESH_FS = `#version 300 es
precision highp float;
in vec3 vW;
in vec3 vN;
in vec2 vUv;
in vec3 vCol;
in vec3 vObj;
in float vDepth;
// kinds: 0 desk, 1 form, 2 glossy print, 3 matte paper, 4 solid (vertex colour), 5 staple, 6 padlock body
uniform int uKind;
uniform int uLook; // 0 studio (real materials), 1 cel (flat, outlined)
uniform sampler2D uTex;
uniform sampler2D uNoise;
uniform sampler2D uShadow;
uniform vec3 uEye, uLamp, uLampDir;
uniform float uPower, uFocus, uAperture, uGloss, uFrost, uMetal, uRough, uCoat, uLine;
uniform vec2 uCard; // card size in mm (cel ink edges)
uniform vec4 uUvRect; // which part of the picture a card shows (a cropped print, its trimmed strips)
uniform vec3 uTable, uTable2; // the studio backdrop's two colours: peach running into pink
uniform vec4 uShadowRect;
out vec4 o;

const vec3 LAMPC = vec3(1.0, 0.905, 0.77);
const vec3 AMB = vec3(0.80, 0.85, 0.96);
const float PI = 3.14159265;

// the studio backdrop: peach running into pink
vec3 sweep(vec2 xz) { return mix(uTable, uTable2, smoothstep(-460.0, 420.0, xz.x * 0.8 - xz.y * 0.6)); }
vec3 tone(vec3 c) { c = 1.0 - exp(-c * 1.85); return pow(c, vec3(1.0 / 2.2)); }
float aa(float x, float e) { float w = max(fwidth(x), 1e-4); return smoothstep(e - w, e + w, x); }

// The room the shiny things reflect: dim warm walls, the bright desk below, the lamp's shade up and to the left,
// and a tall soft window on the right. Rougher surfaces see it wider and dimmer.
vec3 room(vec3 R, float rough, vec3 L) {
  vec3 c = mix(vec3(0.13, 0.12, 0.11), vec3(0.36, 0.35, 0.33), smoothstep(-0.05, 0.85, R.y));
  c = mix(c, vec3(0.78, 0.75, 0.70), 1.0 - smoothstep(-0.4, 0.02, R.y)); // (edges in order: reversed is undefined on Apple GPUs)
  float w = 0.012 + rough * 0.4;
  c += LAMPC * smoothstep(1.0 - w * 2.2, 1.0 - w * 0.25, dot(R, L)) * mix(7.0, 1.2, rough);
  vec3 Wd = normalize(vec3(0.9, 0.42, 0.35));
  vec3 Wr = normalize(cross(vec3(0.0, 1.0, 0.0), Wd));
  float a = dot(R, Wd);
  vec2 q = vec2(dot(R, Wr), R.y - Wd.y * a) / max(a, 0.05);
  float s = 0.02 + rough * 0.5;
  float win = step(0.0, a) * (1.0 - smoothstep(0.42 - s, 0.42 + s, abs(q.x))) * (1.0 - smoothstep(0.75 - s, 0.75 + s, abs(q.y)));
  // window bars
  win *= 1.0 - (1.0 - smoothstep(0.012, 0.03 + s, abs(q.x))) * (1.0 - rough);
  c += vec3(0.86, 0.93, 1.05) * win * mix(2.6, 0.7, rough);
  return c;
}

// one physically based layer: diffuse + GGX lamp highlight + the room's reflection
vec3 layer(vec3 alb, float metal, float rough, vec3 N, vec3 V, vec3 L, vec3 lamp, vec3 amb, float refl) {
  vec3 F0 = mix(vec3(0.04), alb, metal);
  float ndl = max(dot(N, L), 0.0), ndv = max(dot(N, V), 1e-3);
  vec3 H = normalize(L + V);
  float nh = max(dot(N, H), 0.0), vh = max(dot(V, H), 0.0);
  float a = max(rough * rough, 0.003);
  float d = nh * nh * (a * a - 1.0) + 1.0;
  float D = a * a / (PI * d * d);
  float k = (rough + 1.0) * (rough + 1.0) / 8.0;
  float G = (ndl / (ndl * (1.0 - k) + k)) * (ndv / (ndv * (1.0 - k) + k));
  vec3 F = F0 + (1.0 - F0) * pow(1.0 - vh, 5.0);
  vec3 spec = D * G * F / (4.0 * ndl * ndv + 1e-3) * lamp * ndl;
  vec3 Fr = F0 + (max(vec3(1.0 - rough), F0) - F0) * pow(1.0 - ndv, 5.0);
  vec3 diff = alb * (1.0 - metal) * (lamp * ndl + amb);
  return diff + spec + room(reflect(-V, N), rough, L) * Fr * refl;
}

// a clear varnish or gloss on top (prints, the stamp's wood)
vec3 coat(vec3 base, float rough, vec3 N, vec3 V, vec3 L, vec3 lamp) {
  float ndv = max(dot(N, V), 1e-3);
  float F = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);
  vec3 H = normalize(L + V);
  float nh = max(dot(N, H), 0.0);
  float a = rough * rough;
  float d = nh * nh * (a * a - 1.0) + 1.0;
  float D = a * a / (PI * d * d);
  vec3 spec = D * 0.25 * lamp * max(dot(N, L), 0.0) * (0.04 + 0.96 * pow(1.0 - max(dot(V, H), 0.0), 5.0));
  return base * (1.0 - F) + spec + room(reflect(-V, N), rough, L) * F * 0.8;
}

// the cel look: shade / lit / hot tones split at crisp terminators, a cool lift in the shade, a rim, a glint
vec3 cel(vec3 alb, vec3 N, vec3 V, vec3 L, float sh, float glint) {
  float nl = dot(N, L) * mix(0.15, 1.0, step(0.6, sh));
  vec3 shade = alb * vec3(0.66, 0.68, 0.82), lit = alb, hot = min(alb * 1.1 + 0.04, vec3(1.0));
  vec3 c = mix(shade, lit, aa(nl, 0.05));
  c = mix(c, hot, aa(nl, 0.65));
  float rim = aa(1.0 - max(dot(N, V), 0.0), 0.62) * smoothstep(-0.2, 0.3, dot(N, normalize(vec3(-0.3, 0.55, -0.8))));
  c = mix(c, vec3(1.0, 0.98, 0.93), rim * 0.55 * glint);
  vec3 H = normalize(L + V);
  c = mix(c, vec3(1.0, 0.99, 0.95), aa(dot(N, H), 0.985) * glint);
  return c;
}

void main() {
  if (uLine > 0.0) { o = vec4(0.11, 0.1, 0.13, 0.0); return; } // the cel outline hull
  vec3 N = normalize(vN);
  if (!gl_FrontFacing) N = -N;
  vec3 V = normalize(uEye - vW);
  vec3 Lv = uLamp - vW;
  float dist = length(Lv);
  vec3 L = Lv / dist;
  float spot = smoothstep(0.40, 0.94, dot(-L, uLampDir));
  float atten = uPower * spot / (1.0 + pow(dist / 1100.0, 2.0));
  vec4 nz = texture(uNoise, vW.xz * 0.0045);
  vec4 nf = texture(uNoise, vW.xz * 0.055);
  vec4 smap = texture(uShadow, (vW.xz - uShadowRect.xy) / uShadowRect.zw);
  float sh = uKind == 0 ? smap.r : (uKind == 1 ? smap.g : 1.0);
  if (uLook == 1) sh = mix(0.72, 1.0, smoothstep(0.62, 0.68, sh)); // hard-edged cel shadows
  vec3 lamp = LAMPC * atten * sh;
  vec3 amb = AMB * (0.22 + 0.1 * N.y) * mix(0.72, 1.0, sh);
  vec3 alb;
  vec3 col;
  float coc = clamp(uAperture * abs(vDepth - uFocus) / vDepth, 0.0, 1.0);

  if (uKind == 0) {
    // the desk: a seamless studio backdrop, peach running into pink (the app's own colours)
    alb = sweep(vW.xz) * (uLook == 1 ? 1.0 : 0.97 + 0.05 * nz.g);
    col = uLook == 1 ? alb * mix(vec3(0.74, 0.75, 0.86), vec3(1.0), sh) * 1.02 : layer(alb, 0.0, 0.72, N, V, L, lamp, amb, 0.35);
  } else if (uKind == 1 || uKind == 3) {
    vec2 uv = vUv;
    if (uKind == 3) uv = mix(uUvRect.xy, uUvRect.zw, vUv);
    alb = uKind == 1 ? texture(uTex, uv).rgb * vec3(0.99, 0.98, 0.955) : (gl_FrontFacing ? texture(uTex, uv, uFrost * 3.2).rgb : vec3(0.92, 0.91, 0.89));
    if (uKind == 3) alb = mix(alb, vec3(0.78, 0.79, 0.81), uFrost * 0.35);
    if (uLook == 1) {
      col = cel(alb, N, V, L, sh, 0.0) * 1.04;
      vec2 e = min(uv, 1.0 - uv) * uCard;
      col = mix(vec3(0.11, 0.1, 0.13), col, aa(min(e.x, e.y), 0.32)); // ink edge
    } else {
      alb *= 0.98 + 0.04 * nf.r;
      N = normalize(N + vec3(nf.b - 0.5, 0.0, nf.a - 0.5) * 0.06);
      col = layer(alb, 0.0, 0.75, N, V, L, lamp, amb, 0.2);
    }
  } else if (uKind == 2) {
    alb = gl_FrontFacing ? texture(uTex, mix(uUvRect.xy, uUvRect.zw, vUv)).rgb : vec3(0.92, 0.91, 0.89);
    if (uLook == 1) {
      col = cel(alb, N, V, L, sh, gl_FrontFacing ? 0.8 : 0.0) * 1.04;
      vec2 e = min(vUv, 1.0 - vUv) * uCard;
      col = mix(vec3(0.11, 0.1, 0.13), col, aa(min(e.x, e.y), 0.35));
    } else {
      col = layer(alb, 0.0, 0.6, N, V, L, lamp, amb, 0.0);
      if (gl_FrontFacing) col = coat(col, 0.07, N, V, L, lamp); // photo gloss: the room slides across it
    }
  } else if (uKind == 5) {
    vec4 t = texture(uTex, vUv);
    if (t.a < 0.5) discard;
    col = uLook == 1 ? cel(vec3(0.72, 0.74, 0.78), N, V, L, 1.0, 1.0) : layer(vec3(0.85, 0.86, 0.88), 1.0, 0.25, N, V, L, lamp, amb, 1.0);
  } else {
    alb = vCol;
    // the padlock's face: a keyhole cut into the brass (round top, slot below), with a dark inside
    float hole = 0.0;
    if (uKind == 6 && vObj.y > 4.0) {
      vec2 q = vObj.xz - vec2(0.0, 1.2);
      float circle = length(q) - 1.9;
      vec2 sq = abs(q - vec2(0.0, 2.4)) - vec2(0.75, 2.4);
      float slot = length(max(sq, 0.0)) + min(max(sq.x, sq.y), 0.0);
      float k = min(circle, slot);
      hole = 1.0 - smoothstep(-0.12, 0.12, k);
      float lip = (1.0 - smoothstep(0.0, 0.5, abs(k - 0.3))) * (1.0 - hole);
      N = normalize(N + vec3(q.x, 0.0, q.y) * 0.08 * lip);
    }
    if (uLook == 1) {
      col = cel(alb, N, V, L, 1.0, uMetal > 0.5 ? 1.0 : 0.4) * 1.05;
    } else {
      if (uGloss < 0.5 && uMetal < 0.5) {
        // wood: long grain along the turned handle, rings across the base
        float g = texture(uNoise, vec2(atan(vObj.z, vObj.x) * 0.25, vObj.y * 0.012)).r;
        float rings = sin(length(vObj.xz) * 1.4 + g * 6.0) * 0.5 + 0.5;
        alb *= 0.8 + 0.28 * g + 0.08 * rings;
      }
      col = layer(alb, uMetal, uRough, N, V, L, lamp, amb, 1.0);
      if (uCoat > 0.0) col = mix(col, coat(col, 0.12, N, V, L, lamp), uCoat);
    }
    col = mix(col, vec3(0.02, 0.018, 0.016), hole);
  }
  o = vec4(uLook == 1 ? pow(clamp(col, 0.0, 1.0), vec3(1.0 / 2.2)) : tone(col), coc);
}`;

/** Ink printed or stamped on the paper: multiplies what's under it (blend DST_COLOR, ZERO; alpha kept). */
export const DECAL_FS = `#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uTex;
uniform float uAlpha, uReveal, uInk;
out vec4 o;
void main() {
  vec4 t = texture(uTex, vUv);
  float a = t.a * uAlpha * (vUv.x <= uReveal ? 1.0 : 0.0);
  vec3 ink = pow(t.rgb, vec3(1.0 / 2.2));
  o = vec4(mix(vec3(1.0), ink * uInk, a), 1.0);
}`;

/** Soft shadows, drawn top-down into a light map (white = lit), one quad per caster, multiplied together. */
export const SHADOW_VS = `#version 300 es
layout(location = 0) in vec2 aPos;  // ground x, z
layout(location = 1) in vec2 aNor;  // caster-space mm
layout(location = 2) in vec2 aUv;   // caster half size
layout(location = 3) in vec3 aCol;  // corner radius, softness, strength
uniform vec4 uRect;
out vec2 vL;
out vec2 vHalf;
out vec3 vP;
void main() {
  vL = aNor; vHalf = aUv; vP = aCol;
  vec2 p = (aPos - uRect.xy) / uRect.zw * 2.0 - 1.0;
  gl_Position = vec4(p, 0.0, 1.0);
}`;
export const SHADOW_FS = `#version 300 es
precision highp float;
in vec2 vL;
in vec2 vHalf;
in vec3 vP;
out vec4 o;
float sdRR(vec2 p, vec2 b, float r) { vec2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }
void main() {
  float d = sdRR(vL, vHalf, vP.x);
  float a = (1.0 - smoothstep(-vP.y, vP.y, d)) * abs(vP.z);
  // negative strength: falls on the table only (the form's own shadow must not darken the form)
  o = vP.z < 0.0 ? vec4(1.0 - a, 1.0, 1.0, 1.0) : vec4(vec3(1.0 - a), 1.0);
}`;

// ------------------------------------------------------------------------------------------- post -----------

export const QUAD_VS = `#version 300 es
const vec2 P[3] = vec2[3](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
out vec2 vUv;
void main() { vec2 p = P[gl_VertexID]; vUv = p * 0.5 + 0.5; gl_Position = vec4(p, 0.0, 1.0); }`;

export const DOWN_FS = `#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uSrc;
uniform vec2 uTexel;
out vec4 o;
void main() {
  vec4 a = texture(uSrc, vUv + uTexel * vec2(-0.5, -0.5));
  vec4 b = texture(uSrc, vUv + uTexel * vec2(0.5, -0.5));
  vec4 c = texture(uSrc, vUv + uTexel * vec2(-0.5, 0.5));
  vec4 d = texture(uSrc, vUv + uTexel * vec2(0.5, 0.5));
  o = vec4((a.rgb + b.rgb + c.rgb + d.rgb) * 0.25, max(max(a.a, b.a), max(c.a, d.a)));
}`;

export const BLUR_FS = `#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uSrc;
uniform vec2 uDir;
uniform float uMax;
out vec4 o;
void main() {
  vec4 c0 = texture(uSrc, vUv);
  float r = uMax * c0.a;
  vec3 acc = c0.rgb;
  float ws = 1.0;
  for (int i = 1; i <= 6; i++) {
    float t = float(i) / 6.0;
    float w = exp(-t * t * 2.2);
    vec4 a = texture(uSrc, vUv + uDir * t * r);
    vec4 b = texture(uSrc, vUv - uDir * t * r);
    float ka = w * (0.12 + 0.88 * a.a);
    float kb = w * (0.12 + 0.88 * b.a);
    acc += a.rgb * ka + b.rgb * kb;
    ws += ka + kb;
  }
  o = vec4(acc / ws, c0.a);
}`;

export const FINAL_FS = `#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uSharp;
uniform sampler2D uBlur;
uniform vec2 uRes;
uniform float uTop, uDpr, uTime;
uniform vec3 uBg;
out vec4 o;
void main() {
  vec4 s = texture(uSharp, vUv);
  vec3 b = texture(uBlur, vUv).rgb;
  vec3 c = mix(s.rgb, b, smoothstep(0.04, 0.42, s.a));
  // under the words the scene melts into the page
  float y = (1.0 - vUv.y) * uRes.y / uDpr;
  float f = 1.0 - smoothstep(uTop * 0.5, uTop * 1.04, y);
  c = mix(c, uBg, f * 0.97);
  // and at the bottom edge, so the film ends softly into the page under it
  float yb = vUv.y * uRes.y / uDpr;
  c = mix(c, uBg, (1.0 - smoothstep(0.0, 110.0, yb)) * 0.75);
  vec2 q = vUv - 0.5;
  c *= 1.0 - dot(q, q) * 0.09;
  float n = fract(sin(dot(gl_FragCoord.xy + fract(uTime) * 97.0, vec2(12.9898, 78.233))) * 43758.5453);
  c += (n - 0.5) * 0.016;
  o = vec4(c, 1.0);
}`;
