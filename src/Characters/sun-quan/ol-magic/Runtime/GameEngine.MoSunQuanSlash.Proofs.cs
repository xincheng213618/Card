namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsMoQuanyuAddedTargetFact(CardUseFrame use, MoQuanyuTargetAddedEvent fact) =>
        use.Action is { Type: CardActionType.Use } a && IsSlashCard(use.CardKind) && fact.UseId == use.Id && fact.ActionId == a.ActionId &&
        fact.Source.SkillId == "ol:mo-quanyu" && fact.Source.BindingId == "singleton-slash-effects" && fact.Source.OwnerSeat == use.SourceSeat &&
        fact.TargetIndex >= 1 && IsValidPlayerSeat(fact.TargetSeat) && fact.TargetSeat != use.SourceSeat &&
        _contentRegistry.GetSkill(fact.Source.SkillId).Program?.GameplayHash == fact.GameplayHash &&
        ProgramEventHistory<MoQuanyuSlashStartedEvent>().Count(e => e.FrameId == fact.FrameId && e.Source == fact.Source &&
            e.GameplayHash == fact.GameplayHash && e.Original.UseId == use.Id && e.Original.ActionId == a.ActionId &&
            (e.Mask & MoQuanyuBit(MoQuanyuEffect.Qingming)) != 0) == 1 &&
        ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.FrameId && e.SkillId == fact.Source.SkillId &&
            e.BindingId == fact.Source.BindingId && e.OwnerSeat == fact.Source.OwnerSeat && e.SkillInstanceId == fact.Source.SkillInstanceId &&
            e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1 &&
        ProgramEventHistory<MoQuanyuTargetAddedEvent>().Count(e => e.FrameId == fact.FrameId && e.UseId == use.Id) == 1;
    private bool HasMoQuanyuAddedTargetTail(CardUseFrame use) =>
        ProgramEventHistory<MoQuanyuTargetAddedEvent>().Any(e => IsMoQuanyuAddedTargetFact(use, e));

    private bool ValidMoQuanyuSlash(ProgramSkillFrame f) => f.MoQuanyuSlash is { } r &&
        f.SkillId == "ol:mo-quanyu" && f.TriggerId == "singleton-slash-effects" && f.InstructionIndex == 1 &&
        r.Source == new CardConversionSource(f.SkillId, f.TriggerId, f.OwnerSeat, f.SkillInstanceId) &&
        r.GameplayHash == f.GameplayHash && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash &&
        r.Mask is > 0 and <= 63 && r.Stage == MoQuanyuSlashStage.AdditionalTarget &&
        f.WindowContext is { } context && TryGetMoQuanyuSingletonUse(f.OwnerSeat, context, out var use, out _, out _) &&
        use.MoQuanyuSoleUse == r.Original && use.MoQuanyuSlashPolicy is null &&
        ProgramEventHistory<MoQuanyuSlashStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
            e.GameplayHash == r.GameplayHash && e.Original == r.Original && e.Mask == r.Mask && e.Entered == r.Entered) == 1 &&
        !ProgramEventHistory<MoQuanyuSlashAppliedEvent>().Any(e => e.FrameId == f.Id);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28912)]
    private void AssertMoQuanyuSlash(ProgramSkillFrame f)
    {
        if (f.MoQuanyuSlash is null)
        {
            if (ProgramEventHistory<MoQuanyuSlashStartedEvent>().Any(e => e.FrameId == f.Id) &&
                !ProgramEventHistory<MoQuanyuSlashAppliedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Quanyu cannot discard an issued sword policy.");
            return;
        }
        if (!ValidMoQuanyuSlash(f)) throw new InvalidOperationException("Quanyu lost its original finalized singleton, signed source or frozen effect mask.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse &&
            (_pendingDecision is not { IsPrivate: false } prompt || prompt.PlayerSeat != f.OwnerSeat ||
                !AssistedChoicesEqual(prompt.Choices, f.MoQuanyuSlash.Choices)))
            throw new InvalidOperationException("Quanyu cannot replace its issued additional-target options.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28912)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28912)]
    private bool ContinueIssuedMoQuanyuSlash(ProgramSkillFrame f) => f.MoQuanyuSlash is not null && ValidMoQuanyuSlash(f);

    private void AssertMoQuanyuSlashUses()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.MoQuanyuSoleUse is { } sole && (use.Action is not { Type: CardActionType.Use } a ||
                sole.UseId != use.Id || sole.ActionId != a.ActionId || sole.ActorSeat != use.SourceSeat || sole.Kind != use.CardKind ||
                !IsValidPlayerSeat(sole.TargetSeat) || ProgramEventHistory<MoQuanyuSoleUseCapturedEvent>().Count(e => e.Original == sole) != 1))
                throw new InvalidOperationException("A Mo singleton receipt must retain its exact original native Use.");
            if (use.MoQuanyuQuota is { } q && (use.Action is not { Type: CardActionType.Use } action ||
                q.UseId != use.Id || q.ActionId != action.ActionId || q.ActorSeat != use.SourceSeat || q.Kind != use.CardKind ||
                !IsSlashCard(use.CardKind) || (q.ChoiceMask & MoQuanyuBit(MoQuanyuEffect.Liuxing)) == 0 ||
                q.Source.SkillId != "ol:mo-quanyu" || q.Source.OwnerSeat != use.SourceSeat ||
                _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash != q.GameplayHash ||
                ProgramEventHistory<MoQuanyuQuotaAcceptedEvent>().Count(e => e.Receipt == q) != 1))
                throw new InvalidOperationException("Meteor's quota receipt lost the accepted sole-target Slash.");
            if (use.MoQuanyuSlashPolicy is { } r && (use.MoQuanyuSoleUse != r.Original || r.Stage != MoQuanyuSlashStage.Applied ||
                r.Mask is < 1 or > 63 || _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
                ProgramEventHistory<MoQuanyuSlashStartedEvent>().Where(e => e.Original == r.Original).ToArray() is not [var start] ||
                start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.Mask != r.Mask || start.Entered != r.Entered ||
                ProgramEventHistory<MoQuanyuSlashAppliedEvent>().Count(e => e.FrameId == start.FrameId && e.UseId == use.Id &&
                    e.ActionId == r.Original.ActionId && e.Mask == r.Mask && e.AddedTargetSeat == r.AddedTargetSeat) != 1))
                throw new InvalidOperationException("Quanyu cannot change or duplicate an accepted Slash policy.");
            if (use.MoQuanyuExtraRound is { } round && (use.MoQuanyuSlashPolicy is not { } policy ||
                (policy.Mask & MoQuanyuBit(MoQuanyuEffect.Baili)) == 0 || round.Source != policy.Source || round.GameplayHash != policy.GameplayHash ||
                round.ActionId != policy.Original.ActionId || round.Native.ActionId != round.ActionId || round.Native.Source != round.Source ||
                round.Native.WholeGroupRepeated != round.Started || round.Active && !round.Started ||
                round.Native.OriginalAttacks.GroupBy(s => s.TargetIndex).Any(g => g.Count() != 1) ||
                round.Native.OriginalAttacks.Any(s => s.TargetIndex < 0 || s.TargetIndex >= use.TargetSeats.Count) ||
                ProgramEventHistory<MoQuanyuExtraRoundIssuedEvent>().Count(e => e.FrameId == round.ProgramFrameId && e.UseId == use.Id &&
                    e.ActionId == round.ActionId && e.Source == round.Source && e.GameplayHash == round.GameplayHash) != 1 ||
                ProgramEventHistory<MoQuanyuExtraRoundStartedEvent>().Count(e => e.UseId == use.Id) != (round.Started ? 1 : 0)))
                throw new InvalidOperationException("Baili must retain its own additional native effect without replacing other issuers.");
        }
    }
}
