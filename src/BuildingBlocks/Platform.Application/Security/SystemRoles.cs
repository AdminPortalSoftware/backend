namespace Platform.Application.Security;

/// <summary>
/// Roles provisioned for every new tenant. Tenants may edit their permissions (except Owner)
/// and create additional roles.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Pastor = "Pastor";
    public const string FinanceOfficer = "Finance Officer";
    public const string ContentEditor = "Content Editor";
    public const string GroupLeader = "Group Leader";
    public const string Usher = "Usher";
    public const string Member = "Member";

    public static IReadOnlyDictionary<string, (string Description, IReadOnlySet<string> Permissions)> Defaults { get; } =
        new Dictionary<string, (string, IReadOnlySet<string>)>
        {
            [Owner] = ("Full, irrevocable access to the organisation.", Permissions.All),
            [Administrator] = ("Manages the organisation, users and all modules.", Permissions.All),
            [Pastor] = ("Pastoral oversight of people, groups, events and prayer.", Set(
                Permissions.People.Read, Permissions.People.Write, Permissions.People.NotesRead, Permissions.People.NotesWrite,
                Permissions.People.HouseholdsManage, Permissions.People.FollowUpsManage,
                Permissions.Groups.Read, Permissions.Groups.Write, Permissions.Groups.MembersManage,
                Permissions.Events.Read, Permissions.Events.Write, Permissions.Events.AttendanceRead,
                Permissions.Giving.Reports,
                Permissions.Communications.Read, Permissions.Communications.Send, Permissions.Communications.AnnouncementsManage,
                Permissions.Communications.PrayerRequestsRead, Permissions.Communications.PrayerRequestsManage,
                Permissions.Content.Read)),
            [FinanceOfficer] = ("Records and reports on giving.", Set(
                Permissions.People.Read,
                Permissions.Giving.Read, Permissions.Giving.Record, Permissions.Giving.Refund, Permissions.Giving.FundsManage,
                Permissions.Giving.BatchesManage, Permissions.Giving.PledgesManage, Permissions.Giving.Reports)),
            [ContentEditor] = ("Manages the website and app content.", Set(
                Permissions.Content.Read, Permissions.Content.Write, Permissions.Content.Publish, Permissions.Content.Delete,
                Permissions.Content.MediaManage, Permissions.Content.MenusManage,
                Permissions.Events.Read, Permissions.Communications.AnnouncementsManage)),
            [GroupLeader] = ("Leads groups and records their attendance.", Set(
                Permissions.People.Read, Permissions.Groups.Read, Permissions.Groups.MembersManage,
                Permissions.Events.Read, Permissions.Events.AttendanceRecord, Permissions.Events.AttendanceRead)),
            [Usher] = ("Checks people in and records headcounts and offerings.", Set(
                Permissions.People.Read, Permissions.Events.Read, Permissions.Events.AttendanceRecord, Permissions.Giving.Record)),
            [Member] = ("Default role for members using the website and mobile app.", Set()),
        };

    private static IReadOnlySet<string> Set(params string[] permissions) => permissions.ToHashSet(StringComparer.Ordinal);
}
