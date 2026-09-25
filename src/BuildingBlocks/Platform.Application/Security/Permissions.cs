using System.Reflection;

namespace Platform.Application.Security;

/// <summary>
/// The permission catalogue — the single source of truth for authorisation.
/// Format: <c>{module}.{resource}.{action}</c>. Roles are just named sets of these strings,
/// editable per tenant. Adding a permission here makes it available to the role editor.
/// </summary>
public static class Permissions
{
    public static class Tenant
    {
        public const string Read = "tenancy.tenant.read";
        public const string Manage = "tenancy.tenant.manage";
        public const string BranchesManage = "tenancy.branches.manage";
        public const string SettingsManage = "tenancy.settings.manage";
    }

    public static class Identity
    {
        public const string UsersRead = "identity.users.read";
        public const string UsersManage = "identity.users.manage";
        public const string RolesRead = "identity.roles.read";
        public const string RolesManage = "identity.roles.manage";
        public const string ApiKeysManage = "identity.apikeys.manage";
        public const string AuditRead = "identity.audit.read";
    }

    public static class People
    {
        public const string Read = "people.people.read";
        public const string Write = "people.people.write";
        public const string Delete = "people.people.delete";
        public const string Export = "people.people.export";
        public const string NotesRead = "people.notes.read";
        public const string NotesWrite = "people.notes.write";
        public const string HouseholdsManage = "people.households.manage";
        public const string FollowUpsManage = "people.followups.manage";
        public const string CustomFieldsManage = "people.customfields.manage";
    }

    public static class Groups
    {
        public const string Read = "groups.groups.read";
        public const string Write = "groups.groups.write";
        public const string Delete = "groups.groups.delete";
        public const string MembersManage = "groups.members.manage";
    }

    public static class Events
    {
        public const string Read = "events.events.read";
        public const string Write = "events.events.write";
        public const string Delete = "events.events.delete";
        public const string RegistrationsManage = "events.registrations.manage";
        public const string AttendanceRecord = "events.attendance.record";
        public const string AttendanceRead = "events.attendance.read";
    }

    public static class Giving
    {
        public const string Read = "giving.donations.read";
        public const string Record = "giving.donations.record";
        public const string Refund = "giving.donations.refund";
        public const string FundsManage = "giving.funds.manage";
        public const string BatchesManage = "giving.batches.manage";
        public const string PledgesManage = "giving.pledges.manage";
        public const string Reports = "giving.reports.read";
    }

    public static class Content
    {
        public const string Read = "content.content.read";
        public const string Write = "content.content.write";
        public const string Publish = "content.content.publish";
        public const string Delete = "content.content.delete";
        public const string MediaManage = "content.media.manage";
        public const string MenusManage = "content.menus.manage";
    }

    public static class Communications
    {
        public const string Read = "comms.messages.read";
        public const string Send = "comms.messages.send";
        public const string TemplatesManage = "comms.templates.manage";
        public const string AnnouncementsManage = "comms.announcements.manage";
        public const string PrayerRequestsRead = "comms.prayer.read";
        public const string PrayerRequestsManage = "comms.prayer.manage";
    }

    private static readonly Lazy<IReadOnlySet<string>> _all = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal));

    public static IReadOnlySet<string> All => _all.Value;

    public static bool IsKnown(string permission) => All.Contains(permission);
}
