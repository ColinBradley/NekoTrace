/*
 * The whole look of the trace view, assuming nothing of the page around it. Scoped under the element so it
 * cannot leak, and plain selectors and custom properties throughout so a host can override any of it.
 *
 * A canvas takes no CSS, so the properties under "canvas painting" are read by script and applied while
 * drawing. Set them anywhere that cascades to the element; reloadStyle picks them up.
 */


const STYLE_ELEMENT_ID = "neko-trace-view-styles";

/*
 * Appended to the end of head, so a host overrides by specificity rather than by load order. Not a cascade
 * layer: an unlayered rule beats a layered one whatever its specificity, so a host carrying
 * `* { padding: 0 }` - as NekoTrace's app.css does - would strip the padding out of the whole view.
 *
 * The id is there so a host can find it and take it out.
 */
export function installStyles() {
    if (document.getElementById(STYLE_ELEMENT_ID) !== null) {
        return;
    }

    const style = document.createElement("style");
    style.id = STYLE_ELEMENT_ID;
    style.textContent = STYLES;

    document.head.append(style);
}

const css = (strings: TemplateStringsArray) => strings.raw[0]!;

const STYLES = css`
neko-trace-view {
    /* Canvas painting. Read by script, so only whole values work here - no calc(), no relative units. */
    --neko-trace-font-family: monospace;
    --neko-trace-font-size: 14px;
    --neko-trace-span-error-overlay-color: rgba(255, 0, 0, .7);
    --neko-trace-span-parent-overlay-color: rgba(0, 0, 0, .3);
    --neko-trace-span-active-border-color: #dd8451;
    --neko-trace-span-hot-border-color: #FF8644;
    --neko-trace-span-transition-border-color: #CCC9;
    --neko-trace-span-text-color: #FFF;
    --neko-trace-time-offset-text-color: #FFF;
    --neko-trace-time-line-color: #FFF6;
    --neko-trace-hover-text-background-color: #000C;
    --neko-trace-hover-text-color: #FFF;

    /* One colour per distinct value of the attribute named by data-span-color-selector, in order. */
    --neko-trace-span-colors:
        #3A4B33, #61594F, #3F4F44, #8E5E37, #004487,
        #2A9D8F, #00BFFF, #4C4C9D, #A8D676, #5917BC,
        #8F7842, #9B5DE5, #457B9D, #8F2C9E, #963F3F;

    /* Ordinary CSS from here down. */
    --neko-trace-canvas-background: #181818;
    --neko-trace-button-border: 1px solid #555;
    --neko-trace-copied-color: #26b050;
    --neko-trace-gap: 1em;

    display: block;
    min-width: 0;
    min-height: 0;

    /*
     * A size container so the layout below can ask about the shape of the box the host gave us rather
     * than the shape of the window. A container query styles descendants and not the container itself,
     * which is why the grid is one level in.
     */
    container-type: size;
    container-name: neko-trace-view;
}

neko-trace-view .trace-view-layout {
    display: grid;
    width: 100%;
    height: 100%;
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: min-content minmax(0, 1fr) minmax(0, 1fr);
}

/*
 * Wide enough to split down the middle, so the detail panel goes beside the graph rather than under it.
 * The width floor is there because a short narrow box is still wider than it is tall, and halving one
 * leaves a flame graph and an attribute list that are each too narrow to read. Below it they stack, which
 * is what a docked side panel wants whatever its shape.
 *
 * The selectors repeat the element name because a container query does nothing for specificity - written
 * as bare classes these would lose to the rules above whatever order they came in.
 */
@container neko-trace-view (min-aspect-ratio: 1 / 1) and (min-width: 1000px) {
    neko-trace-view .trace-view-layout {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        grid-template-rows: min-content minmax(0, 1fr);
    }

    neko-trace-view .trace-view-controls {
        grid-column: 1 / -1;
    }
}

neko-trace-view .trace-view-controls {
    display: flex;
    flex-direction: row;
    align-items: center;
    gap: var(--neko-trace-gap);
    padding: .5em;
}

neko-trace-view .trace-view-canvas {
    display: block;
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    background-color: var(--neko-trace-canvas-background);
    cursor: grab;
    touch-action: none;
}

    neko-trace-view .trace-view-canvas.panning {
        cursor: grabbing;
    }

neko-trace-view .trace-view-details {
    display: flex;
    flex-direction: column;
    gap: var(--neko-trace-gap);
    padding: 1em;
    overflow: auto;
    min-width: 0;
    min-height: 0;
}

neko-trace-view .span-links {
    display: flex;
    gap: 1ch;
}

neko-trace-view .events {
    display: flex;
    flex-direction: column;
    gap: .5em;
}

/* --- The definition lists ------------------------------------------------------------------------- */

neko-trace-view .span-info {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    gap: .3em 1em;
    margin: 0;
}

    neko-trace-view .span-info dt {
        font-weight: bold;
        margin: 0;
    }

    neko-trace-view .span-info dd {
        margin: 0;
        overflow: hidden;
        white-space: nowrap;
        /* The value does its own ellipsing, otherwise text-overflow stops the copy button being painted */
        text-overflow: clip;
    }

        neko-trace-view .span-info dd:hover {
            overflow: visible;
            white-space: pre;
        }

            neko-trace-view .span-info dd:hover .text {
                max-width: none;
                overflow: visible;
            }

neko-trace-view .text {
    display: inline-block;
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
}

/* --- Copy and hide buttons ------------------------------------------------------------------------ */

neko-trace-view .copy-button {
    /* Sticky so it sits at the end of the text, but slides over it when the text is too long to fit */
    position: sticky;
    inset-inline-end: 0;
    margin-inline-start: .5ch;
    padding: 0.1em .2em;
    font-size: 1em;
    /* Values are rendered with white-space: pre while hovered, which the icons must not inherit */
    white-space: normal;
    line-height: 0;
    /* Top, so that it stays on the first line of values that span several */
    vertical-align: top;
    color: inherit;
    background-color: canvas;
    border: var(--neko-trace-button-border);
    border-radius: .2em;
    cursor: pointer;
}

neko-trace-view .hide-button {
    display: inline-block;
    margin-inline-start: .5ch;
    padding: 0.1em .2em;
    line-height: 0;
    vertical-align: middle;
    color: inherit;
    text-decoration: none;
    background-color: canvas;
    border: var(--neko-trace-button-border);
    border-radius: .2em;
    cursor: pointer;
}

neko-trace-view :is(.copy-button, .hide-button) svg {
    width: 1em;
    height: 1em;
}

neko-trace-view .copy-button .copied-icon,
neko-trace-view .copy-button[data-copied] .copy-icon {
    display: none;
}

neko-trace-view .copy-button[data-copied] .copied-icon {
    display: inline;
    color: var(--neko-trace-copied-color);
}

/*
 * Hidden with opacity rather than display so that the key column doesn't resize while the mouse moves
 * down the list.
 */
neko-trace-view :is(.copy-button, .hide-button) {
    opacity: 0;
}

neko-trace-view :is(
    dt:hover .hide-button,
    dt:has(+ dd:hover) .hide-button,
    dt:hover + dd .copy-button,
    dd:hover .copy-button,
    .copy-button:focus-visible,
    .hide-button:focus-visible
) {
    opacity: 1;
}

/* --- Controls ------------------------------------------------------------------------------------- */

neko-trace-view .inline-control {
    display: flex;
    flex-direction: row;
    align-items: center;
    gap: .5em;
    margin: 0;
}

neko-trace-view .inline-control input[type=checkbox] {
    zoom: 1.3;
    margin: 0;
}

/* --- The tips popover ----------------------------------------------------------------------------- */

neko-trace-view hint {
    position: relative;
    cursor: help;
}

neko-trace-view hint-icon {
    anchor-name: --neko-trace-hint;
}

neko-trace-view hint-content {
    display: none;
    position: absolute;
    position-anchor: --neko-trace-hint;
    position-area: bottom;
    position-try-fallbacks: --neko-trace-hint-right, --neko-trace-hint-left;
    top: anchor(bottom);
    z-index: 9001;
    padding: .5em;
    white-space: nowrap;
    background-color: canvas;
    border: var(--neko-trace-button-border);
}

    neko-trace-view hint-content ul {
        margin: 0;
        padding-inline-start: 2ch;
    }

    neko-trace-view hint-content h2 {
        margin: 0 0 .3em;
        font-size: 1em;
    }

neko-trace-view hint:hover > hint-content {
    display: block;
}

@position-try --neko-trace-hint-right {
    position-area: none;
    left: anchor(left);
}

@position-try --neko-trace-hint-left {
    position-area: none;
    right: anchor(right);
}
`;
