using System.Text;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralPackageMigrator.Integration.Tests;

/// <summary>
/// Encoding details that only show up on a real file system.
/// </summary>
public sealed class FileEncodingTests : IDisposable
{
    private const string ProjectXml =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <ItemGroup>\r\n" +
        "    <PackageReference Include=\"Pkg\" Version=\"1.0.0\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>\r\n";

    private readonly string _root;
    private readonly ServiceProvider _services;

    public FileEncodingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cpm-enc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _services = new ServiceCollection().AddCentralPackageMigrator().AddLogging().BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Keeps_the_byte_order_mark_as_it_was(bool withBom)
    {
        string path = Path.Combine(_root, "A", "A.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ProjectXml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));

        Migrate();

        byte[] bytes = File.ReadAllBytes(path);
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.Should().Be(withBom);
    }

    [Fact]
    public void Keeps_CRLF_line_endings()
    {
        string path = Path.Combine(_root, "A", "A.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ProjectXml);

        Migrate();

        File.ReadAllText(path).Should().Be(ProjectXml.Replace(" Version=\"1.0.0\"", string.Empty, StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(_root, "Directory.Packages.props")).Should().NotMatchRegex("[^\r]\n");
    }

    private void Migrate()
    {
        var service = _services.GetRequiredService<IMigrationService>();
        service.Apply(new MigrationRequest { RootPath = _root, WorkingDirectory = _root }, dryRun: false);
    }
}
