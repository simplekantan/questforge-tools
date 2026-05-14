using QuestForge.Schema;
using QuestForge.Tools.Validator;

namespace QuestForge.Tools.Validator.Tests;

/// <summary>
/// Verifies that a structurally valid but non-trivial quest produces no errors.
/// This catches rules that accidentally flag valid-but-complex quests — a failure
/// mode the minimal QuestBuilder.Valid() baseline cannot detect.
/// </summary>
public class ValidComplexQuestTests
{
    [Fact]
    public void ComplexQuest_WithBranches_Recovery_Fragment_ProducesNoErrors()
    {
        // Fragment with no required parameters — structurally valid to reference without params.
        var fragment = new FragmentDefinition
        {
            SchemaVersion = "1.0.0",
            FragmentId    = "travel/ul-dah-to-gridania",
            Parameters    = [],
            Steps         = [QuestBuilder.Step("frag-travel-step", "playerZone() == 132")]
        };

        var registry = new InMemoryFragmentRegistry([fragment]);

        var quest = new QuestDefinition
        {
            SchemaVersion     = "1.0.0",
            Id                = 65657,
            Name              = "Complex Test Quest",
            Expansion         = "arr",
            Category          = "msq",
            Enabled           = true,
            SupportStatus     = new SupportStatus { Implementation = "complete", KnownIssues = [] },
            LastVerifiedPatch = "7.4",
            Requirements      = new Requirements
            {
                MinLevel = 1,
                Prereqs  = [new PrerequisiteRef(65656, "complete")]
            },
            AcceptFrom   = new NpcLocation(1000789, 128, new Position3(9.5f, 40f, 14.2f)),
            Chain        = new Chain
            {
                Previous = [65656],
                Next     = [new ChainNext("default", 65658)]
            },
            Contributors = ["@alice"],
            Notes        = "Integration test quest exercising branches, recovery, and fragments.",
            Sequences    =
            [
                new QuestSequence
                {
                    Sequence = 0,
                    Steps    =
                    [
                        new TravelStep
                        {
                            Id          = "travel-to-questgiver",
                            Destination = new TravelDestination(128, new Position3(9.5f, 40f, 14.2f)),
                            RouteHint   = new RouteHint("Ul'dah - Steps of Nald"),
                            Recover     = new RecoverConfig
                            {
                                // goto same scope — valid; goto infinite-loop detection is Phase 2+
                                OnTimeout = new GotoRecoverAction { StepId = "travel-to-questgiver" }
                            },
                            Expect = new PredicateExpect { Predicate = "playerZone() == 128" }
                        },
                        new TalkStep
                        {
                            Id             = "talk-to-questgiver",
                            Target         = new NpcLocation(1000790, 128, new Position3(9.5f, 40f, 14.2f)),
                            DialogueChoices = [new DialogueChoice("yesno", "Accept quest?", "yes")],
                            Notes          = "Initial quest dialogue.",
                            Expect         = new AllExpect { All = ["questSequence(65657) >= 1", "playerZone() == 128"] }
                        }
                    ]
                },

                new QuestSequence
                {
                    Sequence = 1,
                    SkipIf   = "questFlag(65657, 5)",
                    Steps    =
                    [
                        new BranchStep
                        {
                            Id       = "choose-combat-or-wait",
                            Branches =
                            [
                                new BranchCase(
                                    "playerLevel() >= 20",
                                    [
                                        new CombatStep
                                        {
                                            Id     = "fight-enemies",
                                            Target = new CombatTarget("nearestHostile", 20f),
                                            Recover = new RecoverConfig
                                            {
                                                OnPlayerDefeated = new UseReturnRecoverAction { ThenRetry = true }
                                            },
                                            Expect = new PredicateExpect { Predicate = "not playerInCombat()" }
                                        }
                                    ]
                                ),
                                new BranchCase(
                                    "default",
                                    [
                                        new AwaitUserStep
                                        {
                                            Id     = "await-user-combat",
                                            Reason = "Please defeat the nearby enemies, then continue.",
                                            Expect = new PredicateExpect { Predicate = "questFlag(65657, 3)" }
                                        }
                                    ]
                                )
                            ],
                            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 2" }
                        }
                    ]
                },

                new QuestSequence
                {
                    Sequence = 2,
                    Steps    =
                    [
                        new FragmentStep
                        {
                            Id     = "travel-to-gridania",
                            Ref    = "travel/ul-dah-to-gridania",
                            Params = null,
                            Expect = new PredicateExpect { Predicate = "playerZone() == 132" }
                        },
                        new CutsceneStep
                        {
                            Id     = "watch-arrival-cutscene",
                            Skip   = "ifAllowed",
                            Expect = new PredicateExpect { Predicate = "questSequence(65657) >= 3" }
                        }
                    ]
                },

                new QuestSequence
                {
                    Sequence = 255,
                    Steps    =
                    [
                        new TurnInStep
                        {
                            Id     = "turn-in-quest",
                            Target = new NpcLocation(1000790, 132, new Position3(0f, 0f, 0f)),
                            Expect = new PredicateExpect { Predicate = "isQuestComplete(65657)" }
                        }
                    ]
                }
            ]
        };

        var errors = QuestBuilder.Validate(quest, registry: registry).ToList();
        QuestBuilder.AssertNoErrors(errors);
    }
}