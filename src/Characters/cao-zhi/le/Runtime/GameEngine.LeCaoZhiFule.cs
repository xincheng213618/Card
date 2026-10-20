namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeCaoZhiFuleSkill = "le:fule";
    private const string FuleInitialBinding = "mark-initial-entities";
    private bool HasLeCaoZhiFuleCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeLeCaoZhiFule);
    private static CardConversionSource FuleSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    private FuEntityNameSnapshot? CaptureLeCaoZhiFuEntity(int cardId)
    {
        var completed = ProgramEventHistory<FuMarkingCompletedEvent>().Select(e => (e.FrameId, e.Origin)).ToHashSet();
        var mark = ProgramEventHistory<FuEntityAssignedEvent>().LastOrDefault(e => e.CardId == cardId && completed.Contains((e.FrameId, e.Origin)));
        return mark is null ? null : new(mark.CardId, mark.PrintedKind, mark.AliasKind, mark.Revision);
    }
    private bool IsFuleHandLimitExempt(CharacterState owner, int cardId) => HasRuntimeSkill(owner, LeCaoZhiFuleSkill) &&
        _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && CaptureLeCaoZhiFuEntity(cardId) is not null;
    private string? FulePublicLabel(int cardId) => CaptureLeCaoZhiFuEntity(cardId) is null ? null : "赋";
    private string? FuleHandLabel(CharacterState owner, Card card) => CaptureLeCaoZhiFuEntity(card.Id) is { } fu
        ? fu.AliasKind is { } alias ? $"赋·{CardCatalog.Get(alias).DisplayName}" : "赋" : null;
    private CardSnapshot ProjectFuleHandCard(CharacterState owner, Card card, CardSnapshot snapshot)
    {
        var label = FuleHandLabel(owner, card);
        return label is null ? snapshot : snapshot with
        { SkillMarkLabel = snapshot.SkillMarkLabel is { } existing ? existing.Replace("赋", label, StringComparison.Ordinal) : label };
    }

    // Content recipes are immutable for this match. This never scans the live
    // DrawPile: exhaustion, reshuffle and temporary generated entities cannot
    // shrink or extend the set of available names.
    private CardKind[] FuleModeNames() => LeCaoZhiModeNamePool();
    private ProgramLifecycleTriggerWindowFrame FreezeFuleInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeCaoZhiFuleCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.FuleInitialEntities is not null) throw new InvalidOperationException("Fule cannot recapture its original opening Hand.");
        var owners = window.Candidates.Where(c => c.SkillId == LeCaoZhiFuleSkill && c.BindingId == FuleInitialBinding &&
            GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.InitializeLeCaoZhiFule }]).Select(c => c.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(move => move.CardId == card.Id && move.To == CardLocation.Hand(owner));
            if (card.Id <= 0 || entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("Fule needs each original physical opening-Hand entry.");
            return new FuleInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { FuleInitialEntities = entities };
    }
    private bool IsIssuedFuleInitialCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        candidate.SkillId == LeCaoZhiFuleSkill && candidate.BindingId == FuleInitialBinding && IsValidPlayerSeat(candidate.OwnerSeat) &&
        _contentRegistry.GetSkill(candidate.SkillId).Program is { } program && program.GameplayHash == candidate.GameplayHash &&
        LeCaoZhiComposition.IsFuleProgram(program) && context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == candidate.OwnerSeat &&
        _phase == TurnPhase.NotStarted && _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is
            ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.GameStarting,
                Continuation: ProgramLifecycleContinuation.CompleteGameStarting, FuleInitialEntities: not null } window &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && window.Candidates[window.CandidateIndex] == candidate &&
        context.SourceSeat == window.OwnerSeat && context.TargetSeat == window.OwnerSeat &&
        !ProgramEventHistory<FuleInitializedEvent>().Any(done => done.Source.OwnerSeat == candidate.OwnerSeat);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29080, SkillProgramEffectOp.InitializeLeCaoZhiFule)]
    private bool CanOfferFuleInitial(ProgramTriggerLegalityContext gate) => IsIssuedFuleInitialCandidate(gate.Candidate, gate.Context);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29080)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29080)]
    private bool PermitIssuedFuleInitial(ProgramSkillFrame f)
    {
        if (f.SkillId != LeCaoZhiFuleSkill || f.TriggerId != FuleInitialBinding || f.ActivationId != f.TriggerId || f.InstructionIndex is < 0 or > 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            !ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.GameStarting) || !OriginalHandBindingStarted(f, SkillProgramTriggerWindow.GameStarting)) return false;
        return IsIssuedFuleInitialCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash,
            GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) || f.InstructionIndex == 1 &&
            ProgramEventHistory<FuleInitializedEvent>().Count(done => done.FrameId == f.Id && done.Source == FuleSource(f) && done.GameplayHash == f.GameplayHash) == 1;
    }
    private SkillProgramStepOutcome InitializeLeCaoZhiFule(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || !PermitIssuedFuleInitial(f) ||
            ProgramEventHistory<FuleInitializedEvent>().Any(done => done.Source.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Fule requires its once-issued original opening-Hand mother.");
        var window = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(frame => frame.Id == f.WindowContext!.ParentFrameId);
        var entities = window.FuleInitialEntities!.Where(entity => entity.OwnerSeat == f.OwnerSeat).ToArray();
        if (entities.Select(entity => entity.CardId).Distinct().Count() != entities.Length || entities.Any(entity =>
            !_cardMovements.Any(move => move.Sequence == entity.EntrySequence && move.CardId == entity.CardId &&
                move.CardKind == entity.PrintedKind && move.To == CardLocation.Hand(f.OwnerSeat))))
            throw new InvalidOperationException("Fule cannot replace its frozen opening entity set with a later Hand.");
        AssignFuleEntities(f, entities.Select(entity => entity.CardId).ToArray(), FuMarkOrigin.OpeningHand);
        AdvanceEventRulesAndQueueFact(new FuleInitializedEvent(f.Id, FuleSource(f), f.GameplayHash, window.Id, entities.Length));
        return SkillProgramStepOutcome.Continue;
    }
    private void MarkWenlanFuEntities(ProgramSkillFrame supplied, IReadOnlyList<int> ids, FuMarkOrigin origin)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || f.SkillId != "le:wenlan" || f.GameplayHash != supplied.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            origin is not (FuMarkOrigin.WenlanObtain or FuMarkOrigin.WenlanHandRewrite) || !IsExactWenlanFuMarking(f, ids, origin) ||
            ids.Distinct().Count() != ids.Count || ids.Any(id => id <= 0 ||
                (origin == FuMarkOrigin.WenlanObtain ? _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat) :
                    !IsOwnedPlayableLocation(_players[f.OwnerSeat], _cardZones.GetLocation(id)))))
            throw new InvalidOperationException("Fu marking requires Wenlan's exact paid acquire or confirmed private Hand receipt.");
        AssignFuleEntities(f, ids, origin);
    }
    private void AssignFuleEntities(ProgramSkillFrame f, IReadOnlyList<int> ids, FuMarkOrigin origin)
    {
        if (ProgramEventHistory<FuMarkingCompletedEvent>().Any(done => done.FrameId == f.Id && done.Origin == origin) ||
            ProgramEventHistory<FuEntityAssignedEvent>().Any(mark => mark.FrameId == f.Id && mark.Origin == origin))
            throw new InvalidOperationException("A Fu batch may assign its actual entities only once.");
        var pool = FuleModeNames();
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index]; var card = GetAdvancedCard(id); var old = CaptureLeCaoZhiFuEntity(id);
            if (old is not null && old.PrintedKind != card.Kind) throw new InvalidOperationException("A Fu entity cannot change its printed face.");
            var candidates = pool.Where(kind => kind != card.Kind && kind != old?.AliasKind).ToArray();
            var before = _random.State;
            var alias = old is not null && origin != FuMarkOrigin.WenlanHandRewrite || candidates.Length == 0
                ? old?.AliasKind : candidates[_random.Next(candidates.Length)];
            var changed = old is null || old.AliasKind != alias;
            var revision = changed ? ++_resolutionSequence : old!.Revision;
            AdvanceEventRulesAndQueueFact(new FuEntityAssignedEvent(f.Id, FuleSource(f), f.GameplayHash, origin, index, id, card.Kind,
                old?.AliasKind, alias, old?.Revision ?? 0, revision, before, _random.State, changed));
        }
        // All scalar updates become observable together after this completion.
        AdvanceEventRulesAndQueueFact(new FuMarkingCompletedEvent(f.Id, origin, ids.Count));
    }
    private bool CanUseFuleAlias(CharacterState owner, Card card, IndexedSkillProgramInstance instance, SkillProgramViewAs rule,
        bool forResponse, bool dyingUse, int? requestedUseTarget)
    {
        if (instance.SkillId != LeCaoZhiFuleSkill) return true;
        var program = _contentRegistry.GetSkill(instance.SkillId).Program;
        if (!owner.IsAlive || program is null || !LeCaoZhiComposition.IsFuleProgram(program) || FulePureProviderFrontier(owner.Seat) ||
            !HasRuntimeSkillInstance(owner, instance.SkillId, instance.SkillInstanceId) ||
            ActivityRuntimeProgram(program).ViewAs.SingleOrDefault(view => view.Id == rule.Id) != rule ||
            CaptureLeCaoZhiFuEntity(card.Id) is not { AliasKind: { } alias } || alias != rule.OutputKind ||
            !IsOwnedPlayableLocation(owner, _cardZones.GetLocation(card.Id))) return false;
        if (requestedUseTarget is { } requested) return rule.OutputKind == CardKind.Peach && !forResponse && !dyingUse && IsWenyiPeachSupplyQuery(owner, requested);
        if (dyingUse || forResponse && rule.OutputKind is CardKind.Peach or CardKind.Alcohol)
            return ActiveDying is { } dying && dying.ResponderSeat == owner.Seat &&
                (rule.OutputKind == CardKind.Peach || rule.OutputKind == CardKind.Alcohol && dying.VictimSeat == owner.Seat);
        if (!forResponse) return true;
        if (IsSlashCard(rule.OutputKind)) return FuleTrueSlashUseFrontier(owner);
        return rule.OutputKind == CardKind.Nullification || rule.OutputKind == CardKind.Dodge && IsProgramResponseCardUse(owner, CardKind.Dodge);
    }
    private bool IsFuleAliasSource(CardConversionSource? source) => source is { SkillId: LeCaoZhiFuleSkill } &&
        _contentRegistry.GetSkill(source.SkillId).Program is { } p && LeCaoZhiComposition.IsFuleProgram(p) && ViewAsRule(source) is not null;
    private bool FulePureProviderFrontier(int actor)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        return ActiveFactionCardRequest is { AwaitingProviders: true } f && f.CurrentCandidateSeat == actor && At(f.OwnerFrameId) ||
            ActiveFactionDefense is { } d && d.CurrentCandidateSeat == actor && At(d.Attack.ResolutionId);
    }
    private bool FuleTrueSlashUseFrontier(CharacterState owner)
    {
        if (FulePureProviderFrontier(owner.Seat)) return false;
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        if (ActiveBorrowedSword is { ActiveAttack: null } borrowed && borrowed.WeaponOwnerSeat == owner.Seat && At(borrowed.ResolutionId)) return true;
        if (ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == owner.Seat &&
            SameAttackOwner(qinglong.Attack, ActiveCardAttack) && At(qinglong.Attack.ResolutionId)) return true;
        if (top is not ProgramSkillFrame f || GetPausedPrivateOfferEffect(f) is not { } effect) return false;
        if (IsQigongRequest(f)) return f.LvKuangLvXiang!.Actor == owner.Seat;
        if (IsCaoChunInitialRequest(f) || IsWuJingHejiRequest(f)) return f.OwnerSeat == owner.Seat;
        return RoundOnceCardlessProgramActor(f, effect.Op, owner.Seat);
    }
    private bool FuleSlashAliasAllowed(CharacterState owner, bool forResponse) => FuleTrueSlashUseFrontier(owner);
    private IReadOnlyList<SlashUseVariant> FulePhysicalSlashVariants(CharacterState owner, Card card) => SlashKinds
        .Where(kind => IsFulePhysicalSlashKind(owner, card, kind)).Select(kind => new SlashUseVariant(kind, null, false)).ToArray();
    private bool IsFulePhysicalSlashKind(CharacterState owner, Card card, CardKind kind) => IsSlashCard(kind) &&
        GetProgramViewAsConversions(owner, card, kind, false).Any(source => IsFuleAliasSource(source) && ViewAsRule(source)!.OutputKind == kind);
    private bool FuleNativeConversionMatchesOutput(CharacterState owner, CardConversionSource source, CardKind kind) =>
        !IsFuleAliasSource(source) || !FuleTrueSlashUseFrontier(owner) || ViewAsRule(source)!.OutputKind == kind;
    private bool FuleAllowsUnspecifiedForcedSlash(CharacterState owner, Card card, CardKind kind) =>
        !IsSlashCard(kind) || !FuleTrueSlashUseFrontier(owner) || IsNativeResponseCard(card, kind) || FulePhysicalSlashVariants(owner, card).Count == 0;
    private CardKind FuleSelectedSlashKind(CharacterState owner, Card card, CardKind fallback, CardConversionSource? explicitSource = null)
    {
        var source = explicitSource ?? _selectedUseConversion ?? _selectedResponseConversion;
        return source is not null && IsFuleAliasSource(source) && ViewAsRule(source) is { } rule && IsSlashCard(rule.OutputKind) &&
            GetProgramViewAsConversions(owner, card, rule.OutputKind, false).Contains(source) ? rule.OutputKind : fallback;
    }
    private CardKind FuleForcedAiSlashKind(PendingDecision decision, Card card, CardKind fallback)
    {
        var choices = decision.Choices.Where(choice => choice.Cards is [var id] && id == card.Id).ToArray();
        if (choices.Any(choice => choice.Parameters.GetValueOrDefault("response-card-kind") == fallback.ToString())) return fallback;
        var alias = choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") == LeCaoZhiFuleSkill &&
            Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("response-card-kind"), out var kind) && IsSlashCard(kind));
        return alias is null ? fallback : Enum.Parse<CardKind>(alias.Parameters["response-card-kind"]);
    }
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 29080)]
    private void AddFuleMissingNativeActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        foreach (var card in playable.Where(card => selectedPhysicalCardId is null || card.Id == selectedPhysicalCardId))
        {
            if (IsTurnHandCardRestricted(actor, card)) continue;
            foreach (var kind in SlashKinds)
            foreach (var source in GetProgramViewAsConversions(actor, card, kind, false).Where(source => source.SkillId == LeCaoZhiFuleSkill && ViewAsRule(source)!.OutputKind == kind))
            {
                actions.RemoveAll(action => action.CardId == card.Id && action.ConversionSource == source);
                foreach (var variant in GetSlashUseVariants(actor, card, kind, source))
                {
                    var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: variant.EffectiveKind);
                    IReadOnlyList<CardConversionSource>? additional = variant.AdditionalConversionSource is { } chained ? [chained] : null;
                    var name = CardCatalog.Get(variant.EffectiveKind).DisplayName;
                    foreach (var target in targets) actions.Add(new LegalAction(LegalActionKind.Slash, card.Id, target.Seat,
                        DescribeConversion(source, $"以【{name}】使用【{card.DisplayName}】"), variant.EffectiveKind)
                        { ConversionSource = source, AdditionalConversionSources = additional });
                    AddFangtianHalberdSlashActions(actions, actor, card, targets, name, variant.EffectiveKind, source, additional);
                    AddProgramTargetCountSlashActions(actions, actor, card, targets, name, variant.EffectiveKind, source, additional);
                }
            }
            if (_cardZones.GetLocation(card.Id) == CardLocation.WoodenOxGrain(actor.Seat)) AddFuleGrainUseActions(actions, actor, card);
            foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.Lightning, false).Where(source => source.SkillId == LeCaoZhiFuleSkill))
                if (!actor.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(actor, CardKind.Lightning) &&
                    !IsCardUseForbidden(actor.Seat, CardKind.Lightning, CardActionType.Use) && !IsSelfTargetForbiddenAction(actor, CardKind.Lightning, [actor.Seat]))
                    actions.Add(new LegalAction(LegalActionKind.Lightning, card.Id, actor.Seat,
                        DescribeConversion(source, $"以【闪电】使用【{card.DisplayName}】"), CardKind.Lightning) { ConversionSource = source });
        }
    }
    private void AddFuleGrainUseActions(List<LegalAction> actions, CharacterState actor, Card card)
    {
        if (IsRuleWounded(actor))
            foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.Peach, false).Where(IsFuleAliasSource))
                actions.Add(new(LegalActionKind.Peach, card.Id, actor.Seat,
                    DescribeConversion(source, $"以【桃】使用【{card.DisplayName}】"), CardKind.Peach) { ConversionSource = source });
        foreach (var kind in new[] { CardKind.Indulgence, CardKind.SupplyShortage })
        foreach (var source in GetProgramViewAsConversions(actor, card, kind, false).Where(IsFuleAliasSource))
        foreach (var target in _players.Where(target => target.IsAlive && target.Seat != actor.Seat && !target.JudgmentAreaAbolished &&
            !HasJudgmentEffectiveCard(target, kind) && (kind != CardKind.SupplyShortage ||
                HasWuLuKangDixianMaterialPrivilege(actor, kind, [card.Id], source) || GuanFirstRedDistance(actor, card) || HasPhaseSuitAllowance(actor, card) || HasCardDistanceExemption(actor, target, kind, excludedEquipmentId: card.Id) ||
                HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, kind) || HasProvenanceUseDistance(actor, card) ||
                GetCombatDistance(actor.Seat, target.Seat) <= GetCardUseDistanceLimit(actor, kind))))
            actions.Add(new(kind == CardKind.Indulgence ? LegalActionKind.Indulgence : LegalActionKind.SupplyShortage,
                card.Id, target.Seat, DescribeConversion(source, $"以【{CardCatalog.Get(kind).DisplayName}】使用【{card.DisplayName}】"), kind)
                { ConversionSource = source });
    }
    private IEnumerable<Card> FuleDyingAlcoholGrainCards(CharacterState owner) => GetPlayableCards(owner)
        .Where(card => _cardZones.GetLocation(card.Id) == CardLocation.WoodenOxGrain(owner.Seat) &&
            GetProgramViewAsConversions(owner, card, CardKind.Alcohol, true, dyingUse: true).Any(IsFuleAliasSource));
    [ProgramCapability]
    private sealed class LeCaoZhiFuleCapability(GameEngine engine) : ILeCaoZhiFuleHost
    { public SkillProgramStepOutcome Initialize(ProgramSkillFrame frame) => engine.InitializeLeCaoZhiFule(frame); }
}
