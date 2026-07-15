using Microsoft.Extensions.DependencyInjection;
using TeknikServis.Application.Interfaces;
using TeknikServis.Application.Services;

namespace TeknikServis.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITicketService, TicketService>();
        return services;
    }
}
