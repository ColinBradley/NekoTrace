namespace NekoTrace.Cli;

using System.Globalization;
using System.Text;

/// <summary>
/// The query string for one request, built by adding whatever the caller asked for and nothing else.
/// </summary>
/// <remarks>
/// Every <c>Add</c> ignores a null, which is what makes an omitted flag mean "leave it to the server" rather
/// than "send the CLI's idea of the default". Two sets of defaults for the same option is how the two drift,
/// and the endpoints already state theirs in <c>--help</c> because those descriptions came from the same
/// place. Values are formatted invariantly: a query string is not a locale, whatever the machine thinks.
/// </remarks>
internal sealed class Query
{
    private readonly StringBuilder mQuery = new();

    public Query Add(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return this;
        }

        mQuery.Append(mQuery.Length is 0 ? '?' : '&')
            .Append(Uri.EscapeDataString(key))
            .Append('=')
            .Append(Uri.EscapeDataString(value));

        return this;
    }

    public Query Add(string key, bool? value) =>
        value is null ? this : this.Add(key, value.Value ? "true" : "false");

    public Query Add(string key, int? value) =>
        this.Add(key, value?.ToString(CultureInfo.InvariantCulture));

    public Query Add(string key, double? value) =>
        this.Add(key, value?.ToString("R", CultureInfo.InvariantCulture));

    public override string ToString() => mQuery.ToString();
}
