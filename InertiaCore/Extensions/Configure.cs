using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

        // Check if TempData services are available for error bag functionality
        CheckTempDataAvailability(app);

        app.UseMiddleware<Middleware>();

        return app;
    }

    /// <summary>Globally enable Inertia history encryption for all routes.</summary>
    public static IApplicationBuilder UseInertiaEncryptHistory(this IApplicationBuilder app)
    {
        app.UseMiddleware<EncryptHistoryMiddleware>();
        return app;
    }

    /// <summary>
    /// Register a pipeline stage that catches unhandled exceptions from
    /// downstream middleware and, for Inertia requests, hands them to the
    /// callback registered via <see cref="Inertia.HandleExceptionsUsing"/>.
    /// If the callback configures a component via
    /// <c>response.Render(...)</c>, the corresponding Inertia page is written
    /// to the response. Otherwise the exception is re-thrown so the default
    /// ASP.NET Core exception handler can take over.
    /// <para>
    /// Register this early in the pipeline (before any middleware that writes
    /// to the response body) so the exception handler still has a clean
    /// response to write into. If <c>UseInertiaExceptionHandler</c> is placed
    /// after response buffering begins, the rendered error page may fail to
    /// serialize cleanly.
    /// </para>
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="handler">
    /// Optional inline callback. If supplied, this is a shortcut for calling
    /// <see cref="Inertia.HandleExceptionsUsing"/> during pipeline configuration.
    /// </param>
    public static IApplicationBuilder UseInertiaExceptionHandler(this IApplicationBuilder app,
        Action<ExceptionResponse>? handler = null)
    {
        if (handler != null)
        {
            var factory = app.ApplicationServices.GetRequiredService<IResponseFactory>();
            factory.HandleExceptionsUsing(handler);
        }

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                if (!context.IsInertiaRequest())
                {
                    throw;
                }

                var factory = context.RequestServices.GetRequiredService<IResponseFactory>();
                var callback = factory.GetExceptionHandler();
                if (callback == null)
                {
                    throw;
                }

                if (context.Response.HasStarted)
                {
                    throw;
                }

                var exceptionResponse = new ExceptionResponse(ex, context);
                callback(exceptionResponse);

                if (!exceptionResponse.HasComponent)
                {
                    throw;
                }

                await exceptionResponse.ExecuteAsync(factory);
            }
        });
    }

    private static void CheckTempDataAvailability(IApplicationBuilder app)
    {
        // Skip warning in test environments
        var environment = app.ApplicationServices.GetService<IWebHostEnvironment>();
        if (environment?.EnvironmentName == "Test" ||
            (environment?.EnvironmentName != "Development" && IsTestEnvironment()))
        {
            return;
        }

        try
        {
            var tempDataFactory = app.ApplicationServices.GetService<ITempDataDictionaryFactory>();
            if (tempDataFactory == null)
            {
                var logger = app.ApplicationServices.GetService<ILogger<IApplicationBuilder>>();
                logger?.LogWarning("TempData services are not configured. Error bag functionality will be limited. " +
                                   "Consider adding services.AddSession() and app.UseSession() to enable full error bag support.");
            }
        }
        catch (Exception)
        {
            // If we can't check for TempData services, that's also a sign they might not be configured
            var logger = app.ApplicationServices.GetService<ILogger<IApplicationBuilder>>();
            logger?.LogWarning("Unable to verify TempData configuration. Error bag functionality may be limited. " +
                               "Ensure services.AddSession() and app.UseSession() are configured for full error bag support.");
        }
    }

    private static bool IsTestEnvironment()
    {
        // Check if we're running in a test context by looking for common test assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        return assemblies.Any(a =>
            a.FullName?.Contains("nunit", StringComparison.OrdinalIgnoreCase) == true ||
            a.FullName?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true ||
            a.FullName?.Contains("mstest", StringComparison.OrdinalIgnoreCase) == true ||
            a.FullName?.Contains("testhost", StringComparison.OrdinalIgnoreCase) == true);
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
