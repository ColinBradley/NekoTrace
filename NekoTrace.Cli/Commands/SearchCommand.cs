namespace NekoTrace.Cli.Commands;

using System.CommandLine;

/// <summary>
/// <c>GET api/spans</c>. The span predicate, one option per dimension.
/// </summary>
/// <remarks>
/// Flag shaped rather than a query language, which is a decision taken in <c>docs/ai-access.md</c> and not one
/// the CLI gets to revisit: a second language to learn is a cost paid by every caller for a case these cover.
/// </remarks>
internal static class SearchCommand
{
    private static readonly string[] sKinds = ["Internal", "Server", "Client", "Producer", "Consumer"];

    public static Command Create()
    {
        var name = new Option<string>("--name")
        {
            Description = "Wildcard pattern over the span name, e.g. 'GET*' or '*Grain*'. Case insensitive.",
        };

        var traceId = new Option<string>("--trace-id", "--trace")
        {
            Description = "Restrict to one trace. Omit to search every trace the server holds.",
        };

        var minDurationSeconds = new Option<double?>("--min-duration-seconds")
        {
            Description = "Only spans lasting at least this long, in seconds. Accepts fractions, e.g. 0.05.",
        };

        var maxDurationSeconds = new Option<double?>("--max-duration-seconds")
        {
            Description = "Only spans lasting at most this long, in seconds.",
        };

        var startedAfter = new Option<string>("--started-after")
        {
            Description =
                "Only spans that started at or after this instant. ISO 8601, e.g. '2026-08-09T14:00:00Z'. "
                + "Read as UTC when no offset is given.",
        };

        var startedBefore = new Option<string>("--started-before")
        {
            Description =
                "Only spans that started at or before this instant. ISO 8601, read as UTC without an offset.",
        };

        var hasError = new Option<bool?>("--has-error")
        {
            Description = "Only spans that are (true) or are not (false) marked as failed.",
        };

        var kind = new Option<string>("--kind")
        {
            Description = "Only spans of this kind: " + string.Join(", ", sKinds) + ".",
        };

        var attributeFilter = new Option<string>("--attribute-filter")
        {
            Description =
                "Only spans carrying one of these attributes, as 'key=value;key=value'. Values are compared "
                + "case insensitively, and a span matches when any one pair matches. This decides which "
                + "spans come back; --attribute-keys decides what is printed of them.",
        };

        var attributeKeys = new Option<string>("--attribute-keys")
        {
            Description =
                "Which of each match's attributes to print, as comma separated key prefixes — 'http.,db.' or "
                + "'url.full'. '*' prints every attribute. Omit for the default, which prints all of them "
                + "except otel.library.* and telemetry.sdk.*.",
        };

        var includeAttributes = new Option<bool?>("--include-attributes")
        {
            Description =
                "Print the matches' attributes at all. Defaults to true; pass 'false' for just ids, names "
                + "and durations.",
        };

        var limit = new Option<int?>("--limit")
        {
            Description =
                "How many matches to print. The server defaults to 50. It does not limit the match count or "
                + "the shared attribute block, which always describe every match — so lower it freely when "
                + "you want the totals rather than the rows.",
        };

        var command = new Command(
            "search",
            "Finds spans by name, duration, status, kind, time or attribute, within one trace or across "
            + "every trace held. Every option is optional and they combine with AND. Each match prints with "
            + "its attributes, above them the attributes identical across every match, and below them how "
            + "many matched in total — both covering the whole result rather than the page, so `--limit 1` "
            + "answers 'how many, and what do they all share' for the price of one row. Under --format flat "
            + "the trailing attributes field is what `grep -o` and `sort | uniq -c` work on when the matches "
            + "do not all agree. Returns trace and span ids to feed to `NekoTrace.Cli span`."
        );

        command.Options.Add(name);
        command.Options.Add(traceId);
        command.Options.Add(minDurationSeconds);
        command.Options.Add(maxDurationSeconds);
        command.Options.Add(startedAfter);
        command.Options.Add(startedBefore);
        command.Options.Add(hasError);
        command.Options.Add(kind);
        command.Options.Add(attributeFilter);
        command.Options.Add(attributeKeys);
        command.Options.Add(includeAttributes);
        command.Options.Add(limit);

        command.SetSessionAction((parseResult, session, cancellationToken) =>
            session.WriteAsync(
                "api/spans",
                new Query()
                    .Add("name", parseResult.GetValue(name))
                    .Add("traceId", parseResult.GetValue(traceId) ?? session.UploadedTraceId)
                    .Add("minDuration", parseResult.GetValue(minDurationSeconds))
                    .Add("maxDuration", parseResult.GetValue(maxDurationSeconds))
                    .Add("startedAfter", Timestamps.ToUtc(parseResult.GetValue(startedAfter), "--started-after"))
                    .Add("startedBefore", Timestamps.ToUtc(parseResult.GetValue(startedBefore), "--started-before"))
                    .Add("hasError", parseResult.GetValue(hasError))
                    .Add("kind", Kind(parseResult.GetValue(kind)))
                    .Add("attributeFilter", parseResult.GetValue(attributeFilter))
                    .Add("attributeKeys", parseResult.GetValue(attributeKeys))
                    .Add("includeAttributes", parseResult.GetValue(includeAttributes))
                    .Add("limit", parseResult.GetValue(limit)),
                cancellationToken
            )
        );

        return command;
    }

    /// <summary>
    /// Checked here rather than sent on, because the server drops a kind it cannot read instead of erroring —
    /// which would turn a typo into a search across every kind, quietly and with plausible looking results.
    /// </summary>
    private static string? Kind(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        foreach (var known in sKinds)
        {
            if (string.Equals(known, value, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        throw new CliException(
            "--kind: '" + value + "' is not a span kind. They are " + string.Join(", ", sKinds) + "."
        );
    }
}
