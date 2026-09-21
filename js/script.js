let tabMenu = document.getElementById("tabMenu");
let tabCompendium = document.getElementById("tabCompendium");
let tabSettings = document.getElementById("tabSettings");

let join = document.getElementById("join");
let tabs = [tabMenu, tabCompendium, tabSettings];



tabs.forEach((tab) => tab.addEventListener("click", () => {

    tabs.forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");
     if (tab === tabMenu) {
        join.style.display = "block";

    } else if (tab === tabCompendium) {
        join.style.display = "none";
    } else if (tab === tabSettings) {
        join.style.display = "none";
    }
}));

tabMenu.classList.add("active");