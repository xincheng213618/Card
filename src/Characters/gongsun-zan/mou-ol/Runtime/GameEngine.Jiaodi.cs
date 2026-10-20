using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouGszObtainReason = "skill-program.mou-ol:jiaodi.obtain", MouGszDiscardReason = "skill-program.mou-ol:jiaodi.discard";
    [ProgramCapability] private sealed class MouGongsunZanCapability(GameEngine g) : IMouGongsunZanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveMouGongsunZanJiaodi ? g.BeginMouGszJiaodi(f) : g.BeginMouGszBaojing(f); }
    private CardConversionSource MouGszSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32700, SkillProgramEffectOp.ResolveMouGongsunZanJiaodi)]
    private bool CanOfferMouGszJiaodi(ProgramTriggerLegalityContext gate)
    {
        if (gate.WindowContext?.CardUse is not { } c || LifecycleCardUse(c.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } a } use) return false;
        return a.ActorSeat == gate.Candidate.OwnerSeat && a.ActionId == c.CardActionId && IsSlashCard(a.EffectiveKind) && a.EffectiveDesignatedTargetSeats is [var target] && target != a.ActorSeat && _players[target].IsAlive &&
            !ProgramEventHistory<MouGongsunZanStartedEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == a.ActorSeat);
    }
    private CardUseFrame? MouGszUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } c } context || f.TriggerId != "unique-slash" || f.ActivationId != f.TriggerId ||
            f.SkillId != MouGongsunZanComposition.Jiaodi || LifecycleCardUse(c.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } a } use || a.ActionId != c.CardActionId || a.ActorSeat != f.OwnerSeat || !IsSlashCard(a.EffectiveKind) ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w || w.ParentFrameId != use.Id || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex]))) return null;
        return use;
    }
    private SkillProgramStepOutcome BeginMouGszJiaodi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = MouGszUse(f) ?? throw new InvalidOperationException("Jiaodi lost its native Slash designation parent.");
        if (f.InstructionIndex != 1 || f.MouGongsunZan is not null || use.Action!.EffectiveDesignatedTargetSeats is not [var target] || target == f.OwnerSeat ||
            ProgramEventHistory<MouGongsunZanStartedEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == f.OwnerSeat)) throw new InvalidOperationException("Jiaodi requires its one original other target exactly once.");
        var ownRange = GetAttackRange(f.OwnerSeat); var targetRange = GetAttackRange(target);
        var r = new MouGongsunZanReceipt(MouGszSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, use.Id, use.Action.ActionId, target, ownRange, targetRange)
        { Stage = targetRange <= ownRange ? MouGongsunZanStage.Obtain : MouGongsunZanStage.Discard, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouGongsunZan = r });
        AdvanceEventRulesAndQueueFact(new MouGongsunZanStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.UseId, r.ActionId, target, ownRange, targetRange));
        if (targetRange <= ownRange)
        {
            var before = use.CardAttack?.DamageAmount ?? use.ProgramAdjustedSlashBaseDamage ?? 1;
            UpdateLifecycleCardUse(use.Id, u => u with
            {
                CardAttack = u.CardAttack is { } attack ? attack with { DamageAmount = checked(attack.DamageAmount + 1) } : null,
                ProgramAdjustedSlashBaseDamage = checked((u.ProgramAdjustedSlashBaseDamage ?? before) + 1),
                PreparedTargetAttacks = u.PreparedTargetAttacks is null ? null : Array.AsReadOnly(u.PreparedTargetAttacks.Select(a => a with { DamageAmount = checked(a.DamageAmount + 1) }).ToArray())
            });
            AdvanceEventRulesAndQueueFact(new MouGongsunZanDamageGrantedEvent(f.Id, use.Id, r.ActionId, before, checked(before + 1)));
        }
        ContinueMouGsz(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MouGszChoice(ProgramSkillFrame f, string branch, string text, int? target = null) =>
        new(new($"mou-gongsun-zan.{f.Id}.{branch}.{target ?? -1}"), text, [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "mou-gongsun-zan", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouGszChoices(ProgramSkillFrame f)
    {
        var r = f.MouGongsunZan!;
        if (r.Stage == MouGongsunZanStage.RangeChoice) return [MouGszChoice(f, "increase", "攻击范围+1"), MouGszChoice(f, "decrease", "攻击范围-1（最少为1）")];
        if (r.Stage is MouGongsunZanStage.Obtain or MouGongsunZanStage.Discard)
        {
            if (!_players[r.OriginalTarget].IsAlive) return [];
            var obtain = r.Stage == MouGongsunZanStage.Obtain;
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.OriginalTarget,
                obtain ? [CardZoneKind.Hand] : [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment],
                obtain ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Discard,
                destination: obtain ? SkillProgramCardDestination.OwnerHand : SkillProgramCardDestination.DiscardPile)
                .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-gongsun-zan", ["branch"] = obtain ? "obtain" : "discard" } }).ToArray());
        }
        if (r.Stage == MouGongsunZanStage.ExtraTarget && MouGszUse(f) is { } use &&
            !HasTurnCardTargetRestriction(f.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly))
            return Array.AsReadOnly(_players.Where(p => !use.TargetSeats.Contains(p.Seat) && CanBeProgramCardUseRoleTarget(use, p))
                .Select(p => MouGszChoice(f, "extra-target", $"额外指定{p.Name}", p.Seat)).ToArray());
        return [];
    }
    private void PublishMouGsz(ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices)
    {
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.MouGongsunZan!.OriginalTarget, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-gongsun-zan")]
    private void AnswerMouGsz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGongsunZan: { } r } f || !ValidMouGsz(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !MouGszChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Gongsun Zan requires the canonical current target/card/range choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == MouGongsunZanStage.RangeChoice)
        {
            var amount = branch == "increase" ? 1 : -1;
            AdvanceEventRulesAndQueueFact(new MouGongsunZanBaojingGrantedEvent(f.Id, r.Source, r.GameplayHash, r.OriginalTarget, amount, r.Phase!));
            FinishMouGsz(f); return;
        }
        if (r.Stage == MouGongsunZanStage.ExtraTarget)
        {
            var use = MouGszUse(f)!; var seat = choice.Targets.Single();
            var targets = Array.AsReadOnly(use.TargetSeats.Append(seat).ToArray());
            var action = CloneDesignatedExtraTargetAction(use.Action!, targets);
            UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
            AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id, f.SkillId, f.OwnerSeat, use.Id, seat));
            AdvanceEventRulesAndQueueFact(new MouGongsunZanExtraTargetEvent(f.Id, use.Id, r.ActionId, seat)); FinishMouGsz(f); return;
        }
        var from = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), r.OriginalTarget);
        var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        var obtain = r.Stage == MouGongsunZanStage.Obtain; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouGongsunZan = r with { Stage = obtain ? MouGongsunZanStage.ObtainChildren : MouGongsunZanStage.DiscardChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (obtain) MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(MouGszObtainReason), disclosure: CardMovementDisclosure.FaceDown);
        else MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new(MouGszDiscardReason), parentFrameId: f.Id);
        SealMouGsz(f.Id, obtain ? "obtain" : "discard"); ContinueMouGsz(f.Id);
    }
    private void ContinueMouGsz(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGongsunZan: { } r } f || f.Id != id) return;
        if (r.Stage is MouGongsunZanStage.ObtainChildren or MouGongsunZanStage.DiscardChildren)
        {
            if (DrainMouGsz(f)) return; f = GetActiveProgramFrame(id); r = f.MouGongsunZan!;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouGongsunZan = r with { Stage = r.Stage == MouGongsunZanStage.ObtainChildren ? MouGongsunZanStage.Discard : MouGongsunZanStage.ExtraTarget } }); r = f.MouGongsunZan!;
        }
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishMouGsz(f); return; }
        if (r.Stage == MouGongsunZanStage.Discard && r.TargetRange < r.OwnerRange) { FinishMouGsz(f); return; }
        var choices = MouGszChoices(f);
        if (choices.Count > 0) { PublishMouGsz(f, choices); return; }
        if (r.Stage == MouGongsunZanStage.Obtain)
        { ReplaceRuntimeTop(f with { MouGongsunZan = r with { Stage = MouGongsunZanStage.Discard } }); ContinueMouGsz(id); return; }
        if (r.Stage == MouGongsunZanStage.Discard)
        { ReplaceRuntimeTop(f with { MouGongsunZan = r with { Stage = MouGongsunZanStage.ExtraTarget } }); ContinueMouGsz(id); return; }
        FinishMouGsz(f);
    }
    private void FinishMouGsz(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new MouGongsunZanCompletedEvent(f.Id, f.MouGongsunZan!.UseId)); ReplaceRuntimeTop(f = f with { MouGongsunZan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouGongsunZanJiaodi, SkillProgramEffectOp.ResolveMouGongsunZanBaojing)]
    private PromptChoice ChooseMouGszAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouGongsunZan!; var owner = _players[f.OwnerSeat];
        if (r.Stage == MouGongsunZanStage.RangeChoice) return p.Choices.Single(c => c.Parameters["branch"] == (AreProgramDistributionAllies(owner, _players[r.OriginalTarget]) ? "increase" : "decrease"));
        if (r.Stage == MouGongsunZanStage.ExtraTarget)
        { var view = CreateSnapshot(f.OwnerSeat); return p.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets.Single(), new(0, 0, 0, 0, 0, 1, false, false))).First(); }
        return p.Choices.OrderByDescending(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[r.OriginalTarget]) : 0).First();
    }
}
