using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.FileSystem;
using CentralPackageMigrator.Infrastructure.Process;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Infrastructure;

/// <summary>
/// Extension methods for registering all infrastructure and core services
/// into a <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Central Package Migrator services — both the infrastructure
    /// implementations (file system, process runner, serializer) and the core
    /// domain services (detection, scanning, collection, conflict detection,
    /// planning, and file editing).
    /// </summary>
    public static IServiceCollection AddCentralPackageMigrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Infrastructure services
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IProcessRunner, DotNetProcessRunner>();
        services.AddSingleton<PlanSerializer>();

        // Core domain services
        services.AddSingleton<ISolutionDetector, SolutionDetector>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IPackageCollector, PackageCollector>();
        services.AddSingleton<IConflictDetector, ConflictDetector>();
        services.AddSingleton<IMigrationPlanner, MigrationPlanner>();
        services.AddSingleton<IFileEditEngine, FileEditEngine>();

        return services;
    }
}
