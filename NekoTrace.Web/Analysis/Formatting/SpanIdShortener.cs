namespace NekoTrace.Web.Analysis.Formatting;

/// <summary>
/// Shortens span ids to the shortest prefix that is still unique within one response, the way git shortens
/// commit hashes.
/// </summary>
/// <remarks>
/// A 16 character span id repeated down a tree of a few thousand lines is a real share of the output, and
/// none of it carries meaning. Every endpoint accepts a prefix back, so a shortened id is not a dead end.
/// One length is used for the whole response rather than a per-id minimum, because a column of ids that are
/// all the same width is easier to read and to explain than one where each is as short as it can be.
/// </remarks>
internal sealed class SpanIdShortener
{
    public const int MINIMUM_LENGTH = 4;

    private readonly int mLength;

    private SpanIdShortener(int length)
    {
        mLength = length;
    }

    /// <summary>Leaves ids alone. For <c>detail=full</c>, where the whole id is wanted.</summary>
    public static SpanIdShortener None { get; } = new(int.MaxValue);

    public static SpanIdShortener For(IEnumerable<string> spanIds)
    {
        var ids = spanIds.Distinct(StringComparer.Ordinal).ToArray();

        if (ids.Length < 2)
        {
            return new SpanIdShortener(MINIMUM_LENGTH);
        }

        var longest = ids.Max(id => id.Length);

        for (var length = MINIMUM_LENGTH; length < longest; length++)
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            var collided = false;

            foreach (var id in ids)
            {
                if (!prefixes.Add(id.Length <= length ? id : id[..length]))
                {
                    collided = true;

                    break;
                }
            }

            if (!collided)
            {
                return new SpanIdShortener(length);
            }
        }

        return new SpanIdShortener(longest);
    }

    public string Shorten(string spanId) =>
        spanId.Length <= mLength ? spanId : spanId[..mLength];
}
