import { queryOptionNames, writeUrlParameter, type TraceViewOptions } from "./urlState.ts";

export class TraceOptionsComponent {

    private readonly groupSpansComponent: CheckBoxComponent;
    private readonly adjustClockSkewComponent: CheckBoxComponent;

    public constructor(root: HTMLElement) {
        this.groupSpansComponent = createCheckbox(
            "Group Spans",
            checked => writeUrlParameter(queryOptionNames.groupSpans, checked ? undefined : "false")
        );

        this.adjustClockSkewComponent = createCheckbox(
            "Adjust Clock Skew",
            checked => writeUrlParameter(queryOptionNames.adjustClockSkew, checked ? undefined : "false")
        );

        root.append(
            this.groupSpansComponent.root,
            this.adjustClockSkewComponent.root,
            createHint()
        );
    }

    public setOptions(options: TraceViewOptions) {
        this.groupSpansComponent.input.checked = options.groupSpans;
        this.adjustClockSkewComponent.input.checked = options.adjustClockSkew;
    }
}

function createCheckbox(text: string, onChange: (checked: boolean) => void): CheckBoxComponent {
    const label = document.createElement("label");
    label.className = "inline-control";

    const input = document.createElement("input");
    input.type = "checkbox";
    input.addEventListener("change", () => onChange(input.checked));

    const caption = document.createElement("span");
    caption.textContent = text;

    label.append(input, caption);

    return { input, root: label };
}

const TIPS = [
    "Click and drag to pan.",
    "<code>MouseWheel</code> to zoom in and out.",
    "<code>Alt + MouseWheel</code> to scroll vertically.",
    "<code>Alt + Shift + MouseWheel</code> to scroll horizontally.",
    "Double click to reset zoom and location.",
];

function createHint(): HTMLElement {
    const hint = document.createElement("hint");

    const icon = document.createElement("hint-icon");
    icon.textContent = "❔";

    const content = document.createElement("hint-content");
    content.innerHTML = `<h2>Tips</h2><ul>${TIPS.map(t => `<li>${t}</li>`).join("")}</ul>`;

    hint.append(icon, content);

    return hint;
}

interface CheckBoxComponent {
    readonly root: HTMLElement;
    readonly input: HTMLInputElement;
};
