import type { SpanData } from "./types.js";

export function initialize(targetCanvas: HTMLCanvasElement, spans: SpanData[]) {
    const renderer = (targetCanvas as any).traceRenderer ??= new TraceRenderer(targetCanvas);

    renderer.setSpans(spans);
}

class TraceRenderer {

    private readonly canvasContext: CanvasRenderingContext2D;

    private spans: SpanData[] = [];

    public constructor(
        private readonly canvasElement: HTMLCanvasElement
    ) {
        this.canvasContext = canvasElement.getContext("2d")!;
    }

    public setSpans(spans: SpanData[]) {
        this.spans = spans;

        this.render();
    }

    private render() {
        this.canvasContext.fillStyle = "rgb(200 0 0)";
        this.canvasContext.fillRect(10, 10, 50, 50);

        this.canvasContext.fillStyle = "rgb(0 0 200 / 50%)";
        this.canvasContext.fillRect(30, 30, 50, 50);
    }
}