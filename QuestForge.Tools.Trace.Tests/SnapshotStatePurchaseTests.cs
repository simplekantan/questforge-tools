using System.Text.Json;
using QuestForge.Adapters.Tracing;
using QuestForge.Adapters.Types;
using QuestForge.Engine.Authoring;
using Xunit;
using static QuestForge.Tools.Trace.Tests.TraceTestHelpers;

namespace QuestForge.Tools.Trace.Tests;

/// <summary>
/// RED-phase tests for Slice E.3: SnapshotState purchase correlation (offline mirror).
///
/// GWT-PO1..PO5 from §13.5. Mirrors SnapshotStateCombatTests conventions exactly.
///
/// RED reason (behavioral): SnapshotState does not yet recognise ShopOpened /
/// CurrencyBalance / VendorItemCount — Apply returns false and PurchaseDetected stays null.
/// </summary>
public sealed class SnapshotStatePurchaseTests
{
    // ── Constants from §13.5 ─────────────────────────────────────────────────
    private static readonly QuestId BuyQuest = new(70001u);
    private const uint Item     = 1601u;
    private const uint SealItem = 6141u;

    // ── Time anchor (mirrors SnapshotStateCombatTests) ───────────────────────
    private static readonly DateTimeOffset T_Zero = T0;
    private static readonly DateTimeOffset T_100  = T0.AddMilliseconds(100);
    private static readonly DateTimeOffset T_200  = T0.AddMilliseconds(200);
    private static readonly DateTimeOffset T_300  = T0.AddMilliseconds(300);
    private static readonly DateTimeOffset T_400  = T0.AddMilliseconds(400);
    private static readonly DateTimeOffset T_500  = T0.AddMilliseconds(500);
    private static readonly DateTimeOffset T_900  = T0.AddMilliseconds(900);

    // ── Formatting helpers ───────────────────────────────────────────────────
    private static string FormatPd(PurchaseDetection? pd)
    {
        if (pd is null) return "(null)";
        var deltas = string.Join(",", pd.ItemDeltas.Select(kv => $"{kv.Key}:{kv.Value}"));
        return $"ShopWasOpen={pd.ShopWasOpen} ItemDeltas=[{deltas}] GilDropped={pd.GilDropped} SealsDropped={pd.SealsDropped}";
    }

    // ── Event builders (mirror combat tests ObsAt shape) ─────────────────────

    private static ObservationEvent ObsAt(string method, JsonElement? argument, JsonElement? value, DateTimeOffset at)
        => new(RunId: "aaa", Method: method, Argument: argument, Value: value, At: at);

    /// <summary>ShopOpened {value: bool} — mirrors InCombat shape.</summary>
    private static ObservationEvent ShopOpenedObs(bool open, DateTimeOffset at)
    {
        var value = JsonSerializer.SerializeToElement(new { value = open });
        return ObsAt("ShopOpened", null, value, at);
    }

    /// <summary>CurrencyBalance {gil: long, seals: int}.</summary>
    private static ObservationEvent CurrencyBalanceObs(long gil, int seals, DateTimeOffset at)
    {
        var value = JsonSerializer.SerializeToElement(new { gil, seals });
        return ObsAt("CurrencyBalance", null, value, at);
    }

    /// <summary>VendorItemCount {itemId: uint, count: int}.</summary>
    private static ObservationEvent VendorItemCountObs(uint itemId, int count, DateTimeOffset at)
    {
        var value = JsonSerializer.SerializeToElement(new { itemId, count });
        return ObsAt("VendorItemCount", null, value, at);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PO1 — clean gil purchase: ShopOpened + VendorItemCount + CurrencyBalance
    //           correlate into a PurchaseDetected with the correct deltas.
    //
    // CONTRACT: baseline is the balance at shop-open (after the pre-open CurrencyBalance
    // seed); GilDropped = baseline − current; SealsDropped = 0.
    // Matches the engine sequence from PurchaseSnapshotAggregatorTests.GwtPL1.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PO1_CleanGilPurchase_PurchaseDetectedCarriesDeltas()
    {
        // GIVEN
        var state = new SnapshotState(BuyQuest);

        // Seed pre-open balance (becomes baseline at shop-open)
        state.Apply(CurrencyBalanceObs(10_000L, 0, T_Zero));

        // Shop opens → baseline snapshotted
        state.Apply(ShopOpenedObs(true, T_100));

        // Item count rose
        state.Apply(VendorItemCountObs(Item, 1, T_200));

        // Gil fell
        state.Apply(CurrencyBalanceObs(9_000L, 0, T_300));

        // THEN
        var pd = state.ToSnapshot(T_900).PurchaseDetected;

        Assert.NotNull(pd);
        Assert.True(pd!.ShopWasOpen, $"ShopWasOpen must be true. Got: {FormatPd(pd)}");
        Assert.True(pd.ItemDeltas.ContainsKey(Item),
            $"ItemDeltas must contain {Item}. Got: {FormatPd(pd)}");
        Assert.Equal(1, pd.ItemDeltas[Item]);
        Assert.Equal(1000L, pd.GilDropped);
        Assert.Equal(0, pd.SealsDropped);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PO2 — recognised: all three new methods return true from Apply.
    //
    // CONTRACT: Apply returns true for recognised methods, false for unknown ones
    // (mirrors InCombat/EnemyKilled discipline).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PO2_AllThreeNewMethods_ApplyReturnsTrue()
    {
        var state = new SnapshotState(BuyQuest);

        var rShopOpened     = state.Apply(ShopOpenedObs(true, T_Zero));
        var rCurrency       = state.Apply(CurrencyBalanceObs(5_000L, 0, T_100));
        var rVendorItem     = state.Apply(VendorItemCountObs(Item, 1, T_200));

        Assert.True(rShopOpened,  "ShopOpened must return true (recognised)");
        Assert.True(rCurrency,    "CurrencyBalance must return true (recognised)");
        Assert.True(rVendorItem,  "VendorItemCount must return true (recognised)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PO3 — no shop-open event → PurchaseDetected is null (guard requires shop-open).
    //
    // CONTRACT: VendorItemCount + CurrencyBalance alone do not constitute a purchase
    // span. PurchaseDetected must be null (or report no purchase).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PO3_NoShopOpened_PurchaseDetectedIsNull()
    {
        // GIVEN — no ShopOpened{true}
        var state = new SnapshotState(BuyQuest);

        state.Apply(VendorItemCountObs(Item, 1, T_Zero));
        state.Apply(CurrencyBalanceObs(9_000L, 0, T_100));

        // THEN
        var pd = state.ToSnapshot(T_900).PurchaseDetected;

        // The guard requires shop-open; without it PurchaseDetected must be absent.
        Assert.True(pd is null || !pd.ShopWasOpen,
            $"Without ShopOpened{{true}} PurchaseDetected must be null or ShopWasOpen==false. Got: {FormatPd(pd)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PO4 — GC-seal offline purchase: seals dropped, gil unchanged.
    //
    // CONTRACT: mirror of PO1 for the GC seal path.
    // SealsDropped = baseline − current; GilDropped = 0.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PO4_GcSealPurchase_SealsDroppedGilUnchanged()
    {
        // GIVEN
        var state = new SnapshotState(BuyQuest);

        // Seed pre-open balance
        state.Apply(CurrencyBalanceObs(5_000L, 2_000, T_Zero));

        // Shop opens
        state.Apply(ShopOpenedObs(true, T_100));

        // Three of a seal item acquired
        state.Apply(VendorItemCountObs(SealItem, 3, T_200));

        // Only seals fell
        state.Apply(CurrencyBalanceObs(5_000L, 1_400, T_300));

        // THEN
        var pd = state.ToSnapshot(T_900).PurchaseDetected;

        Assert.NotNull(pd);
        Assert.True(pd!.ItemDeltas.ContainsKey(SealItem),
            $"ItemDeltas must contain {SealItem}. Got: {FormatPd(pd)}");
        Assert.Equal(3, pd.ItemDeltas[SealItem]);
        Assert.Equal(600, pd.SealsDropped);
        Assert.Equal(0L, pd.GilDropped);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GWT-PO5 — window reset: after a detected purchase, ResetPendingKeyItemDeltas
    //            clears PurchaseDetected; a subsequent ShopOpened{true} re-baselines
    //            cleanly with no phantom drop.
    //
    // CONTRACT: mirrors GWT-T7' (combat reset test). ResetPendingKeyItemDeltas is the
    // window-reset call; the purchase span (including baselines) is cleared. The next
    // shop-open starts fresh.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PO5_WindowReset_ClearsPurchaseSpan_NextShopOpenRebaselines()
    {
        // GIVEN — establish a completed purchase
        var state = new SnapshotState(BuyQuest);

        state.Apply(CurrencyBalanceObs(10_000L, 0, T_Zero));
        state.Apply(ShopOpenedObs(true, T_100));
        state.Apply(VendorItemCountObs(Item, 1, T_200));
        state.Apply(CurrencyBalanceObs(9_000L, 0, T_300));

        // Precondition: purchase IS detected before reset
        var pdBefore = state.ToSnapshot(T_500).PurchaseDetected;
        Assert.NotNull(pdBefore);

        // PART A: reset clears the purchase span
        state.ResetPendingKeyItemDeltas();

        var pdAfterReset = state.ToSnapshot(T_500).PurchaseDetected;
        Assert.True(pdAfterReset is null || !pdAfterReset.ShopWasOpen,
            $"After Reset PurchaseDetected must be null/empty. Got: {FormatPd(pdAfterReset)}");

        // PART B: subsequent shop-open re-baselines; no phantom drop from the old baseline
        // Current balance is 9000 (from last CurrencyBalance); open at 9000.
        state.Apply(ShopOpenedObs(true, T_500));

        // No further balance change — drop should be 0
        var pdAfterReopen = state.ToSnapshot(T_900).PurchaseDetected;
        Assert.NotNull(pdAfterReopen);
        Assert.Equal(0L, pdAfterReopen!.GilDropped);
        Assert.Equal(0, pdAfterReopen.SealsDropped);
    }
}
