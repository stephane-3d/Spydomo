using Microsoft.EntityFrameworkCore;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;

namespace Spydomo.Infrastructure.PulseRules
{
    public sealed class PulseObservationRepository : IPulseObservationRepository
    {
        private readonly IDbContextFactory<SpydomoContext> _dbFactory;
        public PulseObservationRepository(IDbContextFactory<SpydomoContext> dbFactory) => _dbFactory = dbFactory;

        public async Task UpsertTodayAsync(int companyId, string type, string topicKey, DateTime nowUtc, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var today = DateOnly.FromDateTime(nowUtc);

            var row = await db.PulseObservationIndices
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId &&
                    x.Type == type &&
                    x.TopicKey == topicKey &&
                    x.DateBucket == today, ct);

            if (row is null)
            {
                db.PulseObservationIndices.Add(new PulseObservationIndex
                {
                    CompanyId = companyId,
                    Type = type,
                    TopicKey = topicKey,
                    DateBucket = today,
                    FirstSeenAt = nowUtc,
                    LastSeenAt = nowUtc,
                    Count = 1
                });
            }
            else
            {
                row.LastSeenAt = nowUtc;
                row.Count += 1;
                // no need for Update(row) when tracked
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<bool> ExistsTodayAsync(int companyId, string type, string topicKey, DateOnly today, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.PulseObservationIndices
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Type == type && x.TopicKey == topicKey && x.DateBucket == today, ct);
        }

        public async Task<DateTime?> GetLastNotifiedAtAsync(int companyId, string type, string topicKey, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var row = await db.PulseTopicStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Type == type && x.TopicKey == topicKey, ct);

            return row?.LastNotifiedAt;
        }

        public async Task SetLastNotifiedAtAsync(int companyId, string type, string topicKey, DateTime whenUtc, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var row = await db.PulseTopicStates
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Type == type && x.TopicKey == topicKey, ct);

            if (row is null)
            {
                db.PulseTopicStates.Add(new PulseTopicState
                {
                    CompanyId = companyId,
                    Type = type,
                    TopicKey = topicKey,
                    LastNotifiedAt = whenUtc,
                    UpdatedAt = whenUtc
                });
            }
            else
            {
                row.LastNotifiedAt = whenUtc;
                row.UpdatedAt = whenUtc;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<int> CountSinceAsync(int companyId, string type, string topicKey, DateTime sinceUtc, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var sinceDay = DateOnly.FromDateTime(sinceUtc);

            return await db.PulseObservationIndices
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId
                            && x.Type == type
                            && x.TopicKey == topicKey
                            && x.DateBucket >= sinceDay)
                .SumAsync(x => x.Count, ct);
        }
    }
}
