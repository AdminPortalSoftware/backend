using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Application.Security;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Tenancy.Features;

public sealed record BranchResponse(
    Guid Id, string Name, string Code, bool IsHeadquarters, string Status, string? Email, string? Phone,
    string? TimeZone, Address Address, Guid? LeaderPersonId, DateOnly? EstablishedOn);

public sealed record SaveBranchRequest(
    string Name, string Code, bool IsHeadquarters, string? Email, string? Phone, string? TimeZone,
    Address? Address, Guid? LeaderPersonId, DateOnly? EstablishedOn, BranchStatus Status = BranchStatus.Active);

internal sealed class SaveBranchValidator : AbstractValidator<SaveBranchRequest>
{
    public SaveBranchValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(32);
        RuleFor(x => x.TimeZone).Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            .WithMessage("Unknown IANA time zone.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public static class Branches
{
    private static readonly Error NotFound = Error.NotFound("branch.not_found", "The branch was not found.");
    private static readonly Error CodeTaken = Error.Conflict("branch.code_taken", "Another branch already uses this code.");

    private static BranchResponse ToResponse(this Branch b) => new(
        b.Id, b.Name, b.Code, b.IsHeadquarters, b.Status.ToString(), b.Email, b.Phone, b.TimeZone, b.Address,
        b.LeaderPersonId, b.EstablishedOn);

    internal sealed class SaveHandler(TenancyDbContext db) : ICommandHandler<(Guid? Id, SaveBranchRequest Request), BranchResponse>
    {
        public async Task<Result<BranchResponse>> Handle((Guid? Id, SaveBranchRequest Request) command, CancellationToken ct)
        {
            var (id, r) = command;
            var code = r.Code.ToUpperInvariant();
            if (await db.Branches.AnyAsync(b => b.Code == code && b.Id != id, ct))
            {
                return CodeTaken;
            }

            Branch? branch;
            if (id is null)
            {
                branch = Branch.Create(r.Name, code, r.IsHeadquarters);
                db.Branches.Add(branch);
            }
            else
            {
                branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
                if (branch is null)
                {
                    return NotFound;
                }
            }

            branch.Update(r.Name, code, r.Email, r.Phone, r.TimeZone, r.Address ?? Address.Empty, r.LeaderPersonId, r.EstablishedOn, r.Status);

            // Exactly one headquarters per tenant.
            if (r.IsHeadquarters)
            {
                branch.SetHeadquarters(true);
                var others = await db.Branches.Where(b => b.IsHeadquarters && b.Id != branch.Id).ToListAsync(ct);
                others.ForEach(o => o.SetHeadquarters(false));
            }

            await db.SaveChangesAsync(ct);
            return branch.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("branches", "Branches");

        group.MapGet("/", async (TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Branches.AsNoTracking()
                    .OrderByDescending(b => b.IsHeadquarters).ThenBy(b => b.Name)
                    .Select(b => b.ToResponse()).ToListAsync(ct)))
            .RequirePermission(Permissions.Tenant.Read)
            .WithSummary("List branches / campuses");

        group.MapGet("/{id:guid}", async (Guid id, TenancyDbContext db, CancellationToken ct) =>
                await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) is { } b
                    ? Results.Ok(b.ToResponse())
                    : NotFound.ToProblem())
            .RequirePermission(Permissions.Tenant.Read)
            .WithSummary("Get a branch");

        group.MapPost("/", async (SaveBranchRequest request, ICommandHandler<(Guid?, SaveBranchRequest), BranchResponse> handler, CancellationToken ct) =>
                (await handler.Handle((null, request), ct)).ToCreated(b => $"/api/v1/branches/{b.Id}"))
            .WithValidation<SaveBranchRequest>()
            .RequirePermission(Permissions.Tenant.BranchesManage)
            .WithSummary("Create a branch");

        group.MapPut("/{id:guid}", async (Guid id, SaveBranchRequest request, ICommandHandler<(Guid?, SaveBranchRequest), BranchResponse> handler, CancellationToken ct) =>
                (await handler.Handle((id, request), ct)).ToHttp())
            .WithValidation<SaveBranchRequest>()
            .RequirePermission(Permissions.Tenant.BranchesManage)
            .WithSummary("Update a branch");

        group.MapDelete("/{id:guid}", async (Guid id, TenancyDbContext db, CancellationToken ct) =>
            {
                var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
                if (branch is null)
                {
                    return NotFound.ToProblem();
                }

                db.Branches.Remove(branch);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .RequirePermission(Permissions.Tenant.BranchesManage)
            .WithSummary("Archive (soft delete) a branch");
    }
}
