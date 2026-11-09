import { test } from "node:test";
import assert from "node:assert/strict";
import {
  STOPS, DURATION, WORDS, frame, A1, A2, J1, J2, J3, E, JOBS, clockAt, meterAt, shrinkKB, fmtKB, ringAt, aimAt,
  shiftDown, RINGS, SLICE, camAt, CAM, spring, knock, BOX, NOTE, A4, keys, LOCK, passwordAt, PASSWORD_LEN,
  scrollAt, timeAt, scrollRate, SCROLL_TOTAL, SCROLL_BASE, DWELL, CROP, CROP_UV, WORDS_AT,
} from "../assets/js/film.js";

const steps = (a, b, n = 400) => Array.from({ length: n + 1 }, (_, i) => a + ((b - a) * i) / n);

test("stops rise and the film ends on the last one", () => {
  for (let i = 1; i < STOPS.length; i++) assert.ok(STOPS[i] > STOPS[i - 1] + 2, `stop ${i}`);
  assert.equal(DURATION, STOPS[STOPS.length - 1]);
  assert.equal(WORDS.length, STOPS.length + 1, "one extra line: the crop");
});

test("the first try bounces back, you crop it, the second try is thrown off: in order, before the first stop", () => {
  const order = [A1.in0, A1.over, A1.hit, A1.back, A1.held, CROP.in, CROP.squeeze0, CROP.squeeze1, CROP.cut, CROP.out,
    A2.in0, A2.over, A2.hit, A2.back, A2.fall, STOPS[1]];
  for (let i = 1; i < order.length; i++) assert.ok(order[i] > order[i - 1], `step ${i}`);
  const [u0, v0, u1, v1] = CROP_UV;
  assert.ok(Math.abs(((u1 - u0) * 66) / ((v1 - v0) * 85) - 35 / 45) < 0.01, "the crop has the passport box's shape");
});

test("each job's events are in order and inside its stop", () => {
  const order = ["in0", "in1", "shift", "open", "aim", "hot", "let", "work0", "work1", "place1", "done", "end"];
  JOBS.forEach((J, i) => {
    for (let k = 1; k < order.length; k++) assert.ok(J[order[k]] >= J[order[k - 1]], `job ${i} ${order[k]}`);
    assert.ok(J.in0 > STOPS[i + 1], `job ${i} starts after its stop`);
    assert.ok(J.end <= STOPS[i + 2] + 1e-9, `job ${i} ends by the next stop`);
  });
  assert.ok(E.t0 > J3.end && E.hit < E.lift && E.lift < E.gone && E.gone < DURATION);
});

test("words stay short enough for a phone", () => {
  WORDS.forEach(([h, p], i) => {
    assert.ok(h.length <= 30, h);
    assert.ok(p.length <= (i === WORDS.length - 1 ? 90 : 80), p); // the last line has the room the camera makes
  });
});

test("the words follow the film: each line only after what it talks about", () => {
  assert.equal(WORDS_AT.length, WORDS.length);
  assert.equal(frame(0).shown, 0);
  assert.equal(frame(A1.hit - 0.05).shown, 0, "no 'rejected' before the first slam");
  assert.equal(frame(CROP.squeeze0).shown, 1, "'so you crop it' while it's being cropped");
  for (let T = 0; T < A2.hit; T += 0.01) assert.ok(frame(T).shown < 2, `'you cropped it' too early at ${T}`);
  assert.equal(frame(STOPS[1] - 0.3).shown, 2);
  assert.equal(frame(J1.shift).shown, 3);
  assert.equal(frame(J2.shift).shown, 4);
  assert.equal(frame(J3.shift).shown, 5);
  assert.equal(frame(E.hit - 0.05).shown, 5, "the last words wait for the stamp");
  assert.equal(frame(DURATION).shown, 6);
  assert.ok(frame(DURATION).endA > 0.99);
  assert.ok(frame(E.hit).endA < 0.01);
  for (const T of steps(0, DURATION)) {
    const f = frame(T);
    assert.ok(f.wordsA >= 0 && f.wordsA <= 1, `alpha at ${T}`);
  }
});

test("the clock only goes forward and stops at 11:53", () => {
  let last = "";
  for (const T of steps(0, DURATION)) {
    const c = clockAt(T);
    assert.ok(c.time >= last, `${c.time} after ${last}`);
    last = c.time;
  }
  assert.equal(clockAt(0).time, "11:41 PM");
  assert.equal(clockAt(DURATION).time, "11:53 PM");
  assert.match(clockAt(DURATION).note, /6 minutes/);
  assert.match(clockAt(0).note, /11:59/);
});

test("the size counts down, stops just under the limit and turns green only then", () => {
  assert.equal(fmtKB(3174), "3.1 MB");
  assert.equal(fmtKB(312.4), "312 KB");
  assert.equal(fmtKB(48.6), "48.6 KB");
  let prev = Infinity;
  for (const T of steps(J1.work0 - 0.2, J1.work1 + 0.2)) {
    const kb = shrinkKB(T, J1, 1229, 48.6);
    assert.ok(kb <= prev + 1e-9);
    prev = kb;
  }
  assert.ok(Math.abs(shrinkKB(J1.work1, J1, 1229, 48.6) - 48.6) < 1e-6);
  assert.ok(shrinkKB(J1.work1, J1, 1229, 48.6) < 50);
  const m = meterAt(J1.work1 + 0.1);
  assert.equal(m.text, "48.6 KB");
  assert.equal(m.good, true);
  assert.equal(meterAt(J1.work0 + 0.1).good, false);
  assert.equal(meterAt(J2.in1).text, "Locked");
  assert.equal(meterAt(J2.work1 + 0.1).text, "Unlocked");
  assert.equal(meterAt(J2.work1 + 0.1).good, true);
  assert.equal(meterAt(A2.hit).text, "1.2 MB");
  assert.equal(meterAt(A1.held).text, "3.1 MB");
  assert.ok(meterAt((CROP.in + CROP.cut) / 2).a < 0.01, "the label steps aside for the crop frame");
});

test("the meter hides while the ring is open", () => {
  for (const J of JOBS) {
    const mid = (J.open + J.let) / 2;
    assert.ok(meterAt(mid).a < 0.01, `meter at ${mid}`);
    // (the statement's meter comes back once the padlock is off)
    assert.ok(meterAt(J === J2 ? J.work1 - 0.1 : (J.work0 + J.work1) / 2).a > 0.99);
  }
});

test("the ring opens, lights its slice, and shuts; Shift is down only while it's open", () => {
  JOBS.forEach((J, i) => {
    const o = RINGS[i];
    const page = o.page || "faves";
    assert.ok(o.slice in SLICE[page]);
    assert.equal(ringAt(J.shift - 0.01, J, o).open, 0);
    assert.ok(ringAt(J.hot + 0.1, J, o).open > 0.99);
    assert.equal(ringAt(J.hot + 0.1, J, o).hot, SLICE[page][o.slice]);
    assert.equal(ringAt(J.hot + 0.1, J, o).page, page);
    assert.equal(ringAt(J.let + 0.25, J, o).open, 0);
    assert.ok(shiftDown(J.shift + 0.1, J) && !shiftDown(J.let + 0.01, J));
    const [ax, ay] = aimAt(J.hot, J, o.slice, 1, page);
    assert.ok(Math.hypot(ax, ay) > 90);
  });
});

test("the camera has no gaps or jumps", () => {
  for (const phone of [false, true]) {
    const ks = CAM[phone ? "phone" : "desk"];
    for (let i = 1; i < ks.length; i++) assert.ok(ks[i][0] > ks[i - 1][0], `key ${i}`);
    let prev = camAt(0, phone);
    for (const T of steps(0, DURATION, 2000)) {
      const c = camAt(T, phone);
      for (const v of [c.c[0], c.c[1], c.w, c.h, c.el, c.az, c.dof]) assert.ok(Number.isFinite(v));
      assert.ok(Math.abs(c.el - prev.el) < 1, `elevation jump at ${T}`);
      assert.ok(Math.abs(c.c[1] - prev.c[1]) < 6, `pan jump at ${T}`);
      prev = c;
    }
    assert.ok(camAt(DURATION, phone).el >= 60, "the end looks down at the whole form");
  }
});

test("springs settle and knocks ring out", () => {
  assert.equal(spring(0), 0);
  assert.ok(Math.abs(spring(3) - 1) < 1e-3);
  assert.ok(Math.max(...steps(0, 1).map((t) => spring(t))) > 1.0, "it overshoots a little");
  assert.ok(Math.max(...steps(0, 1).map((t) => spring(t))) < 1.3);
  assert.equal(knock(-1), 0);
  assert.ok(Math.abs(knock(1.5)) < 1e-4);
});

test("boxes and notes sit inside the sheet and do not collide", () => {
  const rects = Object.values(BOX);
  for (const b of rects) assert.ok(b.x >= 10 && b.y >= 10 && b.x + b.w <= A4.w - 10 && b.y + b.h <= A4.h - 10);
  for (let i = 0; i < rects.length; i++)
    for (let j = i + 1; j < rects.length; j++) {
      const a = rects[i], b = rects[j];
      assert.ok(a.y + a.h < b.y || b.y + b.h < a.y, "boxes overlap");
    }
  assert.ok(NOTE.photo[1] > BOX.photo.y + BOX.photo.h && NOTE.photo[1] < BOX.stmt.y - 6);
  assert.ok(NOTE.stmt[1] > BOX.stmt.y + BOX.stmt.h && NOTE.stmt[1] < BOX.docs.y - 6);
});

test("keys eases between frames and holds at the ends", () => {
  const ks = [[0, [0, 0]], [1, [10, 20]]];
  assert.deepEqual(keys(-1, ks), [0, 0]);
  assert.deepEqual(keys(2, ks), [10, 20]);
  assert.deepEqual(keys(0.5, ks), [5, 10]);
});

test("the statement is refused, then unlocked through the password window, in order", () => {
  const order = [J2.in1, LOCK.hit, LOCK.back, LOCK.held, J2.shift, J2.let, LOCK.pwIn, LOCK.type0, LOCK.type1, LOCK.enter, LOCK.pwOut, LOCK.pop, LOCK.clear, LOCK.gone, J2.place0];
  for (let i = 1; i < order.length; i++) assert.ok(order[i] > order[i - 1], `step ${i}`);
  assert.equal(passwordAt(LOCK.pwIn - 0.1).a, 0);
  assert.equal(passwordAt(LOCK.type0 - 0.01).dots, 0);
  assert.equal(passwordAt(LOCK.enter).dots, PASSWORD_LEN);
  assert.ok(passwordAt(LOCK.enter).press);
  assert.ok(passwordAt((LOCK.type0 + LOCK.type1) / 2).a > 0.99);
  assert.equal(passwordAt(LOCK.pop).a, 0, "the window is gone before the padlock pops");
  let prev = 0;
  for (const T of steps(LOCK.type0, LOCK.pwOut)) { const d = passwordAt(T).dots; assert.ok(d >= prev); prev = d; }
});

test("the meter hides while the password window is up", () => {
  for (const T of steps(LOCK.pwIn, LOCK.pwOut, 40)) assert.ok(meterAt(T).a < 0.01, `meter at ${T}`);
});

test("scroll maps to the film and back, and only forward", () => {
  assert.equal(timeAt(0), 0);
  assert.equal(timeAt(SCROLL_TOTAL + 1), DURATION);
  assert.ok(SCROLL_TOTAL > 6 && SCROLL_TOTAL < 11, `total ${SCROLL_TOTAL}`);
  let prev = -1;
  for (const u of steps(0, SCROLL_TOTAL, 2000)) { const T = timeAt(u); assert.ok(T >= prev); prev = T; }
  for (const T of steps(0, DURATION, 300)) assert.ok(Math.abs(timeAt(scrollAt(T)) - T) < 0.01, `round trip at ${T}`);
});

test("the important parts need more scroll, and results hold for a moment", () => {
  const per = (a, b) => (scrollAt(b) - scrollAt(a)) / (b - a);
  assert.ok(per(J1.work0, J1.work1) > per(J1.in0, J1.shift) * 1.4, "the countdown is heavier than the drag in");
  assert.ok(per(E.t0, E.hit) > per(J3.in0, J3.shift) * 2, "the seal is the heaviest moment");
  for (const [t, amt] of DWELL) {
    const hold = scrollAt(t + 0.15) - scrollAt(t - 0.15);
    assert.ok(hold > amt * 0.85, `a hold at ${t}`);
  }
  assert.ok(scrollRate(J1.work0 + 0.5) > SCROLL_BASE);
});
