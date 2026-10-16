using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XingGuStoreReason = "skill-program.zhang-shi-ping.xinggu.initial";
    private const string XingGuDiamondReason = "skill-program.zhang-shi-ping.xinggu.diamond";
    private static bool IsXingGuMount(Card card) => EquipmentCatalog.IsEquipment(card.Kind) &&
        EquipmentCatalog.Get(card.Kind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse;
    private IReadOnlyList<PromptChoice> XingGuPlacementChoices(int owner, string instance)
    {
        if (!_players[owner].IsAlive || _publicPersistentPiles.GetValueOrDefault((owner, "ol:xinggu", instance)) is not { } pile) return [];
        var result = new List<PromptChoice>();
        foreach (var card in PublicPileCards(pile).Where(IsXingGuMount).OrderBy(c => c.Id))
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != owner))
                if (CanPlaceZhangShiPingMount(owner, target.Seat, card, pile.Location))
                    result.Add(new(new($"xinggu.{owner}.{instance}.{card.Id}.{target.Seat}"), $"将【{card.DisplayName}】置于{target.Name}的装备区", [card.Id], [target.Seat],
                        new Dictionary<string, string> { ["program-action"] = "zhang-shi-ping-xinggu", ["card"] = card.Id.ToString(CultureInfo.InvariantCulture) }));
        return Array.AsReadOnly(result.ToArray());
    }
    private SkillProgramStepOutcome BeginXingGu(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:xinggu" || f.InstructionIndex != 1 || f.ZhangShiPingXingGu is not null || f.WindowContext is not { } context ||
            operation is not (SkillProgramEffectOp.InitializeXingGuMountPile or SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond) ||
            context.Window != (operation == SkillProgramEffectOp.InitializeXingGuMountPile ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.TurnEnding))
            throw new InvalidOperationException("Xinggu requires its exact zero-input game-start or own ending binding.");
        var initial = operation == SkillProgramEffectOp.InitializeXingGuMountPile;
        var pile = initial ? EnsurePublicPileSource(f, 3) : _publicPersistentPiles.GetValueOrDefault((f.OwnerSeat, f.SkillId, f.SkillInstanceId))
            ?? throw new InvalidOperationException("Xinggu lost its exact public mount pile source.");
        if (initial && ProgramEventHistory<ZhangShiPingXingGuStartedEvent>().Any(e => e.Source == ZhangShiPingSource(f) && e.Initial))
            throw new InvalidOperationException("Xinggu cannot initialize its public mount pile twice.");
        ReplaceRuntimeTop(f = f with { ZhangShiPingXingGu = new(ZhangShiPingSource(f), f.GameplayHash, f.InstructionIndex, context.ParentFrameId,
            _turnNumber, initial ? XingGuStage.InitialChildren : XingGuStage.Choose, pile.Location, _movementSequence, _movementSequence) });
        AdvanceEventRulesAndQueueFact(new ZhangShiPingXingGuStartedEvent(f.Id, ZhangShiPingSource(f), f.GameplayHash, _turnNumber, context.ParentFrameId, initial, pile.Location));
        if (initial)
        {
            for (var count = 0; count < 3; count++)
            {
                var candidates = _cardZones.CardsAt(CardLocation.DrawPile).Where(IsXingGuMount).OrderBy(c => c.Id).ToArray();
                if (candidates.Length == 0) break;
                var card = candidates[_random.Next(candidates.Length)];
                MoveCard(card, CardLocation.DrawPile, pile.Location, new(XingGuStoreReason), movement =>
                {
                    var current = GetActiveProgramFrame(f.Id);
                    ReplaceRuntimeTop(current with { ZhangShiPingXingGu = current.ZhangShiPingXingGu! with { SequenceAfter = movement.Sequence } });
                    AdvanceEventRulesAndQueueFact(new ZhangShiPingXingGuCardStoredEvent(f.Id, card.Id, movement.Sequence));
                });
            }
        }
        ContinueXingGu(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void PublishXingGu(ProgramSkillFrame f)
    {
        var choices = XingGuPlacementChoices(f.OwnerSeat, f.SkillInstanceId);
        if (choices.Count == 0) { FinishXingGu(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "行贾：将一张坐骑牌置于其他角色的装备区。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("zhang-shi-ping-xinggu")]
    private void AnswerXingGu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangShiPingXingGu: { Stage: XingGuStage.Choose } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], XingGuPlacementChoices(f.OwnerSeat, f.SkillInstanceId).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Xinggu lost its exact physical public mount or legal other recipient.");
        AssertZhangShiPing(f); ClearPendingDecision();
        var card = GetAdvancedCard(choice.Cards.Single()); var target = choice.Targets.Single(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, ZhangShiPingXingGu = r with
            { Stage = XingGuStage.PlacementChildren, CardId = card.Id, RecipientSeat = target, SequenceBefore = before, SequenceAfter = before } });
        AdvanceEventRulesAndQueueFact(new ZhangShiPingXingGuPlacementIssuedEvent(f.Id, card.Id, target, before));
        PlaceZhangShiPingMount(f, target, card, r.PileLocation);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhangShiPingXingGu = f.ZhangShiPingXingGu! with { SequenceAfter = _movementSequence } });
        ContinueXingGu(f.Id);
    }
    private bool DrainXingGu(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueXingGu(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangShiPingXingGu: { } r } f && f.Id == id)
        {
            AssertZhangShiPing(f);
            if (DrainXingGu(id)) return;
            f = GetActiveProgramFrame(id); r = f.ZhangShiPingXingGu!;
            if (r.Stage is XingGuStage.InitialChildren or XingGuStage.DiamondChildren || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
            { FinishXingGu(f); return; }
            if (r.Stage == XingGuStage.Choose) { PublishXingGu(f); return; }
            if (r.Stage != XingGuStage.PlacementChildren) throw new InvalidOperationException("Xinggu encountered an unknown paid continuation.");
            var before = _movementSequence;
            var candidates = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => c.Suit == Suit.Diamond).OrderBy(c => c.Id).ToArray();
            var card = candidates.Length == 0 ? null : candidates[_random.Next(candidates.Length)];
            ReplaceRuntimeTop(f = f with { ZhangShiPingXingGu = r with { Stage = XingGuStage.DiamondChildren, DiamondCardId = card?.Id,
                SequenceBefore = before, SequenceAfter = before } });
            if (card is not null)
                MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(XingGuDiamondReason), movement =>
                {
                    var current = GetActiveProgramFrame(id);
                    ReplaceRuntimeTop(current with { ZhangShiPingXingGu = current.ZhangShiPingXingGu! with { SequenceAfter = movement.Sequence } });
                });
            f = GetActiveProgramFrame(id); r = f.ZhangShiPingXingGu!;
            AdvanceEventRulesAndQueueFact(new ZhangShiPingXingGuDiamondIssuedEvent(id, f.OwnerSeat, card?.Id, before, r.SequenceAfter));
        }
    }
    private void FinishXingGu(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ZhangShiPingXingGu = null }); FinishProgramSkill(f, completed: true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26610)]
    private bool ResumeXingGu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangShiPingXingGu: not null } f || f.Id != id) return false; ContinueXingGu(id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond)]
    private PromptChoice AiXingGu(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderByDescending(c =>
        (AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]) ? 10 : 0) +
        (GetEquipment(_players[c.Targets.Single()]).Any(e => EquipmentCatalog.Get(e.Kind).Slot == EquipmentCatalog.Get(GetAdvancedCard(c.Cards.Single()).Kind).Slot) ? 0 : 2)).First();
}
