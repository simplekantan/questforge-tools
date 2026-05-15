using QuestForge.Predicates;

namespace QuestForge.Predicates.Tests;

public class LevenshteinTests
{
    [Theory]
    [InlineData("",            "",            0)]
    [InlineData("abc",         "abc",         0)]
    [InlineData("",            "abc",         3)]
    [InlineData("abc",         "",            3)]
    [InlineData("questSequnece", "questSequence", 2)] // transposition n/e counts as 2 in standard Levenshtein
    [InlineData("isQuestcomplete", "isQuestComplete", 1)]
    [InlineData("abc",         "abd",         1)] // substitution
    [InlineData("ab",          "abc",         1)] // insertion
    [InlineData("abc",         "ac",          1)] // deletion
    [InlineData("sitting",     "kitten",      3)] // classic example
    public void Distance_ReturnsCorrectValue(string a, string b, int expected)
    {
        Assert.Equal(expected, Levenshtein.Distance(a, b));
    }

    [Fact]
    public void Distance_IsSymmetric()
    {
        Assert.Equal(
            Levenshtein.Distance("questSequnece", "questSequence"),
            Levenshtein.Distance("questSequence", "questSequnece"));
    }
}