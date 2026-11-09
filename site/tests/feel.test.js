// What a phone feels: a thud on each refusal, a tick each time a fixed file lands. Nothing on the seal (his call).
import { test } from "node:test";
import assert from "node:assert/strict";
import * as F from "../assets/js/film.js";
import { PATTERNS } from "../assets/js/haptics.js";

const total = (p) => p.reduce((a, b) => a + b, 0);

test("the felt moments come in film order, on the right events", () => {
  const t = F.FEELS.map((f) => f.t);
  assert.deepEqual(t, [...t].sort((a, b) => a - b));
  assert.deepEqual(F.FEELS.map((f) => f.kind), ["thud", "thud", "tick", "thud", "tick", "tick"]);
  assert.deepEqual(t, [F.A1.hit, F.A2.hit, F.J1.place1, F.LOCK.hit, F.J2.place1, F.J3.place1]);
  assert.ok(F.FEELS.every((f) => Math.abs(f.t - F.E.hit) > 1), "nothing on the seal");
  for (let i = 1; i < t.length; i++) assert.ok(t[i] - t[i - 1] > 1, "never two buzzes on top of each other");
  // each tick comes when its file has really landed fixed
  for (const J of [F.J1, F.J2, F.J3]) assert.ok(F.meterAt(J.place1).good, "fixed when it ticks");
});

test("a thud is short and firm, a tick is tiny", () => {
  assert.deepEqual(Object.keys(PATTERNS).sort(), ["thud", "tick"]);
  assert.ok(total(PATTERNS.thud) <= 120, `thud ${total(PATTERNS.thud)} ms`);
  assert.ok(total(PATTERNS.tick) <= 25, `tick ${total(PATTERNS.tick)} ms`);
  assert.ok(PATTERNS.tick[0] < PATTERNS.thud[0], "a tick is lighter than a thud");
});
