namespace NekoTrace.Web.Analysis.Formatting;

/// <summary>
/// One line's worth of a value that may be many lines long.
/// </summary>
/// <remarks>
/// Line breaks and tabs are folded out before truncating. In the tree that is because a value like
/// <c>error.stack</c> is many lines long and one of those landing mid-tree destroys the indentation carrying
/// the structure; in <see cref="FlatFormatter"/> it is load bearing, since a stray newline would split one
/// span across two lines and a stray tab would shift every field after it into the wrong column.
/// </remarks>
internal static class Inline
{
    private const string BREAK = " ⏎ ";

    public static string? Value(string? value, int length)
    {
        if (value is null)
        {
            return null;
        }

        var folded = value.ReplaceLineEndings(BREAK).Replace('\t', ' ');

        return folded.Length <= length ? folded : folded[..length] + "…";
    }
}
