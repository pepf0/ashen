let tabMenu = document.getElementById("tabMenu");
let tabCompendium = document.getElementById("tabCompendium");
let tabSettings = document.getElementById("tabSettings");
let tabs = [tabMenu, tabCompendium, tabSettings];

let join = document.getElementById("join");
let compendium = document.getElementById("compendium");



tabs.forEach((tab) => tab.addEventListener("click", () => {

    tabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
     if (tab === tabMenu) {
        join.style.display = "block";
        compendium.style.display = "none";
    } else if (tab === tabCompendium) {
        compendium.style.display = "block";
        join.style.display = "none";
    } else if (tab === tabSettings) {
        join.style.display = "none";
        compendium.style.display = "none"
    }
}));

tabMenu.classList.add("active");

let itemsEffects = {};

Promise.all([
    fetch("json/items.json").then(r => r.json()),
    fetch("json/effects.json").then(r => r.json())
]).then(([itemsJson, effectsJson]) => {
    itemsEffects = loadItemsAndEffects(itemsJson, effectsJson);
    renderCompendium();
});

function loadItemsAndEffects(itemsJson, effectsJson) {
    let effects = Object.fromEntries(effectsJson.map(e => [e.id, e]))
    let items = Object.fromEntries(itemsJson.map(i => [i.id, i]))

    return { items, effects };
}

function renderCompendium() {
    let types = ["Weapon", "Buff", "Shirt", "Charm", "Passive"];
    for (let type of types) {
        let categoryTitle = document.createElement("h2");
        categoryTitle.textContent = type;
        let grid = document.createElement("div");
        grid.classList.add("item-grid");
        grid.id = `${type}-grid`;
        compendium.appendChild(categoryTitle);
        for (let item of Object.values(itemsEffects.items)) {
            if (item.type !== type.toLowerCase()) {
                continue;
            }
            let itemCard = document.createElement("div");
            itemCard.classList.add("item-card");
            itemCard.dataset.pack = item.pack
            let itemImg = document.createElement("img");
            itemImg.src = `./img/item/${item.sprite}`;
            itemImg.alt = item.name;
            itemCard.append(itemImg);
            grid.append(itemCard)
        }
        compendium.appendChild(grid);
    }
}