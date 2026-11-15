using System.Reflection;
using Microsoft.OpenApi.Models;

namespace GLMS.Api.Infrastructure;

/// <summary>Swagger / OpenAPI: the API documents itself at /swagger, with an Authorize button for the JWT.</summary>
public static class SwaggerSetup
{
    public static IServiceCollection AddGlmsSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "TechMove GLMS API",
                Version = "v1",
                Description = "Service layer of the Global Logistics Management System. The ONLY component that talks to SQL Server. " +
                              "Sign in with POST /api/auth/login (admin / Admin@12345 in the demo), then click Authorize and paste the token."
            });

            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Paste the accessToken returned by POST /api/auth/login.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });

            // XML comments (/// summaries on the controllers) become the operation descriptions.
            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) options.IncludeXmlComments(xml);
        });
        return services;
    }

    public static WebApplication UseGlmsSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "GLMS API v1");
            options.DocumentTitle = "TechMove GLMS API";
        });
        return app;
    }
}
