namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string WuJingFactionBind = "heji-native-faction";
    private bool WuJingFactionTarget(ProgramSkillFrame f) => f.WuJing is { TargetSeat: var target } && _players[f.OwnerSeat].IsAlive && _players[target].IsAlive && target != f.OwnerSeat &&
        !HasTurnCardTargetRestriction(f.OwnerSeat, SkillProgramCardTargetRestriction.SelfOnly) && SlashKinds.Any(kind => !IsCardUseForbidden(f.OwnerSeat, kind, CardActionType.Use) && !IsDirectedCardTargetProhibited(f.OwnerSeat, target, kind) && !IsSlashProhibited(_players[target]));
    private void BeginWuJingFaction(ProgramSkillFrame f)
    {
        var r = f.WuJing!; var policy = GetFactionResponsePolicy(_players[f.OwnerSeat], CardKind.Slash);
        if (r.Stage != WuJingStage.UseChoice || policy is null || ActiveFactionCardRequest is not null || !WuJingFactionTarget(f)) throw new InvalidOperationException("Heji faction request lost its exact same-target Slash need.");
        ReplaceRuntimeTop(f = f with { WuJing = r with { Stage = WuJingStage.FactionRequest, FactionPolicy = policy } });
        var seats = GetFactionProviderSeats(f.OwnerSeat, policy.FactionId);
        ActiveFactionCardRequest = new FactionCardRequestHandle(this, f.Id, FactionCardRequestPurpose.AssistedProgramUse, f.OwnerSeat, seats, policy.FactionId, targetSeat: r.TargetSeat, programSkillFrameId: f.Id, policySource: policy, assistedResultBind: WuJingFactionBind);
        _status = EngineStatus.Running; AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id, f.OwnerSeat, seats, policy.SkillId, IsActiveUse: true, r.TargetSeat)); AdvanceFactionSlashCandidate();
    }
    private bool ValidateWuJingFactionParent(FactionCardRequestHandle pending, ProgramSkillFrame f)
    {
        if (f.WuJing is not { FactionPolicy: { } policy } r) return false;
        if (r.Operation != SkillProgramEffectOp.UseHejiAtCompletedSingleTarget || r.Stage is not (WuJingStage.FactionRequest or WuJingStage.IssuingUse or WuJingStage.NativeUse) || r.Source != WuJingSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber ||
            pending.ProgramSkillFrameId != f.Id || pending.ResolutionId != f.Id || pending.OwnerSeat != f.OwnerSeat || pending.TargetSeat != r.TargetSeat || pending.PolicySource != policy || pending.AssistedResultBind != WuJingFactionBind || !pending.IsAssistedProgramUse ||
            ProgramEventHistory<FactionSlashRequestedEvent>().Count(e => e.ResolutionId == f.Id && e.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.TargetSeat && e.SkillId == policy.SkillId) != 1)
            throw new InvalidOperationException("Heji faction Slash lost its exact original policy, target or requested return.");
        return true;
    }
    private bool HasWuJingFactionDistance(int actor, int target) => ActiveFactionCardRequest is { IsAssistedProgramUse: true, ProgramSkillFrameId: { } id } pending && pending.OwnerSeat == actor && pending.TargetSeat == target &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is { WuJing: not null } owner && ValidateWuJingFactionParent(pending, owner) && WuJingFactionTarget(owner);
    private bool PrepareWuJingFactionNative(ProgramSkillFrame f, FactionCardRequestHandle pending, CharacterState provider, IReadOnlyList<Card> cards, CardKind kind)
    {
        if (!ValidateWuJingFactionParent(pending, f)) return false;
        if (f.WuJing!.Stage != WuJingStage.FactionRequest || cards.Count == 0 || provider.Seat == f.OwnerSeat || !pending.CandidateSeats.Contains(provider.Seat) || cards.Any(c => _cardZones.GetLocation(c.Id).OwnerSeat != provider.Seat)) throw new InvalidOperationException("Heji faction Slash lost its real provider-owned materials.");
        ReplaceRuntimeTop(f with { WuJing = f.WuJing with { Stage = WuJingStage.IssuingUse, Before = _movementSequence, After = _movementSequence, Selection = new(kind, cards.Count == 2 && HasWeaponAbility(provider, CardKind.ZhangbaSerpentSpear), false) { CardIds = cards.Select(c => c.Id).ToArray() } } }); return true;
    }
    private bool FinishWuJingFactionDeclined(long id, FactionCardRequestHandle pending)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuJing: { FactionPolicy: not null } } f || f.Id != id) return false;
        ValidateWuJingFactionParent(pending, f); FinishWuJing(f, false); return true;
    }
}
