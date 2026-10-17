namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Optional target windows are enabled by the loaded catalog, retaining old
    // card actions and event shapes for historical content fingerprints.

    private bool HasJianjiNativeUseCapability => _contentRegistry.ProgramDependencies
        .HasActivationOperation(SkillProgramEffectOp.DrawAndOfferJianjiExactCard);

    private bool HasFinalizedTargetProgram(CardKind kind)
    {
        if (HasActivityOrdinaryTrickProjection && (kind is CardKind.AllianceFeast or CardKind.SowBeans or CardKind.TransplantWood or CardKind.ReturnSoul || IsWenheCard(kind) || IsDragonBoatTrick(kind)) &&
            _players.Where(player => player.IsAlive).Any(player => GetSkillBindingShard(player)
                .GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized)
                .Any(binding => binding.Trigger.CardKinds.Contains(kind)))) return true;
        var category = CardCatalog.Get(kind).CategoryName switch
        {
            "基本牌" => SkillProgramCardCategory.Basic,
            "装备牌" => SkillProgramCardCategory.Equipment,
            _ => SkillProgramCardCategory.Trick
        };
        // Response exchanges and paired responses need the original trick's
        // accepted Use identity even without a target-changing subscriber.
        return HasCuiYanCapability || HasHuaManCapability || HasXinPiCapability || HasCaoChunCapability || HasNeifaCapability || HasSuFeiCapability || HasJianjiNativeUseCapability || HasFanChouCapability || HasGuoSiCapability || HasYisuanCapability && category == SkillProgramCardCategory.Trick ||
            _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ExchangeRespondedCardEntities) && IsOrdinaryTrick(kind) ||
            _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.JoinAdjacentRedCardTarget) &&
            (category == SkillProgramCardCategory.Basic || IsOrdinaryTrick(kind)) ||
            _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardSameCategoryForExtraEffect) && ExtraEffectCardKind(kind) ||
            HasTargetedOrdinaryTrickAdjustment && IsOrdinaryTrick(kind) ||
            _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferPairedColorCardDisposition) &&
            category == SkillProgramCardCategory.Trick ||
            TracksCompletedUndamagedUseDamage && IsCompletedUndamagedDamageUse(kind) && IsOrdinaryTrick(kind) ||
            _contentRegistry.ProgramDependencies.HasFinalizedCardTrigger(kind, category) ||
            ((_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferOriginalTargetAddition) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AddTargetForQirangPhysicalTrick) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiAppendLegalTargets) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantActorHandLimitPenalty)) &&
                IsOrdinaryTrick(kind) && _contentRegistry.ProgramDependencies.HasFinalizedCardTrigger(kind, SkillProgramCardCategory.InstantTrick));
    }

    private IReadOnlyList<int> GetImplicitSelfCardUseTargets(CardKind kind, int sourceSeat,
        IReadOnlyList<int> targets) => targets.Count == 0 && HasFinalizedTargetProgram(kind) &&
        (kind == CardKind.Alcohol || CardCatalog.Get(kind).CategoryName == "装备牌")
            ? [sourceSeat] : targets;

    private bool TryBeginEquipmentTargetPrograms(CharacterState source, Card equipment, long frameId)
    {
        if (!HasFinalizedTargetProgram(equipment.Kind)) return false;
        // Equipment commitment observes its installed slot, after target hooks finish.
        if (!TryBeginFinalizedSimpleCardPrograms(frameId,
                new(equipment.Id, SimpleCardUseEffect.EquipmentPlacement)))
            CompleteEquipmentUse(source, equipment, frameId);
        return true;
    }

    private bool TryBeginFinalizedSimpleCardPrograms(long frameId, ProgramSimpleCardContinuation continuation)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        if (continuation.Effect == SimpleCardUseEffect.Equipment || !HasFinalizedTargetProgram(use.CardKind) ||
            !TryMarkFinalizedSimpleProgramsStarted(frameId)) return false;
        var action = use.Action ?? throw new InvalidOperationException("A finalized card use lost its action.");
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseTargetsFinalized,
            action.TargetSeats, ProgramCardContinuation.FinalizedSimpleCard, simpleContinuation: continuation);
    }
}
