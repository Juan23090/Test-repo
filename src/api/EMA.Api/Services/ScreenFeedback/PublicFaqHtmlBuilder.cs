using System.Net;
using System.Text;
using EMA.Api.DTOs;

namespace EMA.Api.Services.ScreenFeedback;

public static class PublicFaqHtmlBuilder
{
    public const string PublishedBlobID = "public-faqs.html";

    public static string Build(IReadOnlyList<PublicFaqDto> faqs)
    {
        ArgumentNullException.ThrowIfNull(faqs);

        var builder = new StringBuilder();
        builder.Append(
            "<!doctype html><html lang=\"en\"><head>" +
            "<meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<title>Frequently Asked Questions - EMA</title>" +
            "<style>" +
            "body{margin:0;background:#fff;color:#111;font-family:Arial,Helvetica,sans-serif;}" +
            ".faq-page{width:min(920px,calc(100% - 2rem));margin:1rem auto 3rem;}" +
            ".public-faq-group{margin-top:1.8rem;}" +
            ".public-faq-item{margin:.55rem 0;border:1px solid #c8c8c8;border-radius:4px;background:#fff;}" +
            ".public-faq-item summary{padding:.8rem .9rem;font-weight:700;cursor:pointer;}" +
            ".public-faq-answer{padding:0 .9rem .9rem;white-space:pre-wrap;}" +
            "</style></head><body><main class=\"faq-page\">" +
            "<h1>Frequently Asked Questions</h1>" +
            "<p>Answers to common questions about EmailMoney.App.</p>");

        if (faqs.Count == 0)
        {
            builder.Append("<p>No public FAQs are currently available.</p>");
        }
        else
        {
            foreach (var group in faqs
                         .GroupBy(item => NormalizeHeading(item.ScreenTitle))
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("<section class=\"public-faq-group\"><h2>")
                    .Append(Encode(group.Key))
                    .Append("</h2>");

                foreach (var faq in group
                             .OrderBy(item => item.Question, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.FAQID))
                {
                    builder.Append("<details class=\"public-faq-item\"><summary>")
                        .Append(Encode(faq.Question))
                        .Append("</summary><div class=\"public-faq-answer\">")
                        .Append(Encode(faq.Answer))
                        .Append("</div></details>");
                }

                builder.Append("</section>");
            }
        }

        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private static string NormalizeHeading(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "General"
            : value.Trim();

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
