using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Spydomo.Infrastructure.PulseRules
{
    public static class DiReflectionRegistrar
    {
        public static IServiceCollection AddByInterfaceScan(
            this IServiceCollection services,
            ServiceLifetime lifetime,
            IEnumerable<Assembly> assemblies,
            params Type[] serviceInterfaceRoots)
        {
            bool IsServiceInterface(Type t) =>
                t.IsInterface && serviceInterfaceRoots.Any(root => root.IsAssignableFrom(t));

            var allTypes = assemblies.SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
            });

            foreach (var impl in allTypes.Where(t =>
                         t is { IsAbstract: false, IsInterface: false } &&
                         !t.IsGenericTypeDefinition))
            {
                var serviceIfaces = impl.GetInterfaces().Where(IsServiceInterface).ToArray();
                if (serviceIfaces.Length == 0) continue;

                foreach (var @iface in serviceIfaces)
                {
                    var descriptor = ServiceDescriptor.Describe(@iface, impl, lifetime);

                    // ✅ prevents duplicate registrations of the same (iface, impl) pair
                    services.TryAddEnumerable(descriptor);
                }
            }

            return services;
        }
    }
}
