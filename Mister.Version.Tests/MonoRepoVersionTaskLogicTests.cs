using Xunit;
using Mister.Version;
using System.Reflection;

namespace Mister.Version.Tests
{
    public class MonoRepoVersionTaskLogicTests
    {
        private MonoRepoVersionTask CreateTask()
            => new MonoRepoVersionTask();

        [Theory]
        [InlineData("main", "Main")]
        [InlineData("master", "Main")]
        [InlineData("release/1.2.3", "Release")]
        [InlineData("release-2.0.0", "Release")]
        [InlineData("v3.1.4", "Release")]
        [InlineData("feature/my-feature", "Feature")]
        [InlineData("bugfix/fix", "Feature")]
        [InlineData("hotfix/foo", "Feature")]
        [InlineData("dev/blah", "Feature")]
        public void DetermineBranchType_Works(string branchName, string expectedType)
        {
            var task = CreateTask();
            var result = task.DetermineBranchType(branchName);
            Assert.Equal(expectedType, result.ToString());
        }

        [Theory]
        [InlineData("release/1.2.3", 1, 2, 3)]
        [InlineData("release-2.0.0", 2, 0, 0)]
        [InlineData("v3.1.4", 3, 1, 4)]
        [InlineData("main", null, null, null)]
        [InlineData("feature/xyz", null, null, null)]
        public void ExtractReleaseVersion_HandlesVariousFormats(string input, int? major, int? minor, int? patch)
        {
            var task = CreateTask();
            var ver = task.ExtractReleaseVersion(input);
            if (major == null)
            {
                Assert.Null(ver);
            }
            else
            {
                Assert.NotNull(ver);
                Assert.Equal(major, ver.Major);
                Assert.Equal(minor, ver.Minor);
                Assert.Equal(patch, ver.Patch);
            }
        }

        [Theory]
        [InlineData("1.2.3", 1, 2, 3)]
        [InlineData("0.9.0", 0, 9, 0)]
        [InlineData("5.10", 5, 10, 0)]
        [InlineData("7.2.1-suffix", 7, 2, 1)]
        [InlineData("notaversion", null, null, null)]
        [InlineData("", null, null, null)]
        public void ParseSemVer_Basic(string input, int? major, int? minor, int? patch)
        {
            var task = CreateTask();
            var ver = task.ParseSemVer(input);
            if (major == null)
            {
                Assert.Null(ver);
            }
            else
            {
                Assert.NotNull(ver);
                Assert.Equal(major, ver.Major);
                Assert.Equal(minor, ver.Minor);
                Assert.Equal(patch, ver.Patch);
            }
        }
    }
}