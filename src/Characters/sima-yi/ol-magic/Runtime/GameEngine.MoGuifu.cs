namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsMoGuifuSource(CardConversionSource? source) => source?.SkillId == MoSimaYiComposition.Guifu;
    private bool IsMoGuifuHandLimitExempt(CharacterState owner, int id) => HasMoSimaYi && owner.IsAlive &&
        id > 0 && _cardZones.GetLocation(id) == CardLocation.Hand(owner.Seat) && MoSimaYi.IsOwnMarked(owner.Seat, id);

    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 33900)]
    private string? MoGuifuEntityLabel(int id) => HasMoSimaYi && MoSimaYi.IsMarked(id) ? "诡伏" : null;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 33901, SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge)]
    private bool CanAcquireMoGuifu(ProgramTriggerLegalityContext gate) =>
        gate.Context.Window != SkillProgramTriggerWindow.AfterHealthChanged ||
        gate.Context.HpChange is { } change && change.TargetSeat == gate.Candidate.OwnerSeat && change.HpBefore != change.HpAfter;

    private bool HasMoGuifuNativeUse(int actor, int id, CardKind kind) => _resolutionStack.OfType<CardUseFrame>().Any(use =>
        use.Action is { Type: CardActionType.Use } action && action.ActorSeat == actor && action.PhysicalCards is [var physical] &&
        physical.CardId == id && action.EffectiveKind == kind && action.ConversionChain.Any(IsMoGuifuSource) &&
        ProgramEventHistory<MoSimaYiNameUsedEvent>().Any(e => e.ActionId == action.ActionId && e.ActorSeat == actor && e.Kind == kind));

    private bool MoGuifuMaterial(CharacterState owner, int id, CardKind kind) => HasMoSimaYi && id > 0 &&
        HasRuntimeSkill(owner, MoSimaYiComposition.Guifu) && MoSimaYi.IsOwnMarked(owner.Seat, id) &&
        (_cardZones.GetLocation(id) == CardLocation.Hand(owner.Seat) && MoSimaYi.NameAvailable(owner.Seat, kind) || HasMoGuifuNativeUse(owner.Seat, id, kind));

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 33900)]
    private bool MoGuifuConversionUnavailable(ProgramConfiguredViewAsQuery q)
    {
        if (q.Instance.SkillId != MoSimaYiComposition.Guifu) return false;
        if (!q.Owner.IsAlive || !HasRuntimeSkillInstance(q.Owner, q.Instance.SkillId, q.Instance.SkillInstanceId) ||
            !MoGuifuMaterial(q.Owner, q.Card.Id, q.Rule.OutputKind) || !IsOwnedPlayableLocation(q.Owner, _cardZones.GetLocation(q.Card.Id)) ||
            FulePureProviderFrontier(q.Owner.Seat)) return true;
        if (q.RequestedUseTarget is not null)
            return q.Rule.OutputKind != CardKind.Peach || q.ForResponse || q.DyingUse || !IsWenyiPeachSupplyQuery(q.Owner, q.RequestedUseTarget.Value);
        if (q.DyingUse || q.ForResponse && q.Rule.OutputKind is CardKind.Peach or CardKind.Alcohol)
            return ActiveDying is not { } dying || dying.ResponderSeat != q.Owner.Seat ||
                q.Rule.OutputKind == CardKind.Alcohol && dying.VictimSeat != q.Owner.Seat;
        if (!q.ForResponse) return false;
        if (IsSlashCard(q.Rule.OutputKind)) return !FuleTrueSlashUseFrontier(q.Owner);
        return q.Rule.OutputKind != CardKind.Nullification &&
            !(q.Rule.OutputKind == CardKind.Dodge && IsProgramResponseCardUse(q.Owner, CardKind.Dodge));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33901)]
    private void CaptureMoGuifuActualName(IGameEvent fact)
    {
        if (!HasMoSimaYi) return;
        var action = fact switch
        {
            CardUseDeclaredEvent d => LifecycleCardUse(d.ResolutionId)?.Action,
            CardActionAcceptedEvent accepted => accepted.Action,
            _ => null
        };
        if (action is not { Type: CardActionType.Use } || !action.ConversionChain.Any(IsMoGuifuSource) ||
            ProgramEventHistory<MoSimaYiNameUsedEvent>().Any(e => e.ActionId == action.ActionId)) return;
        if (action.PhysicalCards is not [var physical] || !MoSimaYi.IsOwnMarked(action.ActorSeat, physical.CardId) ||
            !MoSimaYi.NameAvailable(action.ActorSeat, action.EffectiveKind))
            throw new InvalidOperationException("Guifu must consume one marked entity and one unused recorded name of its actual user.");
        AdvanceEventRulesAndQueueFact(new MoSimaYiNameUsedEvent(action.ActionId, action.ActorSeat, _turnNumber, action.EffectiveKind));
    }

    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 33900)]
    private bool MoGuifuSlashQuota(ProgramSlashQuotaQuery q) => q.PhysicalCard is { } card && MoGuifuMaterial(q.Actor, card.Id, q.Kind) ||
        q.PhysicalCardIds is [var id] && MoGuifuMaterial(q.Actor, id, q.Kind);

    [ProgramTargetRule(ProgramTargetRulePoint.MaterialPrivilegeAllowance, 33900)]
    private bool MoGuifuAlcoholPrivilege(ProgramNativeUseMaterialQuery q) => q.Kind == CardKind.Alcohol &&
        q.PhysicalCardIds is [var id] && IsValidPlayerSeat(q.ActorSeat) &&
        (q.Source is null || IsMoGuifuSource(q.Source) && q.Source.OwnerSeat == q.ActorSeat &&
            q.Source.BindingId == "recorded-" + q.Kind &&
            HasRuntimeSkillInstance(_players[q.ActorSeat], q.Source.SkillId, q.Source.SkillInstanceId)) &&
        MoGuifuMaterial(_players[q.ActorSeat], id, q.Kind);

    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 33900)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 33900)]
    private bool MoGuifuSlashDebitExempt(long useId, int actor) => LifecycleCardUse(useId)?.Action is { Type: CardActionType.Use } a &&
        a.ActorSeat == actor && a.ConversionChain.Any(IsMoGuifuSource) &&
        ProgramEventHistory<MoSimaYiNameUsedEvent>().Any(e => e.ActionId == a.ActionId && e.ActorSeat == actor);


    private IReadOnlyList<SlashUseVariant> MoGuifuPhysicalSlashVariants(CharacterState owner, Card card) => SlashKinds
        .Where(kind => GetProgramViewAsConversions(owner, card, kind, false).Any(s => IsMoGuifuSource(s) && ViewAsRule(s)!.OutputKind == kind))
        .Select(kind => new SlashUseVariant(kind, null, false)).ToArray();
    private bool MoGuifuNativeMatchesOutput(CharacterState owner, CardConversionSource s, CardKind kind) =>
        !IsMoGuifuSource(s) || !FuleTrueSlashUseFrontier(owner) || ViewAsRule(s)!.OutputKind == kind;
    private bool MoGuifuUnspecifiedForcedSlash(CharacterState owner, Card card, CardKind kind) =>
        !IsSlashCard(kind) || !FuleTrueSlashUseFrontier(owner) || IsNativeResponseCard(card, kind) || MoGuifuPhysicalSlashVariants(owner, card).Count == 0;
    private CardKind MoGuifuSelectedSlashKind(CharacterState owner, Card card, CardKind fallback, CardConversionSource? explicitSource = null)
    {
        var source = explicitSource ?? _selectedUseConversion ?? _selectedResponseConversion;
        return source is not null && IsMoGuifuSource(source) && ViewAsRule(source) is { } rule && IsSlashCard(rule.OutputKind) &&
            GetProgramViewAsConversions(owner, card, rule.OutputKind, false).Contains(source) ? rule.OutputKind : FuleSelectedSlashKind(owner, card, fallback, explicitSource);
    }
    private CardKind MoGuifuAiSlashKind(PendingDecision d, Card card, CardKind fallback)
    {
        var choice = d.Choices.FirstOrDefault(c => c.Cards is [var id] && id == card.Id &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == MoSimaYiComposition.Guifu);
        return choice is not null && Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("response-card-kind"), out var kind)
            ? kind : FuleForcedAiSlashKind(d, card, fallback);
    }

    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 33900)]
    private void AddMoGuifuNativeActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (!HasMoSimaYi) return;
        foreach (var card in playable.Where(c => selectedPhysicalCardId is null || c.Id == selectedPhysicalCardId))
        {
            if (IsTurnHandCardRestricted(actor, card)) continue;
            foreach (var kind in SlashKinds)
            foreach (var source in GetProgramViewAsConversions(actor, card, kind, false).Where(s => IsMoGuifuSource(s) && ViewAsRule(s)!.OutputKind == kind))
            {
                actions.RemoveAll(a => a.CardId == card.Id && a.ConversionSource == source);
                foreach (var variant in GetSlashUseVariants(actor, card, kind, source))
                {
                    var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: variant.EffectiveKind);
                    IReadOnlyList<CardConversionSource>? additional = variant.AdditionalConversionSource is { } chained ? [chained] : null;
                    foreach (var target in targets) actions.Add(new(LegalActionKind.Slash, card.Id, target.Seat,
                        DescribeConversion(source, $"诡伏：{CardCatalog.Get(variant.EffectiveKind).DisplayName}"), variant.EffectiveKind)
                        { ConversionSource = source, AdditionalConversionSources = additional });
                    AddFangtianHalberdSlashActions(actions, actor, card, targets, "诡伏", variant.EffectiveKind, source, additional);
                    AddProgramTargetCountSlashActions(actions, actor, card, targets, "诡伏", variant.EffectiveKind, source, additional);
                }
            }
            foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.Lightning, false).Where(IsMoGuifuSource))
                if (!actor.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(actor, CardKind.Lightning) &&
                    !IsCardUseForbidden(actor.Seat, CardKind.Lightning, CardActionType.Use) && !IsSelfTargetForbiddenAction(actor, CardKind.Lightning, [actor.Seat]))
                    actions.Add(new(LegalActionKind.Lightning, card.Id, actor.Seat, "诡伏：闪电", CardKind.Lightning) { ConversionSource = source });

        }
    }

    private IReadOnlyList<ActivePlayerEffectSnapshot>? AppendMoGuifuRecords(CharacterState owner, IReadOnlyList<ActivePlayerEffectSnapshot>? existing)
    {
        if (!HasMoSimaYi || MoSimaYi.Records(owner.Seat) is not { Count: > 0 } records) return existing;
        var names = records.Select(r => r.CardKind is { } kind ? CardCatalog.Get(kind).DisplayName : _contentRegistry.GetSkill(r.SkillId!).Name);
        return Array.AsReadOnly((existing ?? []).Append(new ActivePlayerEffectSnapshot(MoSimaYiComposition.Guifu,
            "诡伏·记录", string.Join("、", names), owner.Seat, ActivePlayerEffectExpiry.RuleCleared)).ToArray());
    }
}
