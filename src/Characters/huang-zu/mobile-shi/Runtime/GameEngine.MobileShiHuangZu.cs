using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiHzChizhang = "mobile:shi-chizhang", ShiHzDuanyang = "mobile:shi-duanyang";
    private static CardConversionSource ShiHzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiHz => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiHuangZuStore);
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 47900)]
    private bool ShiHzFinalizedTrickWindow(ProgramTargetCardWindowQuery q) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang) && q.Category == SkillProgramCardCategory.Trick;
    [ProgramCapability] private sealed class ShiHuangZuHost(GameEngine e) : IShiHuangZuHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShiHz(f, op); }

    private CardUseFrame? ShiHzHandDamage(ProgramSkillWindowContext c, int owner) =>
        c.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && c.CardUse is { } identity &&
        LifecycleCardUse(identity.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } a } use &&
        use.CardKind != CardKind.Lightning && IsDamageCardKind(use.CardKind) && a.ActionId == identity.CardActionId &&
        a.ActorSeat == owner && a.ProviderSeat == owner && a.PhysicalCards.Count > 0 &&
        a.PhysicalCards.All(m => m.From == CardLocation.Hand(owner)) ? use : null;

    // This is a destination occurrence, not a generic hand-loss occurrence.
    // Resolve a Processing intermediary to its immediately preceding hand entry,
    // and distinguish true Use (including Dodge/Nullification) from merely played cards.
    private CardMovementRecord[] ShiHzNonUseHandSlashes(ProgramSkillWindowContext? c, int owner)
    {
        return c?.MovementBatch is { } batch ? ShiHzNonUseHandSlashes(batch, owner) : [];
    }
    private CardMovementRecord[] ShiHzNonUseHandSlashes(CardMovementBatchContext batch, int owner)
    {
        if (batch.TurnNumber != _turnNumber) return [];
        return batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To && IsSlashCard(m.CardKind) &&
            _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile && !IsNativeTrueUseDiscardMovement(batch, m) &&
            (m.From == CardLocation.Hand(owner) || m.From == CardLocation.Processing &&
             _cardMovements.LastOrDefault(p => p.CardId == m.CardId && p.Sequence < m.Sequence) is { } previous &&
             previous.To == CardLocation.Processing && previous.From == CardLocation.Hand(owner)))
            .DistinctBy(m => m.CardId).OrderBy(m => m.Sequence).ToArray();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 47900)]
    private int[]? ShiHzDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate c, ProgramInstructionFeatures features)
    {
        if (!features.HasOperation(SkillProgramEffectOp.ResolveMobileShiHuangZuStore)) return null;
        var eligible = ShiHzNonUseHandSlashes(batch, c.OwnerSeat).Select(m => m.Sequence).ToHashSet();
        return batch.Movements.Select((m, i) => (m, i)).Where(x => eligible.Contains(x.m.Sequence)).Select(x => x.i).ToArray();
    }
    private ShiHzStoredEvent? ShiHzGrant(long id) => ProgramEventHistory<ShiHzStoredEvent>().SingleOrDefault(e => e.GrantId == id);
    private bool ShiHzStoredAvailable(ShiHzStoredEvent g) => _players[g.Source.OwnerSeat].IsAlive &&
        g.GameplayHash == _contentRegistry.GetSkill(ShiHzDuanyang).Program!.GameplayHash &&
        _publicPersistentPiles.GetValueOrDefault((g.Pile.OwnerSeat, g.Pile.SkillId, g.Pile.SkillInstanceId)) == g.Pile &&
        _cardZones.GetLocation(g.CardId) == g.Pile.Location &&
        !ProgramEventHistory<ShiHzGrantConsumedEvent>().Any(e => e.GrantId == g.GrantId);
    private bool IsIssuedShiHzPile(PublicPersistentPileSource source) => HasShiHz && _players[source.OwnerSeat].IsAlive &&
        ProgramEventHistory<ShiHzStoredEvent>().Any(g => g.Pile == source && g.Phase.TurnNumber == _turnNumber &&
            (!ProgramEventHistory<ShiHzGrantConsumedEvent>().Any(e => e.GrantId == g.GrantId) ||
             _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ShiHuangZu?.GrantId == g.GrantId)));
    private IReadOnlyList<ActualPhaseEndItem> CollectShiHzPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase)
    {
        if (!HasShiHz) return [];
        var trigger = _contentRegistry.GetSkill(ShiHzDuanyang).Program!.Triggers.Single(t => t.Id == "use-current-phase-end");
        return Array.AsReadOnly(ProgramEventHistory<ShiHzStoredEvent>().Where(g => g.Phase == phase && ShiHzStoredAvailable(g)).OrderBy(g => g.GrantId)
            .Select((g, i) => new ActualPhaseEndItem(new(g.Source.OwnerSeat, ShiHzDuanyang, trigger.Id, g.Source.SkillInstanceId,
                g.GameplayHash, trigger.Priority, i), g.GrantId)).ToArray());
    }
    private bool ShiHzIssued(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == ShiHzDuanyang && c.BindingId == "use-current-phase-end" && context.ActualPhaseEnd is { } due &&
        IsExactActualPhaseEndCandidate(c, context) && ShiHzGrant(due.EntitlementId) is { } g &&
        g.Source.OwnerSeat == c.OwnerSeat && g.Source.SkillInstanceId == c.SkillInstanceId && g.GameplayHash == c.GameplayHash &&
        g.Phase == due.Phase && (ShiHzStoredAvailable(g) || _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
            f.WindowContext?.ParentFrameId == context.ParentFrameId && f.OwnerSeat == c.OwnerSeat && f.SkillId == c.SkillId &&
            f.SkillInstanceId == c.SkillInstanceId && f.GameplayHash == c.GameplayHash && f.ShiHuangZu is { } r &&
            r.GrantId == g.GrantId && r.Source == g.Source && r.CardId == g.CardId &&
            ProgramEventHistory<ShiHzGrantConsumedEvent>().Count(e => e.GrantId == g.GrantId && e.FrameId == f.Id) == 1));
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 47900)]
    private bool IsIssuedShiHz(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => ShiHzIssued(c, context);

    private CardUseFrame? ShiHzDamageUse(ProgramSkillWindowContext context, int owner)
    {
        if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.SourceSeat != owner || context.Amount <= 0 ||
            context.DamageFrameId is not { } id || _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == id) is not { } damage ||
            damage.SourceSeat != owner || damage.TargetSeat != context.TargetSeat ||
            LifecycleCardUse(damage.ParentFrameId) is not { ShiHuangZuUse: { } n } use || n.Source.OwnerSeat != owner ||
            use.Action?.ActionId != n.ActionId || !IsSlashCard(use.CardKind)) return null;
        return use;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47900)]
    private bool CanOfferShiHz(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (c.SkillId == ShiHzChizhang) return ShiHzHandDamage(context, c.OwnerSeat) is not null;
        if (c.SkillId != ShiHzDuanyang) return true;
        return t.Id switch
        {
            "store-non-use-slash" => CurrentActualDiscardRecoveryPhase() is not null && ShiHzNonUseHandSlashes(context, c.OwnerSeat).Length > 0,
            "use-current-phase-end" => ShiHzIssued(c, context),
            "damage-recast" => ShiHzDamageUse(context, c.OwnerSeat) is not null,
            _ => false
        };
    }

    private SkillProgramStepOutcome BeginShiHz(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.WindowContext is not { } context || f.ShiHuangZu is not null)
            throw new InvalidOperationException("Shi Huang Zu requires his exact original unpaid trigger instruction.");
        if (op == SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang)
        {
            var use = ShiHzHandDamage(context, f.OwnerSeat) ?? throw new InvalidOperationException("Chizhang lost the actual hand damage Use.");
            var cost = GetProgramCardSet(f, "chizhang-payment");
            if (cost.CardIds.Count != cost.SourceLocations.Count || cost.SourceLocations.Any(l => l != CardLocation.Hand(f.OwnerSeat)) ||
                cost.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat)))
                throw new InvalidOperationException("Chizhang requires its original selected hand cost before its native movement children.");
            var colors = cost.CardIds.Select(id => SuitColor(EffectiveSuit(_players[f.OwnerSeat], GetAdvancedCard(id)))).ToArray();
            var grant = new ShiHzColorsBoundEvent(use.Id, use.Action!.ActionId, f.Id, f.OwnerSeat, colors.Contains(true), colors.Contains(false));
            UpdateLifecycleCardUse(use.Id, u => u with { ShiHuangZuColors = grant });
            AdvanceEventRulesAndQueueFact(grant); return SkillProgramStepOutcome.Continue;
        }
        var stage = op == SkillProgramEffectOp.ResolveMobileShiHuangZuStore ? ShiHzStage.StoreChildren :
            op == SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse ? ShiHzStage.UseChoice : ShiHzStage.RecastChoice;
        var r = new ShiHzReceipt(ShiHzSource(f), f.GameplayHash, _turnNumber, context.ParentFrameId, stage);
        if (stage == ShiHzStage.StoreChildren)
        {
            var candidates = ShiHzNonUseHandSlashes(context, f.OwnerSeat);
            if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
            var phase = CurrentActualDiscardRecoveryPhase() ?? throw new InvalidOperationException("Duanyang lost its actual phase identity.");
            var before = _random.State; var index = _random.Next(candidates.Length); var chosen = candidates[index];
            var pile = EnsurePublicPileSource(f, 0);
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { GrantId = f.Id, CardId = chosen.CardId, Before = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new ShiHzStoredEvent(f.Id, r.Source, r.GameplayHash, phase, context.MovementBatch!.Id,
                chosen.CardId, chosen.Sequence, pile, before, _random.State, candidates.Length, index));
            MoveCard(GetAdvancedCard(chosen.CardId), CardLocation.DiscardPile, pile.Location, new("skill-program.mobile-shi-huang-zu.store"));
            SealShiHz(f.Id); ContinueShiHz(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (stage == ShiHzStage.UseChoice)
        {
            var g = ShiHzGrant(context.ActualPhaseEnd!.EntitlementId) ?? throw new InvalidOperationException("Duanyang lost its issued current-phase card.");
            r = r with { Source = g.Source, GrantId = g.GrantId, CardId = g.CardId };
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r });
            AdvanceEventRulesAndQueueFact(new ShiHzGrantConsumedEvent(g.GrantId, f.Id));
            if (ShiHzTargets(f).Count == 0) { CleanupShiHzStored(f); return SkillProgramStepOutcome.AwaitChild; }
        }
        else
        {
            var use = ShiHzDamageUse(context, f.OwnerSeat) ?? throw new InvalidOperationException("Duanyang recast needs damage from its exact original stored Slash.");
            r = r with { Target = context.TargetSeat!.Value, UseId = use.Id, ActionId = use.Action!.ActionId };
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r });
        }
        PublishShiHz(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<int> ShiHzTargets(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!; var g = ShiHzGrant(r.GrantId);
        if (g is null || !_players[f.OwnerSeat].IsAlive || _cardZones.GetLocation(r.CardId) != g.Pile.Location || _winner != Winner.None) return [];
        var owner = _players[f.OwnerSeat]; var card = GetAdvancedCard(r.CardId); var kind = card.Kind;
        if (!IsSlashCard(kind) || IsCardUseForbidden(owner.Seat, kind, CardActionType.Use) || IsTurnPhysicalUseForbidden(owner.Seat, [card.Id]) ||
            !TargetRules.AllowsNativeUseMaterial(new(owner.Seat, kind, [card.Id]))) return [];
        return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != owner.Seat &&
            CanUseSlashTarget(owner, p, card, effectiveKind: kind, physicalCardIds: [card.Id])).Select(p => p.Seat).ToArray());
    }
    private IReadOnlyList<PromptChoice> ShiHzChoices(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!;
        PromptChoice C(string branch, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null, Dictionary<string,string>? extra = null) =>
            new(new($"shi-huang-zu.{f.Id}.{r.Stage}.{branch}.{extra?.GetValueOrDefault("source-zone") ?? "none"}.{extra?.GetValueOrDefault("slot-index") ?? string.Join("-", cards ?? [])}.{string.Join("-", targets ?? [])}"),
                label, cards ?? [], targets ?? [], new Dictionary<string,string>(extra ?? []) { ["program-action"] = "shi-huang-zu", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == ShiHzStage.UseChoice)
            return Array.AsReadOnly(ShiHzTargets(f).Select(seat => C("use", "使用武将牌上的【" + GetAdvancedCard(r.CardId).DisplayName + "】对 " + _players[seat].Name, [r.CardId], [seat])).ToArray());
        if (r.Stage != ShiHzStage.RecastChoice) return [];
        var choices = new List<PromptChoice>();
        if (r.Selected.Count < 2 && _players[r.Target].IsAlive)
        {
            foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment })
            {
                var cards = _cardZones.CardsAt(new(zone, r.Target));
                for (var i = 0; i < cards.Count; i++)
                {
                    var card = cards[i]; if (r.Selected.Any(s => s.CardId == card.Id)) continue;
                    var opaque = zone == CardZoneKind.Hand && f.OwnerSeat != r.Target;
                    var label = opaque ? $"选择 {_players[r.Target].Name} 的第{i+1}张暗手牌" : $"选择{(zone == CardZoneKind.Judgment ? "判定区" : zone == CardZoneKind.Equipment ? "装备区" : "手牌")}【{card.DisplayName}】";
                    choices.Add(C("card", label, opaque ? [] : [card.Id], extra: new() { ["source-zone"] = zone.ToString(), ["slot-index"] = i.ToString(CultureInfo.InvariantCulture) }));
                }
            }
        }
        choices.Add(C("finish", $"重铸已选 {r.Selected.Count}/2 张，然后摸四张牌"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishShiHz(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!; var choices = ShiHzChoices(f);
        if (choices.Count == 0) { FinishShiHz(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage == ShiHzStage.UseChoice ? "断鞅：选择目标，使用储存的原实体【杀】（不计次数）。" : "断鞅：选择受伤角色区域内至多两张牌，完成后同时重铸，再摸四张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = r.Stage == ShiHzStage.RecastChoice, TargetSeat = r.Stage == ShiHzStage.RecastChoice ? r.Target : f.OwnerSeat,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("shi-huang-zu")]
    private void AnswerShiHz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHuangZu: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiHzChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Duanyang lost its original native target or opaque owned card slot.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (r.Stage == ShiHzStage.UseChoice)
        {
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = ShiHzStage.IssuingUse, Target = choice.Targets.Single() } });
            IssueShiHzNativeUse(f); return;
        }
        if (branch == "card")
        {
            var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var from = new CardLocation(zone, r.Target);
            var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)];
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Selected = r.Selected.Append(new NativeRecastMaterial(card.Id, card.Kind, from, card.IsGeneralWeapon)).ToArray() } });
            PublishShiHz(f); return;
        }
        if (branch != "finish" || r.Selected.Count > 2 || r.Selected.Any(m => _cardZones.GetLocation(m.CardId) != m.From))
            throw new InvalidOperationException("Duanyang recast changed its selected original HEJ entities.");
        if (r.Selected.Count == 0) { IssueShiHzReward(f); return; }
        f = BeginFrameNativeRecastInvocation(f, r.Target, r.Selected.Select(m => m.CardId).ToArray());
        ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = ShiHzStage.RecastCostChildren, Before = _movementSequence }, PendingMovementContinuation = new(r.Target, 0, null) });
        MoveProgramCardsFromMultipleSources(r.Selected.Select(m => m.CardId).ToArray(), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        SealShiHz(f.Id); f = GetActiveProgramFrame(f.Id);
        foreach (var m in r.Selected)
            AdvanceEventRulesAndQueueFact(new ShiHzRecastPaidEvent(f.Id, r.Target, m.CardId, m.From,
                _cardMovements.First(move => move.Sequence > f.ShiHuangZu!.Before && move.CardId == m.CardId && move.From == m.From && move.Reason == CardMoveReasons.RecastDiscard).Sequence));
        ContinueShiHz(f.Id);
    }
    private void SealShiHz(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeFrame(id, f with { ShiHuangZu = f.ShiHuangZu! with { After = _movementSequence } }); }
    private bool DrainShiHz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueShiHz(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHuangZu: { } r } f || f.Id != id || _pendingDecision is not null) return;
        if (r.Stage is ShiHzStage.UseChoice or ShiHzStage.RecastChoice) { PublishShiHz(f); return; }
        if (r.Stage is ShiHzStage.IssuingUse or ShiHzStage.NativeUse or ShiHzStage.RecastDrawing or ShiHzStage.RewardDrawing) return;
        if (DrainShiHz(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.ShiHuangZu!;
        if (r.Stage == ShiHzStage.RecastCostChildren)
        {
            if (!_players[r.Target].IsAlive || _winner != Winner.None)
            {
                for (var i = 0; i < r.Selected.Count; i++) AdvanceEventRulesAndQueueFact(new CardRecastEvent(r.Target, r.Selected[i].CardId, r.Selected[i].PrintedKind, 0));
                f = CompleteFrameNativeRecastInvocation(f, 0); IssueShiHzReward(f); return;
            }
            ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = ShiHzStage.RecastDrawing, Draw = null, Before = _movementSequence, After = _movementSequence } });
            BeginOperationProgramDraw(f, r.Target, r.Selected.Count, CardMoveReasons.RecastDraw); return;
        }
        if (r.Stage == ShiHzStage.RecastDrawChildren) { IssueShiHzReward(f); return; }
        FinishShiHz(f);
    }
    private void IssueShiHzReward(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShiHz(f); return; }
        ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = ShiHzStage.RewardDrawing, Draw = null, Before = _movementSequence, After = _movementSequence } });
        BeginOperationProgramDraw(f, f.OwnerSeat, 4, new("skill-program.mobile-shi-huang-zu.reward"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiHuangZuRecast)]
    private void ReturnShiHzDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.ShiHuangZu is not { Stage: ShiHzStage.RecastDrawing or ShiHzStage.RewardDrawing, Draw: null } r ||
            outcome.RecipientSeat != (r.Stage == ShiHzStage.RecastDrawing ? r.Target : f.OwnerSeat) ||
            outcome.RequestedCount != (r.Stage == ShiHzStage.RecastDrawing ? r.Selected.Count : 4))
            throw new InvalidOperationException("Duanyang lost the exact once-issued recast or four-card native draw.");
        var recast = r.Stage == ShiHzStage.RecastDrawing; var actual = outcome.ActualDrawIds.Count;
        ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = recast ? ShiHzStage.RecastDrawChildren : ShiHzStage.RewardChildren,
            Draw = outcome, ActualDraw = actual, Before = checked((int)outcome.SequenceBefore), After = checked((int)outcome.SequenceAfter) }, PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        if (recast)
        {
            for (var i = 0; i < r.Selected.Count; i++) AdvanceEventRulesAndQueueFact(new CardRecastEvent(r.Target, r.Selected[i].CardId, r.Selected[i].PrintedKind, i < actual ? 1 : 0));
            CompleteFrameNativeRecastInvocation(f, actual);
        }
        else AdvanceEventRulesAndQueueFact(new ShiHzRewardIssuedEvent(f.Id, f.OwnerSeat, actual));
        ContinueShiHz(f.Id);
    }
    private void FinishShiHz(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { ShiHuangZu = f.ShiHuangZu! with { Stage = ShiHzStage.Finished }, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    private void CleanupShiHzStored(ProgramSkillFrame f)
    {
        var r = f.ShiHuangZu!; var g = ShiHzGrant(r.GrantId);
        if (g is null || _cardZones.GetLocation(r.CardId) != g.Pile.Location) { FinishShiHz(f); return; }
        ReplaceRuntimeTop(f = f with { ShiHuangZu = r with { Stage = ShiHzStage.CleanupChildren, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(r.CardId), g.Pile.Location, CardLocation.DiscardPile, new("skill-program.mobile-shi-huang-zu.expired"));
        SealShiHz(f.Id); ContinueShiHz(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 47900)] private bool ResumeShiHz(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiHuangZu: not null } f || f.Id != id) return false; ContinueShiHz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47900)] private bool ReturnShiHzMovement(ProgramSkillFrame f)
    { if (f.ShiHuangZu is null) return false; ContinueShiHz(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47900)]
    private bool PaidShiHzContinues(ProgramSkillFrame f) => f.ShiHuangZu is { Stage: not (ShiHzStage.Finished or ShiHzStage.RecastChoice) };
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47900)]
    private bool ShiHzMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        f.ShiHuangZu is { Stage: ShiHzStage.StoreChildren or ShiHzStage.RecastCostChildren or ShiHzStage.RecastDrawChildren or ShiHzStage.RewardChildren or ShiHzStage.ReturnedUse or ShiHzStage.CleanupChildren } &&
        movement.BeforeCount == 0 && movement.CoverageResultBind is null && effect?.Op is SkillProgramEffectOp.ResolveMobileShiHuangZuStore or SkillProgramEffectOp.ResolveMobileShiHuangZuRecast or SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse, SkillProgramEffectOp.ResolveMobileShiHuangZuRecast)]
    private PromptChoice ShiHzAi(PendingDecision p, ProgramSkillFrame f) => f.ShiHuangZu!.Stage == ShiHzStage.UseChoice ?
        p.Choices.OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])).First() :
        p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "card") ?? p.Choices.Last();

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47900)]
    private void AssertShiHzFrame(ProgramSkillFrame f)
    {
        if (f.ShiHuangZu is not { } r) return;
        if (f.SkillId != ShiHzDuanyang || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            r.WindowId != context.ParentFrameId || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Count > 2 ||
            r.Selected.Select(m => m.CardId).Distinct().Count() != r.Selected.Count ||
            r.Selected.Any(m => m.From.OwnerSeat != r.Target || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException("Duanyang changed its exact source, actual phase, original HEJ materials or finite paid interval.");
        if (f.TriggerId == "store-non-use-slash" && (r.Source != ShiHzSource(f) || ShiHzGrant(f.Id) is not { } stored ||
            stored.CardId != r.CardId || stored.Source != r.Source || stored.GameplayHash != r.GameplayHash || stored.BatchId != context.MovementBatch?.Id ||
            stored.Phase.TurnNumber != r.ActualTurn || stored.CandidateCount < 1 || stored.CandidateIndex < 0 || stored.CandidateIndex >= stored.CandidateCount ||
            !_cardMovements.Any(m => m.CardId == r.CardId && m.From == CardLocation.DiscardPile && m.To == stored.Pile.Location && m.Sequence > r.Before && m.Sequence <= r.After)))
            throw new InvalidOperationException("Duanyang lost its random once-paid original discard-to-general movement.");
        if (f.TriggerId == "use-current-phase-end" && (context.ActualPhaseEnd is not { } due || ShiHzGrant(r.GrantId) is not { } g ||
            due.EntitlementId != g.GrantId || due.Phase != g.Phase || r.Source != g.Source || r.CardId != g.CardId ||
            ProgramEventHistory<ShiHzGrantConsumedEvent>().Count(e => e.GrantId == g.GrantId && e.FrameId == f.Id) != 1))
            throw new InvalidOperationException("Duanyang cannot replace its exact already-issued current-phase entitlement.");
        if (f.TriggerId == "damage-recast" && (r.Source != ShiHzSource(f) || context.TargetSeat != r.Target || context.Amount < 1 ||
            ShiHzDamageUse(context, f.OwnerSeat) is not { } use || use.Id != r.UseId || use.Action?.ActionId != r.ActionId))
            throw new InvalidOperationException("Duanyang recast must remain on the original actual damage and native stored Slash.");
        if (r.Stage == ShiHzStage.RecastChoice && r.Selected.Any(m => _cardZones.GetLocation(m.CardId) != m.From))
            throw new InvalidOperationException("Duanyang cannot select a stale original victim card.");
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiHuangZuPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != ShiHzDuanyang) return [];
        return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "store-non-use-slash", ProgramPublicRuleStateKind.ActivationLimit, 1,
                _skillRuntimeState.GetUsage(owner.Seat, skillId, "store-non-use-slash@" + i.SkillInstanceId, SkillUsageScope.Turn), SkillUsageScope.Turn),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "stored-current-phase", ProgramPublicRuleStateKind.GameActivationProgress,
                ProgramEventHistory<ShiHzStoredEvent>().Count(g => g.Source.OwnerSeat == owner.Seat && g.Source.SkillInstanceId == i.SkillInstanceId && ShiHzStoredAvailable(g)))
        }).ToArray());
    }
}
