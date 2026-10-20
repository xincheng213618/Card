using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private PromptChoice MouDengAiChoice(ProgramSkillFrame f, string branch, string label, int? card = null, ProgramOrdinaryTrickUseOption? option = null) => new(
        new($"mou-deng-ai.{f.Id}.{f.MouDengAiOl!.Stage}.{branch}.{card}.{option?.Id.Value}"), label, card is { } id ? [id] : [], option?.TargetSeats ?? [],
        new Dictionary<string, string> { ["program-action"] = "mou-deng-ai-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["option-id"] = option?.Id.Value ?? "" });
    private IReadOnlyList<PromptChoice> MouDengAiChoices(ProgramSkillFrame f)
    {
        var r = f.MouDengAiOl!; var owner = _players[f.OwnerSeat]; var gu = MouDengAiGu(f.OwnerSeat); var list = new List<PromptChoice>();
        if (r.Stage == MouDengAiOlStage.ExchangeHand)
        {
            if (r.HandCards.Count < gu.Length) list.AddRange(GetHand(owner).Where(c => !r.HandCards.Contains(c.Id)).Select(c => MouDengAiChoice(f, "hand", $"选择手牌【{c.DisplayName}】", c.Id)));
            list.Add(MouDengAiChoice(f, "confirm", r.HandCards.Count == 0 ? "不交换" : $"交换{r.HandCards.Count}张")); return Array.AsReadOnly(list.ToArray());
        }
        if (r.Stage is MouDengAiOlStage.ExchangeGu or MouDengAiOlStage.RemoveGu) return Array.AsReadOnly(gu.Where(c => !r.GuCards.Contains(c.Id)).Select(c =>
            MouDengAiChoice(f, "gu", r.Stage == MouDengAiOlStage.RemoveGu ? $"移出游戏【{c.DisplayName}】" : $"取回谷【{c.DisplayName}】", c.Id)).ToArray());
        if (r.Stage == MouDengAiOlStage.Cost)
        {
            if (owner.MaxHp > 1) list.Add(MouDengAiChoice(f, "maxhp", "减少1点体力上限，使用顺手牵羊"));
            if (gu.Length >= 2) list.Add(MouDengAiChoice(f, "remove", "移出游戏两张谷，使用顺手牵羊"));
            list.Add(MouDengAiChoice(f, "decline", "不发动解腕")); return Array.AsReadOnly(list.ToArray());
        }
        if (r.Stage == MouDengAiOlStage.Material) return Array.AsReadOnly(GetHand(owner).Where(c => MouDengAiSnatchOptions(f, c.Id).Count > 0).Select(c => MouDengAiChoice(f, "material", $"将【{c.DisplayName}】当顺手牵羊", c.Id)).ToArray());
        if (r.Stage == MouDengAiOlStage.Snatch) return Array.AsReadOnly(MouDengAiSnatchOptions(f, r.CardId).Select(o => MouDengAiChoice(f, "snatch", o.Description, option: o)).ToArray());
        return [];
    }
    private void PublishMouDengAiOl(ProgramSkillFrame f)
    {
        var choices = MouDengAiChoices(f); if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive) { FinishMouDengAiOl(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-deng-ai-ol")]
    private void AnswerMouDengAiOl(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDengAiOl: { } r } f || !ValidMouDengAiOl(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !MouDengAiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Mou Deng Ai requires its current owner and exact live option.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch == "decline" || branch == "confirm" && r.HandCards.Count == 0) { FinishMouDengAiOl(f); return; }
        if (branch == "hand")
        { ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { HandCards = r.HandCards.Append(choice.Cards.Single()).ToArray() } }); PublishMouDengAiOl(f); return; }
        if (branch == "confirm")
        { ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.ExchangeGu } }); PublishMouDengAiOl(f); return; }
        if (branch == "gu")
        {
            var ids = r.GuCards.Append(choice.Cards.Single()).ToArray(); ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { GuCards = ids } });
            if (ids.Length < (r.Stage == MouDengAiOlStage.RemoveGu ? 2 : r.HandCards.Count)) { PublishMouDengAiOl(f); return; }
            if (r.Stage == MouDengAiOlStage.ExchangeGu) ConfirmMouDengAiExchange(f); else PayMouDengAiGu(f); return;
        }
        if (branch == "remove")
        { ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.RemoveGu, GuCards = [] } }); PublishMouDengAiOl(f); return; }
        if (branch == "maxhp")
        {
            if (_players[f.OwnerSeat].MaxHp <= 1 || GetHand(_players[f.OwnerSeat]).Count == 0) throw new InvalidOperationException("Jiewan must pay a real reducible maximum HP with a current Hand material.");
            ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.CostChildren, Paid = true, Before = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            ChangeProgramMaximumHp(f, -1); AdvanceEventRulesAndQueueFact(new MouDengAiCostPaidEvent(f.Id, "maximum-hp", 0, _movementSequence, _players[f.OwnerSeat].MaxHp));
            SealMouDengAiMovement(f.Id); ContinueMouDengAiOl(f.Id); return;
        }
        if (branch == "material")
        {
            var id = choice.Cards.Single();
            if (_cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat) || MouDengAiSnatchOptions(f, id).Count == 0) throw new InvalidOperationException("Jiewan lost its legal current Hand conversion.");
            ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.Snatch, CardId = id } }); PublishMouDengAiOl(f); return;
        }
        if (branch == "snatch") { IssueMouDengAiSnatch(f, MouDengAiSnatchOptions(f, r.CardId).Single(o => o.Id.Value == choice.Parameters["option-id"])); return; }
        throw new InvalidOperationException("Unknown Mou Deng Ai choice.");
    }
    private void PayMouDengAiGu(ProgramSkillFrame f)
    {
        var r = f.MouDengAiOl!;
        if (r.GuCards.Count != 2 || r.GuCards.Distinct().Count() != 2 || r.GuCards.Any(id => !MouDengAiIsGu(f.OwnerSeat, id)) || GetHand(_players[f.OwnerSeat]).Count == 0) throw new InvalidOperationException("Jiewan must remove exactly two current Gu entities.");
        var groups = r.GuCards.GroupBy(id => _cardZones.GetLocation(id)).Select(g => (Location: g.Key, Cards: g.Select(GetAdvancedCard).ToArray())).ToArray();
        ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Paid = true, Stage = MouDengAiOlStage.CostChildren, Before = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        foreach (var g in groups) MoveCards(g.Cards, g.Location, CardLocation.OutsideGame, new(MouDengAiRemoveReason), disclosure: CardMovementDisclosure.FaceUp);
        foreach (var id in r.GuCards) AdvanceEventRulesAndQueueFact(new MouDengAiCostPaidEvent(f.Id, "remove-gu", id, _cardMovements.Last(m => m.CardId == id).Sequence, _players[f.OwnerSeat].MaxHp));
        SealMouDengAiMovement(f.Id); ContinueMouDengAiOl(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.CollectMouDengAiJigu, SkillProgramEffectOp.ExchangeMouDengAiJigu, SkillProgramEffectOp.UseMouDengAiJiewan, SkillProgramEffectOp.GrowMouDengAiJiewan)]
    private PromptChoice AiMouDengAiOl(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.MouDengAiOl!; var choices = decision.Choices!; var owner = _players[f.OwnerSeat];
        if (r.Stage == MouDengAiOlStage.ExchangeHand)
        {
            var card = choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), owner)).FirstOrDefault();
            return r.HandCards.Count == 0 && card is not null && MouDengAiGu(f.OwnerSeat).Any(g => GetKeepValue(g, owner) > GetKeepValue(GetAdvancedCard(card.Cards[0]), owner)) ? card : choices.Single(c => c.Parameters["branch"] == "confirm");
        }
        if (r.Stage == MouDengAiOlStage.Cost) return choices.FirstOrDefault(c => c.Parameters["branch"] == "remove") ?? choices.First();
        if (r.Stage == MouDengAiOlStage.ExchangeGu) return choices.OrderByDescending(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), owner)).First();
        if (r.Stage is MouDengAiOlStage.RemoveGu or MouDengAiOlStage.Material) return choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), owner)).First();
        return choices.OrderBy(c => c.Targets.Count > 0 && AreProgramDistributionAllies(owner, _players[c.Targets[0]])).First();
    }
    private bool DrainMouDengAiOl(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void SealMouDengAiMovement(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MouDengAiOl = f.MouDengAiOl! with { After = _movementSequence } }); }
    private void ContinueMouDengAiOl(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDengAiOl: { } r } f || f.Id != id) return;
        if (!ValidMouDengAiOl(f)) throw new InvalidOperationException("Mou Deng Ai lost its signed source or native continuation.");
        if (r.Stage is MouDengAiOlStage.Issuing or MouDengAiOlStage.NativeUse) return;
        if (r.Stage is MouDengAiOlStage.CostChildren or MouDengAiOlStage.Children)
        {
            if (DrainMouDengAiOl(id)) return; f = GetActiveProgramFrame(id); r = f.MouDengAiOl!;
            if (r.Stage == MouDengAiOlStage.Children || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishMouDengAiOl(f); return; }
            var count = MouDengAiGu(f.OwnerSeat).Length; var x = Math.Max(1, count);
            ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Distance = x, Stage = MouDengAiOlStage.Material }, PendingMovementContinuation = null });
            AdvanceEventRulesAndQueueFact(new MouDengAiDistanceFrozenEvent(f.Id, count, x));
        }
        PublishMouDengAiOl(f);
    }
    private void FinishMouDengAiOl(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouDengAiCompletedEvent(f.Id, f.MouDengAiOl!.Operation));
        ReplaceRuntimeTop(f = f with { MouDengAiOl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33400)] private bool ResumeMouDengAiOl(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDengAiOl: not null } f || f.Id != id) return false;
        ContinueMouDengAiOl(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33400)] private bool ReturnMouDengAiMovement(ProgramSkillFrame f) => f.MouDengAiOl is not null && ResumeMouDengAiOl(f.Id);
}
