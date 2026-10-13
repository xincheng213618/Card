# Shared compiled queries and immutable gallery metadata

The registry now materializes its frozen programs once for Set-conflict and
expanded-card-domain validation. Both validations still run, in their original
order and over the complete registry. No validation proof is scoped to an
owner's current skills.

Hand-comparison queries now use the activation's existing compiled operation
bucket. The quota query preserves its single-matching-operation check,
including the original duplicate-item exception boundary. Hand counts, HP,
owner identity, turn history and usage limits still come from live state.
The public-pile projection resolves its paused instruction once and reuses
that effect. It still resolves through the issued frame and registry definition,
rather than through a currently enabled owner's shard.

Snapshot dictionary freezing shares the framework's read-only empty dictionary
for present-empty inputs. Null inputs remain null; nonempty dictionaries are
still detached copies. The retained projection check now asserts these shapes,
input-mutation isolation, rejected observer mutation and null-key validation.

The WPF gallery removes the duplicate `GeneralGallerySeriesOption` record and
eleven copied option objects plus their array per view model. Views read the
existing `GallerySeries` list, now explicitly frozen with `Array.AsReadOnly`.
XAML, selection tags and commands still consume the same names and string IDs.
The retained focus check asserts that a view cannot replace shared metadata.
All in-repository consumers compile; no external binary consumer contract was
found in this WinExe project. Two other audit candidates remain unselected.

This batch changes six production files and two retained checks. Production
line count increases by four because a repeated long expression is expanded
into a local variable and guarded block. It removes duplicate data and work,
not persistence or rule state. No new check registration, framework, runner
switch, rules version or release was added by this batch.

Thirteen focused Core checks and four focused WPF checks passed in 30.787
seconds. They cover comparison quotas and replay, public-pile source queries,
prepared snapshots, frozen dictionaries, registry conflicts, execution plans,
real gallery filtering and focus restoration. The measured routine passed 556
Core checks, failed two named-ambush feature checks, and passed all 75 WPF
checks in 105.515 seconds. Its build took 1.629 seconds, Core 98.074 and WPF
103.513; the test scopes ran in parallel and their durations must not be summed.
The approximate one-minute target remains unmet.

The remaining failures were `paid source loss and zero entity eligibility`
and `invalidated use real actor replacement` within concurrently developed
named-ambush work. A fixture in that work changed during the run. All eight
owned production/test inputs are checked against the final build manifest;
the routine is not claimed to be fully green or to validate later fixture edits.
No manual Full or foreground desktop acceptance was performed.

An eight-sample .NET 8.0.31 component probe invokes the actual private empty
dictionary factory and compares it with the exact pre-change expression
compiled in the probe, using the same delegate signature and alternating order.
Each sample makes 10,000 calls. All baseline samples allocate 120 bytes per
call; all warmed candidate samples allocate zero. This is a source-expression
component comparison, not a before/after whole-binary or whole-match benchmark.
Exact samples, source expression and candidate assembly identity are archived.

`evidence.zip`, `evidence-manifest.json` and `summary.json` preserve exact source
changes, recovery copies, reviews, probe instrumentation and verification logs.
The compatible earlier probe output was reused. Its previous `blocked by policy`
deletion remains protected; no Clean, Rebuild or blocked deletion was retried.
After archive verification and ownership, link and consumer checks, automatic
approval rejected deletion of the exact iteration11 diagnostic directory as
`blocked by policy`. Its 14,423,928 bytes remain on disk, with that path queued
and protected against an automatic retry. Exact preconditions and the rejection
are recorded alongside the summary; no reclaimed-space claim is made.
