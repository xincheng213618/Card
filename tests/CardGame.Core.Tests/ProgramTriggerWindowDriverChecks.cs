using System.Text.Json;
using CardGame.Core;

internal static class ProgramTriggerWindowDriverChecks
{
    public static void TypedWindowDriverPreservesCandidateAndClosingBoundaries()
    {
        var program = LoadProgram();
        var optional = new Host(program, "skip", "optional");
        optional.Eligible = candidate => candidate.BindingId != "skip";
        ProgramTriggerWindowDriver.Run(optional);
        Require(optional.Calls.SequenceEqual([
                "prepare", "read:skip", "eligible:skip", "skip:skip", "prepare", "read:optional",
                "eligible:optional", "resolve:optional", "await", "expose:optional"]) &&
                optional.Frame!.Index == 1 && optional.Frame.Step == ResolutionFrameStep.AwaitingResponse,
            "An ineligible candidate must advance before another read; only an optional prompt changes the parent step.");
        Require(ReferenceEquals(optional.LastContext, optional.Contexts[1]),
            "The driver must retain the typed window's original frozen context.");

        var card = new Host(program, "skip") { PreResolve = true, ReplaceOnClose = true };
        card.Eligible = _ => false;
        ProgramTriggerWindowDriver.Run(card);
        Require(card.Calls.SequenceEqual(["prepare", "read:skip", "eligible:skip", "skip:skip", "prepare", "close"]) &&
                card.Frame is { Id: 99, Index: 0 },
            "A pre-resolved card trigger must not resolve again, and closing must not consume a synchronously installed window.");

        var preparation = new Host(program, "mandatory");
        preparation.Preparations.Enqueue(ProgramTriggerWindowFlow.Retry);
        preparation.Preparations.Enqueue(ProgramTriggerWindowFlow.Yield);
        ProgramTriggerWindowDriver.Run(preparation);
        Require(preparation.Calls.SequenceEqual(["prepare", "prepare"]),
            "Preparation retry must reacquire the top frame; a paid-child yield must stop before candidate reads.");
        preparation.Calls.Clear();
        ProgramTriggerWindowDriver.Run(preparation);
        Require(preparation.Calls.SequenceEqual(["prepare", "read:mandatory", "eligible:mandatory", "resolve:mandatory", "begin:mandatory"]) &&
                preparation.Frame!.Step == ResolutionFrameStep.ResolvingEffect,
            "A mandatory binding must begin without an artificial awaiting or resolving step transition.");

        var group = new Host(program, "alpha", "beta", "mandatory") { SupportsGroups = true };
        ProgramTriggerWindowDriver.Run(group);
        Require(group.Calls.TakeLast(2).SequenceEqual(["await", "group"]) && group.Frame!.Index == 0,
            "A grouped prompt must retain the original cursor until its answer is validated.");
        group.Calls.Clear();
        var selected = Select(group.Candidates[1], "mode");
        ProgramTriggerWindowDriver.ResolveChoiceGroup(group, group.Frame!, group.Candidates[0], group.Contexts[0],
            "mode", selected, "activate", () => group.Calls.Add("clear"));
        Require(group.Calls.SequenceEqual(["clear", "group-skip:alpha", "eligible:beta", "begin:beta"]) &&
                group.Frame!.Index == 0 && group.LastContext is { OccurrenceIndex: 1, ResumeCandidateIndex: 2 },
            "Selecting a group branch must record other branches once and bind the selected occurrence with the full group return cursor.");

        var skipped = new Host(program, "alpha", "beta", "mandatory") { SupportsGroups = true };
        ProgramTriggerWindowDriver.ResolveChoiceGroup(skipped, skipped.Frame!, skipped.Candidates[0], skipped.Contexts[0],
            "mode", Select(skipped.Candidates[0], "mode"), "skip", () => skipped.Calls.Add("clear"));
        Require(skipped.Calls.SequenceEqual(["clear", "resolve:alpha", "group-skip:alpha", "group-skip:beta", "move:2", "continue"]) &&
                skipped.Frame!.Index == 2,
            "Skipping a group must report every member and continue from the first candidate outside that group.");
        var changed = new Host(program, "alpha", "beta", "mandatory") { SupportsGroups = true };
        changed.Eligible = candidate => candidate.BindingId != "beta";
        ProgramTriggerWindowDriver.ResolveChoiceGroup(changed, changed.Frame!, changed.Candidates[0], changed.Contexts[0],
            "mode", Select(changed.Candidates[1], "mode"), "activate", () => changed.Calls.Add("clear"));
        Require(changed.Calls.SequenceEqual(["clear", "group-skip:alpha", "eligible:beta", "group-skip:beta", "move:2", "continue"]),
            "A branch that becomes ineligible while its prompt is open must skip once and advance over the entire group.");

        var malformed = new Host(program, "alpha", "beta") { SupportsGroups = true };
        try
        {
            ProgramTriggerWindowDriver.ResolveChoiceGroup(malformed, malformed.Frame!, malformed.Candidates[0], malformed.Contexts[0],
                "mode", Select(malformed.Candidates[1], "wrong-group"), "activate", () => malformed.Calls.Add("clear"));
            throw new Exception("A malformed choice group was accepted.");
        }
        catch (InvalidOperationException exception)
        {
            Require(exception.Message == "The program choice group identity changed." && malformed.Calls.Count == 0,
                "Malformed group identity must fail before clearing a prompt or advancing its cursor.");
        }
        var failure = new InvalidOperationException("original eligibility failure");
        var throwing = new Host(program, "mandatory") { Eligible = _ => throw failure };
        try { ProgramTriggerWindowDriver.Run(throwing); throw new Exception("The driver swallowed an eligibility failure."); }
        catch (InvalidOperationException exception)
        { Require(ReferenceEquals(exception, failure), "A rule failure must propagate without reflection wrappers or replacement."); }
    }

    private const string SkillId = "fixture:window-driver";
    private static SkillProgram LoadProgram() => SkillProgramCatalog.Load(JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
        skills = new[]
        {
            new { id = SkillId, revision = 1, triggers = new[] { "skip", "optional", "mandatory", "alpha", "beta" }.Select(id => new
            {
                id, window = "drawPhaseEnded", subject = "owner", optional = id != "mandatory",
                choiceGroup = id is "alpha" or "beta" ? "mode" : null,
                effects = new[] { new { op = "draw", target = "owner", amount = 1 } }
            }).ToArray() }
        }
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new Dictionary<string, object>
        {
            [SkillId] = new { name = "Window driver", description = "Typed candidate boundaries",
                triggerChoices = new { alpha = "Alpha", beta = "Beta" } }
        }
    })).Programs[SkillId];

    private static PromptChoice Select(ProgramTriggerCandidate candidate, string group) => new(
        new ChoiceId("fixture:choice"), "Choose", [], [], new Dictionary<string, string>
        {
            ["choice-group"] = group, ["skill-id"] = candidate.SkillId,
            ["binding-id"] = candidate.BindingId, ["skill-instance-id"] = candidate.SkillInstanceId
        });

    private sealed record DriverFrame(long Id, int Index = 0, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
        : ResolutionFrame(Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow, Step);

    private sealed class Host : IProgramTriggerWindowHost<DriverFrame>
    {
        private readonly SkillProgram _program;
        internal Host(SkillProgram program, params string[] bindings)
        {
            _program = program;
            Candidates = Array.AsReadOnly(bindings.Select((binding, index) =>
                new ProgramTriggerCandidate(0, SkillId, binding, "fixture:instance", program.GameplayHash, 0, index)).ToArray());
            Contexts = Array.AsReadOnly(Candidates.Select(candidate => new ProgramSkillWindowContext(
                SkillProgramTriggerWindow.DrawPhaseEnded, 41, 0, OccurrenceIndex: candidate.OccurrenceIndex)).ToArray());
        }
        internal DriverFrame? Frame { get; private set; } = new(41);
        internal IReadOnlyList<ProgramTriggerCandidate> Candidates { get; }
        internal IReadOnlyList<ProgramSkillWindowContext> Contexts { get; }
        internal List<string> Calls { get; } = [];
        internal Queue<ProgramTriggerWindowFlow> Preparations { get; } = new();
        internal Func<ProgramTriggerCandidate, bool> Eligible { get; set; } = _ => true;
        internal bool PreResolve { get; init; }
        internal bool ReplaceOnClose { get; init; }
        internal bool SupportsGroups { get; init; }
        internal ProgramSkillWindowContext? LastContext { get; private set; }
        public bool TryGetFrame(out DriverFrame frame) { frame = Frame!; return Frame is not null; }
        public ProgramTriggerWindowPreparation<DriverFrame> Prepare(DriverFrame frame)
        { Calls.Add("prepare"); return new(Preparations.TryDequeue(out var flow) ? flow : ProgramTriggerWindowFlow.Ready, frame); }
        public bool IsComplete(DriverFrame frame) => frame.Index >= Candidates.Count;
        public ProgramTriggerWindowFlow Close(DriverFrame frame)
        { Calls.Add("close"); Frame = ReplaceOnClose ? new(99) : null; return ProgramTriggerWindowFlow.Yield; }
        public ProgramTriggerWindowEntry Read(DriverFrame frame)
        {
            var candidate = Candidates[frame.Index];
            Calls.Add("read:" + candidate.BindingId);
            return new(candidate, Contexts[frame.Index], PreResolve ? Trigger(candidate) : null);
        }
        public bool CanRun(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
        { Calls.Add("eligible:" + candidate.BindingId); LastContext = context; return Eligible(candidate); }
        private SkillProgramTrigger Trigger(ProgramTriggerCandidate candidate) => _program.Triggers.Single(trigger => trigger.Id == candidate.BindingId);
        public SkillProgramTrigger ResolveTrigger(ProgramTriggerCandidate candidate)
        { Calls.Add("resolve:" + candidate.BindingId); return Trigger(candidate); }
        public void Skip(DriverFrame frame, ProgramTriggerCandidate candidate)
        { Calls.Add("skip:" + candidate.BindingId); Frame = frame with { Index = frame.Index + 1 }; }
        public IReadOnlyList<ProgramTriggerCandidate>? ChoiceGroup(DriverFrame frame, ProgramTriggerCandidate first, string group) =>
            SupportsGroups ? Array.AsReadOnly(Candidates.Skip(frame.Index).TakeWhile(candidate => Trigger(candidate).ChoiceGroup == group).ToArray()) : null;
        public void BeforePrompt(DriverFrame frame) { Calls.Add("await"); Frame = frame with { Step = ResolutionFrameStep.AwaitingResponse }; }
        public void Expose(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) { Calls.Add("expose:" + candidate.BindingId); LastContext = context; }
        public void ExposeGroup(IReadOnlyList<ProgramTriggerCandidate> members, ProgramTriggerCandidate first,
            ProgramSkillWindowContext context, string group) { Calls.Add("group"); LastContext = context; }
        public void Begin(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) { Calls.Add("begin:" + candidate.BindingId); LastContext = context; }
        public int CandidateIndex(DriverFrame frame) => frame.Index;
        public void RecordGroupSkip(DriverFrame frame, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) => Calls.Add("group-skip:" + candidate.BindingId);
        public void MovePastGroup(DriverFrame frame, int nextIndex) { Calls.Add("move:" + nextIndex); Frame = frame with { Index = nextIndex }; }
        public void Continue() => Calls.Add("continue");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
