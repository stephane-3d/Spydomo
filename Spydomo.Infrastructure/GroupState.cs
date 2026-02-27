using Spydomo.DTO;
using Spydomo.Infrastructure.Interfaces;

namespace Spydomo.Infrastructure
{
    public sealed class GroupState : IDisposable
    {
        private readonly ICompanyGroupService _groupService;
        private readonly IClientContextService _clientCtx;

        public IReadOnlyList<CompanyGroupDto> Groups { get; private set; } = Array.Empty<CompanyGroupDto>();
        public bool IsLoaded { get; private set; }

        public event Action? OnChange;

        public GroupState(ICompanyGroupService groupService, IClientContextService clientCtx)
        {
            _groupService = groupService;
            _clientCtx = clientCtx;
        }

        public async Task EnsureLoadedAsync()
        {
            if (IsLoaded) return;
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            var clientId = await _clientCtx.GetCurrentClientIdAsync();
            var list = await _groupService.GetCompanyGroupsForClientAsync(clientId);
            Groups = list ?? new List<CompanyGroupDto>();
            IsLoaded = true;
            OnChange?.Invoke();
        }

        /// Call this after you add/delete if you already know the new list and want to set it directly
        public void SetGroups(IEnumerable<CompanyGroupDto> groups)
        {
            Groups = groups.ToList();
            IsLoaded = true;
            OnChange?.Invoke();
        }

        public void Dispose() => OnChange = null;
    }

}
