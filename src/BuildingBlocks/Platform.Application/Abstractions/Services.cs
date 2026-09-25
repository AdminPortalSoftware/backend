namespace Platform.Application.Abstractions;

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? ReplyTo = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record StoredFile(string Key, string Url, long Size, string ContentType);

/// <summary>Blob storage abstraction (local disk in development, S3/Azure Blob/R2 in production).</summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    string GetPublicUrl(string key);
}
