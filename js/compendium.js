// The item compendium and the big item info card.
import { data, el, sprite, statPretty, effectPretty, PACK_NAMES, TYPES } from "./data.js";

const TYPE_TITLES = { weapon: "Weapons", buff: "Buffs", shirt: "Shirts", charm: "Charms", passive: "Passives" };

export function renderCompendium() {
    const root = document.getElementById("compendium");
    root.replaceChildren();
    for (const type of TYPES) {
        const grid = el("div", { class: "item-grid" });
        for (const it of Object.values(data.items)) {
            if (it.type !== type) continue;
            grid.append(el("button", { class: "item-card", dataset: { pack: it.pack }, title: it.name, onclick: () => showItemInfo(it.id) },
                el("img", { src: sprite(it.id), alt: it.name })));
        }
        root.append(el("section", { class: "panel compendium-section" }, el("h2", { text: TYPE_TITLES[type] }), grid));
    }
}

export function showItemInfo(id) {
    const it = data.items[id];
    const modal = document.getElementById("itemModal");
    document.getElementById("itemInfoName").textContent = it.name;
    document.getElementById("itemInfoMeta").textContent = `${PACK_NAMES[it.pack]} ${it.type} · ${it.price}$`;
    const img = document.getElementById("itemInfoImg");
    img.src = sprite(id);
    img.alt = it.name;
    modal.querySelector(".item-card-big").dataset.pack = it.pack;

    document.getElementById("itemInfoStats").replaceChildren(...Object.entries(it.base_stats ?? {}).map(([k, v]) => {
        const [name, value] = statPretty(k, v);
        return el("li", { text: `${name}: ${value}` });
    }));
    const levelStats = Object.entries(it.level_stats ?? {});
    document.getElementById("itemInfoLevelStatsTitle").hidden = levelStats.length === 0;
    document.getElementById("itemInfoLevelStats").replaceChildren(...levelStats.map(([k, v]) => {
        const [name, value] = statPretty(k.replace(/_add$/, ""), Math.abs(v));
        return el("li", { text: `${name}: ${v < 0 ? "-" : "+"}${value}` });
    }));
    document.getElementById("itemInfoEffects").replaceChildren(...(it.effects ?? []).map(eid =>
        el("li", { text: effectPretty(data.effects[eid]) })));
    document.getElementById("itemInfoDescription").textContent = `"${it.description}"`;

    modal.hidden = false;
    const close = () => { modal.hidden = true; };
    modal.querySelector("[data-close]").onclick = close;
    modal.onclick = e => { if (e.target === modal) close(); };
}
