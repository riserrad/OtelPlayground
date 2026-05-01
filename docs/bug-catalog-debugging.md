# Bug Catalog: Debugging Cheat Sheet

The game ships with a small catalog of bugs. One is picked at random each run. Your job, while playing, is to figure out which one is active by reading the telemetry in the Aspire Dashboard.

This cheat sheet is a walkthrough, not an answer key. For each bug it nudges you through the same triage motion: spot the anomaly in metrics, pivot to traces, confirm with logs. Resist the urge to skip ahead.

The splash gate at game start asks whether you want to **just play** or to **play and learn Observability**. Just Playing runs the station with no bug active so you can focus on the game itself. Learning mode is where this catalog applies: a bug strategy is picked (random by default, override via `BUG_STRATEGY`), and the bug only shows up in telemetry. Pick mode 2 if you want anything below to matter.

After the mode pick, the splash asks for a difficulty: **Tutorial**, **Normal**, **Hard**, or **Expert**. Difficulty is orthogonal to mode. It tunes the gameplay challenge (repairs per cycle, degradation pace, event frequency) without changing whether a bug is active or how telemetry is sampled. Pick whichever difficulty fits the time you have; the bug catalog walkthroughs apply identically across all four.

## Picking a strategy

By default, one of the strategies below is picked at random when the game starts. If you want to control it:

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
2. Now look at `station.repairs.denied`. Is it climbing too? The counter description names three rejection paths: no free slot, repair already in flight on the same subsystem, and retry-quota exhaustion. RetryStorm fires on the third path; the first two fire from the slot-check at press time and are not the bug. Compare the rate of `denied` to the rate of `total` and the rate of times you actually pressed R. If `denied / total` is stable and `total / press-count` is high, you are looking at retries inside a single attempt rather than at slot rejections.
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
- **Tail sampler**: runs after the trace has finished and every span has set its tags. Sees the whole trace at once and can keep traces that contain errors / cascades / failed repairs even if a head sampler would have dropped them. Treat tail sampling here as a conceptual contrast with head sampling; profile-specific wiring is introduced in a follow-up change.

A head sampler can never decide on a tag it has not seen yet. That is the whole reason `SamplingBlindSpot` works as a teaching beat. Head sampling at a low rate cannot bias toward errors, because the error tag does not exist when the decision is made.

**Where tail sampling lives in real production:** in the **OpenTelemetry Collector**, not in your application process. The collector runs as a sidecar or as a fleet-wide service, sees traces from every instance of every service, and can make consistent tail decisions across the whole fleet. The `TailSamplingProcessor` in this codebase is a single-process teaching simulation; it cannot make cross-process decisions and it holds buffered spans in memory of the game itself. **Do not carry the in-process pattern into a production service.** If you want tail sampling in production, run a collector with the `tail_sampling` processor configured.

**Exemplars** are the bridge from a metric data point back to a representative trace. With `.SetExemplarFilter(ExemplarFilterType.TraceBased)` on the `MeterProviderBuilder`, the SDK attaches a TraceId to a small sample of measurements per histogram bucket. Click a slow bucket on `station.repair.effectiveness` in Aspire, follow the exemplar marker, land on a real trace. That closes the metric → trace investigation loop.

1. Open Metrics. Find a histogram bucket that looks anomalous on `station.repair.effectiveness` (e.g. effectiveness < 50%).
2. Click the bucket. The exemplar marker links to a TraceId. Open it.
3. The trace shows the actual `RepairAction` span where the bug fired. From there, the usual span-events / tags / linked logs walk applies.
4. If exemplars are missing on a bucket you expected to find one in, check that the head sampler at game start was not `AlwaysOff` and that the trace was not dropped by tail sampling.

**B2 / D3 cross-reference.** Sprint 004 shipped two more sampler-vs-counter teaching beats that ride this same axis. Game Modes × Difficulty (B2) shipped per-mode sampler profiles: the head-sampling regime the player operates under is selected at game start by their mode pick. JustPlaying defaults to the hull-driven sampler from Sprint 002; Learning defaults to the env-resolved tail-sampling profile. The SamplingBlindSpot walkthrough and the head-vs-tail framing here apply across all profiles unchanged. The OrphanSpan strategy (D3) extends the `Context propagation` section: a span emitted as a root locally can carry a non-default `parent_span_id` if upstream propagation injected one, and the strategy fakes this to teach the `Parent is null && ParentSpanId == default` true-root test. If a sampler in your pipeline treats `Parent is null` as "this is the root, flush now," the strategy will surface that bug as premature flushes on the orphan-tagged spans.

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

**Common bug:** tail samplers, exporters, or correlation middleware that treat `Parent is null` as "this is THE trace root". That assumption holds in a single-process app and breaks at every internal service in a multi-process system. The remote-parented activity gets flushed prematurely, before its in-process children finish, because the buffer thinks it has seen the trace's root. Use `Parent is null && ParentSpanId == default` to test for a true local root. For an example of a span that shows the false-root shape live in this codebase, set `BUG_STRATEGY=OrphanSpan` and watch the cycle exporter. Root spans with `parent_span_id != default` are exactly the failure mode this test guards against, and the strategy walks the player through it.

**Completion signal for cross-process traces.** Root-end + grace works in a single-process app because every trace's root is observable locally. A remote-parented trace never sees its true root end, so the buffer needs a different completion signal. The one this codebase ships is an inactivity timeout: when no activity has been added to a buffered trace for a configurable window, flush. Real production tail samplers in the OpenTelemetry Collector use cross-process trace assembly (and a timeout fallback) for the same reason.

*Teaches: `Parent is null` is not the same as 'trace root' once W3C propagation is in play.*

---

## Multi-cycle Activities

Most spans wrap a synchronous block: open the `using`, do the work, the `Dispose()` stops the Activity. The `RepairAction` span breaks that pattern on purpose. A repair takes 1-3 cycles to apply (the cycle count is computed from how damaged the subsystem is), so the `Activity` is started when you press R and stopped when one of four things happens 1-3 cycles later.

**Symptom:** an Activity has start and stop sites in different methods on different cycles, and the press-time stack is not the stack the stop runs on.

**Look at:** `RepairAction` spans whose start timestamp lands in cycle N and whose stop timestamp lands in cycle N+1, N+2, or N+3. In the trace, walk the `StationCycle` spans during that window. None of them parent `RepairAction`; they each carry a `Link` to it instead.

**Likely cause (intentional, here):** the work the span describes really does outlive the cycle in which it began. The two-cycle commit was a deliberate game-design choice, but the same shape shows up in real systems any time you start an Activity at request-acceptance and stop it at request-completion (long-running jobs, batch workflows, async retries with backoff).

**Common bug:** starting an Activity in one method and forgetting to stop it on every exit path. `RepairAction` has four stop sites and every one is load-bearing:
- **Completion.** The repair finishes naturally; `CompleteRepair` runs in a `finally` so even an exception path stops the Activity.
- **Player cancel.** Pressing C on a subsystem with an in-flight repair. Adds a `RepairCancelled` event with `cancellation.reason="player_cancel"`, then stops.
- **Reject at start.** A repair was attempted but no slot was free or the subsystem already has a repair in flight. The Activity is started before the slot check, so the rejection itself is observable as a span. The reject path then sets status `rejected` and stops the Activity immediately.
- **Shutdown.** Ctrl+C or hull-zero game-over while a repair is in flight. The cycle loop's `finally` walks every in-flight entry and stops each Activity with `cancellation.reason="shutdown"`.

Miss any of these and you ship orphan spans: Activities that never call `Stop()`, never get exported, and surface as "in-progress" forever in dashboards that expect end-time.

**Activity.Current discipline.** Starting a long-lived Activity has a second hazard: `Activity.Current` is ambient. If the calling thread is inside a `StationCycle` Activity at press-time and you call `StartActivity("RepairAction", ...)` directly, the new Activity inherits `StationCycle` as its parent, which is wrong here: `RepairAction` outlives the cycle and would prematurely-close the cycle's tail. The fix is the press-time null-pivot: capture `Activity.Current`, set it to `null`, start the new Activity (which now starts as a true root), then restore the captured `Current` in a `finally`. The `finally` matters because a throw from `StartActivity` or any tag-set in between would otherwise leak the null `Current` into the caller's ambient context.

*Teaches: an Activity's lifetime is bound to the call to `Stop()`, not to the lexical scope where it was started. When the work outlives the lexical scope, every exit path needs a stop call. `Activity.Current` is ambient and inherited by new Activities started under it, so a long-lived span that should be a root needs an explicit null-pivot/restore around `StartActivity` to avoid prematurely-closing whatever Activity was current at press-time.*

---

## ActivityLink as causal lineage

Parent-of is the strong relationship: it asserts `B is a child of A`, which means A's lifetime contains B's lifetime and the trace tree shows B as a sub-step of A. `ActivityLink` is the weak one: it says `B is causally related to A` without claiming containment or parentage. Two distinct relationships, two different question shapes a query can answer.

The space station has two cases where parent-of would falsify the relationship and a `Link` is the right tool.

**Symptom:** spans that are causally related (one triggered the other) but where the trigger's lifetime is not strictly contained inside the triggered span's lifetime, OR where the relationship is many-to-one and parentage would fork the trace tree.

**Pattern A: `StationCycle` links to in-flight `RepairAction`.** Each cycle, before starting the `StationCycle` span, the loop walks `_station.ActiveRepairs.InFlight` and builds an `ActivityLink` list pointing at each in-flight `RepairAction`'s context. The links are passed to `StartActivity(..., links: inFlightLinks)`. The cycle is not a child of any repair (the repair started 1-2 cycles earlier), and the repair is not a child of this cycle (it might outlive the cycle by 1-2 more). Parent-of would lie either direction. Link carries "this cycle relates to that repair" without falsifying the tree.

**Pattern B: `CascadeCheck` links to source `SubsystemTick`.** When a cascade fires, the `CascadeCheck` span is a child of the current `StationCycle` (same cycle, contained lifetime; parentage is correct). But the cascade was *triggered by* a specific `SubsystemTick` earlier in this cycle's tick loop. The trigger relationship is causal, not containment: the tick already completed by the time the cascade check runs. So `CascadeCheck` is parented to `StationCycle` and additionally `Link`s back to the source tick. The dictionary keying the tick-by-name is keyed by `sub.Name` (the original subsystem the loop iterated over), not the redirected target. Under `WrongTargetDegradationStrategy` the two diverge, and the keying choice preserves the cascade's true source.

**Look at:** in the trace UI, a span with `Link` count > 0 and a parent-of edge to its lexical parent. The Link list is a separate panel; it's a peer-of-parent relation, not a child relation.

**Likely cause:** the relationship is causal-but-not-containment. Parent-of would either invert lifetimes (Pattern A) or claim a child relationship that the runtime invariant forbids (Pattern B).

**Common bug:** treating `Link` as a weaker version of parent-of and reaching for it whenever parent-of feels awkward. It is not. `Link` does not propagate context the way parent-of does (samplers and exporters do not follow links the way they follow parent edges). If A really is the parent of B, use parent-of. Reach for `Link` only when parent-of would falsify the relationship.

*Teaches: parent-of asserts containment; `ActivityLink` asserts causal relation without containment. Pick by the runtime invariant the spans need to honor, not by what's convenient at the call site. The two link patterns this codebase uses (cycle → in-flight repair, cascade → source tick) are the two canonical shapes you will hit in real systems: long-lived work spans and cross-step causality within a single workflow.*

## Semantic conventions

Attribute keys are a contract. A metric, span, or log entry tagged `subsystem.name=Oxygen` and another tagged `subsystem=Oxygen` describe the same logical thing, but to a query engine they're two different dimensions. Filter by either key and you only see half the rows. The bug compounds the longer it lives because every consumer (dashboard, alert rule, downstream pipeline) is built against whichever key they happened to see first.

OpenTelemetry maintains a Semantic Conventions spec for exactly this reason: to make the contract explicit and shareable across services. Drift sneaks in through refactors, copy-paste between services, and emission code that doesn't read the existing tag conventions before adding a new tag.

**Symptom:** dashboard filter by attribute shows fewer rows than expected; total row count looks correct in aggregate but key-specific filters return half.

**Look at:** distinct attribute keys per metric or span over a time window. A healthy emission site has one key per logical dimension; a drifted one has two or more.

**Likely cause:** the same logical dimension is emitted under inconsistent keys (e.g. `subsystem.name` vs `subsystem`) across cycles, services, or refactors.

*Teaches: attribute keys are a typed contract; the time to enforce it is at the emission boundary, not after a dashboard quietly under-counts for a week.*

---

## Asymmetric tags as dedup keys

Two error sites can both legitimately increment the same counter. The inner site is the one most queries want to count. The outer site catches the tail of an unhandled exception that escaped the inner; without it, exceptions on rare code paths drop silently and the counter under-reports. With it, common-bail paths double-count: the inner increments once, the inner re-throws, the outer catches it and increments again.

**Symptom:** a counter total that's exactly 2× what your operator intuition expects, on the paths where an exception escapes the inner handler.

**Look at:** the two catch sites that both touch the same counter. Are they both unconditionally incrementing? Is one of them tagging the increment with something the other doesn't?

**Likely cause:** the outer-catch is an exception-safety net, not a separate event class. It exists so the counter doesn't under-report on rare paths. On common paths, both sites fire and the total inflates.

**Pattern this codebase uses:** the outer-catch tags its increment with `failure.layer="outer"`. The inner-catch sites do not set the tag. Standard query convention: `RepairsFailed{failure.layer != "outer"}` returns one count per unique attempt; the unfiltered total includes the outer safety net for the rare paths inner missed. Both queries are useful, and they answer different questions.

**Why asymmetric and not balanced.** An alternative is to tag both sites: `failure.layer="inner"` on the inner catch, `failure.layer="outer"` on the outer. That works but inflates the contract: now every inner-catch site has to remember to set the tag, and forgetting it drops a measurement out of either query. The asymmetric shape leaves inner sites alone and tags only the outer; the deduplication cost lives at exactly one site, the one that knows it might double-count.

**Cancellation paths use a different tag.** Player-cancel and shutdown also increment `station.repairs.failed` because they end an Activity that didn't complete. They tag with `cancellation.reason` (`player_cancel` / `shutdown`), not `failure.layer`. Different tag for a different reason class: a cancelled repair isn't a "failure layer", it's a structured exit. The asymmetric pattern handles double-count dedup; the cancellation tag handles "what kind of non-success was this". Both tags live on the same `station.repairs.failed` counter; they answer orthogonal questions ("did this double-count from the safety net?" vs "what kind of non-success was this?") and a query can filter on either or both.

*Teaches: when two emission sites legitimately count the same event class, an asymmetric marker tag at exactly one site preserves both query shapes (raw total, deduplicated total) without forcing every site to participate in the contract. Reach for asymmetric tagging when the deduplication cost is concentrated at one site (the safety net), not distributed across many.*
