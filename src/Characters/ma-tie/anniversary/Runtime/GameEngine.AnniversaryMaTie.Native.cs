namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record MaTieSlashPlan(LegalAction Action, IReadOnlyList<int> Materials, PromptChoice? Multi = null);
    private ProgramSkillFrame? MaTieQueryFrame(int actor)
    {
        var top = _resolutionStack.LastOrDefault();
        var parent = top switch
        {
            DrawFundedDistinctBasicFrame paid when IsAnniversaryMaTiePaymentFrame(paid) => _resolutionStack.SingleOrDefault(f => f.Id == paid.Payment.ParentFrameId),
            ChainedStateBasicFrame paid when IsAnniversaryMaTiePaymentFrame(paid) => _resolutionStack.SingleOrDefault(f => f.Id == paid.Payment.ParentFrameId),
            ProgramSkillFrame { EquipmentSlotNamedUse: { } slot } when AnniversaryMaTieSlotParent(slot) is { } original => original,
            _ => top
        };
        return parent is ProgramSkillFrame { AnniversaryMaTie: { Stage: AnniversaryMaTieStage.SlashChoice or AnniversaryMaTieStage.Issuing } r } f &&
            ValidMaTie(f) && r.Cursor < r.Actors.Count && r.Actors[r.Cursor] == actor && _players[actor].IsAlive && _players[r.Target].IsAlive ? f : null;
    }
    private bool IsAnniversaryMaTieSlashQuery(CharacterState actor) => MaTieQueryFrame(actor.Seat) is not null;
    private bool AnniversaryMaTieRequestActor(ProgramSkillFrame f, int actor) => f.SkillId == AnniversaryMaTieComposition.Quxian &&
        f.AnniversaryMaTie is { Stage: AnniversaryMaTieStage.SlashChoice or AnniversaryMaTieStage.Issuing or AnniversaryMaTieStage.NativeUse or AnniversaryMaTieStage.NativeReturned } r &&
        r.Cursor >= 0 && r.Cursor < r.Actors.Count && r.Actors[r.Cursor] == actor && ValidMaTie(f);
    private int[] AnniversaryMaTieRequestTargets(ProgramSkillFrame f, int actor) => AnniversaryMaTieRequestActor(f, actor) ? [f.AnniversaryMaTie!.Target] : [];
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 46700)]
    private bool MaTieSlashQuota(ProgramSlashQuotaQuery q) => IsAnniversaryMaTieSlashQuery(q.Actor);
    private IReadOnlyList<MaTieSlashPlan> MaTieSlashPlans(ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!;
        if (r.Stage != AnniversaryMaTieStage.SlashChoice || MaTieQueryFrame(r.Actors[r.Cursor])?.Id != f.Id) return [];
        var actor = _players[r.Actors[r.Cursor]]; var plans = new List<MaTieSlashPlan>();
        foreach (var action in BuildLegalActions(actor, false).Where(a => a.Kind == LegalActionKind.Slash && a.MinCardCount == 0 && a.TargetSeats.Contains(r.Target)))
            plans.Add(new(AnniversaryMaTieComposition.Freeze(action), action.CardId is > 0 ? [action.CardId.Value] : []));
        var view = f with { AssistedSlashRequest = new(actor.Seat, r.Target) };
        foreach (var choice in AssistedPhysicalSlashChoices(view).Where(c => c.Parameters.GetValueOrDefault("request-option") == "use" && c.Cards.Count > 1))
        {
            var (_, kind, source) = ReadAssistedSlashPayment(actor, choice);
            plans.Add(new(new(LegalActionKind.Slash, choice.Cards[0], r.Target, choice.Description, kind) { ConversionSource = source }, Array.AsReadOnly(choice.Cards.ToArray()), choice));
        }
        return Array.AsReadOnly(plans.ToArray());
    }
    private void IssueMaTieSlash(ProgramSkillFrame f, int index)
    {
        var r = f.AnniversaryMaTie!; var plan = MaTieSlashPlans(f)[index]; var actor = _players[r.Actors[r.Cursor]];
        ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.Issuing, SelectedAction = plan.Action, Materials = plan.Materials, Provider = actor.Seat, UseId = 0, ActionId = 0 } });
        if (plan.Multi is { } multi)
        {
            var (cards, kind, source) = ReadAssistedSlashPayment(actor, multi);
            ResolveSlashCore(actor, _players[r.Target], cards[0], kind, actor.Seat, physicalCards: cards, countsTowardSlashLimit: false, conversionSource: source, programSkillCardUseFrameId: f.Id); return;
        }
        if (plan.Action.DrawFundedDistinctBasicUse is not null)
        { BeginDrawFundedDistinctBasic(actor, plan.Action.ConversionSource!, plan.Action.PlayedCardKind!.Value, plan.Action.TargetSeats, r.OriginalDecision!, new(DrawFundedDistinctBasicIntent.Play, f.Id, f.Id, null, f.InstructionIndex, r.Target)); return; }
        if (plan.Action.ChainedStateBasicUse is not null)
        { BeginChainedStateBasic(actor, plan.Action.ConversionSource!, plan.Action.PlayedCardKind!.Value, plan.Action.TargetSeats, r.OriginalDecision!, new(ChainedStateBasicIntent.Play, f.Id, f.Id, null, f.InstructionIndex, r.Target)); return; }
        if (plan.Action.TieredRoundZeroUse is not null && plan.Action.ConversionSource is { } sourceSlot && IsEquipmentSlotNamedSkill(sourceSlot.SkillId))
        { _pendingDecision = r.OriginalDecision; if (TryBeginEquipmentSlotNamedPlay(actor, plan.Action)) return; ClearPendingDecision(); }
        ExecuteAction(actor, plan.Action);
    }
    private bool PrepareAnniversaryMaTieSuppliedSlash(ProgramSkillFrame supplied, int actor, int provider, int target, CardKind kind, IReadOnlyList<Card> cards, CardConversionSource? conversion)
    {
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == supplied.Id);
        if (f.AnniversaryMaTie is not { } r || f.SkillId != AnniversaryMaTieComposition.Quxian) return false;
        if (!AnniversaryMaTieRequestActor(f, actor) || r.Stage is not (AnniversaryMaTieStage.SlashChoice or AnniversaryMaTieStage.Issuing) || r.Target != target || !IsSlashCard(kind) || r.UseId != 0 || !IsValidPlayerSeat(provider))
            throw new InvalidOperationException("Quxian supplied Slash lost its exact actor, target or unissued original frame.");
        var action = new LegalAction(LegalActionKind.Slash, cards.Count > 0 ? cards[0].Id : 0, target, "驱险：使用杀", kind) { ConversionSource = conversion };
        ReplaceRuntimeFrame(f.Id, f with { Step = ResolutionFrameStep.ResolvingEffect, AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.Issuing, SelectedAction = action, Materials = cards.Select(c => c.Id).ToArray(), Provider = provider } }); return true;
    }
    private bool TryPrepareMaTieVariableRequest(ProgramSkillFrame f, CardActionContext action)
    {
        var r = f.AnniversaryMaTie!;
        if (r.Stage != AnniversaryMaTieStage.SlashChoice || r.OriginalDecision is not { } prompt || action.ActorSeat != r.Actors[r.Cursor] || action.ProviderSeat != action.ActorSeat || action.RequesterSeat is not null || !action.TargetSeats.SequenceEqual(new[] { r.Target }) || action.ConversionChain is not [var source]) return false;
        var ks = ProgramEventHistory<KenshangRequestStartedEvent>().LastOrDefault(e => e.OwnerFrameId == f.Id && e.ActorSeat == action.ActorSeat && e.TargetSeat == r.Target && e.Source == source && e.RequestOp == SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian);
        var ts = ProgramEventHistory<TousuiRequestStartedEvent>().LastOrDefault(e => e.OwnerFrameId == f.Id && e.ActorSeat == action.ActorSeat && e.TargetSeat == r.Target && e.Source == source && e.RequestOp == SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian);
        if (ks is not null)
        {
            var materials = ProgramEventHistory<KenshangRequestMaterialSelectedEvent>().Where(e => e.OwnerFrameId == f.Id && e.OriginalPromptId == ks.OriginalPromptId).OrderBy(e => e.Index).ToArray();
            if (ks.ActorSeat != action.ActorSeat || ks.TargetSeat != r.Target || materials.Length < 2 || !materials.Select(e => e.CardId).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
                ProgramEventHistory<KenshangRequestFinishedEvent>().LastOrDefault(e => e.OwnerFrameId == f.Id && e.ActorSeat == action.ActorSeat) is not { Accepted: true } accepted || accepted.MaterialCount != materials.Length) return false;
        }
        else if (ts is not null)
        {
            var materials = ProgramEventHistory<TousuiRequestMaterialSelectedEvent>().Where(e => e.OwnerFrameId == f.Id && e.OriginalPromptId == ts.OriginalPromptId).OrderBy(e => e.Index).ToArray();
            if (ts.ActorSeat != action.ActorSeat || ts.TargetSeat != r.Target || materials.Length < 1 || !materials.Select(e => e.CardId).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
                ProgramEventHistory<TousuiRequestFinishedEvent>().LastOrDefault(e => e.OwnerFrameId == f.Id && e.ActorSeat == action.ActorSeat) is not { Accepted: true } accepted || accepted.MaterialCount != materials.Length) return false;
        }
        else return false;
        return PrepareAnniversaryMaTieSuppliedSlash(f, action.ActorSeat, action.ProviderSeat, r.Target, action.EffectiveKind, action.PhysicalCards.Select(c => GetAdvancedCard(c.CardId)).ToArray(), source);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46700)] private void BindAnniversaryMaTieSlash(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use || !IsSlashCard(action.EffectiveKind)) return;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { AnniversaryMaTie: { } } f || f.SkillId != AnniversaryMaTieComposition.Quxian) return;
        var r = f.AnniversaryMaTie!;
        if (r.Stage == AnniversaryMaTieStage.SlashChoice && TryPrepareMaTieVariableRequest(f, action)) { f = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id); r = f.AnniversaryMaTie!; }
        if (r.Stage != AnniversaryMaTieStage.Issuing || r.SelectedAction is not { } selected || !ValidMaTie(f) || r.UseId != 0 || action.ActorSeat != r.Actors[r.Cursor] || action.ProviderSeat != r.Provider ||
            action.RequesterSeat != (r.Provider == action.ActorSeat ? (int?)null : action.ActorSeat) || !action.TargetSeats.SequenceEqual(selected.TargetSeats) ||
            !action.TargetSeats.Contains(r.Target) || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(r.Materials) ||
            action.EffectiveKind != (selected.PlayedCardKind ?? (selected.CardId is > 0 ? ApplyProgramUseAppearance(_players[action.ActorSeat], GetAdvancedCard(selected.CardId.Value), selected.ConversionSource).Kind : CardKind.Slash)) ||
            selected.ConversionSource is { } source && !action.ConversionChain.Contains(source)) throw new InvalidOperationException("Quxian native Use changed its exact selected actor, provider, target, materials or conversion.");
        var origin = new AnniversaryMaTieNativeOrigin(f.Id, r.Source, r.Hash, r.Cursor, use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, r.Target);
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.NativeUse, UseId = use.Id, ActionId = action.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { AnniversaryMaTieOrigin = origin, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieSlashIssuedEvent(origin));
    }
    private bool MaTieNativeMatches(CardUseFrame use, ProgramSkillFrame f) => use.AnniversaryMaTieOrigin is { } n && f.AnniversaryMaTie is { Stage: AnniversaryMaTieStage.NativeUse } r && ValidMaTie(f) &&
        n.FrameId == f.Id && n.Source == r.Source && n.Hash == r.Hash && n.Index == r.Cursor && n.UseId == use.Id && n.UseId == r.UseId && n.ActionId == r.ActionId && n.Target == r.Target &&
        use.Action is { Type: CardActionType.Use } a && a.ActionId == n.ActionId && a.ActorSeat == n.Actor && n.Actor == r.Actors[r.Cursor] && a.ProviderSeat == n.Provider && n.Provider == r.Provider && a.RequesterSeat == (n.Actor == n.Provider ? (int?)null : n.Actor) &&
        a.PhysicalCards.Select(c => c.CardId).SequenceEqual(r.Materials) && ProgramEventHistory<AnniversaryMaTieSlashIssuedEvent>().Count(e => e.Origin == n) == 1;
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 46700)] private bool MaTieNoDebit(long id, int actor) => LifecycleCardUse(id) is { AnniversaryMaTieOrigin: { } n } use && n.Actor == actor &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is { } parent && MaTieNativeMatches(use, parent);
    [NativeUseValidation(46700)] private void ValidateMaTieNative(CardUseFrame use)
    { if (use.AnniversaryMaTieOrigin is { } n && (use.Step != ResolutionFrameStep.Completed || !use.ProgramUseCommitted || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is not { } f || !MaTieNativeMatches(use, f))) throw new InvalidOperationException("Quxian requires its exact wholly completed committed native use."); }
    [NativeUseReturn(46700)] private void ReturnMaTieNative(CardUseFrame use)
    {
        if (use.AnniversaryMaTieOrigin is not { } n) return; var f = GetActiveProgramFrame(n.FrameId); var r = f.AnniversaryMaTie!;
        if (!MaTieNativeMatches(use, f) || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<AnniversaryMaTieSlashReturnedEvent>().Any(e => e.UseId == use.Id)) throw new InvalidOperationException("Quxian cannot repeat or substitute a native return.");
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.NativeReturned, Used = r.Used.Contains(n.Actor) ? r.Used : r.Used.Append(n.Actor).ToArray() } });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieSlashReturnedEvent(f.Id, n.Index, n.UseId, n.ActionId));
        if (use.CardAttack is not { Active: true }) ContinueMaTie(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 46700)] private bool ReturnMaTieAttack()
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryMaTie.Stage: AnniversaryMaTieStage.NativeReturned } f) return false; ContinueMaTie(f.Id); return true; }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46701)] private void ObserveMaTieDamage(DamageAppliedEvent e)
    {
        if (e.Amount <= 0 || e.SourceCardUseResolutionId is not { } useId || LifecycleCardUse(useId) is not { AnniversaryMaTieOrigin: { } n } use || e.TargetSeat != n.Target ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is not { } parent || !MaTieNativeMatches(use, parent)) return;
        var damage = _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.TargetSeat == e.TargetSeat && d.Amount == e.Amount);
        if (damage is null) throw new InvalidOperationException("Quxian applied damage lost its actual owning damage frame.");
        ReplaceRuntimeFrame(parent.Id, parent with { AnniversaryMaTie = parent.AnniversaryMaTie! with { TargetDamaged = true } });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieTargetDamagedEvent(parent.Id, useId, damage.Id, n.Target, e.Amount));
    }
}
