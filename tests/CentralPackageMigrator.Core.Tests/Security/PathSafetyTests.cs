using CentralPackageMigrator.Core.Security;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Security;

public sealed class PathSafetyTests
{
    private static readonly string Root = Path.GetFullPath("/repo");

    [Fact]
    public void Path_inside_root_is_under_root()
    {
        PathSafety.IsUnderRoot(Path.Combine(Root, "src", "A.csproj"), Root).Should().BeTrue();
    }

    [Fact]
    public void Sibling_directory_sharing_the_prefix_is_not_under_root()
    {
        PathSafety.IsUnderRoot(Path.GetFullPath("/repo-evil/A.csproj"), Root).Should().BeFalse();
    }

    [Fact]
    public void Root_itself_is_not_under_root()
    {
        PathSafety.IsUnderRoot(Root, Root).Should().BeFalse();
    }

    [Fact]
    public void Root_with_trailing_separator_is_handled()
    {
        PathSafety.IsUnderRoot(Path.Combine(Root, "A.csproj"), Root + Path.DirectorySeparatorChar).Should().BeTrue();
    }

    [Theory]
    [InlineData("Directory.Packages.props")]
    [InlineData("src/App/App.csproj")]
    [InlineData("src\\App\\App.csproj")]
    [InlineData("weird..name/App.csproj")]
    public void Accepts_plain_relative_paths(string path)
    {
        PathSafety.ValidateRelativePath(path).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("/etc/passwd")]
    [InlineData("\\server\\share")]
    [InlineData("C:/x")]
    [InlineData("C:x")]
    public void Rejects_anything_else(string path)
    {
        PathSafety.ValidateRelativePath(path).Should().NotBeNull();
    }
}
