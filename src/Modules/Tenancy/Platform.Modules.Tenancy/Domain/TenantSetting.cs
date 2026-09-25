using Platform.SharedKernel.Domain;

namespace Platform.Modules.Tenancy.Domain;

/// <summary>
/// Typed key/value configuration per tenant (branding, giving options, social links...).
/// <see cref="IsPublic"/> settings are exposed to the website and mobile app anonymously.
/// </summary>
public sealed class TenantSetting : TenantEntity
{
    private TenantSetting() { }

    public string Key { get; private set; } = null!;

    /// <summary>JSON document.</summary>
    public string Value { get; private set; } = "null";

    public bool IsPublic { get; private set; }

    public static TenantSetting Create(string key, string jsonValue, bool isPublic) =>
        new() { Key = key, Value = jsonValue, IsPublic = isPublic };

    public void Update(string jsonValue, bool isPublic)
    {
        Value = jsonValue;
        IsPublic = isPublic;
    }
}
