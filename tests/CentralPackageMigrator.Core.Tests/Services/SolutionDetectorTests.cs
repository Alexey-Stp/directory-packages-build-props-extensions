using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class SolutionDetectorTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly SolutionDetector _sut;

    public SolutionDetectorTests()
    {
        _sut = new SolutionDetector(_fs, NullLogger<SolutionDetector>.Instance);
    }

    [Fact]
    public void Finds_sln_file_in_start_directory()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");

        var result = _sut.Detect(root);

        result.DetectionMethod.Should().Be(RootDetectionMethod.SolutionFile);
        result.SolutionFilePath.Should().Contain("MySolution.sln");
        result.RootPath.Should().Be(root);
    }

    [Fact]
    public void Finds_sln_file_in_parent_directory()
    {
        string root = Path.GetFullPath("/repo");
        string subDir = Path.Combine(root, "src", "MyProject");
        _fs.AddDirectory(subDir);
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");

        var result = _sut.Detect(subDir);

        result.DetectionMethod.Should().Be(RootDetectionMethod.SolutionFile);
        result.SolutionFilePath.Should().Contain("MySolution.sln");
    }

    [Fact]
    public void Falls_back_to_git_root_when_no_sln()
    {
        string root = Path.GetFullPath("/repo");
        string subDir = Path.Combine(root, "src");
        _fs.AddDirectory(subDir);
        _fs.AddDirectory(Path.Combine(root, ".git"));

        var result = _sut.Detect(subDir);

        result.DetectionMethod.Should().Be(RootDetectionMethod.GitRoot);
        result.RootPath.Should().Be(root);
        result.SolutionFilePath.Should().BeNull();
    }

    [Fact]
    public void Falls_back_to_current_directory_when_no_sln_and_no_git()
    {
        string dir = Path.GetFullPath("/some/random/dir");
        _fs.AddDirectory(dir);

        var result = _sut.Detect(dir);

        result.DetectionMethod.Should().Be(RootDetectionMethod.CurrentDirectory);
        result.RootPath.Should().Be(dir);
    }

    [Fact]
    public void Detects_existing_DirectoryPackagesProps()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");
        _fs.AddFile(Path.Combine(root, "Directory.Packages.props"), "<Project />");

        var result = _sut.Detect(root);

        result.HasExistingDirectoryPackagesProps.Should().BeTrue();
    }

    [Fact]
    public void Detects_existing_DirectoryBuildProps()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");
        _fs.AddFile(Path.Combine(root, "Directory.Build.props"), "<Project />");

        var result = _sut.Detect(root);

        result.HasExistingDirectoryBuildProps.Should().BeTrue();
    }

    [Fact]
    public void Throws_on_nonexistent_directory()
    {
        var act = () => _sut.Detect("/nonexistent/path/that/does/not/exist");

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Throws_on_null_or_empty_path()
    {
        var act = () => _sut.Detect("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Explicit_root_does_not_walk_up_to_a_parent_solution()
    {
        string root = Path.GetFullPath("/repo");
        string subDir = Path.Combine(root, "src", "MyProject");
        _fs.AddDirectory(subDir);
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");

        var result = _sut.Detect(subDir, isExplicitRoot: true);

        result.DetectionMethod.Should().Be(RootDetectionMethod.ExplicitPath);
        result.RootPath.Should().Be(subDir);
        result.SolutionFilePath.Should().BeNull();
    }

    [Fact]
    public void Explicit_root_records_a_solution_file_directly_inside_it()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddFile(Path.Combine(root, "MySolution.sln"), "");

        var result = _sut.Detect(root, isExplicitRoot: true);

        result.SolutionFilePath.Should().EndWith("MySolution.sln");
    }

    [Fact]
    public void Finds_slnx_files()
    {
        string root = Path.GetFullPath("/repo");
        string subDir = Path.Combine(root, "src");
        _fs.AddDirectory(subDir);
        _fs.AddFile(Path.Combine(root, "MySolution.slnx"), "<Solution />");

        var result = _sut.Detect(subDir);

        result.DetectionMethod.Should().Be(RootDetectionMethod.SolutionFile);
        result.SolutionFilePath.Should().EndWith("MySolution.slnx");
    }
}
