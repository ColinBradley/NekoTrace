namespace InfoCat.Web.Repositories;

using System;
using System.Collections.Generic;

public sealed record SpanEvent
{
    public required string Name { get; init; }

    public required DateTimeOffset Time { get; init; }

    public required Dictionary<string, object?> Attributes { get; init; }
}