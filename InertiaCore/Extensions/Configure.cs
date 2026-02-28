using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaCore.Extensions;

public static class Configure
{
    public static IApplicationBuilder UseInertia(this IApplicationBuilder app)
    {
        var factory = app.ApplicationServices.GetRequiredService<IResponseFactory>();
        Inertia.UseFactory(factory);

        var viteBuilder = app.ApplicationServices.GetService<IViteBuilder>();
        if (viteBuilder != null)
        {
            Vite.UseBuilder(viteBuilder);
            Inertia.Version(Vite.GetManifestHash);
        }

        app.UseMiddleware<Middleware>();

        return app;
    }

    public static IServiceCollection AddInertia(this IServiceCollection services,
        Action<InertiaOptions>? options = null)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient();

        services.AddSingleton<IResponseFactory, ResponseFactory>();
        services.AddSingleton<IGateway, Gateway>();
        services.AddSingleton<IInertiaSerializer, DefaultInertiaSerializer>();

        services.Configure<MvcOptions>(mvcOptions => { mvcOptions.Filters.Add<InertiaActionFilter>(); });

        if (options != null) services.Configure(options);

        return services;
    }

    public static IServiceCollection UseInertiaSerializer<TImplementation>(this IServiceCollection services)
        where TImplementation : IInertiaSerializer
    {
        services.Replace(
            new ServiceDescriptor(typeof(IInertiaSerializer), typeof(TImplementation), ServiceLifetime.Singleton)
        );

        return services;
    }

    public static IServiceCollection AddViteHelper(this IServiceCollection services,
        Action<ViteOptions>? options = null)
    {
        services.AddSingleton<IViteBuilder, ViteBuilder>();
        if (options != null) services.Configure(options);

        return services;
    }
}
