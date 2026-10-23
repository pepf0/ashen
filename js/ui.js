// Small UI helpers: views, toasts, dialogs and the event banner.
import { el, hideTooltip } from "./data.js";

const VIEWS = ["Auth", "Menu", "Compendium", "Leaderboard", "Settings", "Admin", "Lobby", "Game", "Combat", "GameOver"];

export function showView(name) {
    hideTooltip();
    for (const v of VIEWS) document.getElementById(`view${v}`).hidden = v !== name;
    document.body.dataset.view = name;
}

export function currentView() { return document.body.dataset.view; }

export function toast(message, kind = "error") {
    const t = el("div", { class: `toast ${kind}`, text: message });
    document.getElementById("toasts").append(t);
    setTimeout(() => t.classList.add("out"), 3200);
    setTimeout(() => t.remove(), 3700);
}

const dialog = () => document.getElementById("dialog");
let resolveCurrent = null;

/** Closes the open dialog with a value, e.g. from a clickable element inside its body. */
export function resolveDialog(value) { resolveCurrent?.(value); }

/** Opens the shared dialog. buttons: [{label, kind, value}]. Resolves with the clicked value (null when closed). */
export function openDialog(title, body, buttons = [{ label: "OK", value: true, kind: "btn-primary" }]) {
    return new Promise(resolve => {
        const d = dialog();
        document.getElementById("dialogTitle").textContent = title;
        document.getElementById("dialogBody").replaceChildren(body ?? "");
        resolveCurrent?.(null);
        const done = value => {
            if (resolveCurrent !== done) return;
            resolveCurrent = null;
            d.hidden = true;
            hideTooltip();
            resolve(value);
        };
        resolveCurrent = done;
        document.getElementById("dialogButtons").replaceChildren(...buttons.map(b =>
            el("button", { class: `btn ${b.kind ?? ""}`, text: b.label, onclick: () => done(b.value) })));
        d.querySelector("[data-close]").onclick = () => done(null);
        d.onclick = e => { if (e.target === d) done(null); };
        d.hidden = false;
    });
}

export function closeDialog() { resolveDialog(null); }

let announceTimer;
export function announce(text) {
    const a = document.getElementById("announce");
    a.textContent = text;
    a.hidden = false;
    a.classList.remove("show");
    void a.offsetWidth;
    a.classList.add("show");
    clearTimeout(announceTimer);
    announceTimer = setTimeout(() => { a.hidden = true; }, 4500);
}

export function formatTime(seconds) {
    seconds = Math.max(0, Math.ceil(seconds));
    const m = Math.floor(seconds / 60), s = seconds % 60;
    return m > 0 ? `${m}:${String(s).padStart(2, "0")}` : `${s}`;
}
