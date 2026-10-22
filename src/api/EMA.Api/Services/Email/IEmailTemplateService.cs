namespace EMA.Api.Services.Email;

public interface IEmailTemplateService
{
    Task<string> RenderTemplateAsync(
        string templateFileName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default);

    string RenderString(
        string template,
        IReadOnlyDictionary<string, string?> values);
}