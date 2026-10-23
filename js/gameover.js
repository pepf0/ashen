// End of match: podium, standings and a bit of confetti.
import { el, itemCard } from "./data.js";

const PACK_COLORS = ["#40e0d0", "#e0407a", "#a8ff40", "#6040e0", "#c0c0ff"];

export function renderGameOver(msg, app) {
    const standings = msg.standings;
    const top = standings.filter(s => s.placement <= 3).slice(0, 3);
    // Podium order: 2nd, 1st, 3rd
    const order = [top.find(s => s.placement === 2), top.find(s => s.placement === 1), top.find(s => s.placement === 3)]
        .map((s, i) => s ?? (i === 1 ? top[0] : null));
    document.getElementById("podium").replaceChildren(...order.map((s, i) => {
        const place = ["second", "first", "third"][i];
        if (!s) return el("div", { class: `podium-spot ${place} empty` });
        return el("div", { class: `podium-spot ${place}`, style: `animation-delay: ${[0.5, 1.1, 0.2][i]}s` },
            el("div", { class: "podium-name" }, s.name, s.id === app.user.id ? el("span", { class: "muted", text: " (you)" }) : null),
            el("div", { class: "podium-build" }, s.build.slice(0, 3).map(b => itemCard(b.id, { level: b.level }))),
            el("div", { class: "podium-block" }, el("span", { text: `${s.placement}` })));
    }));

    const tbody = document.querySelector("#standingsTable tbody");
    tbody.replaceChildren(...standings.map(s => el("tr", { class: s.id === app.user.id ? "me" : "" },
        el("td", { text: `${s.placement}` }),
        el("td", {}, el("b", { text: s.name })),
        el("td", {}, el("div", { class: "mini-build" }, s.build.map(b => itemCard(b.id, { level: b.level, size: "small" })))),
        el("td", { text: `${s.wins} / ${s.losses}` }),
        el("td", { text: `${s.lives}` }),
        el("td", { class: "small", text: s.titles.join(", ") || "–" }))));

    const winner = standings.find(s => s.placement === 1);
    document.querySelector(".gameover-title").textContent = winner ? `${winner.name} wins!` : "Match over";
    confetti(document.getElementById("confetti"));
}

function confetti(canvas) {
    const ctx = canvas.getContext("2d");
    const dpr = window.devicePixelRatio || 1;
    const resize = () => {
        canvas.width = canvas.clientWidth * dpr;
        canvas.height = canvas.clientHeight * dpr;
    };
    resize();
    const pieces = Array.from({ length: 140 }, () => ({
        x: Math.random() * canvas.width,
        y: -Math.random() * canvas.height * 0.6,
        size: (4 + Math.random() * 5) * dpr,
        speed: (1.5 + Math.random() * 2.5) * dpr,
        drift: (Math.random() - 0.5) * dpr,
        color: PACK_COLORS[Math.floor(Math.random() * PACK_COLORS.length)]
    }));
    const start = performance.now();
    const step = now => {
        if (canvas.closest(".view")?.hidden) return;
        const age = (now - start) / 1000;
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.globalAlpha = Math.max(0, 1 - Math.max(0, age - 4) / 2);
        for (const p of pieces) {
            p.y += p.speed;
            p.x += p.drift + Math.sin((p.y + p.size) / 30) * 0.5 * dpr;
            // Square "pixels" to match the sprites.
            ctx.fillStyle = p.color;
            ctx.fillRect(Math.round(p.x), Math.round(p.y), p.size, p.size);
        }
        if (age < 6) requestAnimationFrame(step);
        else ctx.clearRect(0, 0, canvas.width, canvas.height);
    };
    requestAnimationFrame(step);
}
