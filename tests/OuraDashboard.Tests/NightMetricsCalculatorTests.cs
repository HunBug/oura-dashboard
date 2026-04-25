using OuraDashboard.Web.Services;
using Xunit;

namespace OuraDashboard.Tests;

/// <summary>
/// Unit tests for <see cref="NightMetricsCalculator.Compute"/>.
/// Focus: RRS (Real Recovery Score) formula and the sub-metrics that feed into it.
/// All expected values are derived analytically from the formula comments in NightMetrics.cs.
/// </summary>
public class NightMetricsCalculatorTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>Creates a minimal NightData with only the fields relevant to metric calculation.</summary>
    private static NightData Make(
        int?              avgHrv         = null,
        double?           avgBreath      = null,
        int?              deepMinutes    = null,
        int?              remMinutes     = null,
        int?              awakeMinutes   = null,
        int?              timeInBed      = null,
        List<SamplePoint>? hr            = null,
        List<SamplePoint>? hrv           = null
    ) => new NightData(
        UserName: "Test",
        Day: DateOnly.MinValue,
        SleepScore: null, ReadinessScore: null,
        TempDeviation: null, TempTrendDeviation: null,
        AverageHrv: avgHrv, AverageHeartRate: null, LowestHeartRate: null,
        AverageBreath: avgBreath,
        DeepMinutes: deepMinutes, RemMinutes: remMinutes,
        LightSleepMinutes: null, AwakeMinutes: awakeMinutes,
        TotalSleepMinutes: null, TimeInBedMinutes: timeInBed,
        Efficiency: null, LatencyMinutes: null, RestlessPeriods: null,
        BedtimeStart: null, BedtimeEnd: null, SleepPhase5Min: null,
        SleepDeepContributor: null, SleepEfficiencyContributor: null,
        SleepLatencyContributor: null, SleepRemContributor: null,
        SleepRestfulnessContributor: null, SleepTimingContributor: null,
        SleepTotalContributor: null,
        ReadinessActivityBalance: null, ReadinessBodyTemp: null,
        ReadinessHrvBalance: null, ReadinessPrevDayActivity: null,
        ReadinessPrevNight: null, ReadinessRecoveryIndex: null,
        ReadinessRhr: null, ReadinessSleepBalance: null,
        StressHighSec: null, RecoveryHighSec: null,
        Steps: null, ActiveCalories: null,
        Spo2Average: null, BreathingDisturbanceIndex: null,
        ResilienceLevel: null, ResilienceSleepRecovery: null,
        ResilienceDaytimeRecovery: null,
        HrvSeries: hrv ?? [],
        HeartRateSeries: hr ?? []
    );

    /// <summary>Builds a heart-rate or HRV sample series with 5-minute intervals.</summary>
    private static List<SamplePoint> Series(params double[] values)
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero);
        return values.Select((v, i) => new SamplePoint(t0.AddMinutes(i * 5), v)).ToList();
    }

    // ── RRS: full-component cases ──────────────────────────────────────────────

    [Fact]
    public void Rrs_PerfectNight_Returns100()
    {
        // All four components at maximum:
        //   HR: 0% above 75  → 35 pts
        //   HRV: avg 20 ms   → 25 pts
        //   Rest: 150 min    → 25 pts
        //   Resp: 14.0 brpm  → 15 pts  (total 100/100)
        var d = Make(avgHrv: 20, avgBreath: 14.0, deepMinutes: 90, remMinutes: 60,
                     hr: Series(70, 70, 70, 70));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(100, m.RealRecoveryScore);
    }

    [Fact]
    public void Rrs_WorstNight_Returns0()
    {
        // All four components at minimum:
        //   HR: 100% above 75 → 0 pts
        //   HRV: avg 0        → 0 pts
        //   Rest: 0 min       → 0 pts
        //   Resp: 18.0 brpm   → 0 pts  (total 0/100)
        var d = Make(avgHrv: 0, avgBreath: 18.0, deepMinutes: 0, remMinutes: 0,
                     hr: Series(80, 80, 80, 80));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(0, m.RealRecoveryScore);
    }

    [Fact]
    public void Rrs_AverageNight_Returns50()
    {
        // HR: 50% above 75  → (1 - 0.5) * 35 = 17.5 pts
        // HRV: avg 10       → (10/20) * 25  = 12.5 pts
        // Rest: 75 min      → (75/150) * 25 = 12.5 pts
        // Resp: 16 brpm     → (18-16)/4 * 15 = 7.5 pts  (total 50/100)
        var d = Make(avgHrv: 10, avgBreath: 16.0, deepMinutes: 37, remMinutes: 38,
                     hr: Series(70, 70, 80, 80));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(50, m.RealRecoveryScore);
    }

    // ── RRS: normalisation when components are missing ─────────────────────────

    [Fact]
    public void Rrs_NoData_ReturnsNull()
    {
        var d = Make();  // all nulls, empty series

        var m = NightMetricsCalculator.Compute(d);

        Assert.Null(m.RealRecoveryScore);
    }

    [Fact]
    public void Rrs_OnlyRespRate_NormalisesOver15Pts()
    {
        // Available = 15 only; perfect resp → 15/15 * 100 = 100
        var d = Make(avgBreath: 14.0);

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(100, m.RealRecoveryScore);
    }

    [Fact]
    public void Rrs_MissingRespRate_NormalisesOverRemainingComponents()
    {
        // Available = 35 + 25 + 25 = 85; all three at max → 85/85 * 100 = 100
        var d = Make(avgHrv: 20, deepMinutes: 90, remMinutes: 60,
                     hr: Series(70, 70, 70, 70));  // avgBreath omitted

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(100, m.RealRecoveryScore);
    }

    // ── RRS: individual component edge cases ──────────────────────────────────

    [Fact]
    public void Rrs_HrvAboveCeiling_IsCappedAt20Ms()
    {
        // AverageHrv = 40 ms; formula clamps to 20 ms → full 25 pts → RRS = 100
        var d = Make(avgHrv: 40);

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(100, m.RealRecoveryScore);
    }

    [Fact]
    public void Rrs_RestorativeAboveTarget_IsCapped()
    {
        // 200 min restoratives → min(200/150, 1) * 25 = 25 pts → RRS = 100
        var d = Make(deepMinutes: 120, remMinutes: 80);  // 200 min total

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(100, m.RealRecoveryScore);
    }

    [Theory]
    [InlineData(14.0, 100)]  // exactly at lower bound → 15 pts / 15 available
    [InlineData(18.0,   0)]  // exactly at upper bound → 0 pts / 15 available
    [InlineData(16.0,  50)]  // midpoint: (18-16)/4*15 = 7.5 → 7.5/15*100 = 50
    [InlineData(13.0, 100)]  // below lower bound still clamps to full score
    public void Rrs_RespRateBoundaries_WithOnlyRespComponent(double brpm, int expectedRrs)
    {
        var d = Make(avgBreath: brpm);

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(expectedRrs, m.RealRecoveryScore);
    }

    // ── HR above 75% sub-metric ────────────────────────────────────────────────

    [Fact]
    public void HrAbove75Pct_AllSamplesAt75_IsZero()
    {
        // > 75 check: exactly 75 bpm does NOT count as above
        var d = Make(hr: Series(75, 75, 75, 75));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(0.0, m.HrAbove75Pct);
    }

    [Fact]
    public void HrAbove75Pct_HalfAbove_Is50()
    {
        var d = Make(hr: Series(70, 70, 80, 80));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(50.0, m.HrAbove75Pct);
    }

    [Fact]
    public void HrAbove75Pct_NoSeries_IsNull()
    {
        var d = Make();  // empty HR series

        var m = NightMetricsCalculator.Compute(d);

        Assert.Null(m.HrAbove75Pct);
    }

    // ── Restorative minutes sub-metric ────────────────────────────────────────

    [Fact]
    public void RestorativeMinutes_SumOfDeepAndRem()
    {
        var d = Make(deepMinutes: 60, remMinutes: 90);

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(150, m.RestorativeMinutes);
    }

    [Fact]
    public void RestorativeMinutes_EitherNull_IsNull()
    {
        var withNullDeep = Make(deepMinutes: null, remMinutes: 60);
        var withNullRem  = Make(deepMinutes: 60,   remMinutes: null);

        Assert.Null(NightMetricsCalculator.Compute(withNullDeep).RestorativeMinutes);
        Assert.Null(NightMetricsCalculator.Compute(withNullRem).RestorativeMinutes);
    }

    // ── HR settling time ──────────────────────────────────────────────────────

    [Fact]
    public void HrSettlingMinutes_FirstConsecutiveTripleBelow75()
    {
        // Samples at 0, 5, 10, 15, 20 min.  HR drops at index 2 (10 min).
        // First 3-consecutive window all ≤ 75: indices [2,3,4] → settling time = 10 min.
        var d = Make(hr: Series(80, 80, 70, 70, 70));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(10, m.HrSettlingMinutes);
    }

    [Fact]
    public void HrSettlingMinutes_NeverSettles_IsNull()
    {
        var d = Make(hr: Series(80, 80, 80, 80, 80));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Null(m.HrSettlingMinutes);
    }

    [Fact]
    public void HrSettlingMinutes_SettlesImmediately_IsZero()
    {
        // All samples ≤ 75 from the start → first window [0,1,2] qualifies → 0 min
        var d = Make(hr: Series(70, 70, 70, 80));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal(0, m.HrSettlingMinutes);
    }

    // ── HRV direction ────────────────────────────────────────────────────────

    [Fact]
    public void HrvDirection_LateHalfHigherBy3ms_IsImproving()
    {
        // Early half avg = 18, late half avg = 22 → delta = +4 → "improving"
        var d = Make(hrv: Series(18, 18, 22, 22));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal("improving", m.HrvDirection);
    }

    [Fact]
    public void HrvDirection_LateHalfLowerBy3ms_IsDeclining()
    {
        // Early half avg = 22, late half avg = 18 → delta = -4 → "declining"
        var d = Make(hrv: Series(22, 22, 18, 18));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal("declining", m.HrvDirection);
    }

    [Fact]
    public void HrvDirection_DeltaWithinDeadband_IsFlat()
    {
        // delta = 1 ms (within ±2 dead-band) → "flat"
        var d = Make(hrv: Series(20, 20, 21, 21));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal("flat", m.HrvDirection);
    }

    // ── HR shape ────────────────────────────────────────────────────────────

    [Fact]
    public void HrShape_HighOverallDropsLater_IsSettled()
    {
        // Overall avg > 75, late avg much lower than early → "settled"
        var d = Make(hr: Series(85, 85, 70, 70));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal("settled", m.HrShape);
    }

    [Fact]
    public void HrShape_LowOverallStaysLow_IsCalm()
    {
        // Overall avg ≤ 75, late ≈ early → "calm"
        var d = Make(hr: Series(65, 65, 65, 65));

        var m = NightMetricsCalculator.Compute(d);

        Assert.Equal("calm", m.HrShape);
    }
}
