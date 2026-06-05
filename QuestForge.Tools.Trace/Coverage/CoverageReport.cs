namespace QuestForge.Tools.Trace.Coverage;

public sealed record CoverageSection(
    int Covered,
    int Total,
    double Percentage,
    IReadOnlyList<string> CoveredItems,
    IReadOnlyList<string> UncoveredItems);

public sealed record CoverageReport(
    CoverageSection Steps,
    CoverageSection Predicates,
    CoverageSection ActionTypes)
{
    public double OverallPercentage
    {
        get
        {
            int totalItems = Steps.Total + Predicates.Total + ActionTypes.Total;
            if (totalItems == 0) return 100.0;
            int coveredItems = Steps.Covered + Predicates.Covered + ActionTypes.Covered;
            return Math.Round(100.0 * coveredItems / totalItems, 1);
        }
    }
}
