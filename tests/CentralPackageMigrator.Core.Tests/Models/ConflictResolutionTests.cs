using CentralPackageMigrator.Core.Models;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Models;

public sealed class ConflictResolutionTests
{
    [Theory]
    [InlineData("Newtonsoft.Json=13.0.3", "Newtonsoft.Json", "13.0.3")]
    [InlineData("Serilog=3.1.1-dev.42", "Serilog", "3.1.1-dev.42")]
    [InlineData("Pkg=[1.0,2.0)", "Pkg", "[1.0,2.0)")]
    [InlineData("Pkg=1.*", "Pkg", "1.*")]
    [InlineData(" Pkg = 1.0.0 ", "Pkg", "1.0.0")]
    public void Parses_valid_entries(string entry, string id, string version)
    {
        var resolution = ConflictResolution.Parse(entry);

        resolution.PackageId.Should().Be(id);
        resolution.Version.Should().Be(version);
    }

    [Theory]
    [InlineData("Pkg")]
    [InlineData("=1.0.0")]
    [InlineData("Pkg=")]
    [InlineData("Pkg=not-a-version")]
    [InlineData("Pkg=1.0\"/><Target Name=\"Evil\"")]
    [InlineData("Bad Id=1.0.0")]
    [InlineData("../x=1.0.0")]
    public void Rejects_invalid_entries(string entry)
    {
        var act = () => ConflictResolution.Parse(entry);

        act.Should().Throw<MigrationException>().Which.Code.Should().Be(ErrorCodes.InvalidArguments);
    }

    [Fact]
    public void Collapses_identical_duplicates()
    {
        var result = ConflictResolution.ParseMany(["Pkg=1.0.0", "pkg=1.0.0"]);

        result.Should().ContainSingle();
    }

    [Fact]
    public void Rejects_contradictory_duplicates()
    {
        var act = () => ConflictResolution.ParseMany(["Pkg=1.0.0", "Pkg=2.0.0"]);

        act.Should().Throw<MigrationException>()
            .Which.Code.Should().Be(ErrorCodes.InvalidArguments);
    }

    [Fact]
    public void Null_input_yields_no_resolutions()
    {
        ConflictResolution.ParseMany(null).Should().BeEmpty();
    }
}
