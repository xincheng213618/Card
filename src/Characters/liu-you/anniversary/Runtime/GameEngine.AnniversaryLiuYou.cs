namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource LiuYouSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private AnniversaryLiuYouStartedEvent[] LiuYouPhaseStarts(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<AnniversaryLiuYouStartedEvent>()
        .Where(e => e.Source.OwnerSeat == owner && e.Source.SkillId == AnniversaryLiuYouComposition.Skill && e.Phase == phase).ToArray();
    private bool LiuYouPhaseAvailable(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive && _currentSeat == owner && _phase == TurnPhase.Play &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && IsRecordedActualDiscardRecoveryPhase(p) &&
        LiuYouPhaseStarts(owner, p) is var starts && starts.Length < _players[owner].Hp &&
        !starts.Any(s => ProgramEventHistory<AnniversaryLiuYouResultEvent>().Any(e => e.FrameId == s.FrameId && e.Winner == owner));
    private bool LiuYouTarget(int owner, int target) => LiuYouPhaseAvailable(owner) && IsValidPlayerSeat(target) && target != owner && _players[target].IsAlive &&
        HasWuLuKangPindianInput(_players[owner]) && HasWuLuKangPindianInput(_players[target]) && CanBePindianTarget(owner, target) &&
        !LiuYouPhaseStarts(owner, CurrentActualDiscardRecoveryPhase()!).Any(e => e.Target == target);
    [ProgramCapability] private sealed class AnniversaryLiuYouQueries(GameEngine e) : IAnniversaryLiuYouQueries
    { public bool Target(int owner, int target) => e.LiuYouTarget(owner, target); public int Hp(int owner) => e._players[owner].Hp; }
    [ProgramCapability] private sealed class AnniversaryLiuYouHost(GameEngine e) : IAnniversaryLiuYouHost
    {
        public void Execute(ProgramSkillFrame f, SkillProgramEffectOp op)
        { if (op == SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan) e.PrepareLiuYou(f); else e.ResolveLiuYou(f); }
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 50000, SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan)]
    private bool CanStartLiuYou(CharacterState owner) => LiuYouPhaseAvailable(owner.Seat) && _players.Any(p => LiuYouTarget(owner.Seat, p.Seat));
    [ProgramPhaseUsedProjection(50000)] private int LiuYouProjectedUses(int owner, string skill, int fallback) => skill == AnniversaryLiuYouComposition.Skill &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ? LiuYouPhaseStarts(owner, phase).Length : fallback;
    private void PrepareLiuYou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryLiuYouComposition.Skill || f.ActivationId != AnniversaryLiuYouComposition.Activation || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.AnniversaryLiuYou is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var peer] || !LiuYouTarget(f.OwnerSeat, peer))
            throw new InvalidOperationException("Kannan requires the current actual own Play, remaining HP quota, and an unchallenged native-legal opponent.");
        var receipt = new AnniversaryLiuYouReceipt(LiuYouSource(f), f.GameplayHash, CurrentActualDiscardRecoveryPhase()!, peer);
        ReplaceRuntimeTop(f with { AnniversaryLiuYou = receipt });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYouStartedEvent(f.Id, receipt.Source, receipt.Hash, receipt.Phase, peer));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 50000)] private void CaptureLiuYouPindian(PindianResultDeterminedEvent fact)
    {
        if (fact.SkillId != AnniversaryLiuYouComposition.Skill) return;
        if (_resolutionStack.LastOrDefault() is not PindianFrame pair || pair.Id != fact.FrameId || pair.Result is { } already && already != fact.Result || pair.ProgramResultBind != AnniversaryLiuYouComposition.Result ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(candidate => candidate.Id == pair.ParentFrameId) is not { AnniversaryLiuYou: { PindianId: null } r } f ||
            f.InstructionIndex != 2 || !ValidLiuYou(f) || pair.SourceSeat != f.OwnerSeat || pair.OpponentSeat != r.Target)
            throw new InvalidOperationException("Kannan cannot capture another native Pindian or repeated revealed result.");
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryLiuYou = r with { PindianId = pair.Id, Result = fact.Result } });
    }
    private bool ValidLiuYou(ProgramSkillFrame f)
    {
        if (f.AnniversaryLiuYou is not { } r || f.SkillId != AnniversaryLiuYouComposition.Skill || f.ActivationId != AnniversaryLiuYouComposition.Activation ||
            f.TriggerId is not null || f.WindowContext is not null || f.SelectedCardIds.Count != 0 || !f.SelectedTargetSeats.SequenceEqual([r.Target]) || r.Source != LiuYouSource(f) ||
            r.Hash != f.GameplayHash || r.Phase.ActorSeat != f.OwnerSeat || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            ProgramEventHistory<AnniversaryLiuYouStartedEvent>().Count(e => e == new AnniversaryLiuYouStartedEvent(f.Id, r.Source, r.Hash, r.Phase, r.Target)) != 1) return false;
        if (r.PindianId is null) return r.Result is null && !r.Completed && f.InstructionIndex is 1 or 2;
        return r.Result is { } result && result.SourceSeat == f.OwnerSeat && result.OpponentSeat == r.Target && f.InstructionIndex is >= 2 and <= 4 &&
            ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId && e.Result == result) == 1;
    }
    private void ResolveLiuYou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!ValidLiuYou(f) || f.InstructionIndex != 3 || f.AnniversaryLiuYou is not { Completed: false, PindianId: { } pairId, Result: { } result } r ||
            _resolutionStack.Any(p => p.Id == pairId) || f.PindianResultBindings is not [{ Name: AnniversaryLiuYouComposition.Result } b] || b.SourceSeat != result.SourceSeat ||
            b.OpponentSeat != result.OpponentSeat || b.SourceRank != result.SourceRank || b.OpponentRank != result.OpponentRank || b.SourceWon != result.SourceWon ||
            ProgramEventHistory<AnniversaryLiuYouResultEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Kannan must settle its once-returned native effective ranks and original bound result.");
        var winner = result.SourceWon ? result.SourceSeat : result.WonBy(result.OpponentSeat) ? result.OpponentSeat : -1;
        ReplaceRuntimeTop(f with { AnniversaryLiuYou = r with { Completed = true } });
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYouResultEvent(f.Id, pairId, result.SourceRank, result.OpponentRank, winner));
        if (winner >= 0) AdvanceEventRulesAndQueueFact(new AnniversaryLiuYouBonusIssuedEvent(f.Id, r.Source, r.Hash, pairId, winner));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50000)] private void AssertLiuYou(ProgramSkillFrame f)
    { if (f.AnniversaryLiuYou is not null && !ValidLiuYou(f)) throw new InvalidOperationException("Kannan lost its actual-Play issue or original paid native Pindian."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50000)]
    private bool KeepLiuYouPaid(ProgramSkillFrame f) => f.AnniversaryLiuYou is not null && ValidLiuYou(f);

    private AnniversaryLiuYouBonusIssuedEvent[] LiuYouUnconsumed(int beneficiary) => ProgramEventHistory<AnniversaryLiuYouBonusIssuedEvent>().Where(e => e.Beneficiary == beneficiary &&
        e.Source.SkillId == AnniversaryLiuYouComposition.Skill && e.Source.BindingId == AnniversaryLiuYouComposition.Activation &&
        _contentRegistry.Skills.TryGetValue(e.Source.SkillId, out var skill) && skill.Program?.GameplayHash == e.Hash &&
        ProgramEventHistory<AnniversaryLiuYouStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash) == 1 &&
        ProgramEventHistory<AnniversaryLiuYouResultEvent>().Count(r => r.FrameId == e.FrameId && r.PindianId == e.PindianId && r.Winner == beneficiary) == 1 &&
        !ProgramEventHistory<AnniversaryLiuYouBonusConsumedEvent>().Any(c => c.GrantFrameId == e.FrameId)).ToArray();
    private void CaptureAnniversaryLiuYouNextSlash(long cardUseFrameId)
    {
        var use = LifecycleCardUse(cardUseFrameId) ?? throw new InvalidOperationException("Kannan requires the actual committing card-use frame.");
        if (use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) || use.AnniversaryLiuYouSlashBonus is not null) return;
        var grants = LiuYouUnconsumed(action.ActorSeat); if (grants.Length == 0) return;
        var amount = grants.Length;
        ReplaceRuntimeFrame(use.Id, use with { AnniversaryLiuYouSlashBonus = amount,
            ProgramAdjustedSlashBaseDamage = use.ProgramAdjustedSlashBaseDamage is { } original ? checked(original + amount) : use.CardAttack is { } attack ? checked(attack.DamageAmount + amount) : null,
            CardAttack = use.CardAttack is { } current ? current with { DamageAmount = checked(current.DamageAmount + amount) } : null });
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new AnniversaryLiuYouBonusConsumedEvent(grant.FrameId, use.Id, action.ActionId, action.ActorSeat));
        AdvanceEventRulesAndQueueFact(new AnniversaryLiuYouSlashCapturedEvent(use.Id, action.ActionId, action.ActorSeat, amount));
    }
}
