using Microsoft.Extensions.DependencyInjection;

namespace SBD.ServiceRegistry;

public class ServiceRegistrationOptions
{
    public int Port { get; set; } = 5000;
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string HealthUrl { get; set; } = "http://localhost:5000/health";
}

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        string serviceName,
        Action<ServiceRegistrationOptions>? configure = null)
    {
        var options = new ServiceRegistrationOptions();
        configure?.Invoke(options);
        return services;
    }
}
