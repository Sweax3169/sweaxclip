// Artwork for the github.com/Sweax3169 profile README (written into ../../sweax3169-profile/assets).
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { textPath, ANTON, ARCHIVO, ARCHIVO_SEMI, MONO, MONO_MED } from "./textpath.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.resolve(here, "../../sweax3169-profile/assets");
fs.mkdirSync(out, { recursive: true });

const INK = "#0A0A0B", PAPER = "#EDEDE7", Y = "#FFE500", DIM = "#6C6C67", MID = "#A3A39C", LINE = "#2B2B30", CARD = "#111113";
const p = (d, fill, extra = "") => `<path d="${d}" fill="${fill}" ${extra}/>`;
const dots = `<pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="#1A1A1D"/></pattern>`;

// ── header ────────────────────────────────────────────────────────────────
{
  const W = 1280, H = 420;
  const name = textPath("SWEAX", { font: ANTON, size: 196, x: 60, y: 282 });
  const builds = textPath("BUILDS", { font: ANTON, size: 62, x: 64, y: 362 });
  const words = ["WINDOWS APPS", "AUDIO PLUGINS", "ROBLOX GAMES", "WEB PLATFORMS"];
  const wx = 64 + builds.width + 18, lineH = 70;
  const rot = [...words, words[0]].map((w, i) => p(textPath(w, { font: ANTON, size: 62, x: wx, y: 362 + i * lineH }).d, Y)).join("");
  // 4 holds + slides; keyTimes for translate steps
  const n = words.length, dur = 10;
  const values = [], times = [];
  for (let i = 0; i <= n; i++) {
    const y = -i * lineH;
    if (i > 0) { values.push(`0 ${y}`); times.push(((i - 1) / n + 0.06 / n * 10 / dur * 1).toFixed(4)); }
  }
  // simpler: explicit schedule
  const sched = [];
  for (let i = 0; i < n; i++) {
    const t0 = i / n, t1 = (i + 0.82) / n;
    sched.push([t0, -i * lineH], [t1, -i * lineH]);
  }
  sched.push([1, -n * lineH]);
  const kv = sched.map(([t, y]) => `0 ${y}`).join(";"), kt = sched.map(([t]) => t.toFixed(4)).join(";");
  const studio = textPath("SWEAX STUDIOS", { font: MONO_MED, size: 13, x: 64, y: 70, letterSpacing: 0.18 });
  const role = textPath("Independent developer  ·  C#  ·  C++  ·  TypeScript  ·  Luau  ·  Python", { font: MONO, size: 13, x: 64, y: 404, letterSpacing: 0.03 });
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <defs>${dots}<clipPath id="rot"><rect x="${wx - 4}" y="302" width="${W - wx}" height="${lineH - 2}"/></clipPath></defs>
  <rect width="${W}" height="${H}" fill="${INK}"/><rect width="${W}" height="${H}" fill="url(#dots)"/>
  <rect width="${W}" height="6" fill="${Y}"/>
  <rect x="64" y="54" width="10" height="20" fill="${Y}"><animate attributeName="opacity" values="1;1;0;0" keyTimes="0;0.5;0.5;1" dur="1.1s" repeatCount="indefinite"/></rect>
  ${p(studio.d, MID, 'transform="translate(20 0)"')}
  ${p(name.d, PAPER)}
  ${p(builds.d, PAPER)}
  <g clip-path="url(#rot)"><g>${rot}<animateTransform attributeName="transform" type="translate" values="${kv}" keyTimes="${kt}" dur="${dur}s" calcMode="spline" keySplines="${sched.slice(1).map(() => "0.6 0 0.2 1").join(";")}" repeatCount="indefinite"/></g></g>
  ${p(role.d, DIM)}
  <g transform="translate(1040 96)">
    ${[0, 1, 2, 3, 4, 5].map(i => `<rect x="${(i % 3) * 58}" y="${Math.floor(i / 3) * 58}" width="48" height="48" fill="${i === 4 ? Y : "none"}" stroke="${i === 4 ? Y : LINE}" stroke-width="1.5"><animate attributeName="fill-opacity" values="${i === 4 ? "1;0.35;1" : "0"}" dur="${2 + i * 0.3}s" repeatCount="indefinite"/></rect>`).join("")}
  </g>
</svg>`;
  fs.writeFileSync(path.join(out, "header.svg"), svg);
}

// ── section strips ────────────────────────────────────────────────────────
function section(no, title, file) {
  const W = 1280, H = 96;
  const n = textPath(no, { font: MONO_MED, size: 14, x: 0, y: 34, letterSpacing: 0.1 });
  const t = textPath(title, { font: ANTON, size: 52, x: 0, y: 88 });
  fs.writeFileSync(path.join(out, file), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="${INK}"/>${p(n.d, Y)}${p(t.d, PAPER)}
  <rect x="${t.width + 24}" y="62" width="${W - t.width - 24}" height="1" fill="${LINE}"/>
  <rect x="${t.width + 24}" y="58" width="44" height="9" fill="${Y}"/></svg>`);
}
section("01", "NOW SHIPPING", "sec-shipping.svg");
section("02", "WHAT I BUILD", "sec-build.svg");
section("03", "TOOLBOX", "sec-stack.svg");
section("04", "HOW I WORK", "sec-work.svg");

// ── toolbox chips ─────────────────────────────────────────────────────────
{
  const groups = [
    ["DESKTOP", ["C#", ".NET 9", "WPF", "Win32 / P/Invoke", "Electron"]],
    ["AUDIO", ["C++", "JUCE", "VST3", "DSP"]],
    ["GAMES", ["Luau", "Roblox Studio", "Blender"]],
    ["WEB", ["TypeScript", "Node.js", "Cloudflare Workers", "D1", "R2"]],
    ["DATA & TOOLS", ["SQLite", "Python", "Java", "Git", "GitHub Actions"]],
  ];
  const W = 1280, rowH = 64;
  const H = groups.length * rowH + 8;
  let body = "";
  groups.forEach(([label, items], r) => {
    const y = r * rowH;
    body += p(textPath(label, { font: MONO_MED, size: 12, x: 0, y: y + 38, letterSpacing: 0.12 }).d, Y);
    let x = 190;
    items.forEach((it) => {
      const t = textPath(it, { font: ARCHIVO_SEMI, size: 18, x: x + 18, y: y + 39 });
      const w = t.width + 36;
      body += `<rect x="${x}" y="${y + 10}" width="${w}" height="42" fill="${CARD}" stroke="${LINE}"/>${p(t.d, PAPER)}`;
      x += w + 10;
    });
    body += `<rect x="0" y="${y + rowH - 1}" width="${W}" height="1" fill="#1A1A1D"/>`;
  });
  fs.writeFileSync(path.join(out, "toolbox.svg"), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}"><rect width="${W}" height="${H}" fill="${INK}"/>${body}</svg>`);
}

// ── footer ────────────────────────────────────────────────────────────────
{
  const W = 1280, H = 200;
  const a = textPath("MAKE THINGS THAT LAST.", { font: ANTON, size: 60, x: W / 2, y: 112, anchor: "middle" });
  const b = textPath("SWEAX STUDIOS  ·  2026", { font: MONO_MED, size: 12, x: W / 2, y: 166, anchor: "middle", letterSpacing: 0.2 });
  fs.writeFileSync(path.join(out, "footer.svg"), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="${INK}"/>${p(a.d, PAPER)}${p(b.d, DIM)}
  <rect x="${W / 2 - 22}" y="132" width="44" height="4" fill="${Y}"><animate attributeName="width" values="44;120;44" dur="3s" repeatCount="indefinite"/><animate attributeName="x" values="${W / 2 - 22};${W / 2 - 60};${W / 2 - 22}" dur="3s" repeatCount="indefinite"/></rect></svg>`);
}
console.log("profile art ->", out);
