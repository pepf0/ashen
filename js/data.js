// Item/effect data and text formatting shared by the compendium, shop and combat views.

export const data = { items: {}, effects: {}, kits: {}, modifiers: {} };

export const TYPES = ["weapon", "buff", "shirt", "charm", "passive"];
export const PACK_NAMES = { pepf: "Pepf", z7fox: "Z7FOX", ely7: "Ely7", xxzweiradxx: "xXZweiradXx", generic: "Generic" };
export const MAX_LEVEL = 5;

export async function loadData() {
    const [items, effects, kits, modifiers] = await Promise.all([
        fetch("json/items.json").then(r => r.json()),
        fetch("json/effects.json").then(r => r.json()),
        fetch("json/kits.json").then(r => r.json()),
        fetch("json/modifiers.json").then(r => r.json())
    ]);
    data.items = Object.fromEntries(items.map(i => [i.id, i]));
    data.effects = Object.fromEntries(effects.map(e => [e.id, e]));
    data.kits = Object.fromEntries(kits.map(k => [k.id, k]));
    data.modifiers = Object.fromEntries(modifiers.map(m => [m.id, m]));
}

export const item = id => data.items[id];
export const sprite = id => `img/item/${data.items[id]?.sprite ?? ""}`;

const round2 = v => Math.round(v * 100) / 100;

export function statPretty(key, value) {
    value = round2(value);
    switch (key) {
        case "damage": return ["Damage", value];
        case "cooldown": return ["Cooldown", `${value}s`];
        case "crit_chance": return ["Crit Chance", `${round2(value * 100)}%`];
        case "crit_damage": return ["Crit Damage", `${value}x`];
        case "damage_reduction": return ["Damage Reduction", `${round2(value * 100)}%`];
        case "dodge_chance": return ["Dodge Chance", `${round2(value * 100)}%`];
        case "max_hp": return ["Max HP", Math.round(value)];
        case "special_chance": return ["Special Chance", `${round2(value * 100)}%`];
    }
    return [key, value];
}

/** Base stats plus level_stats for every level above 1 (same as the server). */
export function statsAtLevel(it, level) {
    const stats = { ...(it.base_stats ?? {}) };
    for (const [key, add] of Object.entries(it.level_stats ?? {})) {
        const stat = key.replace(/_add$/, "");
        stats[stat] = (stats[stat] ?? 0) + add * (level - 1);
    }
    return stats;
}

/** Effect values grow by +50% of their base strength per level above 1 (same as the server). */
export function scaleEffect(effect, level) {
    const s = 1 + 0.5 * (level - 1);
    if (s === 1) return effect;
    const e = { ...effect };
    if (e.mult !== undefined) e.mult = 1 + (e.mult - 1) * s;
    if (e.add !== undefined) e.add *= s;
    if (e.gain !== undefined) e.gain *= s;
    if (e.damage !== undefined) e.damage *= s;
    if (e.start_money !== undefined) e.start_money *= s;
    return e;
}

// Descriptions contain small expressions like {(mult-1)*100}. They come from our own effects.json.
export function effectPretty(effect) {
    const keys = Object.keys(effect);
    let text = effect.description.replace(/\{([^}]+)\}/g, (_, expr) => {
        const value = new Function(...keys, `return ${expr};`)(...keys.map(k => effect[k]));
        return Math.round(value * 100) / 100;
    });
    text = text.replace(/\b1 seconds\b/g, "1 second");
    if (effect.mult !== undefined) {
        text = text.replace(/\bbuff\b/g, "multiplier");
        if (effect.stat !== "cooldown") {
            text = text.replace(/\bby ([\d.]+)%/g, "by a $1% multiplier");
        }
    } else if (effect.add !== undefined) {
        text = text.replace(/\bby ([\d.]+)%/g, "by a flat $1%");
    }
    return text;
}

/** Fills {expressions} in a kit ability description from its own fields. */
export function abilityPretty(ability) {
    const keys = Object.keys(ability);
    return ability.description.replace(/\{([^}]+)\}/g, (_, expr) => {
        const value = new Function(...keys, `return ${expr};`)(...keys.map(k => ability[k]));
        return Math.round(value * 100) / 100;
    });
}

export function abilityById(id) {
    for (const kit of Object.values(data.kits))
        for (const a of kit.abilities) if (a.id === id) return a;
    return null;
}

/** A kit card: name, description and its four abilities with keys. */
export function kitCard(kitId, { selected = false, onclick, footer } = {}) {
    const kit = data.kits[kitId];
    return el("div", { class: "kit-card" + (selected ? " selected" : "") + (onclick ? " clickable" : ""), onclick },
        el("div", { class: "kit-name", text: kit.name }),
        el("div", { class: "muted small", text: kit.description }),
        el("ul", { class: "kit-abilities" }, kit.abilities.map(a => el("li", {},
            el("span", { class: "key" + (a.ultimate ? " ult" : ""), text: a.key.toUpperCase() }),
            el("div", {},
                el("b", { text: a.name }),
                el("span", { class: "muted small", text: a.cooldown ? `  ${a.cooldown}s` : "  ultimate" }),
                el("div", { class: "small", text: abilityPretty(a) }))))),
        footer ?? null);
}

export function el(tag, attrs = {}, ...children) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
        if (v === undefined || v === null || v === false) continue;
        if (k === "class") node.className = v;
        else if (k === "text") node.textContent = v;
        else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
        else if (k === "dataset") Object.assign(node.dataset, v);
        else node.setAttribute(k, v === true ? "" : v);
    }
    for (const child of children.flat()) {
        if (child === null || child === undefined || child === false) continue;
        node.append(child instanceof Node ? child : document.createTextNode(child));
    }
    return node;
}

/** Small square item card with the pack-colored border used everywhere. */
export function itemCard(id, { level, size = "", onclick, extra } = {}) {
    const it = data.items[id];
    const card = el("div", { class: `item-card ${size}`, dataset: { pack: it?.pack ?? "generic" }, onclick },
        el("img", { src: sprite(id), alt: it?.name ?? id, draggable: "false" }));
    if (level > 1) card.append(el("span", { class: "level-badge", text: `Lv ${level}` }));
    if (extra) card.append(extra);
    attachTooltip(card, () => itemTooltip(id, level ?? 1));
    return card;
}

export function itemTooltip(id, level = 1) {
    const it = data.items[id];
    if (!it) return el("div", { text: id });
    const stats = statsAtLevel(it, level);
    return el("div", {},
        el("div", { class: "tt-title" }, it.name, level > 1 ? el("span", { class: "muted", text: `  Lv ${level}` }) : null),
        el("div", { class: "tt-meta", text: `${PACK_NAMES[it.pack]} ${it.type} · ${it.price}$` }),
        el("ul", { class: "tt-stats" }, Object.entries(stats).map(([k, v]) => {
            const [name, value] = statPretty(k, v);
            return el("li", { text: `${name}: ${value}` });
        })),
        el("ul", { class: "tt-effects" }, (it.effects ?? []).map(eid =>
            el("li", { text: effectPretty(scaleEffect(data.effects[eid], level)) })))
    );
}

const tooltip = () => document.getElementById("tooltip");

export function attachTooltip(node, build) {
    node.addEventListener("mouseenter", () => {
        const tt = tooltip();
        tt.replaceChildren(build());
        tt.hidden = false;
        place(node);
    });
    node.addEventListener("mouseleave", () => { tooltip().hidden = true; });
}

function place(node) {
    const tt = tooltip();
    const r = node.getBoundingClientRect();
    const w = tt.offsetWidth, h = tt.offsetHeight;
    let x = r.right + 10, y = r.top;
    if (x + w > window.innerWidth - 8) x = r.left - w - 10;
    if (x < 8) x = Math.min(window.innerWidth - w - 8, r.left);
    if (y + h > window.innerHeight - 8) y = window.innerHeight - h - 8;
    tt.style.left = `${Math.max(8, x)}px`;
    tt.style.top = `${Math.max(8, y)}px`;
}

export function hideTooltip() { tooltip().hidden = true; }
