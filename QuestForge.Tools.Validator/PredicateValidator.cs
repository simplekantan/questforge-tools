using QuestForge.Predicates;
using QuestForge.Schema;

namespace QuestForge.Tools.Validator;

public sealed class PredicateValidator(IFragmentRegistry fragments) : IValidator
{
    // Key: (source, scopeId). scopeId=0 for quest predicates; fragmentId.GetHashCode() for fragments.
    // Keyed on fragment string ID hash — not object-reference hash — so cache hits across calls.
    private readonly Dictionary<(string Source, int ScopeId), ParseResult> _cache = new();

    public IEnumerable<ValidationError> Validate(QuestDefinition quest, ValidationContext ctx)
    {
        foreach (var site in CollectPredicates(quest))
        {
            var key = (site.Predicate, site.ScopeId);
            if (!_cache.TryGetValue(key, out var result))
                _cache[key] = result = PredicateParser.Parse(site.Predicate, site.Scope);

            foreach (var err in result.Errors)
                yield return Translate(err, site, ctx.FilePath);

            if (result.IsSuccess && result.Ast is not null)
                foreach (var err in PredicateChecker.Check(result.Ast, site.Scope))
                    yield return Translate(err, site, ctx.FilePath);
        }
    }

    // ---- predicate site collection ----------------------------------------

    private sealed record Site(
        string Predicate,
        string Location,
        string? StepId,
        IFragmentParameterScope? Scope,
        int ScopeId);

    private IEnumerable<Site> CollectPredicates(QuestDefinition quest)
    {
        if (quest.Chain is { } chain)
            for (var i = 0; i < chain.Next.Length; i++)
            {
                var when = chain.Next[i].When;
                if (!string.IsNullOrEmpty(when))
                    yield return new(when, $"chain/next[{i}]/when", null, null, 0);
            }

        foreach (var seq in quest.Sequences)
        {
            var seqLoc = $"seq:{seq.Sequence}";

            if (!string.IsNullOrEmpty(seq.SkipIf))
                yield return new(seq.SkipIf, seqLoc, null, null, 0);

            foreach (var site in WalkSteps(seq.Steps, seqLoc))
                yield return site;
        }
    }

    private IEnumerable<Site> WalkSteps(Step[] steps, string scopeLoc)
    {
        foreach (var step in steps)
        {
            var stepBase = $"{scopeLoc}/step:{step.Id}";

            foreach (var s in FromExpectValue(step.Expect, $"{stepBase}/expect", step.Id))
                yield return s;
            foreach (var s in FromExpectValue(step.SkipIf, $"{stepBase}/skipIf", step.Id))
                yield return s;

            if (step is BranchStep branch)
                foreach (var s in WalkBranch(branch, scopeLoc, stepBase))
                    yield return s;
            else if (step is FragmentStep fs)
                foreach (var s in WalkFragment(fs, stepBase))
                    yield return s;
        }
    }

    private IEnumerable<Site> WalkBranch(BranchStep branch, string seqScope, string branchBase)
    {
        for (var i = 0; i < branch.Branches.Length; i++)
        {
            var when = branch.Branches[i].When;
            if (!string.IsNullOrEmpty(when))
                yield return new(when, $"{branchBase}/branches[{i}]/when", branch.Id, null, 0);

            var innerScope = $"{seqScope}/branch:{branch.Id}/case:{i}";
            foreach (var s in WalkSteps(branch.Branches[i].Steps ?? [], innerScope))
                yield return s;
        }
    }

    private IEnumerable<Site> WalkFragment(FragmentStep fs, string fsBase)
    {
        if (!fragments.TryGetFragment(fs.Ref, out var frag) || frag is null)
            yield break; // structural validator already reported fragment-not-found

        var scope   = new FragmentScope(frag);
        var scopeId = fs.Ref.GetHashCode();

        foreach (var step in frag.Steps)
        {
            var stepBase = $"{fsBase}[fragment:{fs.Ref}]/step:{step.Id}";

            foreach (var s in FromExpectValue(step.Expect, $"{stepBase}/expect", step.Id, scope, scopeId))
                yield return s;
            foreach (var s in FromExpectValue(step.SkipIf, $"{stepBase}/skipIf", step.Id, scope, scopeId))
                yield return s;
        }
    }

    private static IEnumerable<Site> FromExpectValue(
        ExpectValue? ev, string loc, string? stepId,
        IFragmentParameterScope? scope = null, int scopeId = 0)
    {
        switch (ev)
        {
            case PredicateExpect pe:
                yield return new(pe.Predicate, loc, stepId, scope, scopeId);
                break;
            case AllExpect ae:
                for (var i = 0; i < ae.All.Length; i++)
                    yield return new(ae.All[i], $"{loc}/all[{i}]", stepId, scope, scopeId);
                break;
            case AnyExpect aye:
                for (var i = 0; i < aye.Any.Length; i++)
                    yield return new(aye.Any[i], $"{loc}/any[{i}]", stepId, scope, scopeId);
                break;
        }
    }

    // ---- error translation ------------------------------------------------

    private static ValidationError Translate(ParseError err, Site site, string filePath)
        => new($"predicate/{err.Code}", err.Message, filePath, site.Location, site.StepId);

    // ---- fragment parameter scope -----------------------------------------

    private sealed class FragmentScope(FragmentDefinition frag) : IFragmentParameterScope
    {
        public bool TryGetParameterType(string name, out PredicateType type)
        {
            var p = frag.Parameters.FirstOrDefault(p => p.Name == name);
            if (p is null) { type = default; return false; }
            type = MapType(p.Type);
            return true;
        }

        private static PredicateType MapType(string schemaType) => schemaType switch
        {
            "position" => PredicateType.Position,
            "npcId"    => PredicateType.Int,
            "itemId"   => PredicateType.Int,
            "string"   => PredicateType.String,
            _          => PredicateType.Int
        };
    }
}