using QuestForge.Tools.Trace.Cli;
using Xunit;

namespace QuestForge.Tools.Trace.Tests.Cli;

public class QuestDataRootResolverTests : IDisposable
{
    private readonly string _tempRoot;

    public QuestDataRootResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"qf-trace-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ---------------------------------------------------------------------------
    // T-10: Sibling repo layout — the common case
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResolveQuestDataRoot_FindsSibling()
    {
        /*
         * RED: Will fail until Builder implements QuestDataRootResolver.Resolve.
         *
         * CONTRACT: Given a temp directory layout where tmp/questforge-data/quests/ exists
         *           and "checkout" is the working directory (tmp/checkout/),
         *           When Resolve("tmp/checkout") is called,
         *           Then returns Path.GetFullPath(Path.Combine(tmp, "questforge-data")).
         *
         * BUILDER GUIDANCE: Probe algorithm step 2:
         *   Path.Combine(cwd, "..", "questforge-data", "quests") must exist.
         *   Return Path.GetFullPath(Path.Combine(cwd, "..", "questforge-data")).
         */

        // Arrange
        string checkoutDir    = Path.Combine(_tempRoot, "checkout");
        string questDataDir   = Path.Combine(_tempRoot, "questforge-data");
        string questsDir      = Path.Combine(questDataDir, "quests");

        Directory.CreateDirectory(checkoutDir);
        Directory.CreateDirectory(questsDir);

        string expectedRoot = Path.GetFullPath(questDataDir);

        // Act
        string? actual = QuestDataRootResolver.Resolve(checkoutDir);

        // Assert
        Assert.Equal(
            expectedRoot,
            actual,
            StringComparer.OrdinalIgnoreCase);
    }
}
