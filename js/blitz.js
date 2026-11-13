// Blitz mode: instead of a shop, each round shows a loot drop. Cards fly in and flip one after another,
// specials get a golden beam, upgrades say LEVEL UP, then a big FIGHT button (or the timer) starts the round.
import { el, data, itemCard } from "./data.js";
import { sfx } from "./fx.js";

let shownKey = null;

export function renderBlitz(state, app) {
    const box = document.getElementById("blitzLoot");
    const you = state.you;
    const show = !!state.settings?.blitz && state.phase === "shop" && you && !you.eliminated;
    document.body.classList.toggle("blitz-match", !!state.settings?.blitz);
    if (!show) { box.hidden = true; shownKey = null; return; }
    const key = `${state.round}|${(you.blitzLoot ?? []).map(l => l.id + l.level).join(",")}|${you.blitzHp}`;
    const timer = box.querySelector(".bl-timer b");
    if (timer) timer.textContent = Math.max(0, Math.ceil(state.phaseRemaining ?? 0));
    const btn = box.querySelector(".bl-fight");
    if (btn) btn.classList.toggle("on", !!you.ready);
    if (shownKey === key) return;
    shownKey = key;
    const loot = you.blitzLoot ?? [];
    const cards = loot.map((l, k) => {
        const it = data.items[l.id];
        const special = it?.pack && it.pack !== "generic";
        return el("div", { class: "bl-card" + (special ? " special" : "") + (l.slot === "up" ? " up" : ""), style: `--k:${k}` },
            el("div", { class: "bl-inner" },
                el("div", { class: "bl-back", text: "?" }),
                el("div", { class: "bl-front" },
                    el("div", { class: "bl-badge", text: l.slot === "up" ? `LEVEL ${l.level}!` : special ? "LEGENDARY!" : "NEW!" }),
                    itemCard(l.id, { level: l.level, size: "large", skin: you.appearance?.weaponSkin }),
                    el("div", { class: "bl-name", text: it?.name ?? l.id }))));
    });
    if (you.blitzHp > 0) cards.push(el("div", { class: "bl-card hp", style: `--k:${cards.length}` },
        el("div", { class: "bl-inner" },
            el("div", { class: "bl-back", text: "?" }),
            el("div", { class: "bl-front" }, el("div", { class: "bl-badge", text: "MORE HP!" }),
                el("div", { class: "bl-heart", text: "❤" }), el("div", { class: "bl-name", text: `+${you.blitzHp} HP upgrade${you.blitzHp > 1 ? "s" : ""}` })))));
    if (!cards.length) cards.push(el("div", { class: "bl-empty", text: "Your build is maxed out. Go get them!" }));
    box.replaceChildren(
        el("div", { class: "bl-rays", "aria-hidden": "true" }),
        el("div", { class: "bl-head" },
            el("div", { class: "bl-round", text: `ROUND ${state.round}` }),
            el("div", { class: "bl-title", text: "LOOT DROP!" })),
        el("div", { class: "bl-cards" }, cards),
        el("div", { class: "bl-foot" },
            el("div", { class: "bl-timer" }, "Next fight in ", el("b", { text: Math.ceil(state.phaseRemaining ?? 0) }), "s"),
            el("button", { class: "bl-fight" + (you.ready ? " on" : ""), text: "⚔ FIGHT! ⚔", onclick: () => { if (!state.you.ready) { sfx("ready"); app.socket.send("ready", { ready: true }); } } })),
        el("div", { class: "bl-hint", text: `Max HP ${Math.round(you.maxHp)} · Lives ${you.lives} · Press SPACE to fight` }));
    box.hidden = false;
    sfx("announce");
    cards.forEach((c, k) => setTimeout(() => sfx(c.classList.contains("special") ? "encore" : c.classList.contains("up") ? "levelup" : "buy"), 450 + k * 380));
}
