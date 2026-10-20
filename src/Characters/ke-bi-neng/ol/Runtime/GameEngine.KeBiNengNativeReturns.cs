namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool SameKeBiNengAction(LegalAction a, LegalAction b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNengNativeReturns.SameKeBiNengAction(a,b);
    private bool KeBiNengPaymentRidesOn(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.KeBiNengPingduan is not { Stage: KeBiNengPingduanStage.BasicIssuing, SelectedBasicAction: { ConversionSource: { } source } action } r || !ValidKeBiNeng(f)) return false;
        if (child is DrawFundedDistinctBasicFrame draw) return action.DrawFundedDistinctBasicUse is not null && draw.Payment is { Intent: DrawFundedDistinctBasicIntent.Play } p &&
            p.ParentFrameId == f.Id && p.RequestFrameId == f.Id && p.Cursor == f.InstructionIndex && p.Source == source && p.Source.OwnerSeat == r.ActorSeat &&
            p.ActualTurnNumber == r.ActualTurn && p.ActualTurnOwnerSeat == f.OwnerSeat && p.EffectiveKind == action.PlayedCardKind && draw.TargetSeats.SequenceEqual(action.TargetSeats);
        if (child is ChainedStateBasicFrame chained) return action.ChainedStateBasicUse is not null && chained.Payment is { Intent: ChainedStateBasicIntent.Play } p &&
            p.ParentFrameId == f.Id && p.RequestFrameId == f.Id && p.Cursor == f.InstructionIndex && p.Source == source && p.Source.OwnerSeat == r.ActorSeat &&
            p.ActualTurnNumber == r.ActualTurn && p.ActualTurnOwnerSeat == f.OwnerSeat && p.EffectiveKind == action.PlayedCardKind && chained.TargetSeats.SequenceEqual(action.TargetSeats);
        if (child is ProgramSkillFrame { EquipmentSlotNamedUse: { Intent: EquipmentSlotNamedUseIntent.Play, PlayAction: { } play } draft } payment)
            return payment.OwnerSeat == r.ActorSeat && payment.SkillId == source.SkillId && payment.SkillInstanceId == source.SkillInstanceId && action.TieredRoundZeroUse is not null &&
                draft.OriginalParentId == f.Id && draft.Source == source && SameKeBiNengAction(play, action) && draft.ActualTurn == r.ActualTurn && draft.ActualTurnOwner == f.OwnerSeat &&
                draft.PhaseInstance == r.PhaseInstance && draft.EffectiveKind == action.PlayedCardKind && draft.TargetSignature == EquipmentSlotNamedTargetSignature(action, null) &&
                CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseStartedEvent>().Count(e => e.FrameId == payment.Id && e.Source == source && e.GameplayHash == draft.GameplayHash &&
                    e.OriginalParentId == f.Id && e.OriginalPromptId == draft.OriginalDecision.PromptId && e.OriginalRevision == draft.OriginalDecision.Revision &&
                    e.TargetSignature == draft.TargetSignature && e.OriginalDecisionSignature == draft.OriginalDecisionSignature && e.Intent == EquipmentSlotNamedUseIntent.Play &&
                    e.EffectiveKind == draft.EffectiveKind && e.ActualTurn == r.ActualTurn && e.ActualTurnOwner == f.OwnerSeat && e.PhaseInstance == r.PhaseInstance) == 1;
        return child is CardDeclarationFrame declaration && MatchesKeBiNengPingduanDeclaration(f, declaration);
    }
    private bool IsKeBiNengPingduanBasicQuery(CharacterState actor)
    {
        if (!HasKeBiNengCapability || !actor.IsAlive) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { KeBiNengPingduan: { Stage: KeBiNengPingduanStage.BasicChoice or KeBiNengPingduanStage.BasicIssuing } r } top)
            return r.ActorSeat == actor.Seat && ValidKeBiNeng(top);
        return _resolutionStack.Count >= 2 && _resolutionStack[^2] is ProgramSkillFrame { KeBiNengPingduan: { } parentReceipt } parent && parentReceipt.ActorSeat == actor.Seat &&
            KeBiNengPaymentRidesOn(parent, _resolutionStack[^1]);
    }
    private void BindKeBiNengPingduanNativeUse(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id); if (index < 1) return;
        ProgramSkillFrame? parent = _resolutionStack[index - 1] as ProgramSkillFrame;
        if (parent?.KeBiNengPingduan is not { Stage: KeBiNengPingduanStage.BasicIssuing })
        {
            if (index < 2 || _resolutionStack[index - 2] is not ProgramSkillFrame candidate || !KeBiNengPaymentRidesOn(candidate, _resolutionStack[index - 1])) return;
            parent = candidate;
        }
        var r = parent.KeBiNengPingduan!; var selected = r.SelectedBasicAction ?? throw new InvalidOperationException("Pingduan lost its accepted basic native action.");
        var materials = KeBiNengMaterials(selected); var expectedKind = selected.PlayedCardKind ?? (selected.CardId is > 0 ? AdvancedEffectiveHandKind(_players[r.ActorSeat], GetAttackCard(selected.CardId.Value)) : use.CardKind);
        if (!ValidKeBiNeng(parent) || r.NativeUse is not null || use.SourceSeat != r.ActorSeat || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != r.ActorSeat || action.ProviderSeat != r.ActorSeat || action.EffectiveKind != use.CardKind || use.CardKind != expectedKind || CardCatalog.Get(use.CardKind).CategoryName != "基本牌" ||
            use.PhysicalCardIds is not { } physical || !physical.SequenceEqual(materials) || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(materials) ||
            !action.ConversionChain.SequenceEqual((selected.ConversionSource is { } primary ? new[] { primary } : []).Concat(selected.AdditionalConversionSources ?? [])) ||
            !use.TargetSeats.SequenceEqual(GetImplicitSelfCardUseTargets(use.CardKind, r.ActorSeat, selected.TargetSeats)))
            throw new InvalidOperationException("Pingduan changed its privately accepted native basic name, original actor, actual material, conversion source or targets.");
        var origin = new KeBiNengPingduanUseOrigin(parent.Id, parent.InstructionIndex, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstance, r.ActorSeat, use.Id, action.ActionId, use.CardKind);
        ReplaceRuntimeFrame(parent.Id, parent with { KeBiNengPingduan = r with { Stage = KeBiNengPingduanStage.BasicNative, NativeUse = origin } });
        UpdateLifecycleCardUse(use.Id, current => current with { KeBiNengPingduanUse = origin }); AdvanceEventRulesAndQueueFact(new KeBiNengPingduanNativeIssuedEvent(origin));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 50)]
    private bool HasIssuedKeBiNengPingduanUse(long id, int actor) => LifecycleCardUse(id) is { KeBiNengPingduanUse: { } origin, Action: { } action } use &&
        origin.CardUseFrameId == id && (origin.ActorSeat == actor || use.SourceSeat == actor) && action.ActionId == origin.ActionId && ShownEntityUseActorMatches(use, origin.ActorSeat, origin.ActorSeat) &&
        _resolutionStack.SingleOrDefault(f => f.Id == origin.ProgramFrameId) is ProgramSkillFrame { KeBiNengPingduan: { NativeUse: { } stored } } parent && stored == origin && ValidKeBiNeng(parent) &&
        CompleteProgramEventHistory().OfType<KeBiNengPingduanNativeIssuedEvent>().Count(e => e.Origin == origin) == 1;
    [NativeUseValidation(27040)]
    private void AssertKeBiNengNativeCompletion(CardUseFrame use)
    {
        if (use.KeBiNengPingduanUse is { } origin && (use.Step != ResolutionFrameStep.Completed || !HasIssuedKeBiNengPingduanUse(use.Id, origin.ActorSeat)))
            throw new InvalidOperationException("Pingduan basic use must complete through its original issued native receipt.");
    }
    [NativeUseReturn(27040)]
    private bool ReturnKeBiNengNativeUse(CardUseFrame use)
    {
        if (use.KeBiNengPingduanUse is not { } origin) return false;
        if (_resolutionStack.Any(f => f.Id == use.Id) || _resolutionStack.SingleOrDefault(f => f.Id == origin.ProgramFrameId) is not ProgramSkillFrame { KeBiNengPingduan: { Stage: KeBiNengPingduanStage.BasicNative } r } parent ||
            r.NativeUse != origin || use.Action?.ActionId != origin.ActionId || CompleteProgramEventHistory().OfType<KeBiNengPingduanNativeReturnedEvent>().Any(e => e.Origin == origin))
            throw new InvalidOperationException("Pingduan lost its complete native basic return or returned twice.");
        ReplaceRuntimeFrame(parent.Id, parent with { KeBiNengPingduan = r with { Stage = KeBiNengPingduanStage.BasicReturned, CompletedItems = 1 } });
        AdvanceEventRulesAndQueueFact(new KeBiNengPingduanNativeReturnedEvent(origin));
        AdvanceEventRulesAndQueueFact(new KeBiNengPingduanItemPaidEvent(parent.Id, 1, r.ActorSeat, null, null, _movementSequence, _movementSequence));
        if (use.CardAttack is not { Active: true }) AdvanceRuntimeProgram(parent.Id); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 27040)]
    private bool ReturnKeBiNengNativeAttack()
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { KeBiNengPingduan: { Stage: KeBiNengPingduanStage.BasicReturned } } f) return false; AdvanceRuntimeProgram(f.Id); return true; }
    private bool TryCancelKeBiNengPingduanBasicPayment(long programId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { KeBiNengPingduan: { Stage: KeBiNengPingduanStage.BasicIssuing, NativeUse: null, SelectedBasicAction: { ConversionSource: { } source } } r } f || f.Id != programId || !ValidKeBiNeng(f)) return false;
        var drawCancelled = CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicStartedEvent>().Any(s => s.ParentFrameId == f.Id && s.RequestFrameId == f.Id && s.Cursor == f.InstructionIndex && s.Source == source &&
            s.ActorSeat == r.ActorSeat && s.EffectiveKind == r.SelectedBasicAction.PlayedCardKind && CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicCancelledEvent>().Any(c => c.PaymentFrameId == s.PaymentFrameId && c.ActorSeat == r.ActorSeat && c.Intent == DrawFundedDistinctBasicIntent.Play));
        var chainCancelled = CompleteProgramEventHistory().OfType<ChainedStateBasicCancelledEvent>().Any(e => e.Payment.ParentFrameId == f.Id && e.Payment.RequestFrameId == f.Id && e.Payment.Cursor == f.InstructionIndex && e.Payment.Source == source && e.Payment.Intent == ChainedStateBasicIntent.Play);
        var slotCancelled = CompleteProgramEventHistory().OfType<EquipmentSlotNamedUsePaidEvent>().Any(e =>
            e.Payment is { Intent: EquipmentSlotNamedUseIntent.Play } p && p.OriginalParentId == f.Id && p.Source == source && p.ActualTurn == r.ActualTurn && p.ActualTurnOwner == f.OwnerSeat &&
            p.PhaseInstance == r.PhaseInstance && p.EffectiveKind == r.SelectedBasicAction.PlayedCardKind && p.TargetSignature == EquipmentSlotNamedTargetSignature(r.SelectedBasicAction, null) &&
            CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseStartedEvent>().Count(s => s.FrameId == p.FrameId && s.Source == source && s.GameplayHash == p.GameplayHash &&
                s.Intent == p.Intent && s.EffectiveKind == p.EffectiveKind && s.OriginalParentId == f.Id && s.ActualTurn == p.ActualTurn && s.ActualTurnOwner == p.ActualTurnOwner &&
                s.PhaseInstance == p.PhaseInstance && s.OriginalPromptId == p.OriginalPromptId && s.OriginalRevision == p.OriginalRevision && s.TargetSignature == p.TargetSignature) == 1 &&
            CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseCancelledEvent>().Count(c => c.PaymentFrameId == p.FrameId) == 1 &&
            !CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseIssuedEvent>().Any(i => i.PaymentFrameId == p.FrameId));
        if (!drawCancelled && !chainCancelled && !slotCancelled) return false; FinishKeBiNeng(f, "accepted-basic-payment-cancelled-before-use"); return true;
    }
    private bool MatchesKeBiNengPingduanDeclaration(ProgramSkillFrame root, CardDeclarationFrame d) => root.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicIssuing, SelectedBasicAction: { } selected } r && ValidKeBiNeng(root) &&
        d.OwnerSeat == r.ActorSeat && d.TurnNumber == r.ActualTurn && d.Return is { Purpose: CardDeclarationPurpose.Use, UseAction: { } action } ret && ret.ParentFrameId == root.Id && ret.ParentKind == root.Kind && ret.ActorSeat == r.ActorSeat &&
        SameKeBiNengAction(action, selected) && d.Payment.Source == selected.ConversionSource && d.Payment.ProviderSeat == r.ActorSeat && d.Payment.ActorSeat == r.ActorSeat && d.Payment.DeclarationId == d.Id &&
        selected.CardId == d.Payment.Cost.CardId && d.Payment.DeclaredKind == selected.PlayedCardKind && d.TargetSeats.SequenceEqual(selected.TargetSeats);
    private bool TryReturnKeBiNengPingduanDeclaration(CardDeclarationFrame declaration, bool success)
    {
        if (success || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !MatchesKeBiNengPingduanDeclaration(f, declaration)) return false;
        if (_resolutionStack.Any(x => x.Id == declaration.Id) || declaration.Stage != CardDeclarationStage.Returning || f.KeBiNengPingduan!.NativeUse is not null)
            throw new InvalidOperationException("Pingduan declaration cancellation lost its unique original unpaid native use.");
        FinishKeBiNeng(f, "basic-declaration-produced-no-use"); return true;
    }
}
