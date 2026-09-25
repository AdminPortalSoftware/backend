using Microsoft.EntityFrameworkCore;
using Platform.Modules.People.Contracts;

namespace Platform.Modules.People.Infrastructure;

internal sealed class PeopleDirectory(PeopleDbContext db) : IPeopleDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PersonSummary>> GetSummariesAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PersonSummary>();
        }

        return await db.People.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new PersonSummary(p.Id, p.MemberNumber, (p.PreferredName ?? p.FirstName) + " " + p.LastName, p.Email, p.PhoneNumber, p.PhotoUrl, p.BranchId))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task<Guid?> FindPersonIdByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.People.AsNoTracking().Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
}
