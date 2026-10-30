// Round recap: after every combat phase, who beat whom, by how much, the biggest hit and notable finishes.
import { el } from "./data.js";

export function renderRecap(msg, myId) {
    const panel = document.getElementById("roundRecap");
    const fights = msg.fights ?? [];
    if (!fights.length) { panel.hidden = true; return; }
    const biggest = fights.filter(f => f.biggestHit).sort((a, b) => b.biggestHit.amount - a.biggestHit.amount)[0];
    panel.replaceChildren(
        el("h3", { text: `Round ${msg.round} recap` }),
        el("ul", { class: "recap-list" }, fights.map(f => {
            const w = f.winner, l = w === -1 ? -1 : 1 - w;
            const mine = f.ids.includes(myId);
            const tags = [
                f.flawless ? el("span", { class: "tag recap-flawless", text: "Flawless" }) : null,
                f.clutch ? el("span", { class: "tag recap-clutch", text: "Clutch" }) : null,
                f === biggest ? el("span", { class: "tag recap-big", text: `Biggest hit ${Math.round(f.biggestHit.amount)}${f.biggestHit.crit ? "!" : ""}` }) : null
            ];
            return el("li", { class: mine ? "mine" : "" },
                w === -1
                    ? el("div", { class: "recap-line" }, el("b", { text: f.names[0] }), el("span", { class: "muted", text: " drew with " }), el("b", { text: f.names[1] }))
                    : el("div", { class: "recap-line" },
                        el("b", { class: "recap-win", text: f.names[w] }), el("span", { class: "muted", text: " beat " }), el("span", { class: "recap-lose", text: f.names[l] }),
                        el("span", { class: "muted small", text: ` · ${Math.round(f.hpLeft * 100)}% HP left · ${f.duration}s` })),
                tags.some(Boolean) ? el("div", { class: "recap-tags" }, tags) : null);
        })));
    panel.hidden = false;
    panel.classList.remove("fresh");
    void panel.offsetWidth;
    panel.classList.add("fresh");
}

export function clearRecap() {
    document.getElementById("roundRecap").hidden = true;
}
