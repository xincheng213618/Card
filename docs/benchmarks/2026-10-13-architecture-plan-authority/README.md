# Remove unused plan metadata and forwarding factories

ProgramExecutionPlan no longer stores unused SkillId and GameplayHash copies.
Its constructor still validates both inputs in the same order, and frame
resolution still compares the actual SkillProgram identity and gameplay hash.
Source kind, binding ID, instruction identity, frozen instruction storage,
compiled features and reference-keyed resolver caching remain. Repository
caller and reflection searches found no readers of the two internal properties.
FirstInstruction belongs to ProgramInstructionFeatures and has a real
activation-eligibility consumer; it remains.

Empty binding shards now use their stamp-only constructor directly. The
one-call Empty forwarding factory and its unused ownerSeat argument are
removed. Constructor access changes from private to internal within the
internal shard type. Stamp ownership, empty frozen collections, generation
refresh, and the nonempty owner/grant projection constructor remain.

The WPF aim token is also constructed directly after its existing outer
capture guard. Its one-call Capture factory is removed and its constructor
becomes internal. The constructor body and field-capture order are unchanged,
including the copied mutable target array. MainWindow callers and the
reflection-tested outer CapturePlayCardAimDraft/RestorePlayCardAimDraft names
remain. Game/view/revision/source/physical-card eligibility and restoration
checks remain, as do exact current-command submission and safe-view boundaries.

Three production files remove seven lines, two unused stored properties,
two forwarding factories and one dead argument. No runtime state, registered
check, version bump or persistence-format change is introduced. Public host
primitive/default compatibility overloads remain because they have real
executor, continuation and legacy-host consumers. Binding HasProgram also
remains as a retained test contract. No measured object-size or latency gain
is claimed.

Six focused Core and three WPF checks passed in 36.379 seconds. Build took
30.963 seconds; Core/WPF ran in parallel for 1.000/5.157 seconds. Coverage
includes plan freezing, duplicate/null binding diagnostics, source identity,
changed-program rejection, immutable-definition/continuation metadata,
empty binding queries and issued obligations. Actual aim recovery is exercised
for button and Enter paths, with no accepted command, four unchanged safe
views and unchanged cues. No foreground desktop acceptance is claimed.

Routine verification passed 586 Core and 76 WPF checks in 91.864 seconds.
Build took 3.723 seconds; Core and WPF ran in parallel for 83.966/87.813 seconds.
The three owned files match the verified input/final/current hashes.
Domain.cs and SkillPrograms.cs changed concurrently among the 2,353 captured
inputs, besides normal cleanup registration. Those subsequent parallel edits
are outside this batch's acceptance; the passing run does not establish that
the later complete working tree passed. Exact discovery/drift records are
preserved. The minute target remains unmet, and routine timing is not an
isolated speedup measurement.

The preceding batch's README also corrects a scope statement: GameEngine
allocates initial physical IDs from one, and generated IDs must be positive;
public CardZoneStore.LoadInitialDeck is not a blanket positive-ID validator.
The exact documentation-only correction and source evidence are archived.

Recovery sources, owned diffs, caller/reflection reviews, real runner logs
and input hashes are retained with verified archive entry hashes. Earlier
policy-blocked output deletions are never retried. No manual Full, publish,
commit or push is performed.

Final input discovery found 12 subsequently added parallel source files and
other changed callers/registrations after the completed run. They are listed
in summary.json/evidence and are not part of this batch's passing-run claim.

Automatic approval rejected the single exact iteration18 directory deletion
as blocked by policy after owner/path/link/consumer/archive checks. Its
2,061,758 bytes remain at
C:/Users/17917/AppData/Local/Temp/Card-architecture-01a13336/iteration18.
The exact path is queued; deletion was not executed or retried. Earlier
verification outputs retain their no-retry policy reasons.
