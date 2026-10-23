// Lobby + match chat. The same messages are rendered into every [data-chat] panel.
import { el } from "./data.js";

let messages = [];
let sendFn = () => false;

export function initChat(send) {
    sendFn = send;
    for (const panel of document.querySelectorAll("[data-chat]")) {
        const log = el("ul", { class: "chat-log" });
        const input = el("input", { type: "text", maxlength: "200", placeholder: "Say something" });
        const form = el("form", { class: "chat-form", onsubmit: e => {
            e.preventDefault();
            const text = input.value.trim();
            if (text && sendFn(text)) input.value = "";
        } }, input, el("button", { class: "btn btn-small", type: "submit", text: "Send" }));
        panel.replaceChildren(el("h3", { text: "Chat" }), log, form);
    }
}

export function clearChat() {
    messages = [];
    render();
}

export function addChat(list) {
    messages.push(...list);
    if (messages.length > 50) messages = messages.slice(-50);
    render();
}

function render() {
    for (const log of document.querySelectorAll(".chat-log")) {
        const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 30;
        log.replaceChildren(...messages.map(m => m.userId === 0
            ? el("li", { class: "system", text: m.text })
            : el("li", {}, el("b", { text: m.name }), " ", m.text)));
        if (atBottom) log.scrollTop = log.scrollHeight;
    }
}
