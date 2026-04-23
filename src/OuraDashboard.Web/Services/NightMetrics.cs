namespace OuraDashboard.Web.Services;

/// <summary>
/// Computed custom metrics derived from the intra-night HR and HRV timeseries
/// already loaded in <see cref="NightData"/>. No DB access — pure in-memory calculation.
/// All percentages are 0–100. Nullable means "not enough data to compute."
/// </summary>
public record NightMetrics(
    // ── Cardiovascular ──────────────────────────────────────────────────────
    /// <summary>% of the night where HR sample was above 75 bpm.</summary>
    double? HrAbove75Pct,
    /// <summary>% of the night where HR sample was above 80 bpm.</summary>
    double? HrAbove80Pct,
    /// <summary>
    /// Minutes from session start until HR first "settles" below 75 bpm,
    /// defined as 3+ consecutive 5-min samples all at or below 75 bpm.
    /// Null means HR never settled.
    /// </summary>
    int? HrSettlingMinutes,
    /// <summary>Mean HR of the first half of the sleep session (bpm).</summary>
    double? HrEarlyHalfAvg,
    /// <summary>Mean HR of the second half of the sleep session (bpm).</summary>
    double? HrLateHalfAvg,
    /// <summary>"settled" | "elevated" | "calm" | "variable" — overall level + early-vs-late delta.</summary>
    string HrShape,

    // ── HRV ─────────────────────────────────────────────────────────────────
    /// <summary>% of HRV samples below 12 ms (poor autonomic recovery zone).</summary>
    double? HrvBelow12Pct,
    /// <summary>% of HRV samples in the 12–20 ms range (moderate zone).</summary>
    double? Hrv12To20Pct,
    /// <summary>% of HRV samples above 20 ms (good recovery zone).</summary>
    double? HrvAbove20Pct,
    /// <summary>Mean HRV of the first half of the night.</summary>
    double? HrvEarlyHalfAvg,
    /// <summary>Mean HRV of the second half of the night.</summary>
    double? HrvLateHalfAvg,
    /// <summary>"improving" | "declining" | "flat" | "N/A" (delta threshold ±2 ms).</summary>
    string HrvDirection,
    /// <summary>Highest single HRV sample recorded during the night.</summary>
    double? HrvPeak,

    // ── Summary ──────────────────────────────────────────────────────────────
    /// <summary>Deep + REM combined in minutes.</summary>
    int? RestorativeMinutes,
    /// <summary>Awake minutes as % of time in bed. Highlights what efficiency scores hide.</summary>
    double? AwakePctOfBed,
    /// <summary>
    /// Real Recovery Score (0–100). Composite of HR below-75 proportion (35 pts),
    /// average HRV (25 pts, ceiling 20 ms), restorative sleep (25 pts, target 150 min),
    /// and respiratory rate (15 pts, target ≤14 brpm). Normalised over available components.
    /// </summary>
    int? RealRecoveryScore
);

public static class NightMetricsCalculator
{
    /// <summary>
    /// Computes all custom metrics from the timeseries and scalars already in <paramref name="d"/>.
    /// Safe to call with empty series — returns nulls for metrics that cannot be computed.
    /// </summary>
    public static NightMetrics Compute(NightData d)
    {
        // Filter to samples that have a value (nulls in the series = ring not worn / signal lost)
        var hrSamples  = d.HeartRateSeries.Where(p => p.Value.HasValue).ToList();
        var hrvSamples = d.HrvSeries.Where(p => p.Value.HasValue).ToList();

        // ── HR metrics ───────────────────────────────────────────────────────
        double? hrAbove75Pct   = null;
        double? hrAbove80Pct   = null;
        int?    hrSettlingMins = null;
        double? hrEarlyHalfAvg = null;
        double? hrLateHalfAvg  = null;
        string  hrShape        = "N/A";

        if (hrSamples.Count > 0)
        {
            hrAbove75Pct = hrSamples.Count(p => p.Value!.Value > 75) * 100.0 / hrSamples.Count;
            hrAbove80Pct = hrSamples.Count(p => p.Value!.Value > 80) * 100.0 / hrSamples.Count;

            // Settling: first index in the non-null list where 3 consecutive samples are all ≤ 75 bpm.
            // We use the full series start (including nulls) as the origin so the offset is wall-clock.
            const int consecutiveRequired = 3;
            for (int i = 0; i <= hrSamples.Count - consecutiveRequired; i++)
            {
                bool allBelow = true;
                for (int j = i; j < i + consecutiveRequired; j++)
                {
                    if (hrSamples[j].Value!.Value > 75) { allBelow = false; break; }
                }
                if (allBelow)
                {
                    var origin = d.HeartRateSeries.First().Time;
                    hrSettlingMins = (int)(hrSamples[i].Time - origin).TotalMinutes;
                    break;
                }
            }

            // HR curve shape: first-half vs second-half averages
            int hrMidIdx    = hrSamples.Count / 2;
            var hrVals      = hrSamples.Select(p => p.Value!.Value).ToList();
            var hrEarlyHalf = hrVals.Take(hrMidIdx).ToList();
            var hrLateHalf  = hrVals.Skip(hrMidIdx).ToList();
            if (hrEarlyHalf.Count > 0) hrEarlyHalfAvg = hrEarlyHalf.Average();
            if (hrLateHalf.Count  > 0) hrLateHalfAvg  = hrLateHalf.Average();

            if (hrEarlyHalfAvg.HasValue && hrLateHalfAvg.HasValue)
            {
                double overallHr = hrVals.Average();
                double hrDelta   = hrLateHalfAvg.Value - hrEarlyHalfAvg.Value;
                // "settled": was high, came down. "elevated": stayed high. "calm": stayed low. "variable": rose later.
                hrShape = overallHr > 75
                    ? (hrDelta < -3 ? "settled" : "elevated")
                    : (hrDelta >  3 ? "variable" : "calm");
            }
        }

        // ── HRV metrics ──────────────────────────────────────────────────────
        double? hrvBelow12Pct = null;
        double? hrv12To20Pct  = null;
        double? hrvAbove20Pct = null;
        double? hrvEarlyAvg   = null;
        double? hrvLateAvg    = null;
        string  hrvDirection  = "N/A";
        double? hrvPeak       = null;

        if (hrvSamples.Count > 0)
        {
            var vals = hrvSamples.Select(p => p.Value!.Value).ToList();

            hrvBelow12Pct = vals.Count(v => v < 12)              * 100.0 / vals.Count;
            hrv12To20Pct  = vals.Count(v => v >= 12 && v <= 20)  * 100.0 / vals.Count;
            hrvAbove20Pct = vals.Count(v => v > 20)              * 100.0 / vals.Count;
            hrvPeak       = vals.Max();

            int mid = vals.Count / 2;
            var early = vals.Take(mid).ToList();
            var late  = vals.Skip(mid).ToList();
            if (early.Count > 0) hrvEarlyAvg = early.Average();
            if (late.Count  > 0) hrvLateAvg  = late.Average();

            if (hrvEarlyAvg.HasValue && hrvLateAvg.HasValue)
            {
                double delta = hrvLateAvg.Value - hrvEarlyAvg.Value;
                // ±2 ms dead-band to avoid labelling noise as a "direction"
                hrvDirection = delta > 2 ? "improving" : delta < -2 ? "declining" : "flat";
            }
        }

        // ── Restorative sleep ────────────────────────────────────────────────
        int? restorativeMin = (d.DeepMinutes.HasValue && d.RemMinutes.HasValue)
            ? d.DeepMinutes.Value + d.RemMinutes.Value
            : (int?)null;

        double? awakePctOfBed = (d.AwakeMinutes.HasValue && d.TimeInBedMinutes.HasValue && d.TimeInBedMinutes.Value > 0)
            ? Math.Round(d.AwakeMinutes.Value * 100.0 / d.TimeInBedMinutes.Value, 1)
            : null;

        // ── Real Recovery Score (0–100) ──────────────────────────────────────
        // Each component contributes a weighted sub-score.
        // If a component's data is unavailable, its weight drops out and the
        // result is normalised over what was actually available.
        int? realRecovery = null;
        {
            double score     = 0;
            double available = 0;

            // HR component (35 pts): what fraction of the night HR stayed ≤ 75 bpm
            if (hrAbove75Pct.HasValue)
            {
                score     += (1.0 - hrAbove75Pct.Value / 100.0) * 35;
                available += 35;
            }

            // HRV component (25 pts): average HRV scaled to 0–20 ms ceiling
            if (d.AverageHrv.HasValue)
            {
                score     += Math.Min(d.AverageHrv.Value / 20.0, 1.0) * 25;
                available += 25;
            }

            // Restorative sleep (25 pts): scaled to 150-min target
            if (restorativeMin.HasValue)
            {
                score     += Math.Min(restorativeMin.Value / 150.0, 1.0) * 25;
                available += 25;
            }

            // Respiratory rate (15 pts): ≤14 brpm = full score, linear decay to 0 at 18 brpm
            if (d.AverageBreath.HasValue)
            {
                double resp = d.AverageBreath.Value;
                double pts  = resp <= 14 ? 15 : resp >= 18 ? 0 : (18 - resp) / 4.0 * 15;
                score     += pts;
                available += 15;
            }

            if (available > 0)
                realRecovery = (int)Math.Round(score / available * 100);
        }

        return new NightMetrics(
            HrAbove75Pct:      hrAbove75Pct  is double ha75 ? Math.Round(ha75,  1) : null,
            HrAbove80Pct:      hrAbove80Pct  is double ha80 ? Math.Round(ha80,  1) : null,
            HrSettlingMinutes: hrSettlingMins,
            HrEarlyHalfAvg:   hrEarlyHalfAvg is double heh ? Math.Round(heh, 1) : null,
            HrLateHalfAvg:    hrLateHalfAvg  is double hlh ? Math.Round(hlh, 1) : null,
            HrShape:          hrShape,
            HrvBelow12Pct:     hrvBelow12Pct is double hb12 ? Math.Round(hb12,  1) : null,
            Hrv12To20Pct:      hrv12To20Pct  is double hm   ? Math.Round(hm,    1) : null,
            HrvAbove20Pct:     hrvAbove20Pct is double ha20 ? Math.Round(ha20,  1) : null,
            HrvEarlyHalfAvg:   hrvEarlyAvg   is double he   ? Math.Round(he,    1) : null,
            HrvLateHalfAvg:    hrvLateAvg    is double hl   ? Math.Round(hl,    1) : null,
            HrvDirection:      hrvDirection,
            HrvPeak:           hrvPeak       is double hp   ? Math.Round(hp,    1) : null,
            RestorativeMinutes: restorativeMin,
            AwakePctOfBed:     awakePctOfBed,
            RealRecoveryScore:  realRecovery
        );
    }
}
