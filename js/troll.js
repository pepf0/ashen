// Admin prank effects. Purely visual and local to the target's browser; nothing about the game changes,
// and nothing shows who sent it. Admins open the panel with F8 in a lobby or match.
import { el, data } from "./data.js";
import { announce, toast } from "./ui.js";
import { addChat } from "./chat.js";

const FUNNY_NAMES = ["Kazoo", "Wet Noodle", "Rubber Duck", "Spoon", "Lost Sock", "Banana", "Pool Noodle", "Toaster", "Damp Towel", "Pet Rock"];

const EFFECTS = [
    { id: "banner", label: "Fake event banner", text: true, placeholder: "ben claimed THE DRAGON!" },
    { id: "toast", label: "Fake system popup", text: true, placeholder: "Server: high latency detected" },
    { id: "chat", label: "Fake system chat line", text: true, placeholder: "Bot Grim is watching you." },
    { id: "reconnect", label: "Fake \"Reconnecting…\" (4s)" },
    { id: "lag", label: "Fake high ping (6s)" },
    { id: "shake", label: "Screen shake" },
    { id: "glitch", label: "Glitch flicker (2s)" },
    { id: "flip", label: "Upside down (4s)" },
    { id: "mirror", label: "Mirrored (5s)" },
    { id: "itemnames", label: "Silly item names (60s)" },
    { id: "miss", label: "Fake \"Miss\" in their fight (6s)" }
];

function bodyClass(cls, ms) {
    document.body.classList.remove(cls);
    void document.body.offsetWidth;
    document.body.classList.add(cls);
    setTimeout(() => document.body.classList.remove(cls), ms);
}

export function createTroll(app) {
    let renamedUntil = 0;

    function applyFx({ effect, text }) {
        switch (effect) {
            case "banner": if (text) announce(text); break;
            case "toast": if (text) toast(text, "info"); break;
            case "chat": if (text) addChat([{ userId: 0, name: "", text, at: Date.now() }]); break;
            case "reconnect":
                document.body.classList.add("offline");
                setTimeout(() => document.body.classList.remove("offline"), 4000);
                break;
            case "lag": {
                const badge = el("div", { class: "fake-ping" }, "Ping ", el("b", { text: "812 ms" }));
                document.body.append(badge);
                let n = 0;
                const iv = setInterval(() => { badge.querySelector("b").textContent = `${600 + Math.floor(Math.random() * 900)} ms`; if (++n > 12) { clearInterval(iv); badge.remove(); } }, 500);
                break;
            }
            case "shake": bodyClass("fx-shake", 1300); break;
            case "glitch": bodyClass("fx-glitch", 2000); break;
            case "flip": bodyClass("fx-flip", 4000); break;
            case "mirror": bodyClass("fx-mirror", 5000); break;
            case "itemnames": renameItems(); break;
            case "miss": fakeMisses(); break;
        }
    }

    function renameItems() {
        const now = Date.now();
        if (renamedUntil > now) { renamedUntil = now + 60000; return; }
        renamedUntil = now + 60000;
        const originals = {};
        for (const it of Object.values(data.items)) {
            originals[it.id] = it.name;
            it.name = FUNNY_NAMES[Math.floor(Math.random() * FUNNY_NAMES.length)];
        }
        rerender();
        const restore = () => {
            if (Date.now() < renamedUntil) return setTimeout(restore, renamedUntil - Date.now());
            for (const [id, name] of Object.entries(originals)) if (data.items[id]) data.items[id].name = name;
            rerender();
        };
        setTimeout(restore, 60000);
    }

    function rerender() {
        if (app.game.state) app.game.render(app.game.state);
    }

    function fakeMisses() {
        let n = 0;
        const iv = setInterval(() => {
            const panel = document.querySelector(".fighter:not(.you) .floats");
            if (panel && document.body.dataset.view === "Combat") {
                const f = el("div", { class: "float dodge", text: "Miss" });
                f.style.left = `${15 + Math.random() * 65}%`;
                panel.append(f);
                setTimeout(() => f.remove(), 1100);
            }
            if (++n >= 8) clearInterval(iv);
        }, 750);
    }

    // ---------------- admin panel ----------------
    let panel = null;

    function togglePanel() {
        if (!app.user?.isAdmin) return;
        if (panel) { panel.remove(); panel = null; return; }
        const members = (app.lobby?.members ?? []).filter(m => !m.bot);
        if (!members.length) { toast("Join a lobby or match first.", "info"); return; }
        const checks = members.map(m => ({ m, box: el("input", { type: "checkbox", checked: m.id !== app.user.id }) }));
        const textInput = el("input", { type: "text", maxlength: "120", placeholder: "Text for banners/popups/chat" });
        const send = effect => {
            const targets = checks.filter(c => c.box.checked).map(c => c.m.id);
            if (!targets.length) return toast("Pick at least one player.", "info");
            const fx = EFFECTS.find(e => e.id === effect);
            const text = textInput.value.trim() || fx.placeholder || "";
            app.socket.send("fx", { effect, targets, text });
        };
        panel = el("div", { class: "troll-panel" },
            el("div", { class: "row spread" }, el("b", { text: "Troll panel" }), el("button", { class: "btn btn-small btn-ghost", text: "×", onclick: togglePanel })),
            el("div", { class: "troll-targets" }, checks.map(c => el("label", { class: "check small" }, c.box, el("span", { text: c.m.name + (c.m.id === app.user.id ? " (you)" : "") })))),
            textInput,
            el("div", { class: "troll-effects" }, EFFECTS.map(e => el("button", { class: "btn btn-small", text: e.label, onclick: () => send(e.id) }))),
            el("p", { class: "muted small", text: "Visual only. Targets never see who sent it. F8 closes." }));
        document.body.append(panel);
    }

    document.addEventListener("keydown", e => {
        if (e.key === "F8") { e.preventDefault(); togglePanel(); }
    });

    return { applyFx, togglePanel };
}
