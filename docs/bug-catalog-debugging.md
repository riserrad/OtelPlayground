# Bug Catalog: Debugging Cheat Sheet

The game ships with a small catalog of bugs. One is picked at random each run. Your job, while playing, is to figure out which one is active by reading the telemetry in the Aspire Dashboard.

This cheat sheet is a walkthrough, not an answer key. For each bug it nudges you through the same triage motion: spot the anomaly in metrics, pivot to traces, confirm with logs. Resist the urge to skip ahead.

## Picking a strategy

By default, one of the six strategies below is picked at random when the game starts. If you want to control it:

- `BUG_STRATEGY=<name>` forces a specific strategy. Case-insensitive. Valid names: `LeakyRepair`, `LatencyInjection`, `SilentCounterCorruption`, `StickyCascadeMultiplier`, `WrongTargetDegradation`, `RetryStorm`, `SamplingBlindSpot`, `OrphanSpan`. Unknown names exit with code 2 and print the valid list to stderr.
- `BUG_STRATEGY_SEED=<int>` seeds the RNG that picks both the target subsystem and the strategy. Same seed, same `(target, strategy)` pair. Only used when `BUG_STRATEGY` is unset.

On startup the game logs `Bug strategy: <Name>, target: <Target>` so you know what was chosen. The mechanics of each strategy stay hidden, since figuring those out is the point.

## How to read each section

Each section has the symptom you'll notice as a player, then a numbered walkthrough. The questions are deliberate. If you stop and answer each one in the dashboard before moving on, the bug usually outs itself before you reach the bottom.

---

## LeakyRepair

**Symptom:** you keep repairing one subsystem, but its health gauge keeps trending down anyway.

1. Open Metrics. Pull up `station.subsystem.health` and filter by `subsystem.name=<the one you're repairing>`. Is the line trending down even right after each repair? How does that compare to other subsystems?
2. Switch to `station.repair.effectiveness` as a histogram. What does the distribution look like? Is it a single tight cluster around 100%, or are there two clusters?
3. The bimodal shape is the clue. Click an exemplar in the lower cluster. Where does it take you?
4. You should land on a `RepairAction` span. Compare the `repair.requested` and `repair.applied` tags. Anything noteworthy on the span events?
5. From the trace, jump to the matching logs. What does the ERROR-level entry tell you about the difference between requested and applied?

*Teaches: the canonical metrics → traces → logs triage; how a bimodal histogram gives away two code paths; exemplars as the bridge from a metric data point to the exact span that produced it.*

---

## LatencyInjection

**Symptom:** the game feels sluggish. Nothing in the gauges looks broken.

1. Open Traces. Filter by name=`RepairAction` and sort by duration descending. Are recent spans visibly longer than older ones?
2. Open one of the slow spans. Are there child spans that account for the time, or is the duration sitting on the span itself with nothing to point at?
3. Cross-check `station.repair.effectiveness`. Is it healthy? If repairs apply 100% but feel slow, what does that tell you about where the time is going?
4. Search the logs for anything correlating with the slowdown. Find anything? What does the absence of a log entry suggest, given how loud the repair path normally is?

*Teaches: span duration is a first-class signal, not just a debugging extra. Absence of a log is not the same as "everything is fine". This is the case for span-duration histograms.*

---

## SilentCounterCorruption

**Symptom:** nothing visibly wrong. The game plays normally.

1. Open Metrics and look at `station.cycles.total`. Compute the rate. How does it compare to what you'd expect from the cycle interval the game uses (one cycle every few seconds)?
2. Now switch to Traces and count spans named `StationCycle` over the same window. Does the span count match the metric rate, or one of them?
3. The mismatch IS the bug. Which one do you trust, and why? Spans are concrete events, counters are accumulators.
4. Drill into one `StationCycle` span and pivot to its logs. Do you see one cycle's worth of subsystem-tick log entries, or more?

*Teaches: cross-validating metrics against traces. Trust events you can count, suspect counters you can't tie to a concrete event.*

---

## StickyCascadeMultiplier

**Symptom:** degradation keeps accelerating even after you've patched the critical subsystems back to safe levels.

1. Open `station.subsystem.health` for a subsystem that's currently healthy. Is it falling faster than the `BaseDegradationRate` would predict?
2. Open Traces, filter by name=`SubsystemTick`, and look at the `degradation.rate` tag on recent spans. Expected value is `Base × Cascade × Difficulty`. Is the cascade portion still elevated?
3. Now filter Traces by name=`CascadeCheck`. How recent is the last one? If there hasn't been a cascade in a while, why would `degradation.rate` still be inflated?
4. Confirm with logs. Filter for "Cascade failure". Are there entries that line up with the inflated rate, or is the multiplier ghosting?

*Teaches: reasoning about derived values across spans. The expected formula is the contract; if a tag doesn't match the formula, something upstream forgot to reset.*

---

## WrongTargetDegradation

**Symptom:** a subsystem you've been ignoring is collapsing. The one you're tending is fine.

1. Open `station.subsystem.health` and view all four series at once. Which one is falling faster than the others?
2. Filter logs by `subsystem.name=<the unexpected one>`. Are there degradation entries? How frequent?
3. Open Traces, filter by name=`SubsystemTick` at one of those timestamps. Look at `subsystem.name`, `health.before`, `health.after`. Do the span and the log agree on which subsystem just lost health?
4. Open the parent `StationCycle` for that tick. Do all the child `SubsystemTick` spans agree on the wrong target, or just some of them? What does that tell you about where the redirect is happening?

*Teaches: span attributes as ground truth. Logs and traces correlate by trace_id, so once you have the span you have a free pivot to the log lines that came from inside it.*

---

## RetryStorm

**Symptom:** repair quota runs out fast. "No repairs left" shows up even though you only pressed R once.

1. Open Metrics. Pull the rate for `station.repairs.total`. How does it compare to how often you actually pressed R?
2. Now look at `station.repairs.denied`. Is it climbing too? What does the `denied / total` ratio look like, and is it stable?
3. Open Traces, filter by name=`RepairAction` over a 30-second window. How many spans show up versus how many times you clicked? Look for `repair.attempt` span events and the `attempt.outcome` tag (success, failure, denied).
4. Filter logs by "quota exhausted". How many entries show up inside a single cycle? Is one user click producing one entry, or many?

*Teaches: sanity-checking counter rates against actual user actions. Ratio metrics. Spans record what the system attempted, not just what the user asked for, and that gap is where retry bugs live.*

---

## SamplingBlindSpot

**Symptom:** the counter says N cascade failures but Traces show far fewer. The game-over screen even rubs your nose in it: `Cascade failures: 7  |  Traces captured: 1`.

1. Open Metrics. Pull `station.cascade.failures`. Sum the rate over the session. Does the total line up with what the game-over screen reported?
2. Switch to Traces, filter by `name=CascadeCheck`, count them over the same window. How does the trace count compare to the counter total?
3. The mismatch is the bug. Counters and traces are independent pipelines — sampling decisions only touch traces. Where would you look to confirm the sampler is the cause?
4. Open the `bug.strategy` resource attribute on any span you do still see. The strategy name is the giveaway, but what does the sampler config in `Program.cs` look like to make it land?
5. Compare against `B2` — the hull-driven sampler should ramp up tracing on critical hull. Is it doing that, or is something pinning the rate low?

*Teaches: head sampling makes its decision before the cascade tag exists, so it cannot bias toward errors. Counters and traces are independent — trace loss does not affect metric totals. This is the case for pairing head sampling with tail-based sampling and/or always-on error sampling in production.*


---

## Sampling: head vs tail, and where each lives

The space station has two sampling stages:

- **Head sampler**: runs at span start, before any tag is set. Sees the span name and the parent context, nothing else. Uses cheap inputs to make a fast decision: keep this one, drop this one. In general, any sampler that decides at span creation time is a head sampler.
- **Tail sampler**: runs after the trace has finished and every span has set its tags. Sees the whole trace at once and can keep traces that contain errors / cascades / failed repairs even if a head sampler would have dropped them. Treat tail sampling here as a conceptual contrast with head sampling; mode-specific wiring is introduced in a follow-up change.

A head sampler can never decide on a tag it has not seen yet. That is the whole reason `SamplingBlindSpot` works as a teaching beat. Head sampling at a low rate cannot bias toward errors, because the error tag does not exist when the decision is made.

**Where tail sampling lives in real production:** in the **OpenTelemetry Collector**, not in your application process. The collector runs as a sidecar or as a fleet-wide service, sees traces from every instance of every service, and can make consistent tail decisions across the whole fleet. The `TailSamplingProcessor` in this codebase is a single-process teaching simulation; it cannot make cross-process decisions and it holds buffered spans in memory of the game itself. **Do not carry the in-process pattern into a production service.** If you want tail sampling in production, run a collector with the `tail_sampling` processor configured.

**Exemplars** are the bridge from a metric data point back to a representative trace. With `.SetExemplarFilter(ExemplarFilterType.TraceBased)` on the `MeterProviderBuilder`, the SDK attaches a TraceId to a small sample of measurements per histogram bucket. Click a slow bucket on `station.repair.effectiveness` in Aspire, follow the exemplar marker, land on a real trace. That closes the metric → trace investigation loop.

1. Open Metrics. Find a histogram bucket that looks anomalous on `station.repair.effectiveness` (e.g. effectiveness < 50%).
2. Click the bucket. The exemplar marker links to a TraceId. Open it.
3. The trace shows the actual `RepairAction` span where the bug fired. From there, the usual span-events / tags / linked logs walk applies.
4. If exemplars are missing on a bucket you expected to find one in, check that the head sampler at game start was not `AlwaysOff` and that the trace was not dropped by tail sampling.

*Teaches: head and tail sampling answer different questions. Exemplars are the metric → trace bridge that keeps a low sample rate from hiding the trace you actually want. Production tail sampling lives in the collector; the in-game implementation is a single-process simulation.*

---

## Context propagation

A trace can span multiple processes. Each process sees only its own slice; the trace's true root may live in an upstream service the local process never observes. .NET surfaces three different "parent" concepts on `Activity` that get conflated all the time:

- `Activity.Parent`: the **in-process** parent reference. Always null for an activity started without an in-process parent, even when a remote parent was propagated in.
- `Activity.ParentSpanId`: the propagated parent SpanId. Default (all-zero) if and only if there is no parent at all. Non-default whenever a parent context was supplied, in-process or remote.
- `Activity.HasRemoteParent`: true when the parent context arrived from outside this process (typically via a W3C `traceparent` header). False for in-process parenting.

**Symptom:** trace root Activity has a non-default `parent_span_id` but the parent Activity isn't in the trace.

**Look at:** is this the first process in the trace path, or is upstream supposed to inject a `traceparent` header? Inspect `HasRemoteParent` / `parent_span_id` on the root Activity.

**Likely cause:** the activity was started with a remote parent context (correct propagation from an upstream service). The local process is a downstream service in a multi-process trace.

**Common bug:** tail samplers, exporters, or correlation middleware that treat `Parent is null` as "this is THE trace root". That assumption holds in a single-process app and breaks at every internal service in a multi-process system. The remote-parented activity gets flushed prematurely, before its in-process children finish, because the buffer thinks it has seen the trace's root. Use `Parent is null && ParentSpanId == default` to test for a true local root.

**Completion signal for cross-process traces.** Root-end + grace works in a single-process app because every trace's root is observable locally. A remote-parented trace never sees its true root end, so the buffer needs a different completion signal. The one this codebase ships is an inactivity timeout: when no activity has been added to a buffered trace for a configurable window, flush. Real production tail samplers in the OpenTelemetry Collector use cross-process trace assembly (and a timeout fallback) for the same reason.

*Teaches: cross-process tracing requires distinguishing "no parent at all" from "remote parent". Heuristics built on the single-process assumption silently misbehave once W3C propagation is wired up. The fix is one extra condition; the discipline is testing your trace pipeline against a remote-parented activity so the bug doesn't ship to production.*
