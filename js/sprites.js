// Procedural pixel-art fighters. Each kit has its own head/hat and colors on a shared chibi body.
// Sprites are 16x19 "pixels", facing right. The weapon hand is at HAND.

export const SPRITE_W = 16, SPRITE_H = 19;
export const HAND = { x: 13, y: 12 };

const OUTLINE = "#0b0618", SKIN = "#f0c49a", SKIN_DARK = "#c98f6a", BOOTS = "#2a1d14", EYE = "#1a1030";

// Rows 0-9: head. Every row is padded/cut to 16 columns.
const HEADS = {
    duelist: [
        "...........aa...",
        "..........aa....",
        "....oooooaao....",
        "...oHHHHHHHHo...",
        ".ooHHHHHHHHHHoo.",
        "..ooddddddddoo..",
        "..ossssssesseo..",
        "..ossssssesseo..",
        "..osssssssssso..",
        "...ooooooooooo.."
    ],
    berserker: [
        ".ww..........ww.",
        ".ow..oooooo..wo.",
        "..owoMMMMMMowo..",
        "...oMMMMMMMMo...",
        "..oMmmmmmmmmMo..",
        "..osssssssssso..",
        "..ossssssesseo..",
        "..ossssssesseo..",
        "..ossssBBBBBBo..",
        "...oBBBBBBBBo..."
    ],
    guardian: [
        ".......aa.......",
        "......aaaa......",
        "....ooaaaaoo....",
        "...oMMMMMMMMo...",
        "..oMMMMMMMMMMo..",
        "..oMmMMMMMMMMo..",
        "..oMmMMMoooooo..",
        "..oMmMMMMggMMo..",
        "..oMmMMMMMMMMo..",
        "...oMMMMMMMMo..."
    ],
    trickster: [
        "............oo..",
        "..........ooHo..",
        ".....ooooHHHo...",
        "...ooHHHHHHHo...",
        "..oHHHHHHHHHHo..",
        "..oHsssssssssHo.",
        "..oHkkkkkgkkgo..",
        "..oHsssssssssHo.",
        "..oHHssssssssHo.",
        "...ooooooooooo.."
    ],
    gambler: [
        "....oooooooo....",
        "....oHHHHHHo....",
        "....oHHHHHHo....",
        "....oaaaaaao....",
        "..ooHHHHHHHHoo..",
        "..osssssssssso..",
        "..ossssssesseo..",
        "..ossssssesseo..",
        "..osssssmmmmso..",
        "...ooooooooooo.."
    ],
    sniper: [
        "................",
        ".....oooooo.....",
        "...ooHHHHHHoo...",
        "..oHHHHHHHHHHo..",
        "..oHHHHHHHHHHo..",
        "..oHssssssssHo..",
        "..oHggggggGGgo..",
        "..oHsssssssssHo.",
        "..oHHkkkkkkkHo..",
        "...ooooooooooo.."
    ],
    // Pepf Prime: a turquoise crown with jewels over dark hair.
    pepf: [
        "...Y...Y...Y....",
        "...YY.YYY.YY....",
        "...oYYYYYYYYo...",
        "...oyjyyjyyjo...",
        "..oHHHHHHHHHHo..",
        "..oHsssssssssHo.",
        "..ossssssesseo..",
        "..ossssssesseo..",
        "..osssssmmmsso..",
        "...ooooooooooo.."
    ],
    none: [
        "................",
        ".....oooooo.....",
        "...ooHHHHHHoo...",
        "..oHHHHHHHHHHo..",
        "..oHHHHHHHHHHo..",
        "..oHssssssssHo..",
        "..oHssssesseHo..",
        "..oHssssesseHo..",
        "..oHHssssssHHo..",
        "...ooooooooooo.."
    ]
};

// Rows 10-18: body, the right arm reaches forward to hold the weapon.
const BODY = [
    "....occccco.....",
    "...occccccco....",
    "..occaaaaaccoss.",
    "..occccccccooss.",
    "..oCcccccccCo...",
    "...oCCCCCCCo....",
    "...oCCo.oCCo....",
    "...obbo.obbo....",
    "...oooo.oooo...."
];

// Pepf Prime: the body wears a flowing turquoise cape.
const BODIES = {
    pepf: [
        "..kkoccccco.....",
        "..koccccccco....",
        ".koccaaaaaccoss.",
        ".koccccccccooss.",
        ".KoCcccccccCo...",
        ".KkoCCCCCCCo....",
        "KK.oCCo.oCCo....",
        "...obbo.obbo....",
        "...oooo.oooo...."
    ]
};

export const KIT_COLORS = {
    duelist: "#5b8cff", berserker: "#ff5a4e", guardian: "#ffd166",
    trickster: "#b26bff", gambler: "#4fd67f", sniper: "#40e0d0", none: "#a7a3c8"
};

const PALETTES = {
    duelist: { H: "#3b6fd8", d: "#24448f", a: "#f4f4ff", c: "#2f56b0", C: "#1d3570" },
    berserker: { M: "#9aa0ad", m: "#5c6170", w: "#f1ead2", B: "#e07a2b", c: "#b5302f", C: "#6e1a1f", a: "#3a2a1a" },
    guardian: { M: "#e2b84a", m: "#9c7a24", a: "#e0403a", g: "#fff2a8", c: "#c9a13a", C: "#7a5a1c" },
    trickster: { H: "#6a3fb8", k: "#1a1030", g: "#ff6dff", c: "#4e2c8a", C: "#2a1650", a: "#ff6dff" },
    gambler: { H: "#1f2a22", a: "#2fae5c", m: "#3a2a1a", c: "#2fae5c", C: "#1b6a38" },
    sniper: { H: "#2f8a7e", g: "#3a3020", G: "#40e0d0", k: "#1d4f49", c: "#2f6f68", C: "#1a403c", a: "#ffd166" },
    none: { H: "#5d5a78", c: "#4a4760", C: "#2c2a3c", a: "#8783a8" },
    pepf: { Y: "#40e0d0", y: "#b8fff6", j: "#ffffff", H: "#2a2140", m: "#c98f6a", c: "#13756e", C: "#0a4a46", a: "#b8fff6", k: "#40e0d0", K: "#0a8f86" }
};

const cache = new Map();

/** The kit's sprite as a 16x19 canvas (cached). `tint` recolors the cloth (e.g. a name color cosmetic). */
export function fighterSprite(kit, { flash = false } = {}) {
    const key = `${kit}|${flash}`;
    if (cache.has(key)) return cache.get(key);
    const k = HEADS[kit] ? kit : "none";
    const pal = { o: OUTLINE, s: SKIN, e: EYE, b: BOOTS, ...PALETTES[k] };
    const canvas = document.createElement("canvas");
    canvas.width = SPRITE_W;
    canvas.height = SPRITE_H;
    const ctx = canvas.getContext("2d");
    const rows = [...HEADS[k], ...(BODIES[k] ?? BODY)];
    rows.forEach((row, y) => {
        const line = row.padEnd(SPRITE_W, ".").slice(0, SPRITE_W);
        for (let x = 0; x < SPRITE_W; x++) {
            const ch = line[x];
            if (ch === ".") continue;
            ctx.fillStyle = flash ? (ch === "o" ? "#ffffff" : "#ffffff") : (pal[ch] ?? SKIN_DARK);
            ctx.fillRect(x, y, 1, 1);
        }
    });
    cache.set(key, canvas);
    return canvas;
}

/** A crisp, upscaled portrait for the DOM (kit cards, lobby, podium). */
const portraits = new Map();
export function portraitUrl(kit, scale = 4) {
    const key = `${kit}|${scale}`;
    if (portraits.has(key)) return portraits.get(key);
    const src = fighterSprite(kit);
    const c = document.createElement("canvas");
    c.width = SPRITE_W * scale;
    c.height = SPRITE_H * scale;
    const ctx = c.getContext("2d");
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(src, 0, 0, c.width, c.height);
    const url = c.toDataURL();
    portraits.set(key, url);
    return url;
}

const images = new Map();
/** Loads (and caches) an image; returns it even before it finished loading. */
export function image(src) {
    if (!images.has(src)) {
        const img = new Image();
        img.src = src;
        images.set(src, img);
    }
    return images.get(src);
}
