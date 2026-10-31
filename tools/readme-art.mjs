// Builds the README / site artwork: animated SVG banners and the composed product shot.
// usage: node readme-art.mjs
import sharp from "sharp";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { textPath, ANTON, ARCHIVO, ARCHIVO_SEMI, MONO, MONO_MED } from "./textpath.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const media = path.join(root, "docs", "media");
fs.mkdirSync(media, { recursive: true });

const INK = "#0A0A0B", PAPER = "#EDEDE7", Y = "#FFE500", DIM = "#6C6C67", LINE = "#2B2B30";
const S_PATH = fs.readFileSync(path.join(root, "assets", "brand", "mark.svg"), "utf8").match(/<path fill="#0A0A0B" d="([^"]+)"/)[1];

const mark = (x, y, size, blink = true) => {
  const k = size / 64;
  return `<g transform="translate(${x} ${y}) scale(${k})">
    <rect width="64" height="64" rx="13" fill="${Y}"/>
    <path fill="${INK}" d="${S_PATH}"/>
    <rect x="42.8" y="11" width="4.6" height="42" fill="${INK}">${blink ? `<animate attributeName="opacity" values="1;1;0;0" keyTimes="0;0.5;0.5;1" dur="1.1s" repeatCount="indefinite"/>` : ""}</rect>
  </g>`;
};

const p = (d, fill, extra = "") => `<path d="${d}" fill="${fill}" ${extra}/>`;

// ── hero banner ────────────────────────────────────────────────────────────
function hero(lang) {
  const W = 1280, H = 440;
  const t = lang === "tr"
    ? { tag: "Hiç unutmayan pano.", sub: "Windows için kalıcı pano arşivi — Win + V'nin yerine geçer.", meta: "WINDOWS 10 / 11   ·   YEREL   ·   ÇEVRİMDIŞI   ·   MIT", label: "ARŞİV" }
    : { tag: "A clipboard that never forgets.", sub: "A permanent clipboard archive for Windows. Replaces Win + V.", meta: "WINDOWS 10 / 11   ·   NATIVE   ·   OFFLINE   ·   MIT", label: "ARCHIVE" };

  const word1 = textPath("SWEAX", { font: ANTON, size: 150, x: 64, y: 268 });
  const word2 = textPath("CLIP", { font: ANTON, size: 150, x: 64 + word1.width + 4, y: 268 });
  const tag = textPath(t.tag, { font: ARCHIVO_SEMI, size: 30, x: 66, y: 326 });
  const sub = textPath(t.sub, { font: ARCHIVO, size: 18, x: 66, y: 360 });
  const meta = textPath(t.meta, { font: MONO_MED, size: 12, x: 66, y: 404, letterSpacing: 0.06 });
  const ver = textPath("v1.0.0", { font: MONO_MED, size: 12, x: 66, y: 86, letterSpacing: 0.06 });

  // ticker of archived clips on the right
  const rows = [
    ["TEXT", "Meeting moved to Thursday 14:30", "1m"],
    ["LINK", "github.com/Sweax3169/sweaxclip", "4m"],
    ["SHOT", "1920 × 1080 · Weekly report", "9m"],
    ["CODE", "FindAsync(string query)", "16m"],
    ["COLOR", "#FFE500", "22m"],
    ["TEXT", "Copy it once — find it in ten years.", "38m"],
    ["IMAGE", "1600 × 1000 · moodboard", "1h"],
    ["FILES", "night-drive_140bpm.wav +1", "2h"],
    ["LINK", "figma.com/design/k3Yz9", "3h"],
    ["SHOT", "INVOICE #2026-1142", "5h"],
  ];
  const rowH = 54, x0 = 790, w = 430;
  const rowSvg = (r, i) => {
    const y = i * rowH;
    const kind = textPath(r[0], { font: MONO_MED, size: 11, x: x0 + 18, y: y + 33, letterSpacing: 0.08 });
    const body = textPath(r[1], { font: r[0] === "CODE" || r[0] === "COLOR" ? MONO : ARCHIVO, size: 15, x: x0 + 92, y: y + 33 });
    const ago = textPath(r[2], { font: MONO, size: 11, x: x0 + w - 16, y: y + 33, anchor: "end" });
    const sw = r[0] === "COLOR" ? `<rect x="${x0 + 92 + body.width + 10}" y="${y + 20}" width="16" height="16" fill="${Y}"/>` : "";
    return `<g><rect x="${x0}" y="${y}" width="${w}" height="${rowH - 1}" fill="#111113"/>${p(kind.d, i % 5 === 0 ? Y : DIM)}${p(body.d, PAPER)}${p(ago.d, DIM)}${sw}</g>`;
  };
  const list = rows.map(rowSvg).join("");
  const loopH = rows.length * rowH;
  const head = textPath(t.label, { font: MONO_MED, size: 11, x: x0, y: 70, letterSpacing: 0.12 });
  const count = textPath("∞", { font: ARCHIVO_SEMI, size: 18, x: x0 + w, y: 72, anchor: "end" });

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <defs>
    <clipPath id="win"><rect x="${x0}" y="86" width="${w}" height="${H - 86 - 36}"/></clipPath>
    <linearGradient id="fade" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${INK}" stop-opacity="1"/><stop offset="0.16" stop-color="${INK}" stop-opacity="0"/>
      <stop offset="0.84" stop-color="${INK}" stop-opacity="0"/><stop offset="1" stop-color="${INK}" stop-opacity="1"/>
    </linearGradient>
    <pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="#1A1A1D"/></pattern>
  </defs>
  <rect width="${W}" height="${H}" fill="${INK}"/>
  <rect width="${W}" height="${H}" fill="url(#dots)"/>
  <rect x="0" y="0" width="${W}" height="6" fill="${Y}"/>
  ${mark(64, 44, 56)}
  ${p(ver.d, DIM, `transform="translate(76 0)"`)}
  ${p(word1.d, PAPER)}
  ${p(word2.d, Y)}
  ${p(tag.d, PAPER)}
  ${p(sub.d, "#A3A39C")}
  ${p(meta.d, DIM)}
  ${p(head.d, DIM)}${p(count.d, Y)}
  <rect x="${x0}" y="80" width="${w}" height="1" fill="${LINE}"/>
  <g clip-path="url(#win)">
    <g>
      <animateTransform attributeName="transform" type="translate" from="0 86" to="0 ${86 - loopH}" dur="22s" repeatCount="indefinite"/>
      ${list}
      <g transform="translate(0 ${loopH})">${list}</g>
    </g>
  </g>
  <rect x="${x0}" y="86" width="${w}" height="${H - 86 - 36}" fill="url(#fade)"/>
  <rect x="${x0 - 1}" y="86" width="3" height="${rowH - 1}" fill="${Y}"><animate attributeName="opacity" values="1;0.35;1" dur="2.2s" repeatCount="indefinite"/></rect>
</svg>`;
}

// ── section header strip ───────────────────────────────────────────────────
function section(no, title, file) {
  const W = 1280, H = 96;
  const n = textPath(no, { font: MONO_MED, size: 14, x: 0, y: 34, letterSpacing: 0.1 });
  const t = textPath(title, { font: ANTON, size: 52, x: 0, y: 88 });
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="${INK}"/>
  ${p(n.d, Y)}${p(t.d, PAPER)}
  <rect x="${t.width + 24}" y="62" width="${W - t.width - 24}" height="1" fill="${LINE}"/>
  <rect x="${t.width + 24}" y="58" width="44" height="9" fill="${Y}"/>
</svg>`;
  fs.writeFileSync(path.join(media, file), svg);
}

// ── footer ─────────────────────────────────────────────────────────────────
function footer(lang) {
  const W = 1280, H = 220;
  const line = lang === "tr" ? "Bir kez kopyala. Hep burada." : "Copy it once. Keep it forever.";
  const a = textPath(line, { font: ANTON, size: 64, x: W / 2, y: 130, anchor: "middle" });
  const b = textPath("SWEAX STUDIOS", { font: MONO_MED, size: 12, x: W / 2, y: 178, anchor: "middle", letterSpacing: 0.2 });
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="${INK}"/>
  ${p(a.d, PAPER)}${p(b.d, DIM)}
  <rect x="${W / 2 - 22}" y="150" width="44" height="4" fill="${Y}"><animate attributeName="width" values="44;120;44" dur="3s" repeatCount="indefinite"/><animate attributeName="x" values="${W / 2 - 22};${W / 2 - 60};${W / 2 - 22}" dur="3s" repeatCount="indefinite"/></rect>
</svg>`;
}

for (const lang of ["en", "tr"]) {
  fs.writeFileSync(path.join(media, `hero-${lang}.svg`), hero(lang));
  fs.writeFileSync(path.join(media, `footer-${lang}.svg`), footer(lang));
}
const sections = {
  en: [["01", "ONE SHORTCUT", "sec-shortcut"], ["02", "EVERY KIND, SORTED", "sec-kinds"], ["03", "SEARCH INSIDE PICTURES", "sec-ocr"], ["04", "THE ARCHIVE", "sec-archive"], ["05", "BUILT TO DISAPPEAR", "sec-light"], ["06", "INSTALL", "sec-install"]],
  tr: [["01", "TEK KISAYOL", "sec-shortcut"], ["02", "HER TÜR, AYRI RAFTA", "sec-kinds"], ["03", "RESMİN İÇİNDE ARA", "sec-ocr"], ["04", "ARŞİV", "sec-archive"], ["05", "VARLIĞINI HİSSETTİRMEZ", "sec-light"], ["06", "KURULUM", "sec-install"]],
};
for (const [lang, list] of Object.entries(sections))
  for (const [no, title, file] of list) section(no, title, `${file}-${lang}.svg`);

// ── composed product shot: library with the quick panel on top ───────────
async function productShot(lang) {
  const shots = path.join(root, "docs", "shots", lang);
  const W = 2400, H = 1500;
  const lib = await sharp(path.join(shots, "library-all.png")).resize(1960).png().toBuffer();
  const libMeta = await sharp(lib).metadata();
  const panel = await sharp(path.join(shots, "panel-all.png")).resize(620).png().toBuffer();
  const panMeta = await sharp(panel).metadata();
  const shadow = (w, h, blur, op) => sharp({ create: { width: w + blur * 4, height: h + blur * 4, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } } })
    .composite([{ input: Buffer.from(`<svg width="${w + blur * 4}" height="${h + blur * 4}"><rect x="${blur * 2}" y="${blur * 2}" width="${w}" height="${h}" fill="rgba(0,0,0,${op})"/></svg>`) }])
    .blur(blur).png().toBuffer();
  const frame = (w, h, color) => Buffer.from(`<svg width="${w}" height="${h}"><rect x="0.5" y="0.5" width="${w - 1}" height="${h - 1}" fill="none" stroke="${color}" stroke-width="1"/></svg>`);
  const bg = Buffer.from(`<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">
    <defs><pattern id="d" width="32" height="32" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1.2" fill="#1C1C20"/></pattern>
    <radialGradient id="g" cx="0.72" cy="0.78" r="0.6"><stop offset="0" stop-color="#FFE500" stop-opacity="0.10"/><stop offset="1" stop-color="#FFE500" stop-opacity="0"/></radialGradient></defs>
    <rect width="${W}" height="${H}" fill="#070708"/><rect width="${W}" height="${H}" fill="url(#d)"/><rect width="${W}" height="${H}" fill="url(#g)"/></svg>`);
  const lx = 90, ly = 90, px = W - panMeta.width - 120, py = H - panMeta.height - 70;
  await sharp(bg).composite([
    { input: await shadow(libMeta.width, libMeta.height, 30, 0.7), left: lx - 60, top: ly - 40 },
    { input: lib, left: lx, top: ly },
    { input: frame(libMeta.width, libMeta.height, "#2B2B30"), left: lx, top: ly },
    { input: await shadow(panMeta.width, panMeta.height, 34, 0.9), left: px - 68, top: py - 50 },
    { input: panel, left: px, top: py },
    { input: frame(panMeta.width, panMeta.height, "#FFE500"), left: px, top: py },
  ]).png({ compressionLevel: 9 }).toFile(path.join(media, `product-${lang}.png`));
}

await productShot("en");
await productShot("tr");
console.log("art ok");
