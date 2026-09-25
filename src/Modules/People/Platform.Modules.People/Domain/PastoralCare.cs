using Platform.SharedKernel.Domain;

namespace Platform.Modules.People.Domain;

/// <summary>A family / household grouping people who live together.</summary>
public sealed class Household : TenantAggregateRoot
{
    private Household() { }

    public string Name { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public Guid? PrimaryContactPersonId { get; private set; }

    public static Household Create(string name) => new() { Name = name.Trim() };

    public void Update(string name, string? phoneNumber, Address? address, Guid? primaryContactPersonId)
    {
        Name = name.Trim();
        PhoneNumber = phoneNumber;
        Address = address ?? Address.Empty;
        PrimaryContactPersonId = primaryContactPersonId;
    }
}

public enum NoteCategory
{
    General,
    Pastoral,
    Counselling,
    Prayer,
    FollowUp,
}

public enum NoteVisibility
{
    /// <summary>Visible to anyone with people.notes.read.</summary>
    Staff,

    /// <summary>Visible only to the author (confidential counselling notes).</summary>
    Private,
}

/// <summary>Pastoral / administrative note about a person. Confidential by design.</summary>
public sealed class PersonNote : TenantAggregateRoot
{
    private PersonNote() { }

    public Guid PersonId { get; private set; }
    public NoteCategory Category { get; private set; }
    public NoteVisibility Visibility { get; private set; }
    public string Body { get; private set; } = null!;

    public static PersonNote Create(Guid personId, NoteCategory category, NoteVisibility visibility, string body) =>
        new() { PersonId = personId, Category = category, Visibility = visibility, Body = body.Trim() };
}

public enum FollowUpType
{
    FirstTimeVisitor,
    NewConvert,
    Absentee,
    Counselling,
    HospitalVisit,
    Bereavement,
    Welfare,
    Other,
}

public enum FollowUpStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}

public enum Priority
{
    Low,
    Normal,
    High,
    Urgent,
}

/// <summary>A pastoral care task: call a first-timer, visit someone in hospital, check on absentees.</summary>
public sealed class FollowUp : TenantAggregateRoot
{
    private FollowUp() { }

    public Guid PersonId { get; private set; }
    public FollowUpType Type { get; private set; }
    public FollowUpStatus Status { get; private set; }
    public Priority Priority { get; private set; }

    /// <summary>Staff account responsible (Identity user id).</summary>
    public Guid? AssignedToUserId { get; private set; }

    public DateOnly? DueDate { get; private set; }
    public string? Notes { get; private set; }
    public string? Outcome { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static FollowUp Create(Guid personId, FollowUpType type, Priority priority, Guid? assignedTo, DateOnly? dueDate, string? notes) => new()
    {
        PersonId = personId,
        Type = type,
        Priority = priority,
        Status = FollowUpStatus.Open,
        AssignedToUserId = assignedTo,
        DueDate = dueDate,
        Notes = notes,
    };

    public void Update(Priority priority, Guid? assignedTo, DateOnly? dueDate, string? notes)
    {
        Priority = priority;
        AssignedToUserId = assignedTo;
        DueDate = dueDate;
        Notes = notes;
    }

    public void ChangeStatus(FollowUpStatus status, string? outcome, DateTimeOffset now)
    {
        Status = status;
        Outcome = outcome ?? Outcome;
        CompletedAt = status is FollowUpStatus.Completed or FollowUpStatus.Cancelled ? now : null;
    }
}

public enum CustomFieldType
{
    Text,
    LongText,
    Number,
    Date,
    Boolean,
    Select,
    MultiSelect,
}

/// <summary>Tenant-defined extra field on person profiles (e.g. "Cell zone", "Blood group").</summary>
public sealed class CustomFieldDefinition : TenantAggregateRoot
{
    private CustomFieldDefinition() { }

    public string Key { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public CustomFieldType FieldType { get; private set; }
    public List<string> Options { get; private set; } = [];
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public static CustomFieldDefinition Create(string key, string label, CustomFieldType type, IEnumerable<string>? options, bool isRequired, int sortOrder) => new()
    {
        Key = key,
        Label = label,
        FieldType = type,
        Options = options?.ToList() ?? [],
        IsRequired = isRequired,
        SortOrder = sortOrder,
    };

    public void Update(string label, IEnumerable<string>? options, bool isRequired, bool isActive, int sortOrder)
    {
        Label = label;
        Options = options?.ToList() ?? [];
        IsRequired = isRequired;
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}
