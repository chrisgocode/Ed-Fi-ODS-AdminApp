using System.Reflection;
using EdFi.Admin.DataAccess.Contexts;
using EdFi.Ods.Admin.Api.ActionFilters;
using EdFi.Ods.Admin.Api.Infrastructure.Security;
using EdFi.Ods.AdminApp.Management;
using EdFi.Ods.AdminApp.Management.Api;
using EdFi.Ods.AdminApp.Management.Database;
using EdFi.Security.DataAccess.Contexts;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
namespace EdFi.Ods.Admin.Api.Infrastructure;

public static class WebApplicationBuilderExtensions
{
    public static void AddServices(this WebApplicationBuilder webApplicationBuilder)
    {
        var executingAssembly = Assembly.GetExecutingAssembly();
        webApplicationBuilder.Services.AddAutoMapper(executingAssembly);
        webApplicationBuilder.Services.AddScoped<InstanceContext>();

        foreach (var type in typeof(IMarkerForEdFiOdsAdminAppManagement).Assembly.GetTypes())
        {
            if (type.IsClass && !type.IsAbstract && (type.IsPublic || type.IsNestedPublic))
            {
                var concreteClass = type;

                if (concreteClass == typeof(OdsApiFacade))
                    continue; //IOdsApiFacade is never resolved. Instead, classes inject IOdsApiFacadeFactory.

                if (concreteClass == typeof(OdsRestClient))
                    continue; //IOdsRestClient is never resolved. Instead, classes inject IOdsRestClientFactory.

                if (concreteClass == typeof(TokenRetriever))
                    continue; //ITokenRetriever is never resolved. Instead, other dependencies construct TokenRetriever directly.

                var interfaces = concreteClass.GetInterfaces().ToArray();

                if (concreteClass.Namespace != null)
                {
                    if (concreteClass.Namespace.EndsWith("Database.Commands") || concreteClass.Namespace.EndsWith("Database.Queries"))
                    {
                        if (interfaces.Length == 1)
                        {
                            var serviceType = interfaces.Single();
                            if (serviceType.FullName == $"{concreteClass.Namespace}.I{concreteClass.Name}")
                                webApplicationBuilder.Services.AddTransient(serviceType, concreteClass);
                        }
                        else if (interfaces.Length == 0)
                        {
                            if (concreteClass.Name.EndsWith("Command") || concreteClass.Name.EndsWith("Query"))
                            {
                                webApplicationBuilder.Services.AddTransient(concreteClass);
                            }
                        }
                    }
                }
            }
        }

        // Add services to the container.
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        webApplicationBuilder.Services.AddEndpointsApiExplorer();
        var issuer = webApplicationBuilder.Configuration.GetValue<string>("Authentication:IssuerUrl");
        webApplicationBuilder.Services.AddSwaggerGen(opt =>
        {
            opt.CustomSchemaIds(x => x.FullName);
            opt.OperationFilter<TokenEndpointBodyDescriptionFilter>();
            opt.AddSecurityDefinition(
                "oauth",
                new OpenApiSecurityScheme
                {
                    Flows = new OpenApiOAuthFlows
                    {
                        ClientCredentials = new OpenApiOAuthFlow
                        {
                            TokenUrl = new Uri($"{issuer}{SecurityConstants.TokenEndpointUri}"),
                            Scopes = new Dictionary<string, string>
                            {
                                { SecurityConstants.Scopes.AdminApiFullAccess, "Unrestricted access to all Admin API endpoints" },
                            }
                        },
                    },
                    In = ParameterLocation.Header,
                    Name = HeaderNames.Authorization,
                    Type = SecuritySchemeType.OAuth2
                }
            );
            opt.AddSecurityRequirement(
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                                { Type = ReferenceType.SecurityScheme, Id = "oauth" },
                        },
                        new[] { "api" }
                    }
                }
            );

            opt.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Admin API Documentation", Version = "v1"
            });
            opt.DocumentFilter<OperationResponsesDocumentFilter>();
            opt.DocumentFilter<RemoveSchemaDocumentFilter>();
            opt.DocumentFilter<AddRegisterSchemaDocumentFilter>();
            opt.OperationFilter<OperationDescriptionFilter>();
            opt.SchemaFilter<SwaggerRequiredSchemaFilter>();
            opt.CustomSchemaIds(x =>
            {
                var customSchemaName = x.GetCustomAttributes<DisplaySchemaNameAttribute>().SingleOrDefault();
                return customSchemaName != null ? customSchemaName.Name : x.FullName;
            });
            opt.EnableAnnotations();
            opt.OrderActionsBy(x =>
            {
                return x.HttpMethod != null ? x.HttpMethod.Equals("GET", StringComparison.InvariantCultureIgnoreCase) ? "0"
                    : x.HttpMethod.Equals("POST", StringComparison.InvariantCultureIgnoreCase) ? "1"
                    : x.HttpMethod.Equals("PUT", StringComparison.InvariantCultureIgnoreCase) ? "2"
                    : x.HttpMethod.Equals("DELETE", StringComparison.InvariantCultureIgnoreCase) ? "3" : "4" : "5";
            });
        });

        // Logging
        var loggingOptions = webApplicationBuilder.Configuration.GetSection("Log4NetCore").Get<Log4NetProviderOptions>();
        webApplicationBuilder.Logging.AddLog4Net(loggingOptions);

        // Fluent validation
        webApplicationBuilder.Services.AddFluentValidation(
            opt =>
            {
                opt.RegisterValidatorsFromAssembly(executingAssembly);

                opt.ValidatorOptions.DisplayNameResolver = (type, memberInfo, expression)
                    => memberInfo?
                        .GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>()?.GetName();
            });

        //Databases
        var databaseEngine = webApplicationBuilder.Configuration["AppSettings:DatabaseEngine"];
        var (connectionString, isSqlServer) = webApplicationBuilder.AddDatabases(databaseEngine);

        //Health
        webApplicationBuilder.Services.AddHealthCheck(connectionString, isSqlServer);

        //JSON
        webApplicationBuilder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.WriteIndented = true;
        });

        webApplicationBuilder.Services.AddSecurityUsingOpenIddict(webApplicationBuilder.Configuration, webApplicationBuilder.Environment);
    }

    private static (string adminConnectionString, bool) AddDatabases(this WebApplicationBuilder webApplicationBuilder, string databaseEngine)
    {
        var adminConnectionString = webApplicationBuilder.Configuration.GetConnectionString("Admin");
        var securityConnectionString = webApplicationBuilder.Configuration.GetConnectionString("Security");

        if (DatabaseEngineEnum.Parse(databaseEngine).Equals(DatabaseEngineEnum.PostgreSql))
        {
            webApplicationBuilder.Services.AddDbContext<AdminAppDbContext>(
                options => options.UseNpgsql(adminConnectionString));

            webApplicationBuilder.Services.AddDbContext<AdminApiDbContext>(
                options =>
                {
                    options.UseNpgsql(adminConnectionString);
                    options.UseOpenIddict<ApiApplication, ApiAuthorization, ApiScope, ApiToken, int>();
                });

            webApplicationBuilder.Services.AddScoped<ISecurityContext>(
                sp => new PostgresSecurityContext(securityConnectionString));

            webApplicationBuilder.Services.AddScoped<IUsersContext>(
                sp => new PostgresUsersContext(adminConnectionString));

            return (adminConnectionString, false);
        }

        if (DatabaseEngineEnum.Parse(databaseEngine).Equals(DatabaseEngineEnum.SqlServer))
        {
            webApplicationBuilder.Services.AddDbContext<AdminAppDbContext>(
                options => options.UseSqlServer(adminConnectionString));

            webApplicationBuilder.Services.AddDbContext<AdminApiDbContext>(
                options =>
                {
                    options.UseSqlServer(adminConnectionString);
                    options.UseOpenIddict<ApiApplication, ApiAuthorization, ApiScope, ApiToken, int>();
                });

            webApplicationBuilder.Services.AddScoped<ISecurityContext>(
                sp => new SqlServerSecurityContext(securityConnectionString));

            webApplicationBuilder.Services.AddScoped<IUsersContext>(
                sp => new SqlServerUsersContext(adminConnectionString));

            return (adminConnectionString, true);
        }

        throw new Exception($"Unexpected DB setup error. Engine '{databaseEngine}' was parsed as valid but is not configured for startup.");
    }
}