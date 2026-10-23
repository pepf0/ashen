// Account keybinds: loaded from and saved to the server, shown next to every bound button.
import { api } from "./net.js";
import { el } from "./data.js";

export const ACTIONS = [
    { id: "skill_q", label: "Skill 1 (Q)", group: "Fight" },
    { id: "skill_w", label: "Skill 2 (W)", group: "Fight" },
    { id: "skill_e", label: "Skill 3 (E)", group: "Fight" },
    { id: "skill_ult", label: "Ultimate (R)", group: "Fight" },
    { id: "reroll", label: "Reroll", group: "Shop" },
    { id: "ready", label: "Ready", group: "Shop" },
    { id: "hp", label: "Buy +HP", group: "Shop" },
    ...[1, 2, 3, 4, 5, 6].map(n => ({ id: `buy${n}`, label: `Buy shop slot ${n}`, group: "Shop" }))
];
export const SKILL_ACTIONS = ["skill_q", "skill_w", "skill_e", "skill_ult"];

const DEFAULTS = {
    skill_q: "q", skill_w: "w", skill_e: "e", skill_ult: "r",
    reroll: "d", ready: " ", hp: "h",
    buy1: "1", buy2: "2", buy3: "3", buy4: "4", buy5: "5", buy6: "6"
};

let binds = { ...DEFAULTS };
const listeners = new Set();

export function onKeybindsChanged(fn) { listeners.add(fn); }
function changed() { listeners.forEach(fn => fn()); }

export async function loadKeybinds() {
    try { binds = { ...DEFAULTS, ...(await api("me/keybinds")) }; } catch { binds = { ...DEFAULTS }; }
    changed();
}

export function keyOf(action) { return binds[action]; }

/** Display text for a key, e.g. "Space", "Q", "F1". */
export function keyLabel(key) {
    if (key == null) return "";
    if (key === " ") return "Space";
    if (key.length === 1) return key.toUpperCase();
    return key.replace(/^arrow/, "").replace(/^./, c => c.toUpperCase());
}

export const label = action => keyLabel(binds[action]);

/** The action bound to a key event within the given actions, or null. */
export function actionFor(event, actions) {
    const key = event.key.toLowerCase();
    return actions.find(a => binds[a] === key) ?? null;
}

async function save() {
    try { binds = { ...DEFAULTS, ...(await api("me/keybinds", { method: "PUT", body: binds })) }; } catch { /* keep local */ }
    changed();
}

/** Rebinds an action. A key already used by another action is swapped onto it. */
export async function rebind(action, key) {
    key = key.toLowerCase();
    const other = Object.keys(binds).find(a => a !== action && binds[a] === key);
    if (other) binds[other] = binds[action];
    binds[action] = key;
    await save();
}

export async function resetKeybinds() {
    binds = { ...DEFAULTS };
    await save();
}

/** Settings panel: one row per action; click the key, then press a new one (Esc cancels). */
export function renderKeybindSettings(root) {
    let waiting = null;
    const draw = () => {
        const groups = [...new Set(ACTIONS.map(a => a.group))];
        root.replaceChildren(...groups.map(g => el("div", { class: "kb-group" },
            el("h4", { text: g }),
            ...ACTIONS.filter(a => a.group === g).map(a => el("div", { class: "kb-row" },
                el("span", { class: "grow", text: a.label }),
                el("button", {
                    class: "btn btn-small kb-key" + (waiting === a.id ? " waiting" : ""),
                    text: waiting === a.id ? "Press a key…" : label(a.id),
                    onclick: () => { waiting = a.id; draw(); }
                }))))),
            el("button", { class: "btn btn-ghost btn-small", text: "Reset to defaults", onclick: () => resetKeybinds() }));
    };
    window.addEventListener("keydown", async e => {
        if (!waiting) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        const action = waiting;
        waiting = null;
        if (e.key !== "Escape" && !["Shift", "Control", "Alt", "Meta", "Tab"].includes(e.key)) await rebind(action, e.key);
        draw();
    }, true);
    onKeybindsChanged(draw);
    draw();
}
