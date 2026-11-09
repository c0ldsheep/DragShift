// The Form, in 3D. The whole film as pure data and maths (no DOM, no WebGL), so the tests can check it.
//
// 11:41 PM. An application form lies on a table under a desk lamp; it closes at 11:59. The photo is 3.1 MB and the
// limit is 50 KB. It's thrown back. You crop it (a crop frame closes in, the trimmed strips fall away): 1.2 MB, and
// it's thrown back again, off the table. Then: drag, Shift, Under 50 KB, and the size counts down to 48.6 KB and
// it lands. The bank statement has a password and is thrown back too:
// Shift, Unlock, type it once, the padlock pops off. Three scans become one PDF. A round green seal comes out of
// the screen at you and lands: SUBMITTED, 11:53 PM.
//
// Units: millimetres. The form is an A4 sheet centred on the world origin, lying on the table (y up, z towards the
// viewer). Scroll picks a stop; the page plays the film to it at real speed.

export const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
export const smooth = (x) => { const t = clamp(x); return t * t * (3 - 2 * t); };
export const lerp = (a, b, t) => a + (b - a) * t;
/** 0 → 1 over [a, b], eased. */
export const rise = (T, a, b) => smooth((T - a) / (b - a));
/** Fast start, long soft finish (a number settling). */
export const outQuint = (x) => 1 - Math.pow(1 - clamp(x), 5);
export const outCubic = (x) => 1 - Math.pow(1 - clamp(x), 3);
export const inCubic = (x) => Math.pow(clamp(x), 3);
/** A damped spring from 0 to 1 that overshoots a little and settles (t in seconds since release). */
export function spring(t, freq = 2.6, damp = 0.42) {
  if (t <= 0) return 0;
  const w = 2 * Math.PI * freq;
  const wd = w * Math.sqrt(1 - damp * damp);
  return 1 - Math.exp(-damp * w * t) * (Math.cos(wd * t) + ((damp * w) / wd) * Math.sin(wd * t));
}
/** A knock that rings out: 0 before t = 0, then a fading wobble. */
export const knock = (t, freq = 11, decay = 9) => (t <= 0 ? 0 : Math.sin(t * freq * 2 * Math.PI) * Math.exp(-t * decay));
/** Eased keyframes: [[t, value], ...]; value is a number or an array of numbers. */
export function keys(T, ks, ease = smooth) {
  if (T <= ks[0][0]) return ks[0][1];
  for (let i = 1; i < ks.length; i++) {
    if (T <= ks[i][0]) {
      const [t0, a] = ks[i - 1];
      const [t1, b] = ks[i];
      const u = t1 > t0 ? ease((T - t0) / (t1 - t0)) : 1;
      return Array.isArray(a) ? a.map((v, k) => lerp(v, b[k], u)) : lerp(a, b, u);
    }
  }
  return ks[ks.length - 1][1];
}

// ----------------------------------------------------------------------------------------------- the form ---

export const A4 = { w: 210, h: 297 };
/** Boxes on the form, in mm from its top-left corner. */
export const BOX = {
  photo: { x: 18, y: 66, w: 35, h: 45 },
  stmt: { x: 18, y: 130, w: 33, h: 44 },
  docs: { x: 18, y: 193, w: 36, h: 48 },
};
/** Where the red errors and green ticks print, in form mm (left, baseline). */
export const NOTE = { photo: [18, 117], stmt: [18, 180.5], docs: [62, 237] };
export const STAMP_AT = [150, 256];
/** Form mm → world [x, z]. */
export const toWorld = (fx, fy) => [fx - A4.w / 2, fy - A4.h / 2];
export const centre = (b) => toWorld(b.x + b.w / 2, b.y + b.h / 2);

// ------------------------------------------------------------------------------------------- the ring -------

// The ring's pages, in the app's own words (Catalog.swift). Faves holds starred actions (the sizes can be starred);
// "pdfEdit" is the Edit page the app shows for one PDF.
export const PAGES = {
  faves: [["JPG", "photo"], ["PDF", "pdf"], ["Under 50 KB", "scale"], ["Under 20 KB", "scale"], ["Under 200 KB", "scale"], ["Passport", "person"], ["Print", "printer"]],
  pdfEdit: [["Compress", "shrink"], ["Under KB", "scale"], ["Searchable", "text"], ["Split", "split"], ["Rotate", "rotate"], ["Lock", "lock"], ["Unlock", "unlock"]],
};
export const TABS = ["Faves", "Convert", "Edit", "More", "Print", "Files"];
export const SLICE = Object.fromEntries(Object.entries(PAGES).map(([p, s]) => [p, Object.fromEntries(s.map(([t], i) => [t, i]))]));
/** Offset of slice i of n from the ring's centre (desktop points, before the ring's scale). */
export function sliceOffset(i, n, r = 106) {
  const a = (i / n) * Math.PI * 2;
  return [Math.sin(a) * r, -Math.cos(a) * r];
}

// ------------------------------------------------------------------------------------------- the timing -----

/**
 * One drag with DragShift. The file is brought in over [in0, in1], Shift goes down at `shift`, the ring is open
 * over [open, let], the slice is lit from `hot`, the file shrinks over [work0, work1], is carried to its box over
 * [place0, place1], and the job is over at `end`.
 */
export function job(t0, { work = 1.35, pre = 0 } = {}) {
  const s = t0 + 1.1 + pre;
  const w1 = s + 1.65 + work;
  return {
    t0, in0: t0, in1: t0 + 1.0, shift: s, open: s + 0.22, aim: s + 0.5, hot: s + 0.95, let: s + 1.5,
    work0: s + 1.65, work1: w1, place0: w1, place1: w1 + 0.8, done: w1 + 0.85, end: w1 + 1.6,
  };
}

/** A try that fails: brought over the box, slammed down, thrown back, lands on the table. */
const attempt = (t0) => ({ in0: t0, over: t0 + 0.95, hit: t0 + 1.12, back: t0 + 1.25, fall: t0 + 2.15 });

/** The first try bounces back into your hand (held by `held`). */
export const A1 = { ...attempt(0.2), held: 0.2 + 1.8 };
/** You crop it: the frame fades in, its handles close in, the cut (strips fall, 3.1 → 1.2 MB), the frame goes. */
export const CROP = { in: A1.held + 0.12, squeeze0: A1.held + 0.4, squeeze1: A1.held + 1.15, cut: A1.held + 1.2, out: A1.held + 1.55 };
/** The cropped photo's second try, from your hand: thrown off the table this time. */
export const A2 = { ...attempt(CROP.out + 0.05), from: "hand" };
/** Where the crop lands in the print (texture 0..1): the face, at the passport box's 35 : 45. */
export const CROP_UV = [0.14, 0.06, 0.86, 0.78];
export const J1 = job(A2.fall + 0.45, { work: 1.9 });
// the statement: slammed on its box and refused first (pre), and the work is the password window and the padlock
export const J2 = job(J1.end + 0.15, { pre: 1.2, work: 2.4 });
/** J2's refusal and unlock, as times. */
export const LOCK = {
  hit: J2.in1 + 0.17, back: J2.in1 + 0.3, held: J2.in1 + 0.85,
  pwIn: J2.work0, type0: J2.work0 + 0.35, type1: J2.work0 + 1.15, enter: J2.work0 + 1.4, pwOut: J2.work0 + 1.55,
  pop: J2.work0 + 1.7, clear: J2.work0 + 2.05, gone: J2.work0 + 2.35,
};
export const PASSWORD_LEN = 8;
/** The app's "Unlock PDF" window: visible, how many dots typed, the Unlock button pressed. */
export function passwordAt(T) {
  const a = rise(T, LOCK.pwIn, LOCK.pwIn + 0.2) * (1 - rise(T, LOCK.pwOut, LOCK.pwOut + 0.15));
  const dots = Math.max(0, Math.min(PASSWORD_LEN, Math.floor(((T - LOCK.type0) / (LOCK.type1 - LOCK.type0)) * PASSWORD_LEN) + 1));
  return { a, dots: T < LOCK.type0 ? 0 : dots, press: T >= LOCK.enter && T < LOCK.pwOut + 0.1 };
}
export const J3 = job(J2.end + 0.15, { work: 1.6 });
/** The stamp: comes down, hits, lifts away. */
export const E = { t0: J3.end + 0.15, hit: J3.end + 0.85, lift: J3.end + 1.1, gone: J3.end + 1.75 };
/** What a phone feels (haptics.js), in film order: a short thud on each refusal and a light tick each time a fixed
 * file lands in its box. The seal has none (he tried a long buzz there and it didn't feel good). */
export const FEELS = [
  { t: A1.hit, kind: "thud", why: "Rejected" },
  { t: A2.hit, kind: "thud", why: "Rejected again" },
  { t: J1.place1, kind: "tick", why: "Under 50 KB, uploaded" },
  { t: LOCK.hit, kind: "thud", why: "Locked PDF refused" },
  { t: J2.place1, kind: "tick", why: "Unlocked PDF, uploaded" },
  { t: J3.place1, kind: "tick", why: "1 PDF, uploaded" },
];
export const JOBS = [J1, J2, J3];
export const STOPS = [0, A2.fall + 0.3, J1.end, J2.end, J3.end, E.gone + 0.9];
export const DURATION = STOPS[STOPS.length - 1];
// ------------------------------------------------------------------------------------------- the scroll -----

// The film moves only while you scroll (no snapping, no auto-play). Scroll is measured in screen heights. Some
// stretches take more scroll for the same film, so they feel heavier and nobody rushes past them; at a few
// results the film almost stops for a moment while you keep scrolling.
export const SCROLL_BASE = 0.2; // screen heights per second of film
const WEIGHT = [
  [A1.in0, A2.fall, 1.35], // the two rejections
  [CROP.in, CROP.out, 1.7], // the crop
  [J1.work0, J1.work1, 1.7], // the countdown to 48.6 KB
  [J2.in1, LOCK.held, 1.4], // thrown back for its password
  [LOCK.pwIn, LOCK.gone, 1.5], // the password, the padlock springing open
  [J3.in0, J3.end, 0.85], // the scans: you know the trick by now
  [E.t0, E.hit + 0.15, 2.4], // the seal coming down at the form
  [E.hit + 0.15, E.gone, 1.3],
];
/** Where the film nearly stops so a result sinks in: [film time, extra scroll in screen heights]. */
export const DWELL = [
  [A2.fall + 0.25, 0.35], [J1.place1 + 0.45, 0.35], [J2.place1 + 0.45, 0.35], [J3.place1 + 0.5, 0.3], [E.hit + 0.04, 0.5],
];
/** Scroll needed per second of film at T (screen heights per second). */
export function scrollRate(T) {
  let w = 1;
  for (const [a, b, k] of WEIGHT) if (T >= a && T < b) w = k;
  let r = SCROLL_BASE * w;
  for (const [t, amt] of DWELL) r += (amt / (0.09 * Math.sqrt(2 * Math.PI))) * Math.exp(-((T - t) ** 2) / (2 * 0.09 ** 2));
  return r;
}
const STEP = 0.005;
const CUM = (() => {
  const n = Math.ceil(DURATION / STEP);
  const c = new Float64Array(n + 1);
  for (let i = 1; i <= n; i++) c[i] = c[i - 1] + scrollRate((i - 0.5) * STEP) * STEP;
  return c;
})();
/** The whole film's scroll length, in screen heights. */
export const SCROLL_TOTAL = CUM[CUM.length - 1];
/** Scroll position (screen heights) that shows film time T. */
export function scrollAt(T) {
  const x = clamp(T, 0, DURATION) / STEP;
  const i = Math.min(CUM.length - 2, Math.floor(x));
  return lerp(CUM[i], CUM[i + 1], x - i);
}
/** Film time shown at scroll position u (screen heights). */
export function timeAt(u) {
  if (u <= 0) return 0;
  if (u >= SCROLL_TOTAL) return DURATION;
  let lo = 0, hi = CUM.length - 1;
  while (hi - lo > 1) { const m = (lo + hi) >> 1; if (CUM[m] <= u) lo = m; else hi = m; }
  return Math.min(DURATION, (lo + (u - CUM[lo]) / (CUM[hi] - CUM[lo])) * STEP);
}


export const WORDS = [
  ["The form closes at midnight.", "Your photo is 3.1 MB. The limit is 50 KB. The cyber café shut at 10."],
  ["Rejected.", "So you crop it. Smaller has to be lighter, right?"],
  ["Rejected. Again.", "You cropped it. Still 1.2 MB. Still too big."],
  ["Drag it. Press Shift.", "Pick Under 50 KB. It lands just under the limit."],
  ["Locked PDF? Unlock it.", "Banks add a password. Forms reject it. Shift, Unlock, type it once."],
  ["Three scans. One PDF.", "Drag them together and pick PDF."],
  ["No cyber café. No queue.", "Everything done in 2 clicks. No websites, no uploads. Free forever for Mac and Windows."],
];

/** When each line of words comes in: each one only after what it talks about has happened. */
export const WORDS_AT = [0, A1.hit + 0.15, A2.hit + 0.1, STOPS[1], STOPS[2], STOPS[3], E.hit];

/** What the words layer shows at T: which line, how visible (the old one fades out first), and the end buttons. */
export function frame(T) {
  let k = 0;
  while (k < STOPS.length - 1 && T > STOPS[k]) k++;
  let i = 0;
  while (i < WORDS_AT.length - 1 && T >= WORDS_AT[i + 1]) i++;
  let shown = i;
  let a = 1;
  if (i > 0) {
    const t0 = WORDS_AT[i];
    if (T < t0 + 0.25) { shown = i - 1; a = 1 - rise(T, t0, t0 + 0.25); }
    else a = rise(T, t0 + 0.25, t0 + 0.6);
  }
  return { stop: k, shown, words: WORDS[shown], wordsA: a, endA: rise(T, E.hit + 0.35, E.hit + 1.2) };
}

// ------------------------------------------------------------------------------------------- the clock ------

const CLOCK = [[0, "11:41"], [A1.hit + 0.35, "11:44"], [A2.hit + 0.35, "11:47"], [J1.place1, "11:49"], [J2.place1, "11:51"], [J3.place1, "11:52"], [E.hit, "11:53"]];
/** The time caption: "11:47 PM", the note beside it, and when it last ticked (for the flip). */
export function clockAt(T) {
  let i = 0;
  while (i < CLOCK.length - 1 && T >= CLOCK[i + 1][0]) i++;
  const done = T >= E.hit;
  return {
    time: `${CLOCK[i][1]} PM`,
    note: done ? "Submitted. 6 minutes to spare." : "Last date: today, 11:59 PM",
    since: i === 0 ? 99 : T - CLOCK[i][0],
    done,
  };
}

// ------------------------------------------------------------------------------------------- the meter ------

/** Sizes in KB. 3.1 MB → 48.6 KB, slowing down as it nears the limit (log scale, so every digit moves). */
export function shrinkKB(T, J, from, to) {
  const u = outQuint((T - J.work0) / (J.work1 - J.work0));
  return Math.exp(lerp(Math.log(from), Math.log(to), u));
}
export function fmtKB(kb) {
  if (kb >= 1000) return `${(kb / 1024).toFixed(1)} MB`;
  if (kb >= 100) return `${Math.round(kb)} KB`;
  return `${kb.toFixed(1)} KB`;
}

/**
 * The big size readout next to the thing being uploaded: which thing it follows, its text, red or green, the
 * limit line, and how visible. Hidden while the ring is open.
 */
export function meterAt(T) {
  const show = (a, b, f = 0.2) => rise(T, a, a + f) * (1 - rise(T, b, b + f));
  const ringGap = (J) => 1 - rise(T, J.shift - 0.15, J.shift + 0.05) * (1 - rise(T, J.let + 0.22, J.let + 0.42));
  // the photo: 3.1 MB, hidden while the crop frame is on it, 1.2 MB after the cut, then DragShift takes it to 48.6 KB
  if (T < J1.in0) {
    const kb = Math.exp(lerp(Math.log(3174), Math.log(1229), outCubic((T - CROP.cut) / 0.4)));
    const off = rise(T, CROP.in - 0.15, CROP.in + 0.05) * (1 - rise(T, CROP.cut + 0.02, CROP.cut + 0.2));
    return { of: "p1", icon: "file", text: fmtKB(kb), good: false, limit: "Limit 50 KB", a: show(A1.in0 + 0.35, A2.back + 0.1) * (1 - off) };
  }
  if (T < J2.t0) {
    const kb = shrinkKB(T, J1, 1229, 48.6);
    return { of: "p1", icon: "file", text: fmtKB(kb), good: kb <= 50, limit: "Limit 50 KB", a: show(J1.in0 + 0.4, J1.place1 + 0.1) * ringGap(J1) };
  }
  if (T < J3.t0) {
    // locked until the padlock pops; hidden while the ring and the password window are up
    const open = T >= LOCK.pop;
    const gap = 1 - rise(T, J2.shift - 0.15, J2.shift + 0.05) * (1 - rise(T, LOCK.pop, LOCK.pop + 0.2));
    return { of: "stmt", icon: open ? "unlock" : "lock", text: open ? "Unlocked" : "Locked", good: open, limit: open ? "No password now" : "Password protected", a: show(J2.in0 + 0.4, J2.place1 + 0.1) * gap };
  }
  const one = T > (J3.work0 + J3.work1) / 2;
  return { of: "docs", icon: "pages", text: one ? "1 PDF" : "3 scans", good: one, limit: one ? "3 pages" : "Needs one PDF", a: show(J3.in0 + 0.4, J3.place1 + 0.1) * ringGap(J3) };
}

/** The ring's state at T for job J: open amount, lit slice (or -1), hub text. */
export function ringAt(T, J, { slice, title, sub, page = "faves", tab = 0 }) {
  const open = rise(T, J.shift, J.open) * (1 - rise(T, J.let, J.let + 0.2));
  const hot = T >= J.hot ? SLICE[page][slice] : -1;
  return { open, page, hot, tab, title: hot >= 0 ? slice : title, sub: hot >= 0 ? "Let go to do it" : sub };
}
/** Pointer travel inside the ring: to the slice and back to the centre a little after letting go. */
export function aimAt(T, J, slice, k = 1, page = "faves") {
  const [dx, dy] = sliceOffset(SLICE[page][slice], PAGES[page].length);
  const u = rise(T, J.aim, J.hot) * (1 - rise(T, J.let + 0.15, J.let + 0.6));
  return [dx * k * u, dy * k * u];
}
export const shiftDown = (T, J) => T >= J.shift && T < J.let;
export const RINGS = [
  { slice: "Under 50 KB", title: "IMG_2041.jpg", sub: "1.2 MB" },
  { slice: "Unlock", title: "Statement.pdf", sub: "Locked", page: "pdfEdit", tab: 2 },
  { slice: "PDF", title: "3 files", sub: "Marksheet.jpg and 2 more" },
];

// ------------------------------------------------------------------------------------------- the camera -----

// Each key frames a region of the table: centre [x, z], width and depth in mm that must fit under the words, the
// camera's elevation and azimuth in degrees, and how much the lens blurs what's out of focus (0..1).
export const CAM = {
  desk: [
    [0, { c: [-40, -62], w: 140, h: 120, el: 30, az: 20, dof: 1 }],
    [1.0, { c: [-20, -58], w: 185, h: 110, el: 40, az: 12, dof: 0.85 }],
    [A1.held, { c: [-8, -60], w: 175, h: 105, el: 43, az: 11, dof: 0.82 }],
    [CROP.squeeze0, { c: [16, -64], w: 132, h: 96, el: 47, az: 9, dof: 0.85 }],
    [CROP.out, { c: [14, -63], w: 136, h: 96, el: 47, az: 9, dof: 0.85 }],
    [A2.over, { c: [-14, -54], w: 190, h: 105, el: 44, az: 10, dof: 0.8 }],
    [STOPS[1], { c: [-14, -54], w: 190, h: 105, el: 44, az: 10, dof: 0.8 }],
    [J1.in1, { c: [-20, -56], w: 180, h: 100, el: 48, az: 8, dof: 0.75 }],
    [J1.place1, { c: [-38, -54], w: 150, h: 92, el: 52, az: 6, dof: 0.75 }],
    [J1.end, { c: [-40, -50], w: 140, h: 92, el: 53, az: 6, dof: 0.75 }],
    [J2.in1, { c: [-16, 4], w: 178, h: 100, el: 54, az: 6, dof: 0.7 }],
    [J2.end, { c: [-22, 6], w: 168, h: 100, el: 55, az: 5, dof: 0.7 }],
    [J3.in1, { c: [-16, 62], w: 180, h: 112, el: 56, az: 5, dof: 0.7 }],
    [J3.end, { c: [-20, 64], w: 172, h: 112, el: 57, az: 5, dof: 0.7 }],
    [E.t0 + 0.5, { c: [24, 96], w: 200, h: 130, el: 60, az: 4, dof: 0.6 }],
    [E.lift, { c: [20, 92], w: 205, h: 140, el: 62, az: 3, dof: 0.55 }],
    [E.gone + 0.9, { c: [14, 14], w: 240, h: 282, el: 62, az: 8, dof: 0.3 }],
  ],
  phone: [
    [0, { c: [-52, -58], w: 96, h: 104, el: 34, az: 14, dof: 1 }],
    [1.0, { c: [-44, -26], w: 112, h: 170, el: 44, az: 8, dof: 0.85 }],
    [A1.held, { c: [-46, -14], w: 108, h: 160, el: 46, az: 7, dof: 0.82 }],
    [CROP.squeeze0, { c: [-50, 14], w: 92, h: 120, el: 50, az: 6, dof: 0.85 }],
    [CROP.out, { c: [-50, 12], w: 94, h: 122, el: 50, az: 6, dof: 0.85 }],
    [A2.over, { c: [-44, -22], w: 112, h: 172, el: 48, az: 6, dof: 0.8 }],
    [STOPS[1], { c: [-44, -22], w: 112, h: 172, el: 48, az: 6, dof: 0.8 }],
    [J1.in1, { c: [-44, -22], w: 112, h: 175, el: 50, az: 6, dof: 0.75 }],
    [J1.place1, { c: [-54, -40], w: 100, h: 120, el: 54, az: 5, dof: 0.75 }],
    [J1.end, { c: [-54, -36], w: 100, h: 120, el: 54, az: 5, dof: 0.75 }],
    [J2.in1, { c: [-44, 34], w: 116, h: 140, el: 55, az: 5, dof: 0.7 }],
    [J2.end, { c: [-46, 26], w: 112, h: 126, el: 56, az: 5, dof: 0.7 }],
    [J3.in1, { c: [-42, 70], w: 120, h: 150, el: 56, az: 5, dof: 0.7 }],
    [J3.end, { c: [-44, 72], w: 116, h: 140, el: 57, az: 5, dof: 0.7 }],
    [E.t0 + 0.5, { c: [30, 112], w: 120, h: 130, el: 60, az: 3, dof: 0.6 }],
    [E.lift, { c: [24, 104], w: 140, h: 150, el: 62, az: 2, dof: 0.55 }],
    [E.gone + 0.9, { c: [4, 4], w: 222, h: 300, el: 70, az: 4, dof: 0.3 }],
  ],
};
/** The camera's framing at T (eased between keys). */
export function camAt(T, phone) {
  const ks = CAM[phone ? "phone" : "desk"];
  const flat = ks.map(([t, k]) => [t, [k.c[0], k.c[1], k.w, k.h, k.el, k.az, k.dof]]);
  const [cx, cz, w, h, el, az, dof] = keys(T, flat);
  return { c: [cx, cz], w, h, el, az, dof };
}
