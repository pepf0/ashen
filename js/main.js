// Entry point: login, navigation and routing of server messages to the views.
import { loadData, el } from "./data.js";
import { api, getToken, setToken, GameSocket } from "./net.js";
import { showView, currentView, toast, announce, openDialog, closeDialog } from "./ui.js";
import { renderCompendium } from "./compendium.js";
import { initChat, addChat, clearChat } from "./chat.js";
import { createLobbyView } from "./lobby.js";
import { createGameView } from "./game.js";
import { createCombatView } from "./combat.js";
import { renderGameOver } from "./gameover.js";
import { loadKeybinds, renderKeybindSettings, onKeybindsChanged } from "./keybinds.js";
import { openProfile, renderLocker, nameEl, loadAchievements } from "./profile.js";
import { createFriends } from "./friends.js";
import { createAdmin } from "./admin.js";
import { createTroll } from "./troll.js";
import { startEmbers, sfx, effectsLevel, setEffectsLevel, getVolume, setVolume } from "./fx.js";
import { preloadSkins } from "./skins.js";
import { startDemo } from "./demo.js";

// Relative URLs (api/, ws, json/) need the page URL to end in "/" when hosted under a sub-path.
if (!location.pathname.endsWith("/") && !location.pathname.endsWith(".html")) {
    location.replace(location.pathname + "/" + location.search + location.hash);
}

const app = {
    user: null,
    lobby: null,
    /** What the Play tab shows: "menu", "lobby", "match" or "gameover". */
    mode: "menu",
    socket: new GameSocket()
};
app.lobbyView = createLobbyView(app);
app.game = createGameView(app);
app.combat = createCombatView(app);
app.showGame = () => { if (app.mode === "match") showView("Game"); };
app.friends = createFriends(app);
app.admin = createAdmin(app);
app.troll = createTroll(app);

const tabs = [...document.querySelectorAll("#tabs .tab")];
let activeTab = "menu";

function setTab(name) {
    activeTab = name;
    tabs.forEach(t => t.classList.toggle("active", t.dataset.tab === name));
    if (name === "menu") return showPlay();
    if (name === "compendium") showView("Compendium");
    if (name === "leaderboard") { showView("Leaderboard"); loadLeaderboard(); }
    if (name === "settings") { showView("Settings"); fillSettings(); renderLocker(document.getElementById("lockerPanel")); }
    if (name === "admin") { showView("Admin"); app.admin.render(); }
}

tabs.forEach(t => t.addEventListener("click", () => setTab(t.dataset.tab)));

function setMode(mode) {
    app.mode = mode;
    document.body.dataset.mode = mode;
    if (mode === "match") activeTab = "menu";
    if (activeTab === "menu" || mode === "match") showPlay();
}

function showPlay() {
    tabs.forEach(t => t.classList.toggle("active", t.dataset.tab === "menu"));
    if (app.mode === "match") {
        if (!(currentView() === "Combat" && app.combat.active)) showView("Game");
    } else if (app.mode === "gameover") showView("GameOver");
    else if (app.mode === "lobby") showView("Lobby");
    else { showView("Menu"); refreshLobbies(); }
    app.friends.refresh();
}

// ---------------- auth ----------------

let registering = false;
function renderAuthMode() {
    document.getElementById("authTitle").textContent = registering ? "Create an account" : "Log in";
    document.getElementById("authSubmit").textContent = registering ? "Register" : "Log in";
    document.getElementById("authSwitchText").textContent = registering ? "Already have an account?" : "No account yet?";
    document.getElementById("authSwitch").textContent = registering ? "Log in" : "Register";
    document.getElementById("authPassword").autocomplete = registering ? "new-password" : "current-password";
    document.getElementById("authError").textContent = "";
}

document.getElementById("authSwitch").addEventListener("click", e => {
    e.preventDefault();
    registering = !registering;
    renderAuthMode();
});

document.getElementById("authForm").addEventListener("submit", async e => {
    e.preventDefault();
    const username = document.getElementById("authUsername").value.trim();
    const password = document.getElementById("authPassword").value;
    try {
        const res = await api(registering ? "auth/register" : "auth/login", { method: "POST", body: { username, password } });
        setToken(res.token);
        document.getElementById("authPassword").value = "";
        loggedIn(res.user);
    } catch (err) {
        document.getElementById("authError").textContent = err.message;
    }
});

function showAuth() {
    app.user = null;
    document.body.dataset.auth = "out";
    renderAuthMode();
    showView("Auth");
}

function loggedIn(user) {
    app.user = user;
    document.body.dataset.auth = "in";
    renderUserChip();
    setMode("menu");
    setTab("menu");
    loadKeybinds();
    app.socket.connect();
    api("me").then(me => { app.user = { ...app.user, isAdmin: me.isAdmin }; renderUserChip(); }).catch(() => {});
}

function renderUserChip() {
    document.getElementById("adminTab").hidden = !app.user?.isAdmin;
    document.getElementById("userChip").textContent = app.user ? app.user.displayName : "";
}

// ---------------- socket ----------------

app.socket
    .on("hello", msg => { app.user = { ...msg.user, isAdmin: app.user?.isAdmin }; renderUserChip(); })
    .on("authError", () => { app.socket.disconnect(); setToken(null); showAuth(); })
    .on("replaced", () => toast("Ashen was opened in another tab.", "info"))
    .on("close", () => document.body.classList.add("offline"))
    .on("open", () => document.body.classList.remove("offline"))
    .on("error", msg => toast(msg.message))
    .on("chat", msg => addChat(msg.messages))
    .on("announce", msg => {
        sfx("announce");
        announce(msg.text ? msg.text : `${msg.player} claimed ${msg.name}${msg.reward && msg.reward !== "Title" ? ` (${msg.reward})` : ""}`);
    })
    .on("kicked", () => toast("You were kicked from the lobby.", "info"))
    .on("left", () => {
        app.lobby = null;
        clearChat();
        if (app.mode !== "gameover") setMode("menu");
    })
    .on("lobby", msg => {
        const lobby = msg.lobby;
        if (!lobby) {
            app.lobby = null;
            clearChat();
            if (app.mode !== "gameover") setMode("menu");
            return;
        }
        if (app.lobby?.code !== lobby.code) clearChat();
        app.lobby = lobby;
        app.friends.render();
        app.lobbyView.render(lobby);
        if (lobby.inMatch) {
            if (app.mode !== "match") setMode("match");
        } else if (app.mode === "match" || app.mode === "menu") {
            setMode("lobby");
        }
    })
    .on("state", msg => {
        app.game.render(msg.state);
        app.combat.onState(msg.state);
        if (app.mode !== "match") setMode("match");
        if (msg.state.phase === "shop" && currentView() === "Combat") {
            app.combat.stop();
            showView("Game");
        }
    })
    .on("fightStart", msg => {
        if (app.mode !== "match") setMode("match");
        showView("Combat");
        app.combat.start(msg);
    })
    .on("fightTick", msg => app.combat.tick(msg))
    .on("fightEnd", msg => app.combat.end(msg))
    .on("progress", msg => showProgress(msg.progress))
    .on("friends", msg => { toast(msg.message, "info"); app.friends.refresh(); })
    .on("invite", msg => app.friends.invite(msg))
    .on("info", msg => toast(msg.message, "info"))
    .on("fx", msg => app.troll.applyFx(msg))
    .on("gameOver", msg => {
        app.combat.stop();
        renderGameOver(msg, app);
        setMode("gameover");
        setTab("menu");
    });

initChat(text => app.socket.send("chat", { text }));
renderKeybindSettings(document.getElementById("keybindSettings"));
onKeybindsChanged(() => { if (app.lobby && !app.lobby.inMatch) app.lobbyView.render(app.lobby); });

document.getElementById("backToLobby").addEventListener("click", () => setMode(app.lobby ? "lobby" : "menu"));
document.getElementById("userChip").addEventListener("click", () => { if (app.user) openProfile(app.user.id); });

async function showProgress(p) {
    const panel = document.getElementById("progressPanel");
    const ach = await loadAchievements();
    const cosmetic = id => id.replace(/^(name|frame|victory|nb|pb|ws|t)_/, "").replace(/_/g, " ");
    const pct = p.nextLevelXp ? Math.min(100, (p.levelXp / p.nextLevelXp) * 100) : 100;
    panel.replaceChildren(
        el("div", { class: "row spread" }, el("b", { text: `+${p.xpGained} XP` }),
            el("span", { text: p.levelsGained > 0 ? `Level up! Level ${p.level}` : `Level ${p.level}` })),
        el("div", { class: "xp-bar" }, el("div", { class: "xp-fill grow-in", style: `width:${pct}%` }),
            el("span", { text: p.nextLevelXp ? `${p.levelXp} / ${p.nextLevelXp} XP` : "Max level" })),
        p.newAchievements.length ? el("div", { class: "new-unlocks" }, el("span", { class: "muted small", text: "Achievements: " }),
            ...p.newAchievements.map(id => el("span", { class: "unlock-chip", text: ach.list.find(a => a.id === id)?.name ?? id }))) : null,
        p.newCosmetics.length ? el("div", { class: "new-unlocks" }, el("span", { class: "muted small", text: "New cosmetics: " }),
            ...p.newCosmetics.map(id => el("span", { class: "unlock-chip cosmetic", text: cosmetic(id) }))) : null);
    panel.hidden = false;
    if (p.levelsGained > 0) setTimeout(() => sfx("levelup"), 800);
}

// ---------------- menu ----------------

document.getElementById("createLobby").addEventListener("click", () => app.socket.send("create"));
document.getElementById("soloStart").addEventListener("click", () => app.socket.send("solo", {
    bots: Number(document.getElementById("soloBots").value),
    difficulty: document.getElementById("soloDifficulty").value
}));
document.getElementById("joinForm").addEventListener("submit", e => {
    e.preventDefault();
    const code = document.getElementById("joinCode").value.trim().toUpperCase();
    if (code.length === 5) app.socket.send("join", { code });
    else toast("Lobby codes have 5 letters.");
});
document.getElementById("joinCode").addEventListener("input", e => { e.target.value = e.target.value.toUpperCase(); });
document.getElementById("refreshLobbies").addEventListener("click", refreshLobbies);

async function refreshLobbies() {
    const list = document.getElementById("lobbyList");
    try {
        const lobbies = await api("lobbies");
        list.replaceChildren(...(lobbies.length ? lobbies.map(l => el("li", {},
            el("span", { class: "code", text: l.code }),
            el("span", { class: "grow", text: `${l.host ?? "?"}'s lobby` }),
            el("span", { class: "muted", text: `${l.players}/${l.maxPlayers}` }),
            el("button", { class: "btn btn-small", text: "Join", onclick: () => app.socket.send("join", { code: l.code }) })))
            : [el("li", { class: "muted", text: "No public lobbies right now. Create one!" })]));
    } catch {
        list.replaceChildren(el("li", { class: "muted", text: "Couldn't load lobbies." }));
    }
}
setInterval(() => { if (currentView() === "Menu") refreshLobbies(); }, 5000);

// ---------------- leaderboard ----------------

document.getElementById("onlyMine").addEventListener("change", loadLeaderboard);

async function loadLeaderboard() {
    try {
        const onlyMine = document.getElementById("onlyMine").checked;
        const [board, matches] = await Promise.all([
            api("leaderboard"),
            api(`matches?limit=30${onlyMine && app.user ? `&user=${app.user.id}` : ""}`)
        ]);
        document.querySelector("#leaderboardTable tbody").replaceChildren(...(board.length ? board.map((r, i) =>
            el("tr", { class: r.id === app.user?.id ? "me" : "" },
                el("td", { text: `${i + 1}` }), el("td", {}, nameEl(r.name, null, r.id), r.mastery ? el("div", { class: "mastery", text: r.mastery }) : null),
                el("td", { text: `${r.wins}` }), el("td", { text: `${r.games}` }),
                el("td", { text: `${Math.round(r.winRate * 100)}%` }), el("td", { text: `${r.top3}` }),
                el("td", { text: `${r.avgPlacement}` })))
            : [el("tr", {}, el("td", { colspan: "7", class: "muted", text: "No matches played yet." }))]));
        document.getElementById("matchList").replaceChildren(...(matches.length ? matches.map(m =>
            el("li", { onclick: () => showMatch(m.id) },
                el("div", { class: "row spread" }, el("b", { text: m.winners ? `${m.winners} won` : "Match" }),
                    el("span", { class: "muted small", text: new Date(m.endedAt).toLocaleString() })),
                el("div", { class: "muted small", text: `${m.players} players · ${m.rounds} rounds · lobby ${m.lobby}` })))
            : [el("li", { class: "muted", text: "No matches yet." })]));
    } catch (err) {
        toast(err.message);
    }
}

async function watchReplay(id) {
    try {
        const data = await api(`replays/${id}`);
        closeDialog();
        const back = currentView();
        showView("Combat");
        app.combat.playReplay(data, () => showView(back === "Combat" ? "Leaderboard" : back));
    } catch (err) {
        toast(err.message);
    }
}

async function showMatch(id) {
    const { itemCard } = await import("./data.js");
    try {
        const m = await api(`matches/${id}`);
        const rounds = [...new Set(m.match.fights.map(f => f.round))];
        const fightsView = m.match.fights.length ? el("div", { class: "round-list" }, rounds.map(r => el("div", { class: "round-row" },
            el("b", { text: `Round ${r}` }),
            el("div", { class: "round-fights" }, m.match.fights.filter(f => f.round === r).map(f => el("button", {
                class: "btn btn-small fight-chip", title: "Watch replay", onclick: () => watchReplay(f.id)
            }, el("span", { class: f.winner === 0 ? "won" : "" , text: f.names[0] }), " vs ",
                el("span", { class: f.winner === 1 ? "won" : "", text: f.names[1] }), el("span", { class: "muted", text: ` · ${f.duration.toFixed(0)}s ▶` }))))))) : el("p", { class: "muted small", text: "No replays stored for this match." });
        await openDialog(`Match in lobby ${m.match.lobby}`, el("div", {}, el("table", { class: "table" },
            el("thead", {}, el("tr", {}, ["#", "Player", "Build", "W / L", "Titles"].map(h => el("th", { text: h })))),
            el("tbody", {}, m.players.map(p => el("tr", {},
                el("td", { text: `${p.placement}` }), el("td", {}, el("b", { text: p.name })),
                el("td", {}, el("div", { class: "mini-build" }, p.build.map(b => itemCard(b.id, { level: b.level, size: "small" })))),
                el("td", { text: `${p.wins} / ${p.losses}` }),
                el("td", { class: "small", text: p.titles.join(", ") || "–" }))))), el("h4", { text: "Fights (click to watch)" }), fightsView));
    } catch (err) {
        toast(err.message);
    }
}

// ---------------- settings ----------------

const fxLevel = document.getElementById("fxLevel"), fxVolume = document.getElementById("fxVolume");
fxLevel.value = String(effectsLevel());
fxVolume.value = String(getVolume());
fxLevel.addEventListener("change", () => setEffectsLevel(Number(fxLevel.value)));
fxVolume.addEventListener("input", () => setVolume(Number(fxVolume.value)));
fxVolume.addEventListener("change", () => sfx("coin"));

// Soft click on every button: tiny, but it makes the UI feel alive.
document.addEventListener("click", e => { if (e.target.closest(".btn, .tab, .ab, .kit-card.clickable, .locker-option")) sfx("click", 0.03); });

function fillSettings() {
    document.getElementById("displayName").value = app.user?.displayName ?? "";
    document.getElementById("settingsUsername").textContent = app.user?.username ?? "";
    document.getElementById("nameNote").textContent = "";
    document.getElementById("passwordNote").textContent = "";
}

document.getElementById("nameForm").addEventListener("submit", async e => {
    e.preventDefault();
    const note = document.getElementById("nameNote");
    try {
        app.user = await api("me", { method: "PUT", body: { displayName: document.getElementById("displayName").value } });
        renderUserChip();
        note.textContent = "Saved.";
    } catch (err) {
        note.textContent = err.message;
    }
});

document.getElementById("passwordForm").addEventListener("submit", async e => {
    e.preventDefault();
    const note = document.getElementById("passwordNote");
    try {
        await api("me/password", { method: "PUT", body: {
            oldPassword: document.getElementById("oldPassword").value,
            newPassword: document.getElementById("newPassword").value
        } });
        document.getElementById("oldPassword").value = "";
        document.getElementById("newPassword").value = "";
        note.textContent = "Password changed.";
    } catch (err) {
        note.textContent = err.message;
    }
});

document.getElementById("logoutButton").addEventListener("click", async () => {
    try { await api("auth/logout", { method: "POST" }); } catch { /* already gone */ }
    app.socket.disconnect();
    setToken(null);
    app.lobby = null;
    setMode("menu");
    showAuth();
});

// ---------------- start ----------------

(async function start() {
    await loadData();
    startEmbers();
    preloadSkins();
    startDemo(document.getElementById("heroStage"));
    renderCompendium();
    if (!getToken()) return showAuth();
    try {
        loggedIn(await api("me"));
    } catch (err) {
        if (err.status === 401) setToken(null);
        showAuth();
    }
})();
