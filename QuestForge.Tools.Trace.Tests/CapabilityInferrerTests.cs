using QuestForge.Schema;
using QuestForge.Tools.Trace.Capabilities;
using Xunit;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// Tests for <see cref="CapabilityInferrer"/>.
/// §12.5 sanity test from PHASE_10_PLAN.md.
/// All tests are RED: they will fail until Builder implements CapabilityInferrer.Infer.
/// </summary>
public sealed class CapabilityInferrerTests
{
    // -------------------------------------------------------------------------
    // Quest 66130 baseline: travel+talk steps with playerNear and questSequence predicates
    // -------------------------------------------------------------------------

    [Fact]
    public void Infer_Quest66130_ReturnsExpectedCapabilities_Sorted()
    {
        /*
         * RED: Will fail until Builder implements CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a QuestDefinition matching quest 66130
         *           (two sequences: [travel,talk] and [travel,talk-with-isQuestComplete]),
         *           When  CapabilityInferrer.Infer(quest),
         *           Then  result == ["predicate:isQuestComplete","predicate:playerNear",
         *                           "predicate:questSequence","step:talk","step:travel"]
         *                 (alphabetically sorted, de-duplicated).
         *
         * BUILDER GUIDANCE:
         *   - Walk each step in each sequence.
         *   - Emit "step:<discriminator>" per step type.
         *   - Walk Expect / SkipIf recursively; for PredicateExpect extract function name
         *     (substring before '(') and emit "predicate:<name>".
         *   - Return sorted, de-duplicated result.
         */

        // Arrange — build a minimal QuestDefinition mirroring 66130's structure in-memory.
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 66130u,
            Name              = "Coming to Ul'dah",
            Expansion         = "arr",
            Category          = "msq",
            Enabled           = true,
            SupportStatus     = new SupportStatus { Implementation = "complete", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements { MinLevel = 1, Prereqs = [] },
            AcceptFrom        = new NpcLocation(1003987u, 182, new Position3(35.56f, 4.0f, -151.18f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new TravelStep
                        {
                            Id          = "travel-to-wymond",
                            Destination = new TravelDestination(182, new Position3(35.56f, 4.0f, -151.18f)),
                            Expect      = new PredicateExpect
                                { Predicate = "playerNear({\"x\":35.56,\"y\":4.0,\"z\":-151.18}, 3)" }
                        },
                        new TalkStep
                        {
                            Id     = "talk-to-wymond",
                            Target = new NpcLocation(1003987u, 182, new Position3(35.56f, 4.0f, -151.18f)),
                            Expect = new PredicateExpect { Predicate = "questSequence(66130) >= 255" }
                        }
                    ]
                },
                new QuestSequence
                {
                    Sequence = 255,
                    Steps =
                    [
                        new TravelStep
                        {
                            Id          = "travel-to-momodi",
                            Destination = new TravelDestination(182, new Position3(21.84f, 7.0f, -81.13f)),
                            Expect      = new PredicateExpect
                                { Predicate = "playerNear({\"x\":21.84,\"y\":7.0,\"z\":-81.13}, 3)" }
                        },
                        new TalkStep
                        {
                            Id     = "turn-in-to-momodi",
                            Target = new NpcLocation(1003988u, 182, new Position3(21.84f, 7.0f, -81.13f)),
                            Expect = new PredicateExpect { Predicate = "isQuestComplete(66130)" }
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        var expected = new[]
        {
            "predicate:isQuestComplete",
            "predicate:playerNear",
            "predicate:questSequence",
            "step:talk",
            "step:travel"
        };

        Assert.Equal(expected, caps);
    }

    // -------------------------------------------------------------------------
    // BranchStep → engine:branching capability
    // -------------------------------------------------------------------------

    [Fact]
    public void Infer_QuestWithBranchStep_EmitsEngineBranching()
    {
        /*
         * RED: Will fail until Builder implements CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a QuestDefinition containing a BranchStep,
         *           When  Infer,
         *           Then  result contains "engine:branching" and "step:branch".
         *
         * BUILDER GUIDANCE:
         *   - Emit "step:branch" for any BranchStep (standard step discriminator rule).
         *   - Additionally emit "engine:branching" when any BranchStep is present.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 1u,
            Name              = "Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new BranchStep
                        {
                            Id       = "branch-1",
                            Branches = []
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:branch",     caps);
        Assert.Contains("engine:branching", caps);
    }

    // -------------------------------------------------------------------------
    // FragmentStep → engine:fragments capability
    // -------------------------------------------------------------------------

    [Fact]
    public void Infer_QuestWithFragmentStep_EmitsEngineFragments()
    {
        /*
         * RED: Will fail until Builder implements CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a QuestDefinition containing a FragmentStep,
         *           When  Infer,
         *           Then  result contains "engine:fragments" and "step:fragment".
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 2u,
            Name              = "Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new FragmentStep { Id = "frag-1", Ref = "some-fragment" }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:fragment",    caps);
        Assert.Contains("engine:fragments", caps);
    }

    // -------------------------------------------------------------------------
    // Empty quest → empty capability list
    // -------------------------------------------------------------------------

    [Fact]
    public void Infer_QuestWithNoSteps_ReturnsEmptyList()
    {
        /*
         * RED: Will fail until Builder implements CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a QuestDefinition with Sequences == [] (no steps),
         *           When  Infer,
         *           Then  result is empty.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 0u,
            Name              = "Empty",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "none", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences         = []
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Empty(caps);
    }

    // =========================================================================
    // Phase 11B — AttunementStep capability tests (B1-B4 from PHASE_11B_PLAN.md §3.10)
    // =========================================================================

    [Fact]
    public void Infer_QuestWithAttunementStep_EmitsStepAttune()
    {
        /*
         * RED: Will fail until Builder adds [typeof(AttunementStep)] = "step:attune"
         *      to CapabilityInferrer.StepCapabilities.
         *
         * CONTRACT: Given a QuestDefinition with one AttunementStep,
         *           When CapabilityInferrer.Infer(quest),
         *           Then the result includes "step:attune".
         *
         * BUILDER GUIDANCE: Add to StepCapabilities dictionary:
         *   [typeof(AttunementStep)] = "step:attune",
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 99001u,
            Name              = "Attune Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new AttunementStep
                        {
                            Id     = "attune-limsa",
                            Target = new AetheryteId(8)
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:attune", caps);
    }

    [Fact]
    public void Infer_AttunementStepWithIsAttunedSkipIf_EmitsPredicateIsAttuned()
    {
        /*
         * RED: Will fail until Builder adds AttunementStep to StepCapabilities AND
         *      isAttuned is registered in FunctionRegistry (for parser-based extraction).
         *
         * CONTRACT: Given a quest with AttunementStep.SkipIf = PredicateExpect("isAttuned(53)"),
         *           When Infer,
         *           Then result also includes "predicate:isAttuned".
         *
         * BUILDER GUIDANCE: CapabilityInferrer.WalkExpect extracts the function name
         *   by splitting on '(' and taking the first token. "isAttuned(53)".Split('(')[0]
         *   == "isAttuned". This works without any registry lookup.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 99002u,
            Name              = "Attune with skipIf",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new AttunementStep
                        {
                            Id     = "attune-with-skipif",
                            Target = new AetheryteId(53),
                            SkipIf = new PredicateExpect { Predicate = "isAttuned(53)" }
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:attune", caps);
        Assert.Contains("predicate:isAttuned", caps);
    }

    [Fact]
    public void Infer_MultipleAttunementSteps_DeduplicatesStepAttune()
    {
        /*
         * RED: Will fail until Builder adds AttunementStep to StepCapabilities.
         *
         * CONTRACT: Given two AttunementSteps in the same quest,
         *           When Infer,
         *           Then "step:attune" appears exactly once (deduplication via HashSet).
         *
         * BUILDER GUIDANCE: CapabilityInferrer uses HashSet<string> for caps — adding the
         *   same string twice has no effect.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 99003u,
            Name              = "Two Attunements",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new AttunementStep { Id = "attune-1", Target = new AetheryteId(1) },
                        new AttunementStep { Id = "attune-2", Target = new AetheryteId(2) }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Equal(1, caps.Count(c => c == "step:attune"));
    }

    [Fact]
    public void Infer_MixedSteps_SortedAlphabetically_IncludesAttune()
    {
        /*
         * RED: Will fail until Builder adds AttunementStep to StepCapabilities.
         *
         * CONTRACT: Given a quest with TravelStep, AttunementStep, TalkStep,
         *           When Infer,
         *           Then the result is sorted (StringComparer.Ordinal) and
         *                "step:attune" appears in alphabetical order among
         *                "step:talk", "step:travel".
         *
         * BUILDER GUIDANCE: The sorted list is ["step:attune", "step:talk", "step:travel"]
         *   — "attune" < "talk" < "travel" in ordinal sort.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 99004u,
            Name              = "Mixed Steps",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new TravelStep
                        {
                            Id          = "travel-to-crystal",
                            Destination = new TravelDestination(130)
                        },
                        new AttunementStep
                        {
                            Id     = "attune-crystal",
                            Target = new AetheryteId(8)
                        },
                        new TalkStep
                        {
                            Id     = "talk-after",
                            Target = new NpcLocation(1000u, 130, new Position3(0f, 0f, 0f))
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        // Verify sorted order contains all three step types
        var stepCaps = caps.Where(c => c.StartsWith("step:")).ToList();
        Assert.Contains("step:attune", stepCaps);
        Assert.Contains("step:talk", stepCaps);
        Assert.Contains("step:travel", stepCaps);

        // Verify sorted: attune < talk < travel
        var attuneIdx  = caps.ToList().IndexOf("step:attune");
        var talkIdx    = caps.ToList().IndexOf("step:talk");
        var travelIdx  = caps.ToList().IndexOf("step:travel");
        Assert.True(attuneIdx < talkIdx,   "step:attune should sort before step:talk");
        Assert.True(talkIdx   < travelIdx, "step:talk should sort before step:travel");
    }

    // -------------------------------------------------------------------------
    // De-duplication: same step type in multiple sequences → one tag
    // -------------------------------------------------------------------------

    [Fact]
    public void Infer_MultipleSequencesWithSameStepType_DeduplicatesTags()
    {
        /*
         * RED: Will fail until Builder implements CapabilityInferrer.Infer.
         *
         * CONTRACT: Given a quest with TravelStep in both sequence 0 and sequence 1,
         *           When  Infer,
         *           Then  "step:travel" appears exactly once.
         */

        // Arrange
        var travelStep0 = new TravelStep
        {
            Id          = "t0",
            Destination = new TravelDestination(182)
        };
        var travelStep1 = new TravelStep
        {
            Id          = "t1",
            Destination = new TravelDestination(182)
        };
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 3u,
            Name              = "Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence { Sequence = 0, Steps = [travelStep0] },
                new QuestSequence { Sequence = 1, Steps = [travelStep1] }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Equal(1, caps.Count(c => c == "step:travel"));
    }

    // =========================================================================
    // CI-HO-1/2/3 — HandOverItemStep capability inference (feat/extract-fixtures-update)
    // =========================================================================

    [Fact]
    public void Infer_QuestWithHandOverItemStep_EmitsStepHandOverItem()
    {
        /*
         * RED: Will fail until Builder adds [typeof(HandOverItemStep)] = "step:hand-over-item"
         *      to CapabilityInferrer.StepCapabilities.
         *
         * CONTRACT: Given a QuestDefinition with one HandOverItemStep,
         *           When CapabilityInferrer.Infer(quest),
         *           Then the result contains "step:hand-over-item".
         *
         * BUILDER GUIDANCE: Add to StepCapabilities dictionary:
         *   [typeof(HandOverItemStep)] = "step:hand-over-item",
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 66104u,
            Name              = "HandOver Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new HandOverItemStep
                        {
                            Id     = "hand-over-shard",
                            Target = new NpcLocation(1003987u, 182, new Position3(35.56f, 4.0f, -151.18f)),
                            Items  = [2000386u]
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:hand-over-item", caps);
    }

    [Fact]
    public void Infer_HandOverItemStepWithPlayerHasItemExpect_EmitsPredicatePlayerHasItem()
    {
        /*
         * CONTRACT: Given a HandOverItemStep with Expect = PredicateExpect("not(playerHasItem(2000386))"),
         *           When CapabilityInferrer.Infer(quest),
         *           Then the result contains "step:hand-over-item"
         *                AND the result contains "predicate:not"
         *                AND the result contains "predicate:playerHasItem".
         *
         * WalkExpect uses a regex that finds ALL function call names in the predicate string,
         * so nested calls like not(playerHasItem(X)) emit both predicate tags.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 66104u,
            Name              = "HandOver Expect Test",
            Expansion         = "arr",
            Category          = "side",
            SupportStatus     = new SupportStatus { Implementation = "partial", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements(),
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new HandOverItemStep
                        {
                            Id     = "hand-over-with-expect",
                            Target = new NpcLocation(1003987u, 182, new Position3(35.56f, 4.0f, -151.18f)),
                            Items  = [2000386u],
                            Expect = new PredicateExpect { Predicate = "not(playerHasItem(2000386))" }
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:hand-over-item", caps);
        Assert.Contains("predicate:not", caps);
        Assert.Contains("predicate:playerHasItem", caps);
    }

    [Fact]
    public void Infer_QuestWith66104Steps_EmitsCorrectCapabilities()
    {
        /*
         * RED: Will fail until Builder adds [typeof(HandOverItemStep)] = "step:hand-over-item"
         *      to CapabilityInferrer.StepCapabilities.
         *
         * CONTRACT: Given a quest with AcceptStep, two AttunementSteps, TravelStep,
         *           HandOverItemStep, and TurnInStep,
         *           When CapabilityInferrer.Infer(quest),
         *           Then result contains all of:
         *             "step:accept", "step:attune", "step:hand-over-item",
         *             "step:travel", "step:turn-in"
         *           AND the result is sorted (Ordinal) so these appear in this index order:
         *             accept < attune < hand-over-item < travel < turn-in.
         *
         * BUILDER GUIDANCE: All step tags except "step:hand-over-item" are already registered.
         *   Adding HandOverItemStep to StepCapabilities is the only change required.
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 66104u,
            Name              = "Full Quest 66104",
            Expansion         = "arr",
            Category          = "msq",
            SupportStatus     = new SupportStatus { Implementation = "complete", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements { MinLevel = 1, Prereqs = [] },
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new AcceptStep
                        {
                            Id     = "accept-quest",
                            Target = new NpcLocation(1003987u, 182, new Position3(35.56f, 4.0f, -151.18f))
                        },
                        new AttunementStep { Id = "attune-1", Target = new AetheryteId(8) },
                        new AttunementStep { Id = "attune-2", Target = new AetheryteId(9) },
                        new TravelStep
                        {
                            Id          = "travel-to-npc",
                            Destination = new TravelDestination(182)
                        },
                        new HandOverItemStep
                        {
                            Id     = "hand-over-item",
                            Target = new NpcLocation(1003988u, 182, new Position3(21.84f, 7.0f, -81.13f)),
                            Items  = [2000386u]
                        },
                        new TurnInStep
                        {
                            Id     = "turn-in",
                            Target = new NpcLocation(1003988u, 182, new Position3(21.84f, 7.0f, -81.13f))
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert — all expected tags are present
        Assert.Contains("step:accept",         caps);
        Assert.Contains("step:attune",         caps);
        Assert.Contains("step:hand-over-item", caps);
        Assert.Contains("step:travel",         caps);
        Assert.Contains("step:turn-in",        caps);

        // Assert — sorted order (Ordinal): accept < attune < hand-over-item < travel < turn-in
        var list = caps.ToList();
        var acceptIdx      = list.IndexOf("step:accept");
        var attuneIdx      = list.IndexOf("step:attune");
        var handOverIdx    = list.IndexOf("step:hand-over-item");
        var travelIdx      = list.IndexOf("step:travel");
        var turnInIdx      = list.IndexOf("step:turn-in");

        Assert.True(acceptIdx   < attuneIdx,   "step:accept must sort before step:attune");
        Assert.True(attuneIdx   < handOverIdx, "step:attune must sort before step:hand-over-item");
        Assert.True(handOverIdx < travelIdx,   "step:hand-over-item must sort before step:travel");
        Assert.True(travelIdx   < turnInIdx,   "step:travel must sort before step:turn-in");
    }

    // =========================================================================
    // B8 — CutsceneStep capability inference
    // =========================================================================

    [Fact]
    public void Infer_QuestWithCutsceneStep_EmitsStepCutscene()
    {
        /*
         * CONTRACT: Given a QuestDefinition containing one CutsceneStep with an Expect
         *             predicate of "questSequence(12345) >= 1",
         *           When CapabilityInferrer.Infer(quest),
         *           Then the result contains "step:cutscene"
         *                AND the result contains "predicate:questSequence"
         *                (because the Expect predicate uses the questSequence function).
         *
         * This test is expected to PASS immediately:
         *   [typeof(CutsceneStep)] = "step:cutscene" already exists in CapabilityInferrer.cs line 21.
         *   The questSequence predicate extractor is also already present.
         *   The test is authored here to lock the behavior against future regressions.
         *
         * BUILDER GUIDANCE (if it fails): Verify that StepCapabilities contains
         *   [typeof(CutsceneStep)] = "step:cutscene" and that WalkExpect correctly
         *   extracts the function name from "questSequence(12345) >= 1" as "questSequence".
         */

        // Arrange
        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 12345u,
            Name              = "Cutscene Capability Test",
            Expansion         = "arr",
            Category          = "msq",
            Enabled           = true,
            SupportStatus     = new SupportStatus { Implementation = "complete", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements { MinLevel = 1, Prereqs = [] },
            AcceptFrom        = new NpcLocation(0u, 0, new Position3(0f, 0f, 0f)),
            Sequences =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps =
                    [
                        new CutsceneStep
                        {
                            Id     = "watch-intro",
                            Skip   = "ifAllowed",
                            Expect = new PredicateExpect { Predicate = "questSequence(12345) >= 1" }
                        }
                    ]
                }
            ]
        };

        // Act
        var caps = CapabilityInferrer.Infer(quest);

        // Assert
        Assert.Contains("step:cutscene", caps);
        Assert.Contains("predicate:questSequence", caps);
    }
}
