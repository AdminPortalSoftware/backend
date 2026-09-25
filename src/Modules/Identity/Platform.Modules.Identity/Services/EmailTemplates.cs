using System.Net;
using Platform.Application.Abstractions;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Transactional identity emails. Kept in code for now; the Communications module's
/// tenant-editable templates can take over once branding per tenant is required.
/// </summary>
internal static class EmailTemplates
{
    public static EmailMessage ConfirmEmail(User user, string baseUrl, string token) => Build(
        user, "Confirm your email address",
        "Please confirm your email address to finish setting up your account.",
        "Confirm email", $"{baseUrl}/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}");

    public static EmailMessage ResetPassword(User user, string baseUrl, string token) => Build(
        user, "Reset your password",
        "We received a request to reset your password. This link expires in 1 hour. If you did not ask for this, ignore this email.",
        "Reset password", $"{baseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}");

    public static EmailMessage Invitation(User user, string organisation, string baseUrl, string token) => Build(
        user, $"You've been invited to {organisation}",
        $"You have been invited to join {WebUtility.HtmlEncode(organisation)}. Set your password to get started. This link expires in 7 days.",
        "Accept invitation", $"{baseUrl}/accept-invitation?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}");

    public static EmailMessage AddedToOrganisation(User user, string organisation, string baseUrl) => Build(
        user, $"You now have access to {organisation}",
        $"Your account has been given access to {WebUtility.HtmlEncode(organisation)}.",
        "Sign in", $"{baseUrl}/login");

    private static EmailMessage Build(User user, string subject, string body, string action, string url)
    {
        var name = WebUtility.HtmlEncode(user.FirstName);
        var html = $"""
            <p>Hi {name},</p>
            <p>{body}</p>
            <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:10px 18px;background:#1f2937;color:#fff;border-radius:6px;text-decoration:none">{action}</a></p>
            <p style="color:#6b7280;font-size:12px">If the button does not work, copy this link: {WebUtility.HtmlEncode(url)}</p>
            """;
        return new EmailMessage(user.Email, subject, html, $"Hi {user.FirstName},\n\n{body}\n\n{action}: {url}");
    }
}
