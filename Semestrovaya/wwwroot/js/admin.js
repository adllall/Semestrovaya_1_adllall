"use strict";

const csrfToken = document.querySelector('meta[name="csrf-token"]').content;
const statusMessage = document.getElementById("adminStatus");
const editor = document.getElementById("descriptionEditor");
const counter = document.getElementById("descriptionCount");
const imagePattern = /^\/images\/[a-zA-Z0-9_-]+\.(jpg|jpeg|png|webp)$/i;
const typePattern = /^[\p{L}\p{N} ,()\/\-–—]+$/u;
const formatPattern = /\[(b|i)\]([\s\S]*?)\[\/\1\]/g;
const inputs = {
    cards: {
        form: document.getElementById("cardForm"),
        heading: document.getElementById("cardFormTitle"),
        list: document.getElementById("cardsList"),
        createTitle: "Новая карточка",
        editTitle: "Редактирование карточки"
    },
    categories: {
        form: document.getElementById("categoryForm"),
        heading: document.getElementById("categoryFormTitle"),
        list: document.getElementById("categoriesList"),
        createTitle: "Новая категория",
        editTitle: "Редактирование категории"
    }
};

function renderDescription(value) {
    editor.replaceChildren();
    let offset = 0;
    for (const match of value.matchAll(formatPattern)) {
        editor.append(document.createTextNode(value.slice(offset, match.index)));
        const styled = document.createElement(match[1] === "b" ? "strong" : "em");
        styled.textContent = match[2];
        editor.append(styled);
        offset = match.index + match[0].length;
    }
    editor.append(document.createTextNode(value.slice(offset)));
    updateCounter();
}

function readDescription() {
    const runs = [];
    function append(text, style) {
        if (!text) {
            return;
        }
        const last = runs.at(-1);
        if (last?.style === style) {
            last.text += text;
        } else {
            runs.push({ style, text });
        }
    }
    function visit(node, style = "") {
        if (node.nodeType === Node.TEXT_NODE) {
            append(node.textContent.replace(/\u00a0/g, " "), style);
            return;
        }
        if (node.nodeName === "BR") {
            if (node.parentNode.childNodes.length > 1) {
                append("\n", "");
            }
            return;
        }
        if (node.nodeType !== Node.ELEMENT_NODE) {
            return;
        }

        const block = node.nodeName === "DIV" && node !== editor || node.nodeName === "P";
        if (block && runs.length && !runs.at(-1).text.endsWith("\n")) {
            append("\n", "");
        }
        const nextStyle = node.dataset.style ?? (
            ["B", "STRONG"].includes(node.nodeName) ? "b" :
            ["I", "EM"].includes(node.nodeName) ? "i" : style);
        node.childNodes.forEach(child => visit(child, nextStyle));
        if (block && node.nextSibling) {
            append("\n", "");
        }
    }
    visit(editor);
    return runs.map(run => run.style ? "[" + run.style + "]" + run.text + "[/" + run.style + "]" : run.text)
        .join("").trim();
}

function updateCounter() {
    counter.textContent = readDescription().length + " / 2000";
}

function editorSelection() {
    const selection = window.getSelection();
    return selection.rangeCount && editor.contains(selection.anchorNode) && editor.contains(selection.focusNode)
        ? selection : null;
}

function replaceSelection(selection, content, selectContent = false) {
    const range = selection.getRangeAt(0);
    range.deleteContents();
    range.insertNode(content);
    if (selectContent) {
        range.selectNodeContents(content);
    } else {
        range.setStartAfter(content);
        range.collapse(true);
    }
    selection.removeAllRanges();
    selection.addRange(range);
    updateCounter();
}

editor.addEventListener("input", updateCounter);
editor.addEventListener("paste", event => {
    event.preventDefault();
    const selection = editorSelection();
    if (selection) {
        replaceSelection(selection, document.createTextNode(event.clipboardData.getData("text/plain")));
    }
});
editor.addEventListener("drop", event => event.preventDefault());
document.querySelectorAll("[data-format]").forEach(button => {
    button.addEventListener("mousedown", event => event.preventDefault());
    button.addEventListener("click", () => {
        const selection = editorSelection();
        if (!selection || selection.isCollapsed) {
            editor.focus();
            return;
        }
        const style = button.dataset.format;
        const formatted = document.createElement(style === "b" ? "strong" : "em");
        formatted.dataset.style = style;
        formatted.textContent = selection.toString();
        replaceSelection(selection, formatted, true);
    });
});

async function request(kind, method = "GET", id = null, data = null) {
    const headers = { "X-CSRF-Token": csrfToken };
    if (data !== null) {
        headers["Content-Type"] = "application/json";
    }
    let response;
    try {
        response = await fetch("/api/" + kind + (id === null ? "" : "/" + id), {
            method,
            headers,
            body: data === null ? null : JSON.stringify(data)
        });
    } catch {
        throw new Error("Связь с сервером прервалась. Проверьте список перед повтором.");
    }
    if (response.ok) {
        return response.json();
    }
    const error = await response.json().catch(() => ({}));
    const message = response.status === 401 ? "Сеанс завершился. Войдите снова." : error.message;
    throw Object.assign(new Error(message || "Запрос не выполнен."), { field: error.field });
}

function clearErrors(form) {
    form.querySelectorAll("[data-error], [data-form-message]").forEach(element => { element.textContent = ""; });
    form.querySelectorAll("[aria-invalid]").forEach(element => element.removeAttribute("aria-invalid"));
}

function showError(form, error) {
    const field = Array.from(form.querySelectorAll("[data-error]")).find(element => element.dataset.error === error.field);
    if (field) {
        field.textContent = error.message;
        const control = error.field === "description" && form === inputs.cards.form ? editor : form.elements.namedItem(error.field);
        control?.setAttribute("aria-invalid", "true");
        control?.focus();
    } else {
        const message = form.querySelector("[data-form-message]");
        message.className = "message error-text";
        message.textContent = error.message;
    }
}

function invalid(field, message) {
    throw Object.assign(new Error(message), { field });
}

function cardValues(form) {
    const title = form.elements.title.value.trim();
    const description = readDescription();
    const imageUrl = form.elements.imageUrl.value.trim();
    const cardType = form.elements.cardType.value.trim();
    const categoryId = Number(form.elements.categoryId.value);

    if (title.length < 2 || title.length > 200) {
        invalid("title", "Название должно содержать от 2 до 200 символов.");
    }
    if (!description.replace(formatPattern, "$2").trim() || description.length > 2000) {
        invalid("description", "Описание должно содержать от 1 до 2000 символов.");
    }
    if (imageUrl.length > 255 || !imageUrl.startsWith("/images/") || !imagePattern.test(imageUrl)) {
        invalid("imageUrl", "Укажите путь к изображению вида /images/card.jpg.");
    }
    if (!cardType || cardType.length > 100 || !typePattern.test(cardType)) {
        invalid("cardType", "Проверьте тип карточки.");
    }
    if (!Number.isSafeInteger(categoryId) || categoryId <= 0) {
        invalid("categoryId", "Выберите категорию.");
    }
    return { title, description, imageUrl, cardType, categoryId };
}

function categoryValues(form) {
    const name = form.elements.name.value.trim();
    const description = form.elements.description.value.trim();
    const sortOrder = form.elements.sortOrder.valueAsNumber;
    if (name.length < 2 || name.length > 100) {
        invalid("name", "Название должно содержать от 2 до 100 символов.");
    }
    if (!description || description.length > 1000) {
        invalid("description", "Описание должно содержать от 1 до 1000 символов.");
    }
    if (!Number.isInteger(sortOrder) || sortOrder < 0 || sortOrder > 9999) {
        invalid("sortOrder", "Укажите целое число от 0 до 9999.");
    }
    return { name, description, sortOrder };
}

function changeForm(kind, item) {
    const { form, heading, createTitle, editTitle } = inputs[kind];
    form.reset();
    clearErrors(form);
    delete form.dataset.editId;
    form.querySelector("[data-reset]").hidden = !item;
    heading.textContent = item ? editTitle : createTitle;

    if (kind === "cards") {
        inputs.cards.form.elements.categoryId.value = item?.categoryId ?? "";
        form.elements.title.value = item?.title ?? "";
        form.elements.cardType.value = item?.type ?? "";
        form.elements.imageUrl.value = item?.image ?? "";
        renderDescription(item?.description ?? "");
    } else if (item) {
        form.elements.name.value = item.name;
        form.elements.description.value = item.description;
        form.elements.sortOrder.value = item.sortOrder;
    }

    if (item) {
        form.querySelector("input").focus();
    }
}

function setStatus(message, error = false) {
    statusMessage.classList.toggle("error-text", error);
    statusMessage.textContent = message;
}

function createEntry(kind, item) {
    const row = document.createElement("li");
    row.className = "panel entry";

    const title = document.createElement(kind === "cards" ? "a" : "strong");
    title.textContent = item.title ?? item.name;
    if (kind === "cards") {
        title.href = "/cards/" + item.id;
    }

    const buttons = document.createElement("div");
    buttons.className = "actions";

    const edit = document.createElement("button");
    edit.type = "button";
    edit.textContent = "Редактировать";
    edit.addEventListener("click", () => {
        changeForm(kind, item);
        inputs[kind].form.dataset.editId = item.id;
    });

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "danger";
    remove.textContent = "Удалить";
    remove.addEventListener("click", async () => {
        if (!window.confirm("Удалить «" + title.textContent + "»?")) {
            return;
        }
        remove.disabled = true;
        try {
            await request(kind, "DELETE", item.id);
            if (inputs[kind].form.dataset.editId === String(item.id)) {
                changeForm(kind, null);
            }
            setStatus("Запись удалена.");
            await refreshAfterWrite();
        } catch (error) {
            setStatus(error.message, true);
        } finally {
            remove.disabled = false;
        }
    });
    buttons.append(edit, remove);
    row.append(title, buttons);
    return row;
}

async function refreshLists() {
    const [cards, categories] = await Promise.all([request("cards"), request("categories")]);
    const categorySelect = inputs.cards.form.elements.categoryId;
    const selectedCategory = categorySelect.value;
    categorySelect.replaceChildren(new Option("Выберите категорию", ""),
        ...categories.map(category => new Option(category.name, category.id)));
    categorySelect.value = categories.some(category => String(category.id) === selectedCategory) ? selectedCategory : "";

    const names = [...new Set(cards.map(card => card.type))];
    document.getElementById("cardTypes").replaceChildren(...names.map(name => new Option(name, name)));
    inputs.cards.list.replaceChildren(...cards.map(card => createEntry("cards", card)));
    inputs.categories.list.replaceChildren(...categories.map(category => createEntry("categories", category)));
}

async function refreshAfterWrite() {
    try {
        await refreshLists();
    } catch {
        setStatus("Изменения применены. Обновите страницу, чтобы увидеть актуальный список.", true);
    }
}

for (const [kind, { form }] of Object.entries(inputs)) {
    form.querySelector("[data-reset]").addEventListener("click", () => changeForm(kind, null));
    form.addEventListener("submit", async event => {
        event.preventDefault();
        clearErrors(form);
        setStatus("");
        const submit = form.querySelector('[type="submit"]');
        submit.disabled = true;
        try {
            const data = kind === "cards" ? cardValues(form) : categoryValues(form);
            const id = form.dataset.editId ?? null;
            await request(kind, id === null ? "POST" : "PUT", id, data);
            changeForm(kind, null);
            const message = form.querySelector("[data-form-message]");
            message.className = "message success";
            message.textContent = "Сохранено.";
            await refreshAfterWrite();
        } catch (error) {
            showError(form, error);
        } finally {
            submit.disabled = false;
        }
    });
}

refreshLists().catch(error => setStatus(error.message, true));
