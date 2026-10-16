using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string CaoShuangPlacementReason = "skill-program.caoshuang.shanzhuan.place";
    private bool CaoShuangPlaceCardEligible(int chooser, int target, Card card, CardZoneKind zone) =>
        !card.IsGeneralWeapon && (IsDelayedCard(card.Kind) || EffectiveSuit(_players[target], card) is Suit.Heart or Suit.Diamond or Suit.Club or Suit.Spade) &&
        !IsForeignEquipmentDiscardPrevented(chooser, card, new(zone, target), OwnedCardMoveIntent.Transfer);
    private bool CaoShuangCanPlace(int owner, int target) => IsValidPlayerSeat(target) && owner != target &&
        _players[target] is { IsAlive: true, JudgmentAreaAbolished: false } && GetJudgment(_players[target]).Count == 0 &&
        (GetHand(_players[target]).Any(c => CaoShuangPlaceCardEligible(owner, target, c, CardZoneKind.Hand)) ||
         GetEquipment(_players[target]).Any(c => CaoShuangPlaceCardEligible(owner, target, c, CardZoneKind.Equipment)));

    private SkillProgramStepOutcome BeginCaoShuangShanzhuan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.CaoShuangShanzhuan is not null || f.InstructionIndex != 1 || f.TriggerId is null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } target, Amount: > 0 } context ||
            context.SourceSeat != f.OwnerSeat || target == f.OwnerSeat ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } damage ||
            !DamageProgramMatchesWindow(f, damage))
            throw new InvalidOperationException("Shanzhuan requires the exact real other-target after-damage source window.");
        if (!CaoShuangCanPlace(f.OwnerSeat, target)) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { CaoShuangShanzhuan = new(CaoShuangSource(f), f.GameplayHash, f.InstructionIndex,
            context.ParentFrameId, target), Step = ResolutionFrameStep.AwaitingResponse });
        PublishCaoShuangShanzhuan(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> CaoShuangShanzhuanChoices(ProgramSkillFrame f)
    {
        var r = f.CaoShuangShanzhuan!;
        if (r.Placed || !CaoShuangCanPlace(f.OwnerSeat, r.TargetSeat)) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.TargetSeat,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer,
            canSelect: (zone, card) => CaoShuangPlaceCardEligible(f.OwnerSeat, r.TargetSeat, card, zone))
            .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "caoshuang-shanzhuan" } }).ToArray());
    }
    private void PublishCaoShuangShanzhuan(ProgramSkillFrame f)
    {
        var choices = CaoShuangShanzhuanChoices(f);
        if (choices.Count == 0) { CompleteCaoShuangProgram(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "擅专：将受伤角色的一张手牌或装备牌置于其判定区", choices.SelectMany(c => c.Cards).ToArray(), [], f.CaoShuangShanzhuan!.TargetSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("caoshuang-shanzhuan")]
    private void AnswerCaoShuangShanzhuan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CaoShuangShanzhuan: { Placed: false } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], CaoShuangShanzhuanChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Shanzhuan lost the actual empty-judgment target or anonymous owned-card slot.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CompleteCaoShuangProgram(f, false); return; }
        var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);
        var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var card = (zone == CardZoneKind.Hand ? GetHand(_players[r.TargetSeat]) : GetEquipment(_players[r.TargetSeat]))[slot];
        var from = new CardLocation(zone, r.TargetSeat); var suit = EffectiveSuit(_players[r.TargetSeat], card);
        var effective = IsDelayedCard(card.Kind) ? card.Kind : IsRedSuit(suit) ? CardKind.Indulgence : CardKind.SupplyShortage;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { CaoShuangShanzhuan = r with { Placed = true, CardId = card.Id, From = from,
            EffectiveKind = effective, SequenceBefore = before, SequenceAfter = before }, Step = ResolutionFrameStep.ResolvingEffect });
        MoveCard(card, from, CardLocation.Judgment(r.TargetSeat), new(CaoShuangPlacementReason));
        if (card.Kind != effective) _judgmentEffectiveCardKinds[card.Id] = effective;
        f = GetActiveProgramFrame(f.Id); r = f.CaoShuangShanzhuan! with { SequenceAfter = _movementSequence };
        ReplaceRuntimeTop(f with { CaoShuangShanzhuan = r });
        AdvanceEventRulesAndQueueFact(new CaoShuangShanzhuanPlacedEvent(f.Id, r.Source, r.TargetSeat,
            card.Id, from, card.Kind, effective, suit, before, r.SequenceAfter));
        AdvanceEventRulesAndQueueFact(new DelayedCardPlacedEvent(f.Id, card.Id, effective, f.OwnerSeat, r.TargetSeat));
        ContinueCaoShuangProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick)]
    private PromptChoice ChooseAiCaoShuangPlacement(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.OrderByDescending(c => c.Cards.Count > 0 ? CardCatalog.Get(EntityAtCurrentLocation(c.Cards.Single()).Kind).HandKeepValue : 35)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();

    private bool DrainCaoShuangChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueCaoShuangProgram(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.CaoShuangTuogu is null && f.CaoShuangShanzhuan is null) return;
        AssertCaoShuangProgram(f);
        if (DrainCaoShuangChildren(id)) return;
        if (f.CaoShuangTuogu is { Granted: false }) { PublishCaoShuangTuogu(f); return; }
        if (f.CaoShuangShanzhuan is { Placed: false }) { PublishCaoShuangShanzhuan(f); return; }
        CompleteCaoShuangProgram(GetActiveProgramFrame(id), true);
    }
    private void CompleteCaoShuangProgram(ProgramSkillFrame f, bool completed)
    { ReplaceRuntimeTop(f = f with { CaoShuangTuogu = null, CaoShuangShanzhuan = null }); FinishProgramSkill(f, completed); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26060)]
    private bool ResumeCaoShuang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.CaoShuangTuogu is null && f.CaoShuangShanzhuan is null) return false;
        ContinueCaoShuangProgram(id); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26060)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26060)]
    private bool CanContinuePaidCaoShuang(ProgramSkillFrame f) =>
        f.CaoShuangTuogu is { Granted: true } && ProgramEventHistory<CaoShuangTuoguInheritedEvent>().Any(e => e.FrameId == f.Id && e.Source == f.CaoShuangTuogu.Source) ||
        f.CaoShuangShanzhuan is { Placed: true } && ProgramEventHistory<CaoShuangShanzhuanPlacedEvent>().Any(e => e.FrameId == f.Id && e.Source == f.CaoShuangShanzhuan.Source);
}
