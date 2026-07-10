using Avalonia.Platform.Storage;
using Xunit;

namespace Avalonia.Base.UnitTests;

public class FilePickerFileTypeTests
{
    [Theory]
    [InlineData(".ext", "ext")]
    [InlineData(".ext1.ext2", "ext1.ext2")]
    [InlineData("foo.ext", "ext")]
    [InlineData("*.ext", "ext")]
    [InlineData("*.ext1.*.ext2", "ext2")]
    [InlineData("*.ext1.ext2", "ext1.ext2")]
    [InlineData("*pattern*.ext1.ext2", "ext1.ext2")]
    public void TryGetExtension_For_Simple_Pattern_Should_Return_Extension(string pattern, string expected)
    {
        var extension = FilePickerFileType.TryGetExtension(pattern);

        Assert.Equal(expected, extension);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.*")]
    [InlineData("*.*ext")]
    [InlineData("*.ext*")]
    [InlineData("*foo*")]
    [InlineData("foo")]
    [InlineData(".")]
    [InlineData("..")]
    public void TryGetExtension_For_Invalid_Extension_Should_Return_Null(string pattern)
    {
        var extension = FilePickerFileType.TryGetExtension(pattern);

        Assert.Null(extension);
    }
}
