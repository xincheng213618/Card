namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ZhuJunDrawReason = "skill-program.zhu-jun.cuipo.draw";
    private bool HasZhuJunCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveCuipoActualTurnUse);
    private static int ZhuJunCardNameLength(CardKind kind) => IsSlashCard(kind) ? 1 : kind == CardKind.BorrowedSword ? 2 : CardCatalog.Get(kind).DisplayName.Length;
    // Delayed Lightning is not an ordinary damage trick. Borrowed Sword's
    // subsequent Slash is a separate card; Unexpected Assault itself deals damage.
    private static bool IsZhuJunDamageCard(CardKind kind) => IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27030)]
    private void ObserveZhuJunActualUse(IGameEvent payload)
    {
        if (!HasZhuJunCapability || !HasOngoingActualUseTurn) return;
        CardUseFrame? use = null; CardActionContext? action = null; CurrentTurnCardUseKindsRecordedEvent? ordinal = null;
        CardKind kind; int actor;
        if (payload is CardUseDeclaredEvent declared)
        {
            use = LifecycleCardUse(declared.ResolutionId);
            if (use is null || use.CardId != declared.CardId || use.SourceSeat != declared.SourceSeat || use.CardKind != declared.CardKind)
                throw new InvalidOperationException("Cuipo requires its exact actual native declaration.");
            actor = use.SourceSeat; kind = use.CardKind; action = use.Action;
            ordinal = CompleteProgramEventHistory().OfType<CurrentTurnCardUseKindsRecordedEvent>().SingleOrDefault(e => e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.ActorSeat == actor && e.OriginFrameId == use.Id && e.CardActionId == action?.ActionId);
        }
        else if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted)
        {
            action = accepted.Action; actor = action.ActorSeat; kind = action.EffectiveKind;
            ordinal = CompleteProgramEventHistory().OfType<CurrentTurnCardUseKindsRecordedEvent>().SingleOrDefault(e => e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat && e.ActorSeat == actor && e.CardActionId == action.ActionId);
        }
        else return;
        var length = ZhuJunCardNameLength(kind);
        if (ordinal?.ActualTurnActorUseOrdinal != length || !IsValidPlayerSeat(actor) || !_players[actor].IsAlive) return;
        var bindings = GetSkillBindingShard(_players[actor])?.GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted) ?? [];
        foreach (var binding in bindings.Where(b => b.Trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse)))
        {
            var source = new CardConversionSource(binding.SkillId, binding.Trigger.Id, actor, binding.SkillInstanceId);
            if (ProgramEventHistory<ZhuJunCuipoQualifiedEvent>().Any(e => e.OriginFrameId == ordinal.OriginFrameId && e.ActionId == action?.ActionId && e.Source == source)) continue;
            var qualified = new ZhuJunCuipoQualifiedEvent(ordinal.OriginFrameId, action?.ActionId, source, binding.Program.GameplayHash, _turnNumber, _currentSeat, kind, length, length, IsZhuJunDamageCard(kind));
            AdvanceEventRulesAndQueueFact(qualified);
            if (qualified.Damage)
            {
                if (use is null) throw new InvalidOperationException("Cuipo damage must be issued to the exact native use.");
                var grant = new ZhuJunCuipoDamageReceipt(use.Id, action?.ActionId, source, qualified.GameplayHash, _turnNumber, _currentSeat, kind, length, length, action?.ProviderSeat ?? actor);
                UpdateLifecycleCardUse(use.Id, u => u with { ZhuJunCuipoDamage = new() { Grants = (u.ZhuJunCuipoDamage?.Grants ?? []).Append(grant).ToArray() } });
            }
        }
    }
    private ZhuJunCuipoQualifiedEvent? ZhuJunQualification(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.CardUse is not { } card || card.ActorSeat != candidate.OwnerSeat || context.OwnerSeat != candidate.OwnerSeat) return null;
        var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId);
        var use = LifecycleCardUse(card.ParentCardUseFrameId);
        return ProgramEventHistory<ZhuJunCuipoQualifiedEvent>().SingleOrDefault(e => !e.Damage && e.Source == source && e.GameplayHash == candidate.GameplayHash && e.Turn == _turnNumber && e.TurnOwner == _currentSeat &&
            e.EffectiveKind == card.EffectiveKind && (e.ActionId == card.CardActionId || e.ActionId is null && use is not null && e.OriginFrameId == use.Id && IsExactLegacyActualUseCompletion(use)));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27030)]
    private bool CanOfferZhuJun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse) || ZhuJunQualification(candidate, context) is not null;
    [ProgramCapability] private sealed class ZhuJunCapability(GameEngine e) : IZhuJunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginZhuJunCuipo(f); }
    private SkillProgramStepOutcome BeginZhuJunCuipo(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ZhuJunCuipoDraw is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context) throw new InvalidOperationException("Cuipo requires its exact mandatory committed instruction.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority);
        var q = ZhuJunQualification(candidate, context) ?? throw new InvalidOperationException("Cuipo lost its declaration-qualified actual use.");
        if (ProgramEventHistory<ZhuJunCuipoDrawIssuedEvent>().Any(e => e.Source == q.Source && e.OriginFrameId == q.OriginFrameId && e.ActionId == q.ActionId)) throw new InvalidOperationException("Cuipo cannot draw twice for one accepted use.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ZhuJunCuipoDraw = new(q, context.ParentFrameId, f.InstructionIndex, before, before, 0), PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var count = DrawCards(_players[f.OwnerSeat], 1, true, new(ZhuJunDrawReason)).Count;
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { ZhuJunCuipoDraw = f.ZhuJunCuipoDraw! with { After = _movementSequence, ActualCount = count } });
        AdvanceEventRulesAndQueueFact(new ZhuJunCuipoDrawIssuedEvent(f.Id, context.ParentFrameId, q.OriginFrameId, q.ActionId, q.Source, before, _movementSequence, count));
        ContinueZhuJunCuipo(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueZhuJunCuipo(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhuJunCuipoDraw: { } r } f || f.Id != id) return;
        AssertZhuJunCuipo(f);
        if (TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        AdvanceEventRulesAndQueueFact(new ZhuJunCuipoDrawCompletedEvent(id, r.Qualification.OriginFrameId, r.Qualification.ActionId));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { ZhuJunCuipoDraw = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27030)] private bool ResumeZhuJunCuipo(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhuJunCuipoDraw: not null } f || f.Id != id) return false; ContinueZhuJunCuipo(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27030)] private bool ReturnZhuJunMovement(ProgramSkillFrame f)
    { if (f.ZhuJunCuipoDraw is null) return false; ContinueZhuJunCuipo(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27030)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27030)] private bool ContinuePaidZhuJun(ProgramSkillFrame f)
    { if (f.ZhuJunCuipoDraw is null) return false; AssertZhuJunCuipo(f); return true; }
    private int ZhuJunCuipoDamageBonus(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || LifecycleCardUse(attack.ResolutionId) is not { ZhuJunCuipoDamage: { } state } use) return 0;
        if (state.Grants.Count == 0 || state.Grants.Select(r => r.Source).Distinct().Count() != state.Grants.Count) throw new InvalidOperationException("Cuipo damage requires its frozen distinct active source instances.");
        var actualAction = use.Action;
        foreach (var r in state.Grants)
        {
        if (r.UseId != use.Id || r.EffectiveKind != use.CardKind || attack.EffectiveCardKind != r.EffectiveKind || !IsZhuJunDamageCard(r.EffectiveKind) ||
            r.Ordinal != r.NameLength || r.NameLength != ZhuJunCardNameLength(r.EffectiveKind) ||
            (actualAction is null ? use.SourceSeat != r.Source.OwnerSeat || r.ActionId is not null || !IsActualTurnLegacyVirtualUse(use) : !ShownEntityUseActorMatches(use, r.Source.OwnerSeat, r.ProviderSeat) || r.ActionId is { } actionId && actualAction.ActionId != actionId) ||
            ProgramEventHistory<ZhuJunCuipoQualifiedEvent>().Count(e => e.Damage && e.OriginFrameId == use.Id && e.ActionId == r.ActionId && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Turn == r.Turn && e.TurnOwner == r.TurnOwner && e.EffectiveKind == r.EffectiveKind && e.Ordinal == r.Ordinal && e.NameLength == r.NameLength) != 1)
            throw new InvalidOperationException("Cuipo damage lost its exact issued native card and original accepted actor.");
        }
        return state.Grants.Count;
    }
}
