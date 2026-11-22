using System.Net;
using System.Text.RegularExpressions;

namespace CategoryProduct.MVC.Tests;

/// <summary>Regex-based HTML probes (no parser needed; Razor tag-helper output is stable).</summary>
public static class HtmlHelpers
{
    private static readonly Regex TokenInput = new(
        "<input\\b[^>]*name=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase);
    private static readonly Regex ValueAttr = new("value=\"([^\"]*)\"", RegexOptions.IgnoreCase);

    /// <summary>The antiforgery token from the first hidden form field; fails the test when absent.</summary>
    public static string ExtractAntiforgeryToken(string html)
    {
        var m = TokenInput.Match(html);
        Assert.True(m.Success, "No __RequestVerificationToken field in the page.");
        var v = ValueAttr.Match(m.Value);
        Assert.True(v.Success, "Antiforgery field has no value attribute.");
        return WebUtility.HtmlDecode(v.Groups[1].Value);
    }

    /// <summary>HTML-decodes entities (&amp;amp;, &amp;#x27; ...).</summary>
    public static string Decode(string html) => WebUtility.HtmlDecode(html);

    /// <summary>True when the decoded page contains <paramref name="text"/> (ordinal).</summary>
    public static bool HasText(string html, string text) => Decode(html).Contains(text, StringComparison.Ordinal);

    /// <summary>Decoded, trimmed text of every <c>div.alert-{kind}</c> ("danger", "success", "warning"); empty when none.</summary>
    public static IReadOnlyList<string> Alerts(string html, string kind)
    {
        var rx = new Regex($"<div\\b[^>]*class=\"[^\"]*alert-{Regex.Escape(kind)}[^\"]*\"[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return rx.Matches(html).Select(m => Decode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim()).ToList();
    }

    /// <summary>Text of the first <c>div.alert-{kind}</c>, or null when none.</summary>
    public static string? Alert(string html, string kind) => Alerts(html, kind).FirstOrDefault();

    /// <summary>True when a form posts to <paramref name="actionPath"/> (e.g. "/Categories/Delete/3"); query ignored.</summary>
    public static bool FormAction(string html, string actionPath)
    {
        foreach (Match m in Regex.Matches(html, "<form\\b[^>]*action=\"([^\"]*)\"", RegexOptions.IgnoreCase))
        {
            var a = Decode(m.Groups[1].Value);
            var q = a.IndexOf('?');
            if (q >= 0) a = a[..q];
            if (string.Equals(a, actionPath, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Validation message (decoded, trimmed) rendered by <c>asp-validation-for</c> for <paramref name="field"/>
    /// (matches data-valmsg-for), or null when none/empty. Use field "" for the model-only summary list.
    /// </summary>
    public static string? FieldError(string html, string field)
    {
        if (field.Length == 0)
        {
            var s = Regex.Match(html, "<div\\b[^>]*validation-summary-errors[^>]*>(.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!s.Success) return null;
            var t = Decode(Regex.Replace(s.Groups[1].Value, "<[^>]+>", " ")).Trim();
            return t.Length == 0 ? null : Regex.Replace(t, "\\s+", " ");
        }
        var m = Regex.Match(html,
            $"<span\\b[^>]*data-valmsg-for=\"{Regex.Escape(field)}\"[^>]*>(.*?)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        var text = Decode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
        return text.Length == 0 ? null : text;
    }
}
