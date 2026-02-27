using Spydomo.Models;

namespace Spydomo.Infrastructure.Interfaces
{
    public interface IContentAdapter
    {
        string GetCanonicalText(RawContent content);
    }

}
