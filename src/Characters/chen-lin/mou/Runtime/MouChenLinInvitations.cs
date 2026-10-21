using System.Globalization;
namespace CardGame.Core;
internal sealed partial class MouChenLinRuntime
{
    private const string InvitationAction = "mou-chen-lin-participant";
    public SkillProgramStepOutcome Invite(ProgramSkillFrame supplied)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.SkillId != Yaozuo || f.ActivationId != "invite-authors" || f.TriggerId is not null || f.MouChenLinInvitation is not null || f.MouChenLin is not null || f.InstructionIndex != 1 || !facts.Parent(f) || facts.Phase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || !facts.Recorded(phase)) throw new InvalidOperationException("Yaozuo requires its exact original owner actual-Play invitation.");
        var invitation = new MouChenLinInvitation(Source(f), f.GameplayHash, phase) { Stage = MouChenLinInvitationStage.Racing, Eligible = facts.Living().Where(s => s != f.OwnerSeat).ToArray(), PromptId = prompts.NextId(), Before = facts.Sequence, After = facts.Sequence };
        frames.ReplaceTop(f = f with { MouChenLinInvitation = invitation }); events.AdvanceFact(new MouChenLinInvitationStartedEvent(f.Id, invitation.Source, invitation.GameplayHash, phase, invitation.PromptId)); ContinueInvitation(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    internal bool ValidInvitation(ProgramSkillFrame f) => f.MouChenLinInvitation is { } r && f.SkillId == Yaozuo && f.ActivationId == "invite-authors" && f.TriggerId is null && f.InstructionIndex == 1 && facts.Parent(f) && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && r.Phase.ActorSeat == f.OwnerSeat && r.Phase.TurnNumber == facts.Turn && facts.Recorded(r.Phase) && r.PromptId.Value > 0 && r.Eligible.Distinct().Count() == r.Eligible.Count && !r.Eligible.Contains(f.OwnerSeat) && r.Answered.Distinct().Count() == r.Answered.Count && r.Answered.All(r.Eligible.Contains) && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && facts.History<MouChenLinInvitationStartedEvent>().Count(e => e == new MouChenLinInvitationStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.PromptId)) == 1;
    private PromptChoice InvitationChoice(ProgramSkillFrame f, int actor, string branch, string label, int? card = null, int? target = null) => new(new($"mou-chen-lin-invite.{f.Id}.{actor}.{branch}.{card}.{target}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = InvitationAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["actor"] = actor.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> ParticipantChoices(ProgramSkillFrame f, int actor) => facts.He(actor).Select(c => InvitationChoice(f, actor, "give", "交给" + facts.Name(f.OwnerSeat) + c.Label, c.Id)).Append(InvitationChoice(f, actor, "decline", "不交给牌")).ToArray();
    private IReadOnlyList<PromptChoice> AuthorTargetChoices(ProgramSkillFrame f) => facts.Living().Where(s => s != f.OwnerSeat && s != f.MouChenLinInvitation!.FirstGiver).Select(t => InvitationChoice(f, f.MouChenLinInvitation!.FirstGiver!.Value, "author-target", "令陈琳对" + facts.Name(t) + "执行撰文", target: t)).ToArray();
    [ProgramPipelineHook(ProgramPipelinePhase.ParticipantHostState, 37000)] private ParticipantPromptHostState? CaptureYaozuoHostState()
    {
        // The invitation remains issued while its genuine Give/health/movement
        // child is above it. Such a child pauses input without ending the race.
        var f = frames.OfType<ProgramSkillFrame>().LastOrDefault(p => p.MouChenLinInvitation is not null);
        if (f is not { MouChenLinInvitation: { Closed: false, Stage: MouChenLinInvitationStage.Racing or MouChenLinInvitationStage.MovementChildren } r } || !ValidInvitation(f) || !facts.Alive(f.OwnerSeat)) return null;
        var pending = Array.AsReadOnly(r.Eligible.Where(s => facts.Alive(s) && !facts.Human(s) && !r.Answered.Contains(s)).ToArray());
        var accepting = frames.Top?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse && r.Stage == MouChenLinInvitationStage.Racing && prompts.Current is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } issued && issued.ParticipantFrameId == f.Id && issued.PromptId == r.PromptId && issued.SourceSeat == f.OwnerSeat && issued.SkillPrompt?.SkillId == Yaozuo && issued.ValidCardIds.Count == 0 && issued.Choices.Count == 0;
        return new(new(f.Id, r.PromptId, r.Source, r.GameplayHash), f.OwnerSeat, pending, accepting);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ParticipantPrompt, 37000)] private PendingDecision? ProjectYaozuoParticipant(PendingDecision issued, int actor)
    {
        if (frames.Top is not ProgramSkillFrame { Step: ResolutionFrameStep.AwaitingResponse, MouChenLinInvitation: { Stage: MouChenLinInvitationStage.Racing, Closed: false } r } f || !ValidInvitation(f) || issued.ParticipantFrameId != f.Id || issued.Kind != DecisionKind.ProgramTrigger || issued.PromptId != r.PromptId || issued.SkillPrompt?.SkillId != Yaozuo || issued.SourceSeat != f.OwnerSeat || issued.ValidCardIds.Count != 0 || issued.Choices.Count != 0 || !issued.IsPrivate || !r.Eligible.Contains(actor) || r.Answered.Contains(actor) || !facts.Alive(actor)) return null;
        var choices = ParticipantChoices(f, actor);
        return issued with { PlayerSeat = actor, Choices = Array.AsReadOnly(choices.ToArray()), ValidCardIds = Array.AsReadOnly(choices.SelectMany(c => c.Cards).ToArray()), IsPrivate = true };
    }
    private void PublishInvitation(ProgramSkillFrame f)
    {
        var r = f.MouChenLinInvitation!;
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        if (r.Stage == MouChenLinInvitationStage.Racing)
        {
            // Canonical identity has no private material. Its ordinary NPC
            // seat does not select a winner or run a sequential AI responder.
            prompts.Set(new(DecisionKind.ProgramTrigger, r.Eligible[0], "邀作：所有受邀角色可各自选择交给一张牌，或放弃。", [], [], f.OwnerSeat) { PromptId = r.PromptId, ParticipantFrameId = f.Id, IsPrivate = true, SkillPrompt = new(Yaozuo, facts.SkillName(Yaozuo), facts.SkillName(Yaozuo), facts.SkillDescription(Yaozuo)) }, false);
        }
        else
        {
            var choices = AuthorTargetChoices(f); var chooser = r.FirstGiver!.Value;
            prompts.Set(new(DecisionKind.ProgramTrigger, chooser, "邀作：你是本次第一位真正完成给牌的角色，请选择另一名其他角色。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), IsPrivate = false, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(Yaozuo, facts.SkillName(Yaozuo), facts.SkillName(Yaozuo), facts.SkillDescription(Yaozuo)) }, facts.Human(chooser));
        }
        native.Publish();
    }
    [ProgramAnswer(InvitationAction)] private void AnswerInvitation(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouChenLinInvitation: { } r } f || !ValidInvitation(f) || !int.TryParse(supplied.Parameters.GetValueOrDefault("actor"), NumberStyles.None, CultureInfo.InvariantCulture, out var actor) || supplied.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture)) throw new InvalidOperationException("Yaozuo needs its exact issued participant and owner frame.");
        var view = r.Stage == MouChenLinInvitationStage.Racing && prompts.Current is { } issued ? ProjectYaozuoParticipant(issued, actor) : prompts.Current?.PlayerSeat == actor && r.Stage == MouChenLinInvitationStage.ChooseTarget ? prompts.Current : null;
        if (view?.Choices.SingleOrDefault(c => c.Id == supplied.Id) is not { } canonical || !canonical.Cards.SequenceEqual(supplied.Cards) || !canonical.Targets.SequenceEqual(supplied.Targets) || canonical.Parameters.Count != supplied.Parameters.Count || canonical.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Yaozuo cannot accept another participant's private material or stale published choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = supplied.Parameters["branch"];
        if (branch == "author-target") { frames.ReplaceTop(f = f with { MouChenLinInvitation = r with { Stage = MouChenLinInvitationStage.Written } }); BeginWritten(f, supplied.Targets.Single()); return; }
        if (branch == "decline") { frames.ReplaceTop(f = f with { MouChenLinInvitation = r with { Answered = r.Answered.Append(actor).ToArray() } }); events.AdvanceFact(new MouChenLinInvitationAnswerEvent(f.Id, actor, false)); ContinueInvitation(f.Id); return; }
        var card = facts.He(actor).Single(c => c.Id == supplied.Cards.Single());
        frames.ReplaceTop(f = f with { MouChenLinInvitation = r with { Stage = MouChenLinInvitationStage.MovementChildren, PendingGiver = actor, PendingCard = card, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Give(f); ContinueInvitation(f.Id);
    }
    internal void ContinueInvitation(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouChenLinInvitation: { } r, MouChenLin: null } f && f.Id == id)
        {
            if (!ValidInvitation(f)) throw new InvalidOperationException("Yaozuo lost the original invitation, actual phase or participant payment receipt.");
            if (r.Stage == MouChenLinInvitationStage.MovementChildren)
            {
                if (native.Drain(f)) return;
                if (!r.Answered.Contains(r.PendingGiver)) events.AdvanceFact(new MouChenLinInvitationAnswerEvent(f.Id, r.PendingGiver, false));
                frames.ReplaceTop(f with { MouChenLinInvitation = r with { Stage = MouChenLinInvitationStage.Racing, Answered = r.Answered.Append(r.PendingGiver).Distinct().ToArray(), PendingGiver = -1, PendingCard = null }, PendingMovementContinuation = null }); continue;
            }
            if (!facts.Alive(f.OwnerSeat) || facts.Finished) { Complete(f); return; }
            var unavailable = r.Eligible.Where(s => !facts.Alive(s) && !r.Answered.Contains(s)).ToArray();
            if (unavailable.Length > 0) { foreach (var s in unavailable) events.AdvanceFact(new MouChenLinInvitationAnswerEvent(f.Id, s, false)); frames.ReplaceTop(f with { MouChenLinInvitation = r with { Answered = r.Answered.Concat(unavailable).ToArray() } }); continue; }
            if (r.Stage == MouChenLinInvitationStage.Racing && r.Answered.Count == r.Eligible.Count)
            {
                frames.ReplaceTop(f = f with { MouChenLinInvitation = r with { Closed = true, Stage = MouChenLinInvitationStage.ChooseTarget } }); events.AdvanceFact(new MouChenLinInvitationClosedEvent(f.Id, r.FirstGiver)); native.CloseInvitation(f);
                if (r.FirstGiver is null || !facts.Alive(r.FirstGiver.Value) || AuthorTargetChoices(f).Count == 0) { Complete(f); return; } continue;
            }
            PublishInvitation(f); return;
        }
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 37001)] private void InvitationInvariant(ProgramSkillFrame f) { if (f.MouChenLinInvitation is not null && !ValidInvitation(f)) throw new InvalidOperationException("Invalid Yaozuo issued participant frame."); }
}
