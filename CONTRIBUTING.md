# Contributing

Thank you for your interest in contributing to `cpm-migrate`.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Git

## Building Locally

```bash
git clone https://github.com/your-org/cpm-migrate.git
cd cpm-migrate
dotnet build
```

## Running Tests

```bash
# All tests
dotnet test

# Unit tests only
dotnet test tests/CentralPackageMigrator.Core.Tests

# Golden file tests
dotnet test tests/CentralPackageMigrator.Golden.Tests

# Integration tests
dotnet test tests/CentralPackageMigrator.Integration.Tests

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Running the CLI Locally

```bash
# Against the current directory
dotnet run --project src/CentralPackageMigrator.Cli -- analyze

# Against a specific path
dotnet run --project src/CentralPackageMigrator.Cli -- analyze --root /path/to/solution

# Dry-run apply
dotnet run --project src/CentralPackageMigrator.Cli -- apply --dry-run --root /path/to/solution

# JSON output
dotnet run --project src/CentralPackageMigrator.Cli -- plan --json --root /path/to/solution
```

## Debugging

1. Open the solution in your IDE
2. Set `CentralPackageMigrator.Cli` as the startup project
3. Set command-line arguments (e.g., `analyze --root /path/to/test-solution --verbose`)
4. Set breakpoints and run in debug mode

## Building Release Binaries Locally

```bash
# macOS ARM64
dotnet publish src/CentralPackageMigrator.Cli -c Release --self-contained -r osx-arm64 -p:PublishSingleFile=true -o ./publish

# Linux x64
dotnet publish src/CentralPackageMigrator.Cli -c Release --self-contained -r linux-x64 -p:PublishSingleFile=true -o ./publish

# Windows x64
dotnet publish src/CentralPackageMigrator.Cli -c Release --self-contained -r win-x64 -p:PublishSingleFile=true -o ./publish
```

## Code Style

- Follow the `.editorconfig` rules
- Use file-scoped namespaces
- Prefer immutable records for data types
- All public methods on domain services must validate arguments
- Use `Microsoft.Extensions.Logging` for structured logging
- Run `dotnet format` before submitting

## PR Process

1. Fork the repository
2. Create a feature branch from `main`
3. Make your changes with tests
4. Run `dotnet test` and `dotnet format --verify-no-changes`
5. Open a pull request with a clear description
6. CI must pass before merge

## Adding Test Scenarios

To add a new golden file test:

1. Create a new directory under `tests/CentralPackageMigrator.Golden.Tests/TestData/`
2. Add `.csproj` files representing the scenario
3. Add a test method in `GoldenFileTests.cs` using `LoadScenario("your-scenario")`
4. Assert on the expected plan output
