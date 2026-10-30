// Cinematic overlays for the admin set "Pepf Prime": an entrance title card when the fight starts, a cut-in
// when they fire their ultimate and a title card when they win. DOM on top of the arena canvas, all CSS-animated.
import { el } from "./data.js";
import { portraitUrl } from "./sprites.js";
import { effectsLevel } from "./fx.js";

/** The Pepf Prime name color: every letter is its own span so it can wave and shimmer out of phase. */
export function primeLetters(name) {
    return [...name].map((c, i) => el("span", { class: "pl", style: `--i:${i}`, text: c }));
}

/** Which friend signature set (if any) the appearance wears. */
export const sigKey = appearance => ["z7fox", "ely7", "xxz", "raul"].find(k => !!appearance && Object.values(appearance).some(v => typeof v === "string" && v.endsWith(`_${k}`)));

/** Wearing any piece of the set. */
export const isPrime = appearance => !!appearance && Object.values(appearance).some(v => typeof v === "string" && v.endsWith("_pepf"));

function mount(root, node, seconds) {
    root.append(node);
    setTimeout(() => node.remove(), seconds * 1000);
    return node;
}

/** Shards of light that burst outward from the middle of the card. */
function shards(n) {
    return el("div", { class: "pc-shards" }, Array.from({ length: n }, (_, k) =>
        el("i", { style: `--a:${(k / n) * 360 + (k % 2) * 9}deg; --d:${0.25 + (k % 5) * 0.05}s; --l:${40 + (k * 37) % 60}%` })));
}

/** Fight intro: letterbox, a light streak, then the name card slides through. */
export function primeIntro(root, { name, kit, title }, side) {
    mount(root, el("div", { class: `prime-cine pc-intro side-${side}` },
        el("div", { class: "pc-bar top" }), el("div", { class: "pc-bar bottom" }),
        el("div", { class: "pc-veil" }),
        el("div", { class: "pc-streak" }),
        shards(18),
        el("div", { class: "pc-card" },
            el("div", { class: "pc-portrait" }, el("img", { src: portraitUrl(kit ?? "none", 6), alt: "" })),
            el("div", { class: "pc-text" },
                el("div", { class: "pc-sub", text: title || "Pepf Prime" }),
                el("div", { class: "pc-name", dataset: { text: name } , text: name }),
                el("div", { class: "pc-tag", text: "enters the arena" })))), 2.6);
}

/** Ultimate cut-in: the screen darkens, a diagonal band with the portrait and the ultimate's name slams across. */
export function primeCutIn(root, { name, kit }, ability, side) {
    if (effectsLevel() === 0) return;
    mount(root, el("div", { class: `prime-cine pc-cutin side-${side}` },
        el("div", { class: "pc-dim" }),
        el("div", { class: "pc-band" },
            el("div", { class: "pc-speed" }),
            el("img", { class: "pc-cut-portrait", src: portraitUrl(kit ?? "none", 8), alt: "" }),
            el("div", { class: "pc-cut-text" },
                el("div", { class: "pc-cut-name", text: name }),
                el("div", { class: "pc-cut-ability", dataset: { text: ability }, text: ability }))),
        el("div", { class: "pc-flash" })), 1.4);
}

/** Friend signature sets: a lighter entrance card (no letterbox, no glitch, no shards) in their colors. */
export function sigIntro(root, { name, kit, title, key, tag }, side) {
    mount(root, el("div", { class: `sig-cine sig-${key} side-${side}` },
        el("div", { class: "sc-streak" }),
        el("div", { class: "sc-card" },
            el("img", { class: "sc-portrait", src: portraitUrl(kit ?? "none", 5), alt: "" }),
            el("div", {},
                title ? el("div", { class: "sc-sub", text: title }) : null,
                el("div", { class: "sc-name", text: name }),
                el("div", { class: "sc-tag", text: tag })))), 2.1);
}

/** Friend ultimate cut-in: a thin band in their colors with the ability name (no dim, no flash). */
export function sigCutIn(root, { name, kit, key }, ability, side) {
    if (effectsLevel() === 0) return;
    mount(root, el("div", { class: `sig-cine sig-${key} sc-cut side-${side}` },
        el("div", { class: "sc-band" },
            el("img", { class: "sc-cut-portrait", src: portraitUrl(kit ?? "none", 6), alt: "" }),
            el("div", {}, el("div", { class: "sc-cut-name", text: name }), el("div", { class: "sc-cut-ability", text: ability })))), 1.05);
}

/** Shown to Pepf's opponent: hazard stripes and a warning banner. */
export function primeWarning(root, name) {
    mount(root, el("div", { class: "prime-cine pc-warning" },
        el("div", { class: "pc-hazard top" }), el("div", { class: "pc-hazard bottom" }),
        el("div", { class: "pc-warn-box" },
            el("div", { class: "pc-warn-sign", text: "\u26A0 WARNING \u26A0" }),
            el("div", { class: "pc-warn-text", text: `You are facing ${name}` }),
            el("div", { class: "pc-warn-sub", text: "The Creator" }))), 2.4);
}

/** Hit-streak counter on Pepf's side: grows and changes rank as the streak climbs. */
const RANKS = [[25, "GODLIKE"], [15, "LEGENDARY"], [10, "AMAZING"], [5, "GREAT"], [3, "COMBO"]];
export function primeCombo(root, n, side) {
    let node = root.querySelector(".pc-combo");
    if (!node) {
        node = el("div", { class: "pc-combo" }, el("b", {}), el("span", {}));
        root.append(node);
    }
    node.classList.toggle("right", side === 1);
    node.dataset.rank = RANKS.find(([min]) => n >= min)?.[0] ?? 3;
    node.firstChild.textContent = `${n}`;
    node.lastChild.textContent = `HITS · ${RANKS.find(([min]) => n >= min)?.[1] ?? "COMBO"}`;
    node.classList.remove("pop");
    void node.offsetWidth;
    node.classList.add("pop");
}

/** Kill cam overlay: film bars, a blinking REC dot and "REPLAY". */
export function primeReplay(root) {
    mount(root, el("div", { class: "prime-cine pc-replay" },
        el("div", { class: "pc-bar top" }), el("div", { class: "pc-bar bottom" }),
        el("div", { class: "pc-rec" }, el("i"), "REPLAY"),
        el("div", { class: "pc-scan" })), 1.65);
}

/** Holographic foil: the element tilts toward the pointer and a rainbow-teal sheen follows it. */
export function holo(node, tilt = 8) {
    if (!node || node.dataset.holo) return node;
    node.dataset.holo = "1";
    node.classList.add("holo");
    node.append(el("i", { class: "holo-foil", "aria-hidden": "true" }));
    node.addEventListener("pointermove", e => {
        const r = node.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
        node.style.setProperty("--mx", `${x * 100}%`);
        node.style.setProperty("--my", `${y * 100}%`);
        node.style.setProperty("--rx", `${(0.5 - y) * tilt}deg`);
        node.style.setProperty("--ry", `${(x - 0.5) * tilt}deg`);
        node.classList.add("holo-on");
    });
    node.addEventListener("pointerleave", () => {
        node.classList.remove("holo-on");
        node.style.setProperty("--rx", "0deg");
        node.style.setProperty("--ry", "0deg");
    });
    return node;
}

/** Victory title card: "ENCORE" letter by letter over turning rays. */
export function primeVictory(root, { name, title }) {
    const word = "ENCORE";
    mount(root, el("div", { class: "prime-cine pc-victory" },
        el("div", { class: "pc-bar top" }), el("div", { class: "pc-bar bottom" }),
        el("div", { class: "pc-veil" }),
        el("div", { class: "pc-rays" }),
        el("div", { class: "pc-rays gold" }),
        shards(24),
        el("div", { class: "pc-vic" },
            el("div", { class: "pc-vic-word" }, [...word].map((c, k) => el("span", { style: `--k:${k}`, text: c }))),
            el("div", { class: "pc-vic-sub", text: `${name}${title ? ` · ${title}` : ""}` }))), 3.2);
}
