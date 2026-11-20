"use strict";

const grid = document.getElementById("cardsGrid");
const search = document.getElementById("search");
const type = document.getElementById("supertype");
const sort = document.getElementById("sort");
const count = document.getElementById("cardCount");
const feedback = document.getElementById("galleryMessage");
const refresh = document.getElementById("refresh");
const collator = new Intl.Collator("ru", { sensitivity: "base" });

let entries = Array.from(grid.children);
entries.forEach(attachImageFallback);

function attachImageFallback(card) {
    const image = card.querySelector("img");
    const showPlaceholder = () => {
        const fallback = document.createElement("div");
        fallback.className = "card-placeholder";
        fallback.textContent = "Нет изображения";
        image.replaceWith(fallback);
    };
    image.addEventListener("error", showPlaceholder, { once: true });
    if (image.complete && image.naturalWidth === 0) {
        showPlaceholder();
    }
}

function applyFilters() {
    const query = search.value.trim().toLocaleLowerCase("ru");
    const visible = entries.filter(entry =>
        entry.dataset.title.toLocaleLowerCase("ru").includes(query) &&
        (!type.value || entry.dataset.type === type.value));

    const direction = sort.value === "desc" ? -1 : 1;
    visible.sort((first, second) => direction * collator.compare(first.dataset.title, second.dataset.title));

    grid.replaceChildren(...visible);
    count.textContent = visible.length === 0
        ? "Карточки не найдены."
        : "Показано: " + visible.length + " из " + entries.length;
}

function createCard(card) {
    const element = document.createElement("a");
    element.className = "game-card";
    element.href = "/cards/" + card.id;
    element.dataset.title = card.title;
    element.dataset.type = card.type;

    const image = document.createElement("img");
    image.alt = "";
    image.width = 380;
    image.height = 552;
    image.loading = "lazy";

    const heading = document.createElement("h2");
    heading.textContent = card.title;

    element.append(image, heading);
    image.src = card.image;
    attachImageFallback(element);
    return element;
}

refresh.addEventListener("click", async () => {
    refresh.disabled = true;
    feedback.textContent = "Обновляем каталог…";
    feedback.classList.remove("error-text");

    try {
        const response = await fetch("/api/cards");
        if (!response.ok) {
            throw new Error("HTTP " + response.status);
        }
        const cards = await response.json();
        entries = cards.map(createCard);
        const previousType = type.value;
        const names = [...new Set(cards.map(card => card.type))].sort(collator.compare);
        type.replaceChildren(new Option("Все типы", ""), ...names.map(name => new Option(name, name)));
        if (names.includes(previousType)) {
            type.value = previousType;
        }
        applyFilters();
        feedback.textContent = "Каталог обновлён.";
    } catch {
        feedback.classList.add("error-text");
        feedback.textContent = "Не удалось обновить каталог. Попробуйте ещё раз.";
    } finally {
        refresh.disabled = false;
    }
});

search.addEventListener("input", applyFilters);
type.addEventListener("change", applyFilters);
sort.addEventListener("change", applyFilters);
document.getElementById("reset").addEventListener("click", () => {
    search.value = "";
    type.value = "";
    sort.value = "asc";
    applyFilters();
    search.focus();
});
