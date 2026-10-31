// Profile picture: a quiet monogram in Windows blue (upload by hand in GitHub settings).
import sharp from "sharp";
import opentype from "opentype.js";
import path from "node:path";
import { fileURLToPath } from "node:url";
const here = path.dirname(fileURLToPath(import.meta.url));
const font = opentype.loadSync("C:/Windows/Fonts/seguisb.ttf");
const S = 1024;
const p = font.getPath("S", 0, 0, 560);
const bb = p.getBoundingBox();
const dx = S / 2 - (bb.x1 + bb.x2) / 2, dy = S / 2 - (bb.y1 + bb.y2) / 2;
const glyph = font.getPath("S", dx, dy, 560).toPathData(2);
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${S}" height="${S}">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2B88D8"/><stop offset="1" stop-color="#0B4FA3"/></linearGradient></defs>
  <rect width="${S}" height="${S}" fill="url(#g)"/>
  <path d="${glyph}" fill="#FFFFFF"/>
</svg>`;
await sharp(Buffer.from(svg)).png().toFile(path.resolve(here, "../../sweax3169-profile/avatar.png"));
console.log("ok");
