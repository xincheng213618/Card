using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29101, SkillProgramEffectOp.ResolveMouHuaXiongYaowu)]
    private bool CanOfferMouYaowu(ProgramTriggerLegalityContext c) => c.Candidate.SkillId == MouYaowu && MouHuaXiongComposition.IsYaowu(c.Program) &&
        c.Context.Window == SkillProgramTriggerWindow.BeforeDamageApplied && c.Context.TargetSeat == c.OwnerSeat && c.Context.Amount > 0 &&
        c.Owner.IsAlive && CurrentDamageAttempt is { EffectiveCardKind: { } kind } && IsSlashCard(kind);
    private bool MouYaowuRoot(ProgramSkillFrame f)
    {
        if (f.SkillId != MouYaowu || f.TriggerId != "slash-damage-benefit" || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context || context.TargetSeat != f.OwnerSeat ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouHuaXiongComposition.IsYaowu(p) ||
            _resolutionStack.SingleOrDefault(item => item.Id == context.ParentFrameId) is not BeforeDamageProgramWindowFrame window ||
            window.TargetSeat != f.OwnerSeat || window.Amount <= 0 || window.Prevented || window.RedirectedTargetSeat is not null ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].Candidate)) return false;
        var i = _resolutionStack.FindIndex(item => item.Id == f.Id);
        return i > 0 && _resolutionStack[i - 1].Id == window.Id;
    }
    private SkillProgramStepOutcome ResolveMouYaowu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouYaowuRoot(f) || f.InstructionIndex != 1 || f.MouHuaXiongYaowu is not null || CurrentDamageAttempt is not { EffectiveCardKind: { } kind } attack ||
            !IsSlashCard(kind) || ProgramEventHistory<MouHuaXiongYaowuStartedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Yaowu needs its exact positive native Slash damage-recipient window.");
        var action = LifecycleCardUse(attack.ResolutionId)?.Action;
        var red = action is not null ? action.EffectiveIsRed ?? (action.EffectiveSuit is Suit.Heart or Suit.Diamond) : attack.Card?.Suit is Suit.Heart or Suit.Diamond;
        var source = attack.IsSourceLess ? (int?)null : f.WindowContext!.SourceSeat;
        var r = new MouHuaXiongYaowuReceipt(MouHuaXiongSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId,
            attack.ResolutionId, f.OwnerSeat, source, kind, red);
        ReplaceRuntimeTop(f = f with { MouHuaXiongYaowu = r });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongYaowuStartedEvent(f.Id, r.WindowId, r.AttackId, f.OwnerSeat, source, kind, red));
        if (!red) { CommitMouYaowuChoice(f, "owner-draw", f.OwnerSeat); return SkillProgramStepOutcome.Continue; }
        if (source is not { } seat || !IsValidPlayerSeat(seat) || !_players[seat].IsAlive)
        { CommitMouYaowuChoice(f, "none", null); return SkillProgramStepOutcome.Continue; }
        var choices = MouYaowuChoices(f);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, "耀武：回复1点体力或摸一张牌。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouYaowuChoices(ProgramSkillFrame f)
    {
        var source = f.MouHuaXiongYaowu!.SourceSeat!.Value;
        var options = IsRuleWounded(_players[source]) ? new[] { "recover", "draw" } : ["draw"];
        return Array.AsReadOnly(options.Select(option => new PromptChoice(new($"mou-hua-xiong.yaowu.{f.Id}.{option}"),
            option == "recover" ? "回复1点体力" : "摸一张牌", [], [], new Dictionary<string,string>
            { ["program-action"] = "mou-hua-xiong-yaowu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option-id"] = option })).ToArray());
    }
    private void CommitMouYaowuChoice(ProgramSkillFrame f, string benefit, int? recipient)
    {
        var r = f.MouHuaXiongYaowu!;
        if (r.Benefit is not null || f.ChoiceBindings.Any(b => b.Name == "yaowu-benefit")) throw new InvalidOperationException("Yaowu cannot choose or issue its reward twice.");
        ReplaceRuntimeTop(f with { MouHuaXiongYaowu = r with { Benefit = benefit }, ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings
            .Append(new ProgramChoiceResultBinding("yaowu-benefit", benefit, recipient ?? f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongYaowuBenefitChosenEvent(f.Id, recipient, benefit));
    }
    [ProgramAnswer("mou-hua-xiong-yaowu")]
    private void AnswerMouYaowu(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!MouYaowuRoot(f) || f.InstructionIndex != 1 || f.MouHuaXiongYaowu is not { IsRed: true, Benefit: null, SourceSeat: { } source } ||
            _pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != source ||
            !AssistedChoicesEqual([selected], MouYaowuChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Yaowu only accepts its actual damage source's canonical available reward.");
        ClearPendingDecision();
        if (!_players[source].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CommitMouYaowuChoice(f, "none", null); AdvanceRuntimeProgram(f.Id); return; }
        CommitMouYaowuChoice(f, selected.Parameters.GetValueOrDefault("option-id") == "recover" ? "source-recover" : "source-draw", source);
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuaXiongYaowu)]
    private PromptChoice SelectAiMouYaowu(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c =>
        c.Parameters.GetValueOrDefault("option-id") == "recover" && _players[f.MouHuaXiongYaowu!.SourceSeat!.Value].Hp <= 2) ??
        p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "draw");
    private bool ValidMouYaowuReceipt(ProgramSkillFrame f) => MouYaowuRoot(f) && f.MouHuaXiongYaowu is { } r && r.Source == MouHuaXiongSource(f) &&
        r.GameplayHash == f.GameplayHash && r.OwnerSeat == f.OwnerSeat && r.WindowId == f.WindowContext!.ParentFrameId && IsSlashCard(r.EffectiveKind) &&
        ProgramEventHistory<MouHuaXiongYaowuStartedEvent>().Count(e => e == new MouHuaXiongYaowuStartedEvent(f.Id, r.WindowId, r.AttackId,
            r.OwnerSeat, r.SourceSeat, r.EffectiveKind, r.IsRed)) == 1 && (r.Benefit is null ||
            ProgramEventHistory<MouHuaXiongYaowuBenefitChosenEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var chosen] &&
            chosen.Benefit == r.Benefit && (r.Benefit switch
            { "owner-draw" => !r.IsRed && chosen.RecipientSeat == f.OwnerSeat,
              "source-draw" or "source-recover" => r.IsRed && r.SourceSeat is { } seat && chosen.RecipientSeat == seat,
              "none" => r.IsRed && chosen.RecipientSeat is null, _ => false }) &&
            f.ChoiceBindings.Count(b => b.Name == "yaowu-benefit" && b.OptionId == r.Benefit && b.ChooserSeat == (chosen.RecipientSeat ?? f.OwnerSeat)) == 1);
    private bool IsMouHuaXiongChoiceResult(ProgramSkillFrame f, ProgramChoiceResultBinding binding)
    {
        if (binding.Name != "yaowu-benefit" || f.InstructionIndex < 1 ||
            f.MouHuaXiongYaowu is not { Benefit: not null } r || binding.OptionId != r.Benefit ||
            !ValidMouYaowuReceipt(f)) return false;
        return ProgramEventHistory<MouHuaXiongYaowuBenefitChosenEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var chosen] &&
            chosen.Benefit == binding.OptionId && binding.ChooserSeat == (chosen.RecipientSeat ?? f.OwnerSeat);
    }

}
