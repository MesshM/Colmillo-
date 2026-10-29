// Genera Assets/colmillo.ico sin dependencias: dibuja el icono por software
// (supermuestreo 4x4) y lo empaqueta como ICO con entradas PNG.
// Uso: node tools/make-icon.js
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const SIZES = [16, 20, 24, 32, 40, 48, 64, 256];

// Carmesi de la marca (tokens cobalt-600 -> cobalt-hi de GenI pasados a sRGB).
const TOP = [181, 32, 58];
const BOTTOM = [206, 38, 84];

function bezier(p0, p1, p2, t) {
  const u = 1 - t;
  return [u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]];
}

// Colmillo: borde exterior convexo y borde interior mas recto, punta abajo.
function fangPolygon(dx) {
  const pts = [];
  const a = [0.3 + dx, 0.2];
  const b = [0.62 + dx, 0.2];
  const tip = [0.4 + dx, 0.84];
  for (let i = 0; i <= 24; i++) pts.push(bezier(a, [0.26 + dx, 0.58], tip, i / 24));
  for (let i = 0; i <= 24; i++) pts.push(bezier(tip, [0.58 + dx, 0.5], b, i / 24));
  return pts;
}

function inside(poly, x, y) {
  let c = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i];
    const [xj, yj] = poly[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) c = !c;
  }
  return c;
}

function insideRounded(x, y, r) {
  const cx = Math.min(Math.max(x, r), 1 - r);
  const cy = Math.min(Math.max(y, r), 1 - r);
  return (x - cx) ** 2 + (y - cy) ** 2 <= r * r;
}

const fang = fangPolygon(0.04);
const shine = [
  [0.33, 0.24],
  [0.4, 0.24],
  [0.36, 0.5]
];

function render(size) {
  const px = Buffer.alloc(size * size * 4);
  const ss = 4;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < ss; sy++) {
        for (let sx = 0; sx < ss; sx++) {
          const u = (x + (sx + 0.5) / ss) / size;
          const v = (y + (sy + 0.5) / ss) / size;
          if (!insideRounded(u, v, 0.24)) continue;
          let c = TOP.map((t, i) => t + (BOTTOM[i] - t) * v);
          if (inside(fang, u, v)) c = inside(shine, u, v) ? [255, 236, 226] : [255, 250, 244];
          r += c[0]; g += c[1]; b += c[2]; a += 255;
        }
      }
      const n = ss * ss;
      const o = (y * size + x) * 4;
      const cov = a / n;
      // RGBA no premultiplicado
      px[o] = cov ? Math.round(r / (a / 255)) : 0;
      px[o + 1] = cov ? Math.round(g / (a / 255)) : 0;
      px[o + 2] = cov ? Math.round(b / (a / 255)) : 0;
      px[o + 3] = Math.round(cov);
    }
  }
  return png(size, px);
}

function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

function png(size, rgba) {
  const raw = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) rgba.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0))
  ]);
}

const images = SIZES.map(render);
const header = Buffer.alloc(6 + 16 * images.length);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(images.length, 4);
let offset = header.length;
images.forEach((img, i) => {
  const s = SIZES[i];
  const e = 6 + i * 16;
  header[e] = s >= 256 ? 0 : s;
  header[e + 1] = s >= 256 ? 0 : s;
  header.writeUInt16LE(1, e + 4);
  header.writeUInt16LE(32, e + 6);
  header.writeUInt32LE(img.length, e + 8);
  header.writeUInt32LE(offset, e + 12);
  offset += img.length;
});
const out = path.join(__dirname, '..', 'Assets', 'colmillo.ico');
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, Buffer.concat([header, ...images]));
fs.writeFileSync(path.join(__dirname, '..', 'Assets', 'colmillo-256.png'), images[images.length - 1]);
console.log('icono escrito en', out);
