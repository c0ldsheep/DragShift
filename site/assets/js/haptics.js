// What the film makes a phone feel (film.js FEELS): a short thud on each refusal and a light tick each time a fixed
// file lands in its box.
// Android (Chrome): the Vibration API. The browser only allows it after the person has tapped or pressed a key
// on the page at least once (a scroll doesn't count), so until then we stay quiet instead of being refused.
// iPhone: Safari has no Vibration API. On iOS 18 and later, a switch control toggled from script plays the system
// tap (one fixed strength): 1 tap = tick, 2 = thud. On older iPhones nothing happens. Either way the page
// works the same.

export const PATTERNS = {
  tick: [14],
  thud: [45, 35, 20],
};
const TAPS = { tick: [0], thud: [0, 80] };
let label = null;
export function initHaptics() {
  if ("vibrate" in navigator || label) return;
  label = document.createElement("label");
  label.setAttribute("aria-hidden", "true");
  label.style.cssText = "position:fixed;left:-9999px;top:0;width:1px;height:1px;overflow:hidden;opacity:0;pointer-events:none";
  const input = document.createElement("input");
  input.type = "checkbox";
  input.setAttribute("switch", "");
  input.tabIndex = -1;
  label.append(input);
  document.body.append(label);
}

export function feel(kind = "tick") {
  if (!PATTERNS[kind]) return;
  if ("vibrate" in navigator) {
    if (navigator.userActivation && !navigator.userActivation.hasBeenActive) return; // not tapped yet: it would be refused
    try { navigator.vibrate(PATTERNS[kind]); } catch { /* not allowed */ }
    return;
  }
  if (!label) return;
  for (const ms of TAPS[kind]) setTimeout(() => { try { label.click(); } catch { /* ignore */ } }, ms);
}
