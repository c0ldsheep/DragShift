// Everything printed or photographed in the scene, painted once at load into canvases (no image downloads apart
// from the one photo). It runs in a background worker (paint-worker.js) where the browser can, so painting never
// freezes the page, and on the page itself where it can't; the pictures come out the same either way: the application form, the photo prints, a signature slip, three phone scans, the red
// errors and green ticks the form prints, the rubber-stamp ink, a staple, and the noise the shaders use for paper
// fibre and the table. Generic on purpose: no real department, board, bank or brand.

import { A4, BOX, NOTE } from "../film.js";

const SANS = '-apple-system, "SF Pro Text", "Helvetica Neue", Arial, "Segoe UI", sans-serif';
const SERIF = '"Times New Roman", Times, Georgia, serif';
const MONO = '"SF Mono", Menlo, Consolas, "Courier New", monospace';
const HAND = '"Bradley Hand", "Noteworthy", "Segoe Print", "Comic Sans MS", cursive';
export const INK = { navy: "#1f3466", text: "#1c1c22", grey: "#8b8b90", red: "#c4231f", green: "#16793a", blue: "#1d3f9c", pen: "#1b2f8a" };

export function canvas(w, h) {
  const W = Math.max(1, Math.round(w)), H = Math.max(1, Math.round(h));
  if (typeof document === "undefined") return new OffscreenCanvas(W, H); // in the painting worker
  const c = document.createElement("canvas");
  c.width = W;
  c.height = H;
  return c;
}

/** Every picture the scene needs, in the order it uploads them: [name, paint]. paint() gives a canvas, or
 *  { canvas, ...sizes } for the ones placed by size (the notes, the seal, the staple). */
export function jobs(photo, p1Aspect) {
  return [
    ["form", () => formSheet()],
    ["p1", () => print(photo, { aspect: p1Aspect, zoom: 1.0, fy: 0.42, seed: 3 })],
    ["stmt", () => statement()],
    ["scan0", () => scan("Marksheet", 7)],
    ["scan1", () => scan("Birth certificate", 11)],
    ["scan2", () => scan("Address proof", 5)],
    ["noise", () => noise()],
    ...Object.entries(NOTES).map(([k, [t, c]]) => [`note.${k}`, () => noteLine(t, c, 2.8)]),
    ["seal", () => stampInk()],
    ["staple", () => staple()],
  ];
}

/** A painted result as plain pixels (straight, not premultiplied: exactly what uploading the canvas gives WebGL)
 *  plus its sizes. */
export function pixels(result) {
  const c = result.canvas || result;
  const meta = result.canvas ? { ...result } : {};
  delete meta.canvas;
  const img = c.getContext("2d").getImageData(0, 0, c.width, c.height);
  return { width: c.width, height: c.height, data: img.data, meta };
}
const rng = (seed) => () => ((seed = (seed * 16807) % 2147483647) / 2147483647);

// ------------------------------------------------------------------------------------------- the form -------

/** The application form, printed on A4: 2048 px wide (about 9.75 px per mm). */
export function formSheet() {
  const s = 2048 / A4.w;
  const c = canvas(2048, Math.round(A4.h * s));
  const g = c.getContext("2d");
  g.scale(s, s);
  g.fillStyle = "#fff";
  g.fillRect(0, 0, A4.w, A4.h);
  g.textBaseline = "alphabetic";
  const text = (t, x, y, size, { font = SANS, weight = 400, color = INK.text, align = "left", spacing = 0 } = {}) => {
    g.font = `${weight} ${size}px ${font}`;
    g.fillStyle = color;
    g.textAlign = align;
    if ("letterSpacing" in g) g.letterSpacing = `${spacing}px`;
    g.fillText(t, x, y);
    if ("letterSpacing" in g) g.letterSpacing = "0px";
  };
  const rule = (x0, y, x1, w = 0.3, color = INK.navy) => { g.fillStyle = color; g.fillRect(x0, y - w / 2, x1 - x0, w); };
  const frame = (b, w = 0.32, color = "#3b4250") => { g.strokeStyle = color; g.lineWidth = w; g.strokeRect(b.x, b.y, b.w, b.h); };

  // a double border, the way printed forms have one
  g.strokeStyle = INK.navy;
  g.lineWidth = 0.55; g.strokeRect(8, 8, A4.w - 16, A4.h - 16);
  g.lineWidth = 0.2; g.strokeRect(9.4, 9.4, A4.w - 18.8, A4.h - 18.8);

  text("Form No. A-26/1148", 14, 17, 2.5, { color: "#55575e" });
  text("Last date: 09-11-2026, 11:59 PM", 196, 17, 2.7, { weight: 700, color: INK.red, align: "right" });
  text("APPLICATION FORM", A4.w / 2, 27, 7.2, { font: SERIF, weight: 700, color: INK.navy, align: "center", spacing: 0.35 });
  text("Online registration  ·  Part II: upload your documents", A4.w / 2, 33.5, 3.0, { font: SERIF, color: "#33363d", align: "center" });
  rule(14, 38.5, 196, 0.35);

  // registration number, typed into ten boxes
  text("Registration No.", 18, 49.2, 3.1, { weight: 700 });
  const reg = "2611048213";
  for (let i = 0; i < 10; i++) {
    const x = 60 + i * 6.4;
    g.strokeStyle = "#5a606c"; g.lineWidth = 0.22; g.strokeRect(x, 44.2, 6.4, 6.8);
    text(reg[i], x + 3.2, 49.4, 3.9, { font: MONO, weight: 600, color: "#22252c", align: "center" });
  }
  text("Category: General", 136, 49.2, 2.8, { color: "#33363d" });

  // 1 · photograph
  const title = (n, t, y) => { text(n, 18, y, 3.9, { font: SERIF, weight: 700, color: INK.navy }); text(t, 24, y, 3.9, { font: SERIF, weight: 700, color: INK.navy }); };
  const hint = (lines, b, y0) => lines.forEach((l, i) => text(l, b.x + b.w / 2, b.y + y0 + i * 3.6, 2.4, { color: "#a2a3a8", align: "center" }));
  title("1.", "Photograph", 60.5);
  frame(BOX.photo);
  hint(["Affix recent", "passport size", "photograph"], BOX.photo, 19);
  text("Recent colour photograph, light background", 62, 72, 2.9, { color: "#33363d" });
  text("Size 3.5 × 4.5 cm  ·  JPG / JPEG", 62, 78.5, 2.9, { color: "#33363d" });
  text("File size: 10 KB to 50 KB", 62, 85, 2.9, { weight: 700 });

  // 2 · bank statement
  title("2.", "Bank statement", 124.5);
  frame(BOX.stmt);
  hint(["Last 6 months", "PDF"], BOX.stmt, 20);
  text("Last 6 months, all pages, one PDF", 60, 136, 2.9, { color: "#33363d" });
  text("Password-protected files are not accepted", 60, 142.5, 2.9, { weight: 700 });

  // 3 · documents
  title("3.", "Documents", 187.5);
  frame(BOX.docs);
  hint(["All pages", "as one PDF"], BOX.docs, 21);
  text("Marksheet, birth certificate and", 62, 199, 2.9, { color: "#33363d" });
  text("address proof, merged into one PDF", 62, 205.5, 2.9, { color: "#33363d" });
  text("File size: up to 2 MB", 62, 212, 2.9, { weight: 700 });

  rule(14, 250, 196, 0.3);
  text("Declaration", 18, 257.5, 3.0, { font: SERIF, weight: 700, color: INK.navy });
  text("I hereby declare that all the information and documents given above are true and correct", 18, 263, 2.45, { color: "#33363d" });
  text("to the best of my knowledge. I understand that a wrong document can cancel this application.", 18, 267.4, 2.45, { color: "#33363d" });
  text("Place: ______________________", 18, 281, 2.7, { color: "#33363d" });
  text("Date: 09-11-2026", 18, 287.5, 2.7, { color: "#33363d" });
  rule(140, 282, 192, 0.22, "#5a606c");
  text("Signature of candidate", 192, 287.5, 2.7, { color: "#33363d", align: "right" });
  return c;
}

// ------------------------------------------------------------------------------------------- photos ---------

/** Draws img to cover (w, h), zoomed by z around (fx, fy) (0..1 of the image). */
function cover(g, img, x, y, w, h, z = 1, fx = 0.5, fy = 0.5) {
  const s = Math.max(w / img.width, h / img.height) * z;
  const dw = img.width * s, dh = img.height * s;
  const dx = x + w / 2 - fx * dw, dy = y + h / 2 - fy * dh;
  g.save();
  g.beginPath(); g.rect(x, y, w, h); g.clip();
  g.imageSmoothingQuality = "high";
  g.drawImage(img, Math.min(x, Math.max(x + w - dw, dx)), Math.min(y, Math.max(y + h - dh, dy)), dw, dh);
  g.restore();
}
/** Fine photo grain so an upscaled picture reads as a print, not as a blur. */
function grain(g, w, h, amount, seed) {
  const r = rng(seed);
  const n = canvas(256, 256);
  const k = n.getContext("2d");
  const d = k.createImageData(256, 256);
  for (let i = 0; i < d.data.length; i += 4) { const v = 128 + (r() - 0.5) * 255 * amount; d.data[i] = d.data[i + 1] = d.data[i + 2] = v; d.data[i + 3] = 255; }
  k.putImageData(d, 0, 0);
  g.save();
  g.globalCompositeOperation = "overlay";
  g.globalAlpha = 0.5;
  g.fillStyle = g.createPattern(n, "repeat");
  g.fillRect(0, 0, w, h);
  g.restore();
}

/** A glossy print of the photo with a thin white border. aspect = w / h. */
export function print(img, { aspect = 35 / 45, zoom = 1, fx = 0.5, fy = 0.45, border = 0.035, seed = 3 } = {}) {
  const h = 1024, w = Math.round(h * aspect);
  const c = canvas(w, h);
  const g = c.getContext("2d");
  g.fillStyle = "#fbfaf7";
  g.fillRect(0, 0, w, h);
  const b = Math.round(w * border);
  cover(g, img, b, b, w - 2 * b, h - 2 * b, zoom, fx, fy);
  grain(g, w, h, 0.16, seed);
  return c;
}

/** A phone photo of a signature on a slip of white paper (3 : 1). */
export function signatureSlip() {
  const c = canvas(900, 300);
  const g = c.getContext("2d");
  const grd = g.createRadialGradient(420, 130, 40, 450, 150, 520);
  grd.addColorStop(0, "#fbfaf6"); grd.addColorStop(1, "#e2ded5");
  g.fillStyle = grd; g.fillRect(0, 0, 900, 300);
  g.strokeStyle = INK.pen; g.lineCap = "round"; g.lineJoin = "round";
  g.lineWidth = 7;
  g.beginPath();
  g.moveTo(190, 196);
  g.bezierCurveTo(220, 70, 285, 58, 280, 152);
  g.bezierCurveTo(276, 214, 330, 214, 355, 140);
  g.bezierCurveTo(380, 76, 404, 172, 426, 160);
  g.bezierCurveTo(448, 150, 462, 104, 488, 120);
  g.bezierCurveTo(514, 136, 524, 182, 572, 150);
  g.bezierCurveTo(606, 128, 630, 140, 656, 112);
  g.stroke();
  g.lineWidth = 5;
  g.beginPath(); g.moveTo(214, 232); g.bezierCurveTo(380, 212, 520, 218, 690, 200); g.stroke();
  grain(g, 900, 300, 0.12, 8);
  return c;
}

/** A phone scan of a paper document: warm paper, a title, rows of text and figures. */
export function scan(title, seed) {
  const c = canvas(600, 800);
  const g = c.getContext("2d");
  const grd = g.createLinearGradient(0, 0, 600, 800);
  grd.addColorStop(0, "#f8f6f0"); grd.addColorStop(1, "#ebe7de");
  g.fillStyle = grd; g.fillRect(0, 0, 600, 800);
  const r = rng(seed);
  g.fillStyle = "#24304f"; g.font = `800 38px ${SANS}`; g.fillText(title, 48, 96);
  g.fillStyle = "#b9b3a6"; g.fillRect(48, 118, 504, 4);
  g.fillStyle = "#cfc9bb";
  for (let i = 0; i < 3; i++) g.fillRect(48, 152 + i * 34, 504 * (0.55 + r() * 0.4), 12);
  for (let k = 0; k < 8; k++) {
    const y = 290 + k * 52;
    if (k % 2) { g.fillStyle = "rgba(0,0,0,0.035)"; g.fillRect(48, y - 16, 504, 52); }
    g.fillStyle = "#c4beb0"; g.fillRect(64, y + 2, 160 + r() * 110, 13);
    g.fillStyle = "#3a3f55"; g.font = `700 25px ${SANS}`; g.textAlign = "right";
    g.fillText(String(Math.floor(r() * 90000 + 1000)), 532, y + 18); g.textAlign = "left";
  }
  g.fillStyle = "#c4beb0"; g.fillRect(48, 720, 180, 10);
  grain(g, 600, 800, 0.1, seed + 1);
  return c;
}

/** A bank statement page (no real bank): masked account, a table of entries. A4 portrait. */
export function statement() {
  const c = canvas(600, 848);
  const g = c.getContext("2d");
  g.fillStyle = "#fdfdfb"; g.fillRect(0, 0, 600, 848);
  const r = rng(23);
  g.fillStyle = "#1f3b6e"; g.fillRect(40, 44, 520, 10);
  g.fillStyle = "#1c1c22"; g.font = `800 34px ${SANS}`; g.fillText("Account statement", 40, 104);
  g.fillStyle = "#6a6d75"; g.font = `500 19px ${SANS}`;
  g.fillText("Savings account  XXXX XXXX 4821", 40, 138);
  g.fillText("01 May 2026 to 31 Oct 2026", 40, 164);
  g.fillStyle = "#e9ebef"; g.fillRect(40, 196, 520, 34);
  g.fillStyle = "#4a4e58"; g.font = `700 16px ${SANS}`;
  g.fillText("Date", 52, 219); g.fillText("Details", 160, 219);
  g.textAlign = "right"; g.fillText("Amount", 548, 219); g.textAlign = "left";
  for (let k = 0; k < 14; k++) {
    const y = 262 + k * 38;
    if (k % 2) { g.fillStyle = "rgba(31,59,110,0.04)"; g.fillRect(40, y - 24, 520, 38); }
    g.fillStyle = "#3a3f4b"; g.font = `500 15px ${SANS}`;
    g.fillText(`${String(1 + ((k * 13) % 28)).padStart(2, "0")}/0${5 + (k % 5)}`, 52, y);
    g.fillStyle = "#c9cbd2"; g.fillRect(160, y - 11, 120 + r() * 170, 10);
    g.fillStyle = k % 3 ? "#3a3f4b" : "#16793a"; g.font = `700 15px ${SANS}`; g.textAlign = "right";
    g.fillText(`${k % 3 ? "-" : "+"}${(r() * 9000 + 100).toFixed(2)}`, 548, y); g.textAlign = "left";
  }
  g.fillStyle = "#c9cbd2"; g.fillRect(40, 800, 220, 8);
  return c;
}

// ------------------------------------------------------------------------------------------- decals ---------

const DPM = 18; // decal pixels per mm

/**
 * One printed line (an error or a tick). Returns { canvas, w, h (mm), stops } where stops are the right edges of
 * each character as 0..1 of the width, for typing it out.
 */
export function noteLine(t, color, size = 3.0, weight = 700) {
  const probe = canvas(8, 8).getContext("2d");
  probe.font = `${weight} ${size * DPM}px ${SANS}`;
  const pad = 0.6 * DPM;
  const tw = probe.measureText(t).width;
  const c = canvas(tw + pad * 2, size * DPM * 1.6);
  const g = c.getContext("2d");
  g.font = probe.font;
  g.fillStyle = color;
  g.textBaseline = "middle";
  g.fillText(t, pad, c.height / 2);
  const stops = [];
  for (let i = 1; i <= t.length; i++) stops.push((pad + probe.measureText(t.slice(0, i)).width) / c.width);
  return { canvas: c, w: c.width / DPM, h: c.height / DPM, stops, pad: pad / DPM };
}
export const NOTES = {
  err1: ["✕  File size must be less than 50 KB.", INK.red],
  err2: ["✕  Still too big (1.2 MB). Must be under 50 KB.", INK.red],
  errLock: ["✕  This PDF has a password. Upload it without one.", INK.red],
  ok1: ["✓  Uploaded  ·  48.6 KB", INK.green],
  ok2: ["✓  Uploaded  ·  no password", INK.green],
  ok3: ["✓  Uploaded  ·  1 PDF, 3 pages", INK.green],
};
export { NOTE };

/**
 * The seal: round green rubber-stamp ink, like an office "received" seal. Two rings with the words running round
 * between them, stars at the sides, a band across the middle with SUBMITTED, a tick above and the time below.
 * Gaps where the rubber didn't press.
 */
export function stampInk() {
  const D = 50, OUT = 60; // drawn in 50 mm units, printed 60 mm across
  const c = canvas(D * DPM, D * DPM);
  const g = c.getContext("2d");
  g.scale(DPM, DPM);
  g.translate(D / 2, D / 2);
  const ink = "#13803f";
  g.strokeStyle = ink; g.fillStyle = ink;
  g.lineWidth = 1.5; g.beginPath(); g.arc(0, 0, 23.4, 0, Math.PI * 2); g.stroke();
  g.lineWidth = 0.5; g.beginPath(); g.arc(0, 0, 21.6, 0, Math.PI * 2); g.stroke();
  g.lineWidth = 0.5; g.beginPath(); g.arc(0, 0, 14.6, 0, Math.PI * 2); g.stroke();
  // words round the ring
  const round = (t, r, mid, size, flip) => {
    g.font = `800 ${size}px ${SANS}`;
    g.textAlign = "center"; g.textBaseline = "middle";
    const widths = [...t].map((ch) => g.measureText(ch).width + 0.32);
    const total = widths.reduce((a, b) => a + b, 0);
    let a = mid - (flip ? -1 : 1) * (total / r) / 2;
    [...t].forEach((ch, k) => {
      const da = widths[k] / r;
      const at = a + (flip ? -1 : 1) * da / 2;
      g.save();
      g.rotate(at);
      g.translate(0, flip ? r : -r);
      g.fillText(ch, 0, 0);
      g.restore();
      a += (flip ? -1 : 1) * da;
    });
  };
  round("APPLICATION FORM", 18.1, 0, 3.6, false);
  round("09 · NOV · 2026", 18.1, 0, 3.3, true);
  for (const s of [-1, 1]) {
    g.save(); g.translate(s * 18.1, 0);
    g.font = `900 3.6px ${SANS}`; g.textAlign = "center"; g.textBaseline = "middle"; g.fillText("★", 0, 0.2);
    g.restore();
  }
  // the band
  g.fillRect(-25, -4.6, 50, 0.55);
  g.fillRect(-25, 4.05, 50, 0.55);
  g.save();
  g.beginPath(); g.arc(0, 0, 23.4, 0, Math.PI * 2); g.clip();
  g.clearRect(-25, -4.05, 50, 8.1);
  g.restore();
  g.font = `900 6.4px ${SANS}`; g.textAlign = "center"; g.textBaseline = "middle";
  if ("letterSpacing" in g) g.letterSpacing = "0.35px";
  g.fillText("SUBMITTED", 0, 0.3);
  if ("letterSpacing" in g) g.letterSpacing = "0px";
  // tick and time
  g.lineWidth = 1.1; g.lineCap = "round"; g.lineJoin = "round";
  g.beginPath(); g.moveTo(-2.6, -9.2); g.lineTo(-0.7, -7.3); g.lineTo(2.8, -10.8); g.stroke();
  g.font = `800 2.9px ${SANS}`;
  g.fillText("11:53 PM", 0, 9.2);
  // rubber texture: speckles knocked out, one side pressed lighter
  g.setTransform(1, 0, 0, 1, 0, 0);
  g.globalCompositeOperation = "destination-out";
  const r = rng(17);
  for (let i = 0; i < 1500; i++) {
    g.globalAlpha = 0.2 + r() * 0.7;
    g.beginPath(); g.arc(r() * c.width, r() * c.height, 0.5 + r() * 3, 0, 7); g.fill();
  }
  g.globalAlpha = 0.4;
  const fade = g.createLinearGradient(c.width * 0.15, c.height * 0.1, c.width, c.height);
  fade.addColorStop(0, "rgba(0,0,0,0)"); fade.addColorStop(0.7, "rgba(0,0,0,0)"); fade.addColorStop(1, "rgba(0,0,0,1)");
  g.fillStyle = fade; g.fillRect(0, 0, c.width, c.height);
  return { canvas: c, w: OUT, h: OUT };
}

/** A staple seen from above: a thin bent steel bar. */
export function staple() {
  const c = canvas(14 * DPM, 2.2 * DPM);
  const g = c.getContext("2d");
  const grd = g.createLinearGradient(0, 0, 0, c.height);
  grd.addColorStop(0, "#f2f3f5"); grd.addColorStop(0.45, "#a9adb5"); grd.addColorStop(1, "#5d626c");
  g.fillStyle = grd;
  g.beginPath(); g.roundRect(0.3 * DPM, 0.5 * DPM, 13.4 * DPM, 1.1 * DPM, 0.5 * DPM); g.fill();
  return { canvas: c, w: 14, h: 2.2 };
}

// ------------------------------------------------------------------------------------------- noise ----------

/** 256² tiling noise: r = fine fibre, g = soft blotches, b/a = a fibre normal (x, z). */
export function noise() {
  const N = 256;
  const r = rng(91);
  const lattice = (n) => { const a = new Float32Array(n * n); for (let i = 0; i < a.length; i++) a[i] = r(); return a; };
  const L = [lattice(8), lattice(16), lattice(32), lattice(64), lattice(128)];
  const sample = (a, n, x, y) => {
    const fx = (x / N) * n, fy = (y / N) * n;
    const x0 = Math.floor(fx), y0 = Math.floor(fy);
    const tx = fx - x0, ty = fy - y0;
    const u = tx * tx * (3 - 2 * tx), v = ty * ty * (3 - 2 * ty);
    const at = (i, j) => a[((j % n + n) % n) * n + ((i % n + n) % n)];
    return (at(x0, y0) * (1 - u) + at(x0 + 1, y0) * u) * (1 - v) + (at(x0, y0 + 1) * (1 - u) + at(x0 + 1, y0 + 1) * u) * v;
  };
  const fine = new Float32Array(N * N);
  const c = canvas(N, N);
  const g = c.getContext("2d");
  const d = g.createImageData(N, N);
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      fine[y * N + x] = 0.5 * sample(L[4], 128, x, y) + 0.3 * sample(L[3], 64, x * 1.0, y) + 0.2 * r();
    }
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      const i = y * N + x;
      const f = fine[i];
      const dx = fine[y * N + ((x + 1) % N)] - fine[y * N + ((x + N - 1) % N)];
      const dz = fine[((y + 1) % N) * N + x] - fine[((y + N - 1) % N) * N + x];
      const soft = 0.55 * sample(L[0], 8, x, y) + 0.3 * sample(L[1], 16, x, y) + 0.15 * sample(L[2], 32, x, y);
      d.data[i * 4] = f * 255;
      d.data[i * 4 + 1] = soft * 255;
      d.data[i * 4 + 2] = Math.max(48, Math.min(208, 128 + dx * 300));
      d.data[i * 4 + 3] = Math.max(48, Math.min(208, 128 + dz * 300));
    }
  g.putImageData(d, 0, 0);
  return c;
}
export { HAND };
