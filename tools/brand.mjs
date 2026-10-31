// Renders the SweaxClip mark into PNG sizes and a multi-resolution .ico.
// usage: node brand.mjs
import sharp from "sharp";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const brand = path.join(root, "assets", "brand");
const appAssets = path.join(root, "src", "SweaxClip", "Assets");
const svg = fs.readFileSync(path.join(brand, "icon.svg"));

const png = (size) => sharp(svg, { density: Math.max(72, (72 * size) / 256) * 4 }).resize(size, size).png().toBuffer();

for (const s of [16, 32, 48, 64, 128, 256, 512, 1024]) {
  fs.writeFileSync(path.join(brand, `icon-${s}.png`), await png(s));
}
fs.copyFileSync(path.join(brand, "icon-256.png"), path.join(appAssets, "logo-256.png"));

// ICO with PNG-compressed entries (supported since Windows Vista).
const sizes = [16, 20, 24, 32, 40, 48, 64, 256];
const images = [];
for (const s of sizes) images.push(await png(s));
const header = Buffer.alloc(6 + 16 * sizes.length);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
sizes.forEach((s, i) => {
  const e = 6 + 16 * i;
  header.writeUInt8(s >= 256 ? 0 : s, e);
  header.writeUInt8(s >= 256 ? 0 : s, e + 1);
  header.writeUInt8(0, e + 2);
  header.writeUInt8(0, e + 3);
  header.writeUInt16LE(1, e + 4);
  header.writeUInt16LE(32, e + 6);
  header.writeUInt32LE(images[i].length, e + 8);
  header.writeUInt32LE(offset, e + 12);
  offset += images[i].length;
});
const ico = Buffer.concat([header, ...images]);
fs.writeFileSync(path.join(appAssets, "sweaxclip.ico"), ico);
fs.writeFileSync(path.join(brand, "sweaxclip.ico"), ico);
console.log("ok", ico.length, "bytes ico");
