// Friends list: requests by username, online status, invites and joining friends' lobbies.
import { api } from "./net.js";
import { el } from "./data.js";
import { toast } from "./ui.js";
import { nameEl } from "./profile.js";

export function createFriends(app) {
    let friends = [];

    async function refresh() {
        if (!app.user) return;
        try { friends = await api("friends"); } catch { return; }
        render();
    }

    async function add(username) {
        try {
            await api("friends/request", { method: "POST", body: { username } });
            toast("Friend request sent.", "info");
            refresh();
        } catch (err) { toast(err.message); }
    }

    async function respond(userId, accept) {
        try { await api("friends/respond", { method: "POST", body: { userId, accept } }); } catch (err) { toast(err.message); }
        refresh();
    }

    async function remove(userId) {
        try { await api(`friends/${userId}`, { method: "DELETE" }); } catch (err) { toast(err.message); }
        refresh();
    }

    function render() {
        const inLobby = !!app.lobby;
        for (const panel of document.querySelectorAll("[data-friends]")) {
            const input = el("input", { type: "text", placeholder: "Username", maxlength: "20" });
            const accepted = friends.filter(f => f.status === "accepted");
            const incoming = friends.filter(f => f.status === "incoming");
            const outgoing = friends.filter(f => f.status === "outgoing");
            const order = { "in lobby": 0, online: 1, "in match": 2, offline: 3 };
            accepted.sort((a, b) => order[a.presence] - order[b.presence]);
            panel.replaceChildren(...[
                el("h3", { text: `Friends (${accepted.filter(f => f.presence !== "offline").length} online)` }),
                el("form", { class: "chat-form", onsubmit: e => { e.preventDefault(); if (input.value.trim()) add(input.value.trim()); input.value = ""; } },
                    input, el("button", { class: "btn btn-small", type: "submit", text: "Add" })),
                incoming.length ? el("ul", { class: "friend-list" }, incoming.map(f => el("li", { class: "request" },
                    el("span", { class: "grow" }, nameEl(f.name, null, f.id), el("span", { class: "muted small", text: " wants to be friends" })),
                    el("button", { class: "btn btn-small", text: "Accept", onclick: () => respond(f.id, true) }),
                    el("button", { class: "btn btn-small btn-ghost", text: "Decline", onclick: () => respond(f.id, false) })))) : null,
                el("ul", { class: "friend-list" }, accepted.length ? accepted.map(f => el("li", { class: f.presence === "offline" ? "offline" : "" },
                    el("span", { class: "dot" + (f.presence !== "offline" ? " on" : "") }),
                    el("span", { class: "grow" }, nameEl(f.name, null, f.id), el("span", { class: "muted small", text: ` ${f.presence}` })),
                    f.joinable && f.joinable !== app.lobby?.code
                        ? el("button", { class: "btn btn-small", text: "Join", onclick: () => app.socket.send("join", { code: f.joinable }) }) : null,
                    inLobby && f.presence !== "offline" && !f.joinable
                        ? el("button", { class: "btn btn-small", text: "Invite", onclick: () => app.socket.send("invite", { userId: f.id }) }) : null,
                    el("button", { class: "btn btn-small btn-ghost", title: "Remove friend", text: "×", onclick: () => remove(f.id) })))
                    : [el("li", { class: "muted small", text: "No friends yet. Add someone by username." })]),
                outgoing.length ? el("p", { class: "muted small", text: `Pending: ${outgoing.map(f => f.name).join(", ")}` }) : null
            ].filter(Boolean));
        }
    }

    /** Toast with a Join button for lobby invites. */
    function invite(msg) {
        const t = el("div", { class: "toast info invite" },
            el("span", { text: `${msg.from} invited you to lobby ${msg.code}` }),
            el("button", { class: "btn btn-small btn-primary", text: "Join", onclick: () => { app.socket.send("join", { code: msg.code }); t.remove(); } }),
            el("button", { class: "btn btn-small btn-ghost", text: "×", onclick: () => t.remove() }));
        document.getElementById("toasts").append(t);
        setTimeout(() => t.remove(), 30000);
    }

    setInterval(() => { if (document.querySelector("[data-friends]:not([hidden])")?.offsetParent) refresh(); }, 10000);
    return { refresh, render, invite };
}
