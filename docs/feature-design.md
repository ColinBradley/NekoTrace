# Feature design

## What NekoTrace is

A personal tool you run as one executable, look at traces in, and close again. More david than goliath — quick in and out adventures. "Simple to run" is the product, not a constraint on it.

The explicit non-goal is being a component in someone else's observability stack. NekoTrace does not compete with Tempo, Jaeger or Grafana, and does not want to be a storage backend that they front. Any feature whose payoff arrives only once the user is also running something bigger forfeits the no-infrastructure pitch, and loses to those tools at the job it just took on.

## Is it in scope

Two questions:

1. Would this only pay off if the user also ran something bigger? If so it is out, however well it scores on capability alone.
2. Does it serve someone who wants to run one executable, look at traces, and close it?

Power-user features are welcome as opt-in escape hatches, but must not displace the default surface or make first use harder. If the filter controls and sortable columns ever become vestigial, the tool has stopped being the thing it exists to be.

## Recording a decline

Record whether a declined feature failed on **effort** or on **fit** — only the second stays decided. Difficulty evaporates the moment an implementation gets easier or a library appears; positioning does not. Anything declined on effort alone is worth revisiting; anything declined on fit is not, unless the aim itself changes.

| Feature | Verdict |
| --- | --- |
| [TraceQL](https://grafana.com/docs/tempo/latest/traceql/) in the filter UI | **Fit**, and effort. It is Tempo's language rather than a standard, aimed at a different user. A partial implementation is the trap: users and models carry strong priors about what `resource.` and `>>` mean, so an unsupported subset misleads silently instead of erroring. Ingest also flattens resource and scope attributes into one span bag, so Tempo-compatible scopes would alias each other and over-match — separating them would reach `SpanData`, the serializer, `TraceSerializableData.CURRENT_VERSION`, the upload path and the attribute UI. If expressive filtering is ever wanted, an embedded JS predicate over NekoTrace's own object model is preferred: it cannot impersonate another tool's semantics, and it throws instead of lying. |
| Tempo HTTP API compatibility, so Grafana can query NekoTrace as a data source | **Fit.** It makes "stand up Grafana" the answer to querying properly, turning NekoTrace into a storage backend inside someone else's stack — exactly the swap this document exists to prevent. |

## Filter languages specifically

Any expressive filter language would be UI-only regardless of its syntax. `TraceFilter.IsRejected` needs a monotonicity property — "this can never qualify" — that cannot be recovered from an arbitrary predicate or AST, and it drives ingest discard, trim, and deletion of already-written files. Getting it wrong loses data silently, so the ingest and save filters keep the query-string syntax whatever the UI grows. See [filtering.md](filtering.md).
