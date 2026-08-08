const COPY_VALUE_ATTRIBUTE_NAME = "data-copy-value";
const COPIED_ATTRIBUTE_NAME = "data-copied";
const COPIED_DURATION_MS = 1500;

// Listening on the document keeps this working no matter how much of the DOM blazor swaps out
document.addEventListener("click", async event => {
    if (!(event.target instanceof Element)) {
        return;
    }

    const button = event.target.closest<HTMLElement>(`[${COPY_VALUE_ATTRIBUTE_NAME}]`);
    if (button === null) {
        return;
    }

    if (!await copyText(button.getAttribute(COPY_VALUE_ATTRIBUTE_NAME) ?? "")) {
        return;
    }

    button.setAttribute(COPIED_ATTRIBUTE_NAME, "");
    setTimeout(() => button.removeAttribute(COPIED_ATTRIBUTE_NAME), COPIED_DURATION_MS);
});

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
