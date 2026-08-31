using System.Net.Mail;
using CloudOrders.Application.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class SqlCustomerProfileStore(
    IDbContextFactory<CloudOrdersDbContext> contextFactory,
    ICustomerReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : ICustomerProfileStore
{
    private const int MaximumReferenceAttempts = 5;
    private const string CustomerReferenceAlternateKey = "AK_CustomerProfiles_CustomerReference";
    private const string IssuerObjectIdAlternateKey = "AK_CustomerProfiles_Issuer_ObjectId";

    public async Task<CustomerProfile> GetOrCreateAsync(
        AuthenticatedSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject.Issuer);

        for (var attempt = 1; attempt <= MaximumReferenceAttempts; attempt++)
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await FindAsync(context, subject.Issuer, subject.ObjectId, cancellationToken);
            if (existing is not null)
            {
                return ToProfile(existing);
            }

            var now = timeProvider.GetUtcNow();
            var entity = new CustomerProfileEntity
            {
                Id = Guid.NewGuid(),
                CustomerReference = referenceGenerator.Create(),
                Issuer = subject.Issuer,
                ObjectId = subject.ObjectId,
                ContactEmail = IsValidEmail(subject.VerifiedContactEmail) ? subject.VerifiedContactEmail : null,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.Add(entity);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return ToProfile(entity);
            }
            catch (DbUpdateException exception) when (IsNamedUniqueViolation(exception, CustomerReferenceAlternateKey))
            {
                if (attempt == MaximumReferenceAttempts)
                {
                    throw new InvalidOperationException("Unable to generate a unique customer reference.", exception);
                }
            }
            catch (DbUpdateException exception) when (IsNamedUniqueViolation(exception, IssuerObjectIdAlternateKey))
            {
                var winner = await FindWinnerAsync(subject, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }

                throw;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique customer reference.");
    }

    public async Task<CustomerProfile?> FindByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.CustomerProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.CustomerReference == customerReference, cancellationToken);
        return entity is null ? null : ToProfile(entity);
    }

    private async Task<CustomerProfile?> FindWinnerAsync(AuthenticatedSubject subject, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindAsync(context, subject.Issuer, subject.ObjectId, cancellationToken);
        return entity is null ? null : ToProfile(entity);
    }

    private static Task<CustomerProfileEntity?> FindAsync(
        CloudOrdersDbContext context,
        string issuer,
        Guid objectId,
        CancellationToken cancellationToken) =>
        context.CustomerProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.Issuer == issuer && profile.ObjectId == objectId, cancellationToken);

    private static CustomerProfile ToProfile(CustomerProfileEntity entity) =>
        new(entity.Id, entity.CustomerReference, entity.Issuer, entity.ObjectId, entity.ContactEmail);

    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return string.Equals(new MailAddress(value).Address, value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsNamedUniqueViolation(DbUpdateException exception, string keyName) =>
        exception.InnerException is SqlException sqlException
        && sqlException.Errors.Cast<SqlError>().Any(error =>
            error.Number is 2601 or 2627
            && error.Message.Contains(keyName, StringComparison.Ordinal));
}
