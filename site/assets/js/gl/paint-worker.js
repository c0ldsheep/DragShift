// Paints the scene's pictures (paint.js) off the page, so the page and its loader never freeze while they're drawn.
// Gets the photo once, then sends each picture back as soon as it's ready, as plain pixels (moved, not copied).
import { jobs, pixels } from "./paint.js";

self.onmessage = ({ data: { photo, p1Aspect } }) => {
  for (const [name, paint] of jobs(photo, p1Aspect)) {
    try {
      const { width, height, data, meta } = pixels(paint());
      self.postMessage({ name, width, height, data, meta }, [data.buffer]);
    } catch (e) {
      self.postMessage({ name, error: String(e && e.message ? e.message : e) });
    }
  }
  photo.close?.();
};
