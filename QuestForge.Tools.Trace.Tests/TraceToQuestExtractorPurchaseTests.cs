using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using QuestForge.Schema;
using QuestForge.Tools.Trace.Quest;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for Slice E.3: TraceToQuestExtractor purchase branch (offline mirror).
///
/// GWT-PE1..PE3 from §13.5. Mirrors TraceToQuestExtractorCombatTests conventions exactly.
///
/// RED reason (behavioral): TraceToQuestExtractor has no purchase branch — SnapshotState
/// does not recognise the new observation methods, so PurchaseDetected stays null and the
/// inference engine never returns "purchase-item", so no PurchaseItemStep is ever emitted.
/// </summary>
public sealed class TraceToQuestExtractorPurchaseTests
{
    private const uint QuestId70001 = 70001u;
    private const uint VendorNpcId  = 1001234u;
    private const uint ItemId1601   = 1601u;

    private static readonly DateTimeOffset BaseTime = T0;

    // ── Event builders (mirror combat test helpers) ───────────────────────────

    private static ObservationEvent ObsMs(string method, JsonElement? argument, JsonElement? value, double ms)
        => new() { RunId = "aaa", Data = new ObservationEvent.ObservationData { Method = method, Argument = argument, Value = value } };

    private static JsonElement ShopOpenedValue(bool open)
        => JsonSerializer.SerializeToElement(new { value = open });

    private static JsonElement CurrencyBalanceValue(long gil, int seals = 0)
        => JsonSerializer.SerializeToElement(new { gil, seals });

    private static JsonElement VendorItemCountValue(uint itemId, int count)
        => JsonSerializer.SerializeToElement(new { itemId, count });

    /// <summary>GetTarget value shape for vendor NPC (plain number — no hostile kind).</summary>
    private static JsonElement VendorTargetValue(uint npcId)
        => JsonSerializer.SerializeToElement(npcId);

    // ── Trace builders ────────────────────────────────────────────────────────

    /// <summary>
    /// Standard purchase trace for quest 70001, vendor 1001234, item 1601, gil cost 1000.
    ///
    /// Timeline (ms from T0):
    ///   0      RunStart 70001
    ///   100    GetPlayerZone 128
    ///   150    GetPlayerPosition (10,0,20)
    ///   200    GetTarget 1001234 (plain number — vendor, not hostile)
    ///   250    CurrencyBalance {gil:10000,seals:0}  ← pre-open seed
    ///   300    ShopOpened {value:true}              ← span opens, baseline gil=10000
    ///   350    VendorItemCount {itemId:1601,count:1} ← item rose
    ///   400    CurrencyBalance {gil:9000,seals:0}   ← gil fell
    ///   450    ShopOpened {value:false}             ← span retained after close
    ///   500    Decision (decisionActionType)
    ///   2000   RunEnd
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildPurchaseTrace(string decisionActionType = "wait")
    {
        var events = new List<TraceEvent>
        {
            new RunStartEvent { RunId = "aaa", Data = new RunStartEvent.RunStartData { QuestId = QuestId70001 } },

            // Zone + position
            ObsMs("GetPlayerZone",     null, ZoneValue(128u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 150),

            // Vendor NPC targeted (plain number — sets LastNpcInteracted via RecordInteract at action boundary)
            // Also emit as an Interact submitted to wire LastNpcInteracted properly.

            // Currency seed before shop opens
            ObsMs("CurrencyBalance", null, CurrencyBalanceValue(10_000L, 0), 250),

            // Shop opens — baseline captured at 10000
            ObsMs("ShopOpened", null, ShopOpenedValue(true), 300),

            // Item count rose
            ObsMs("VendorItemCount", null, VendorItemCountValue(ItemId1601, 1), 350),

            // Gil fell
            ObsMs("CurrencyBalance", null, CurrencyBalanceValue(9_000L, 0), 400),

            // Shop closed — span retained
            ObsMs("ShopOpened", null, ShopOpenedValue(false), 450),

            // Decision
            new DecisionEvent { RunId = "aaa", Data = new DecisionEvent.DecisionData { StepId = null, ActionType = decisionActionType } },

            // Submitted interact carrying the vendor NPC id (wires LastNpcInteracted via RecordInteract)
            new ActionSubmittedEvent
            {
                RunId = "aaa",
                Data = new ActionSubmittedEvent.ActionSubmittedData
                {
                    ActionType = "interact",
                    Parameters = JsonSerializer.SerializeToElement(new { target = VendorNpcId })
                }
            },

            new ActionCompletedEvent
            {
                RunId = "aaa",
                Data = new ActionCompletedEvent.ActionCompletedData { ActionType = "interact", Outcome = "ok" }
            },

            new RunEndEvent { RunId = "aaa", Data = new RunEndEvent.RunEndData { Outcome = "done" } }
        };

        return events;
    }

    /// <summary>
    /// Trace with ShopOpened true/false but NO VendorItemCount (browsed, nothing bought).
    /// A wait decision follows so the extractor has a window to process.
    /// </summary>
    private static IReadOnlyList<TraceEvent> BuildBrowseOnlyTrace()
    {
        return
        [
            new RunStartEvent { RunId = "aaa", Data = new RunStartEvent.RunStartData { QuestId = QuestId70001 } },

            ObsMs("GetPlayerZone",     null, ZoneValue(128u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 150),

            // Currency seed
            ObsMs("CurrencyBalance", null, CurrencyBalanceValue(10_000L, 0), 250),

            // Shop opened and closed — but no VendorItemCount
            ObsMs("ShopOpened", null, ShopOpenedValue(true),  300),
            ObsMs("ShopOpened", null, ShopOpenedValue(false), 350),

            // Wait decision (only action in the window)
            new DecisionEvent { RunId = "aaa", Data = new DecisionEvent.DecisionData { StepId = null, ActionType = "wait" } },

            new RunEndEvent { RunId = "aaa", Data = new RunEndEvent.RunEndData { Outcome = "done" } }
        ];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PE1 — end-to-end purchase extraction.
    //
    // CONTRACT: when ShopOpened, CurrencyBalance, VendorItemCount observations are present
    // and the inference engine returns "purchase-item", the extractor must emit a
    // PurchaseItemStep with Target.NpcId, ItemId, Quantity, Currency==Gil, a playerHasItem
    // Expect, and a purchase-review TODO.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PE1_EndToEnd_PurchaseObservations_EmitsPurchaseItemStep()
    {
        // GIVEN
        var events = BuildPurchaseTrace(decisionActionType: "wait");

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var purchaseStep = allSteps.OfType<PurchaseItemStep>().FirstOrDefault();
        Assert.NotNull(purchaseStep);

        // Vendor target
        Assert.Equal(VendorNpcId, purchaseStep!.Target.NpcId);

        // Item
        Assert.Equal(ItemId1601, purchaseStep.ItemId);

        // Quantity
        Assert.Equal(1, purchaseStep.Quantity);

        // Currency
        Assert.Equal(PurchaseCurrency.Gil, purchaseStep.Currency);

        // Expect: playerHasItem(1601,1)
        var expect = Assert.IsType<PredicateExpect>(purchaseStep.Expect);
        Assert.Equal("playerHasItem(1601,1)", expect.Predicate);

        // TODO for purchase review
        Assert.Contains(draft.Todos, t =>
            t.Contains("purchase", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("review", StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PE2 — purchase window whose only decision is 'wait' still emits PurchaseItemStep.
    //
    // CONTRACT: the combat branch precedence rule applies equally to purchase. The
    // wait/terminal-skip guard must NOT drop a window whose inference is "purchase-item".
    // The purchase branch must fire BEFORE the skippable-action check (mirrors combat at
    // TraceToQuestExtractor.cs:206 — wait decision still emits CombatStep).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PE2_WaitDecision_PurchaseWindowNotSkipped_EmitsPurchaseItemStep()
    {
        // GIVEN
        var events = BuildPurchaseTrace(decisionActionType: "wait");

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN — same assertion as PE1: the wait decision must not suppress the purchase step
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var purchaseStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<PurchaseItemStep>()
            .FirstOrDefault();

        Assert.NotNull(purchaseStep);
        Assert.Equal(ItemId1601, purchaseStep!.ItemId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PE3 — vendor visit with no item rise → no PurchaseItemStep emitted.
    //
    // CONTRACT: the correlation guard requires ALL THREE of (shop-open AND item-count
    // rise AND currency drop). A browse-only session (shop open + close, no VendorItemCount)
    // must not produce a PurchaseItemStep; the existing rules drive the window.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PE3_BrowseOnly_NoItemRise_NoPurchaseItemStep()
    {
        // GIVEN
        var events = BuildBrowseOnlyTrace();

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        Assert.DoesNotContain(allSteps, s => s is PurchaseItemStep);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-O2 (refined) — extractor emits PurchaseItemStep with GcCategory==2 and
    //                  GcRankTier==1 when ShopOpened carries GC sibling keys.
    //
    // RED reason: SnapshotState does not yet parse activeGcCategory/activeGcRankTier
    // from the ShopOpened payload, so the projection's ActiveGc fields stay null and
    // StepFactory emits GcCategory==null / GcRankTier==null even though the trace had
    // the keys.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void GO2_Refined_ExtractorEmitsStepWithGcAxes()
    {
        // GIVEN — PE1-shaped trace, verbatim, but ShopOpened{true} carries GC sibling keys
        var shopOpenedWithGc = JsonSerializer.SerializeToElement(
            new { value = true, activeGcCategory = (int?)2, activeGcRankTier = (int?)1 });

        var events = new List<TraceEvent>
        {
            new RunStartEvent { RunId = "aaa", Data = new RunStartEvent.RunStartData { QuestId = QuestId70001 } },

            // Zone + position (mirrors PE1 exactly)
            ObsMs("GetPlayerZone",     null, ZoneValue(128u),             100),
            ObsMs("GetPlayerPosition", null, PositionValue(10f, 0f, 20f), 150),

            // Currency seed before shop opens
            ObsMs("CurrencyBalance", null, CurrencyBalanceValue(10_000L, 0), 250),

            // Shop opens — G5 payload with GC axes (2, 1)
            new ObservationEvent
            {
                RunId = "aaa",
                Data = new ObservationEvent.ObservationData { Method = "ShopOpened", Argument = null, Value = shopOpenedWithGc }
            },

            // Item count rose
            ObsMs("VendorItemCount", null, VendorItemCountValue(ItemId1601, 1), 350),

            // Gil fell
            ObsMs("CurrencyBalance", null, CurrencyBalanceValue(9_000L, 0), 400),

            // Shop closed — span retained
            ObsMs("ShopOpened", null, ShopOpenedValue(false), 450),

            // Decision + interact action (mirrors PE1 exactly)
            new DecisionEvent { RunId = "aaa", Data = new DecisionEvent.DecisionData { StepId = null, ActionType = "wait" } },

            new ActionSubmittedEvent
            {
                RunId = "aaa",
                Data = new ActionSubmittedEvent.ActionSubmittedData
                {
                    ActionType = "interact",
                    Parameters = JsonSerializer.SerializeToElement(new { target = VendorNpcId })
                }
            },

            new ActionCompletedEvent
            {
                RunId = "aaa",
                Data = new ActionCompletedEvent.ActionCompletedData { ActionType = "interact", Outcome = "ok" }
            },

            new RunEndEvent { RunId = "aaa", Data = new RunEndEvent.RunEndData { Outcome = "done" } }
        };

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var allSteps = draft.Definition.Sequences.SelectMany(s => s.Steps).ToList();

        var purchaseStep = allSteps.OfType<PurchaseItemStep>().FirstOrDefault();
        Assert.NotNull(purchaseStep);

        // GC axes must be populated from the ShopOpened sibling keys
        Assert.Equal(2, purchaseStep!.GcCategory);
        Assert.Equal(1, purchaseStep.GcRankTier);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // G-O3a extractor companion — legacy trace (no GC keys in ShopOpened) emits
    // PurchaseItemStep with both GC fields null.
    //
    // Regression guard: PE1 already verifies the step is emitted; this verifies
    // the GC fields are null (not some non-null default) when the payload has no GC keys.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void GO3a_Extractor_LegacyTrace_EmitsNullGcFields()
    {
        // GIVEN — standard PE1 trace (ShopOpened uses legacy {value:bool} shape, no GC keys)
        var events = BuildPurchaseTrace(decisionActionType: "wait");

        // WHEN
        var extractor = new TraceToQuestExtractor();
        var result = extractor.Extract(events);

        // THEN
        var draft = Assert.IsType<Result<QuestDraftResult>.Success>(result).Value;
        var purchaseStep = draft.Definition.Sequences
            .SelectMany(s => s.Steps)
            .OfType<PurchaseItemStep>()
            .FirstOrDefault();

        Assert.NotNull(purchaseStep);
        Assert.Null(purchaseStep!.GcCategory);
        Assert.Null(purchaseStep.GcRankTier);
    }
}
