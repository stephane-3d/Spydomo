using Spydomo.Common.Enums;

namespace Spydomo.Infrastructure.PulseRules
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RuleMetaAttribute : Attribute
    {
        /// <summary>Lower runs earlier inside the track.</summary>
        public int Order { get; init; } = 100;

        /// <summary>Optional: only apply to a specific source type.</summary>
        public DataSourceTypeEnum? AppliesToSource { get; init; }
    }
}
