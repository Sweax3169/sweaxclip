// Converts text to SVG path data with the bundled fonts, so README SVGs render
// identically everywhere (GitHub's image proxy blocks web fonts inside SVG).
import opentype from "opentype.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const fonts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../src/SweaxClip/Assets/Fonts");
const cache = {};
const load = (f) => (cache[f] ??= opentype.loadSync(path.join(fonts, f)));

export const ANTON = "Anton-Regular.ttf";
export const ARCHIVO = "Archivo-Regular.ttf";
export const ARCHIVO_SEMI = "Archivo-SemiBold.ttf";
export const MONO = "JetBrainsMono-Regular.ttf";
export const MONO_MED = "JetBrainsMono-Medium.ttf";

/** Returns { d, width } for text drawn with its baseline at (x, y). letterSpacing in em. */
export function textPath(text, { font = ARCHIVO, size = 16, x = 0, y = 0, letterSpacing = 0, anchor = "start" } = {}) {
  const f = load(font);
  const scale = size / f.unitsPerEm;
  const glyphs = f.stringToGlyphs(text);
  let width = 0;
  glyphs.forEach((g, i) => {
    width += g.advanceWidth * scale;
    if (i < glyphs.length - 1) width += f.getKerningValue(g, glyphs[i + 1]) * scale + letterSpacing * size;
  });
  let cx = anchor === "middle" ? x - width / 2 : anchor === "end" ? x - width : x;
  let d = "";
  glyphs.forEach((g, i) => {
    d += g.getPath(cx, y, size).toPathData(2);
    cx += g.advanceWidth * scale;
    if (i < glyphs.length - 1) cx += f.getKerningValue(g, glyphs[i + 1]) * scale + letterSpacing * size;
  });
  return { d, width };
}
