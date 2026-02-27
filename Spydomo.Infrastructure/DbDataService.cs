using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spydomo.Models;
using Spydomo.Utilities;

namespace Spydomo.Infrastructure
{
    public class DbDataService
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        private readonly ILogger<DbDataService> _logger;

        public DbDataService(
            IDbContextFactory<SpydomoContext> dbFactory,
            ILogger<DbDataService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        // ✅ Get all records for any entity type
        public async Task<List<T>> GetAllAsync<T>(CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Set<T>().ToListAsync(ct);
        }

        // ✅ Get an entity by ID
        public async Task<T?> GetByIdAsync<T>(int id, CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            // FindAsync supports CT via ValueTask overload in newer EF; safest is:
            return await db.Set<T>().FindAsync(new object[] { id }, ct);
        }

        // ✅ Add a new entity
        public async Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Set<T>().Add(entity);
            await db.SaveChangesAsync(ct);
        }

        // ✅ Update an existing entity
        public async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Set<T>().Update(entity);
            await db.SaveChangesAsync(ct);
        }

        // ✅ Delete an entity
        public async Task DeleteAsync<T>(T entity, CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Set<T>().Remove(entity);
            await db.SaveChangesAsync(ct);
        }

        // ✅ Load an entity as a dictionary (e.g., { id => entity })
        public async Task<Dictionary<int, T>> LoadEntityDictionaryAsync<T>(
            Func<T, int> keySelector,
            CancellationToken ct = default) where T : class
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Set<T>().ToDictionaryAsync(keySelector, t => t, ct);
        }

        public async Task<(string CompanyName, List<string> Keywords)> GetCompanyContextAsync(
            int companyId,
            int maxKeywords = 10,
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
            if (company == null)
                throw new InvalidOperationException($"Company with ID {companyId} not found.");

            var keywordTuples = await db.CompanyKeywords.AsNoTracking()
                .Where(k => k.CompanyId == companyId)
                .Select(k => new { k.Keyword, k.Confidence })
                .ToListAsync(ct);

            var contextKeywords = KeywordHelper.GetTopDistinctiveKeywords(
                keywordTuples.Select(k => (k.Keyword, k.Confidence)).ToList(),
                company.Name,
                maxKeywords
            );

            return (company.Name, contextKeywords);
        }

        public async Task AddFilteredUrlAsync(
            int companyId,
            string postUrl,
            int sourceTypeId,
            string reason,
            CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // If you have (CompanyId, PostUrl, SourceTypeId) unique index, you can skip the Exists check
            // and catch DbUpdateException instead. For now, keep your logic:
            bool alreadyExists = await db.FilteredUrls.AsNoTracking().AnyAsync(f =>
                f.CompanyId == companyId &&
                f.PostUrl == postUrl &&
                f.SourceTypeId == sourceTypeId, ct);

            if (alreadyExists)
                return;

            db.FilteredUrls.Add(new FilteredUrl
            {
                CompanyId = companyId,
                PostUrl = postUrl,
                SourceTypeId = sourceTypeId,
                Reason = reason,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Added FilteredUrl companyId={CompanyId} sourceTypeId={SourceTypeId} url={Url} reason={Reason}",
                companyId, sourceTypeId, postUrl, reason);
        }
    }

}
