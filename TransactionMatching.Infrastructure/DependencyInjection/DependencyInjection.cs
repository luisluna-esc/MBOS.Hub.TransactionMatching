using TransactionMatching.Application.Interfaces.Reconciliation;
using TransactionMatching.Application.Dtos.Security;
using TransactionMatching.Application.Interfaces.Audit;
using TransactionMatching.Application.Interfaces.Security;
using TransactionMatching.Application.Services;
using TransactionMatching.Application.Validators.Security;
using TransactionMatching.Core.Configuration;
using TransactionMatching.Core.Interfaces;
using TransactionMatching.Infrastructure.Context;
using TransactionMatching.Infrastructure.Context.Core;
using TransactionMatching.Infrastructure.Interceptors;
using TransactionMatching.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TransactionMatching.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing connection string 'DefaultConnection' in appsettings.");

        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options.UseNpgsql(connectionString)
                   .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        services.Configure<PaginationOptions>(configuration.GetSection("Pagination"));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<PaginationOptions>>().Value);

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<JwtOptions>>().Value);

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IValidator<RoleDto>, RoleValidator>();

        services.AddScoped<IAppUserService, AppUserService>();
        services.AddScoped<IValidator<AppUserDto>, AppUserValidator>();

        services.AddScoped<IAppUserRoleService, AppUserRoleService>();

        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IValidator<MenuDto>, MenuValidator>();
        services.AddScoped<IRoleMenuService, RoleMenuService>();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();

        services.Configure<AuditRetentionOptions>(configuration.GetSection("AuditRetention"));
        services.AddScoped(sp => sp.GetRequiredService<IOptions<AuditRetentionOptions>>().Value);
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Conciliación banco - ASSINET.
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddScoped<IBankAccountService, BankAccountService>();


        return services;
    }
}
