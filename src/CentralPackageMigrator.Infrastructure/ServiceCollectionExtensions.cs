using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.FileSystem;
using CentralPackageMigrator.Infrastructure.Process;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CentralPackageMigrator.Infrastructure;

/// <summary>
/// Extension methods for registering all infrastructure and core services
/// into a <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Central Package Migrator services — both the infrastructure
    /// implementations (file system, process runner, serializer, clock) and the core
    /// domain services (detection, scanning, collection, conflict detection,
    /// planning, file editing and orchestration). Existing registrations of
    /// <see cref="IFileSystem"/>, <see cref="IProcessRunner"/> and <see cref="TimeProvider"/>
    /// are kept, so hosts and tests can substitute them.
    /// </summary>
    public static IServiceCollection AddCentralPackageMigrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Infrastructure services
        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.TryAddSingleton<IProcessRunner, DotNetProcessRunner>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PlanSerializer>();

        // Core domain services
        services.AddSingleton<ISolutionDetector, SolutionDetector>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IPackageCollector, PackageCollector>();
        services.AddSingleton<IConflictDetector, ConflictDetector>();
        services.AddSingleton<IMigrationPlanner, MigrationPlanner>();
        services.AddSingleton<IFileEditEngine, FileEditEngine>();
        services.AddSingleton<IMigrationService, MigrationService>();

        return services;
    }
}
