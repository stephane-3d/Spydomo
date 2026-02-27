using Spydomo.DTO;
using System.Collections.Concurrent;

namespace Spydomo.Web.Classes
{
    public readonly record struct SignalsKey(int ClientId, int? GroupId, int PeriodDays);

    public sealed class DashboardState
    {
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(3);

        private readonly ConcurrentDictionary<SignalsKey, (DateTimeOffset ts, List<StrategicSignalDto> items)> _map
            = new();

        public bool TryGet(SignalsKey key, out List<StrategicSignalDto> items, out TimeSpan age)
        {
            if (_map.TryGetValue(key, out var entry))
            {
                age = DateTimeOffset.UtcNow - entry.ts;
                items = entry.items;
                return true;
            }
            items = new(); age = default;
            return false;
        }

        public void Put(SignalsKey key, List<StrategicSignalDto> items)
            => _map[key] = (DateTimeOffset.UtcNow, items);

        // Optional invalidators you can call after edits/ingestion jobs:
        public void InvalidateClient(int clientId)
        {
            foreach (var k in _map.Keys.Where(k => k.ClientId == clientId).ToList())
                _map.TryRemove(k, out _);
        }
        public void InvalidateGroup(int clientId, int groupId)
        {
            foreach (var k in _map.Keys.Where(k => k.ClientId == clientId && k.GroupId == groupId).ToList())
                _map.TryRemove(k, out _);
        }
    }
}
