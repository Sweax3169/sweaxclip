// Extracts the Anton "S" as an SVG path fitted into the Sweaxclip mark grid.
import opentype from "opentype.js";
const font = opentype.loadSync("../src/SweaxClip/Assets/Fonts/Anton-Regular.ttf");
const H = 42;                     // glyph height in the 64 grid
const p0 = font.getPath("S", 0, 0, 100);
const bb = p0.getBoundingBox();
const s = H / (bb.y2 - bb.y1);
const w = (bb.x2 - bb.x1) * s;
const caretW = 4.6, gap = 6;
const total = w + gap + caretW;
const x0 = (64 - total) / 2, y0 = (64 - H) / 2;
const p = font.getPath("S", 0, 0, 100);
for (const c of p.commands) {
  for (const [kx, ky] of [["x", "y"], ["x1", "y1"], ["x2", "y2"]]) {
    if (c[kx] !== undefined) { c[kx] = +((c[kx] - bb.x1) * s + x0).toFixed(2); c[ky] = +((c[ky] - bb.y1) * s + y0).toFixed(2); }
  }
}
console.log(JSON.stringify({ d: p.toPathData(2), caretX: +(x0 + w + gap).toFixed(2), caretY: y0, caretH: H, caretW, w: +w.toFixed(2) }));
