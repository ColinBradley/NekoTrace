const COPIED_ATTRIBUTE_NAME = "data-copied";
const COPIED_DURATION_MS = 1500;

const COPY_ICON = `<svg class="copy-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <rect x="9" y="9" width="12" height="12" rx="2" />
    <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
</svg>
<svg class="copied-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <path d="M4 12.5 9 18 20 6" />
</svg>`;

/**
 * A span with a copy button beside it. The button is only added when there is something to copy.
 */
export function createCopyableValueElement(value: string, content?: Node): DocumentFragment {
    const container = document.createDocumentFragment();

    const text = document.createElement("span");
    // Values with multiple lines are pre formatted.
    // Otherwise we allow wrapping.
    text.className = value.includes("\n") ? "text multi-line" : "text";
    text.append(content ?? value);
    container.append(text);

    if (value.length > 0) {
        container.append(createCopyButton(value));
    }

    return container;
}

function createCopyButton(value: string): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "copy-button";
    button.title = "Copy";
    button.innerHTML = COPY_ICON;

    // The value is right here in the closure, so nothing has to carry it on the element or go looking.
    button.addEventListener("click", async () => {
        if (!await copyText(value)) {
            return;
        }

        button.setAttribute(COPIED_ATTRIBUTE_NAME, "");
        setTimeout(() => button.removeAttribute(COPIED_ATTRIBUTE_NAME), COPIED_DURATION_MS);
    });

    return button;
}

async function copyText(text: string) {
    // navigator.clipboard only exists in secure contexts, which http from another machine is not
    if (navigator.clipboard !== undefined) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Fall through to the old way
        }
    }

    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.style.position = "fixed";
    textArea.style.opacity = "0";
    document.body.append(textArea);
    textArea.select();

    try {
        return document.execCommand("copy");
    } finally {
        textArea.remove();
    }
}
