// Generic weapon skins (cosmetic): palette swaps of the weapon sprite, cached per sprite and skin.
import { image } from "./sprites.js";
import { data } from "./data.js";

// Luminance ramps from dark to light. "prismatic" shifts hue along the sprite instead.
export const SKINS = {
    ws_rusted: { ramp: ["#2a1408", "#6b3410", "#a65a22", "#d98a4a"], glow: "#d98a4a" },
    ws_obsidian: { ramp: ["#05030a", "#1a1428", "#3a2f55", "#8a7ac8"], glow: "#8a7ac8" },
    ws_frost: { ramp: ["#0b2440", "#2a6aa8", "#7ac8ff", "#f0fbff"], glow: "#9fe8ff" },
    ws_ember: { ramp: ["#2a0602", "#9a2a08", "#ff7a1a", "#ffe08a"], glow: "#ff8a3d" },
    ws_toxic: { ramp: ["#061a06", "#1f6a12", "#6be03a", "#e0ffb0"], glow: "#8aff4a" },
    ws_blood: { ramp: ["#1a0206", "#5a0a14", "#b0182a", "#ff6a7a"], glow: "#ff3a4e" },
    ws_gilded: { ramp: ["#3a2606", "#9a6a10", "#e2b84a", "#fff2c0"], glow: "#ffd166" },
    ws_spectral: { ramp: ["#0a2a2a", "#2aa8a0", "#7af0e0", "#ffffff"], glow: "#7af0e0", alpha: 0.8 },
    ws_void: { ramp: ["#05010d", "#2a0f4a", "#7a3ad8", "#e0b0ff"], glow: "#b26bff" },
    ws_dragon: { ramp: ["#2a2016", "#8a7a5a", "#e8dcc0", "#ffffff"], glow: "#ff5a3d" },
    ws_headhunter: { ramp: ["#0a0a0a", "#3a0a0a", "#c01a2a", "#ffd0d0"], glow: "#ff3c50" },
    ws_prismatic: { prismatic: true, glow: "#e070ff" }
};

const cache = new Map();

function hexToRgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }

function rampColor(ramp, t) {
    const pos = Math.max(0, Math.min(0.999, t)) * (ramp.length - 1);
    const i = Math.floor(pos), f = pos - i;
    const a = hexToRgb(ramp[i]), b = hexToRgb(ramp[Math.min(ramp.length - 1, i + 1)]);
    return a.map((v, k) => Math.round(v + (b[k] - v) * f));
}

function hslToRgb(h, s, l) {
    const k = n => (n + h * 12) % 12, a = s * Math.min(l, 1 - l);
    const f = n => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
    return [f(0), f(8), f(4)].map(v => Math.round(v * 255));
}

/** Only generic weapons can be skinned. */
export function skinApplies(itemId, skin) {
    return !!SKINS[skin] && data.items[itemId]?.pack === "generic" && data.items[itemId]?.type === "weapon";
}

/** The skinned sprite as a canvas, or null if it isn't ready yet (image still loading). */
export function skinnedCanvas(itemId, skin) {
    const key = `${itemId}|${skin}`;
    if (cache.has(key)) return cache.get(key);
    const img = image(`img/item/${data.items[itemId]?.sprite}`);
    if (!img.complete || !img.naturalWidth) return null;
    const def = SKINS[skin];
    const c = document.createElement("canvas");
    c.width = img.naturalWidth;
    c.height = img.naturalHeight;
    const ctx = c.getContext("2d");
    ctx.drawImage(img, 0, 0);
    const pixels = ctx.getImageData(0, 0, c.width, c.height);
    const d = pixels.data;
    for (let i = 0; i < d.length; i += 4) {
        if (!d[i + 3]) continue;
        const lum = (0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]) / 255;
        let rgb;
        if (def.prismatic) {
            const p = i / 4, x = p % c.width, y = Math.floor(p / c.width);
            rgb = hslToRgb(((x + y) / (c.width + c.height)) % 1, 0.85, 0.3 + lum * 0.55);
        } else rgb = rampColor(def.ramp, lum * 1.15);
        d[i] = rgb[0]; d[i + 1] = rgb[1]; d[i + 2] = rgb[2];
        if (def.alpha) d[i + 3] = Math.round(d[i + 3] * def.alpha);
    }
    ctx.putImageData(pixels, 0, 0);
    cache.set(key, c);
    return c;
}

const urls = new Map();
/** Data URL of the skinned sprite for <img> tags; falls back to the plain sprite while loading. */
export function skinnedUrl(itemId, skin) {
    const key = `${itemId}|${skin}`;
    if (urls.has(key)) return urls.get(key);
    const c = skinnedCanvas(itemId, skin);
    if (!c) return `img/item/${data.items[itemId]?.sprite}`;
    const url = c.toDataURL();
    urls.set(key, url);
    return url;
}

/** Preloads the generic weapon sprites so skins are ready when the first card renders. */
export function preloadSkins() {
    for (const it of Object.values(data.items)) if (it.pack === "generic" && it.type === "weapon") image(`img/item/${it.sprite}`);
}
