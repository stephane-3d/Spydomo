using Spydomo.DTO;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface ICurrentUserState
    {
        Task<UserDto?> GetOrLoadAsync(CancellationToken ct = default);
        Task<int?> GetClientIdAsync(CancellationToken ct = default);
        bool IsInRole(string role);
        void Clear();
    }

}
