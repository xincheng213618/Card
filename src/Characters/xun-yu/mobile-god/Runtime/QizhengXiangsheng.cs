using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum QizhengStage { Designation, Response, PaidResponse, Gain, Damage, Finishing }
public sealed record QizhengReceipt(long NullificationId, long ActionId, int Target, int TargetIndex, int EffectOrdinal, QizhengStage Stage)
{
    public bool Ambush { get; init; }
    public string Nonce { get; init; } = "";
    public string Commitment { get; init; } = "";
    public CardKind? ResponseKind { get; init; }
    public long ResponseActionId { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QizhengReceipt? Qizheng { get; init; }
}
public sealed record QizhengEffectStartedEvent(long UseId, long NullificationId, long ActionId, int Target, int TargetIndex, int EffectOrdinal) : IGameEvent;
public sealed record QizhengSecretCommittedEvent(long UseId, long NullificationId, string Commitment) : IGameEvent;
public sealed record QizhengResponsePaidEvent(long UseId, long NullificationId, CardKind Kind) : IGameEvent;
public sealed record QizhengResolvedEvent(long UseId, long NullificationId, int Target, bool Ambush, CardKind? ResponseKind) : IGameEvent;
[ProgramRuntimeModule] internal static class QizhengRoutes
{ internal static bool HasReceipt(CardUseFrame f) => f.Qizheng is not null; }

public sealed partial class GameEngine
{
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 30300)]
    private void AddQizhengNaturalActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        foreach (var card in playable.Where(card => card.Kind == CardKind.QizhengXiangsheng && (selectedPhysicalCardId is null || selectedPhysicalCardId == card.Id) && !HasProgramCardIdentity(actor, card)))
        foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, CardKind.QizhengXiangsheng, EffectiveSuit(actor, card), enforceUsePermission: true, physicalCardIds: [card.Id]))
            actions.Add(new(option.ActionKind, card.Id, option.TargetSeats.Single(), option.Description, TargetSeats: option.TargetSeats));
    }
    private bool TryExecuteQizhengNaturalUse(CharacterState actor, Card card, LegalAction action)
    {
        if (card.Kind != CardKind.QizhengXiangsheng || action.Kind != LegalActionKind.QizhengXiangsheng || action.ConversionSource is not null) return false;
        if (!BuildLegalActions(actor, selectedPhysicalCardId: card.Id).Any(a => a.Kind == action.Kind && a.CardId == card.Id && a.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("Qizheng lost its current legal designation.");
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats);
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, LifecycleCardUse(id)!.TargetSeats, LegalActionKind.QizhengXiangsheng);
        return true;
    }
    private void ResolveQizhengXiangshengEffect(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { Action: { Type: CardActionType.Use } action, Qizheng: null } use || use.Id != pending.ParentFrameId ||
            LeChengyanNativeEffectKind(use) != CardKind.QizhengXiangsheng || pending.ActionKind != LegalActionKind.QizhengXiangsheng || GetNullificationTargetSeat(pending) is not { } target)
            throw new InvalidOperationException("Qizheng requires its original native ordinary-trick return.");
        if (!_players[target].IsAlive)
        { FinishCardUse(use.Id, GetNullificationEffectCard(pending), CardKind.QizhengXiangsheng); return; }
        var r = new QizhengReceipt(pending.Id, action.ActionId, target, use.TargetIndex, NativeExtraEffectRoundOrdinal(use.Id), QizhengStage.Designation);
        ReplaceRuntimeTop(use = use with { Qizheng = r, Step = ResolutionFrameStep.AwaitingResponse });
        AdvanceEventRulesAndQueueFact(new QizhengEffectStartedEvent(use.Id, r.NullificationId, r.ActionId, target, r.TargetIndex, r.EffectOrdinal));
        if (!IsExtraCardEffectRound(use.Id)) AdvanceEventRulesAndQueueFact(new CardUsedEvent(use.CardId, CardKind.QizhengXiangsheng, use.SourceSeat, target));
        PublishQizheng(use);
    }
    private static string QizhengCommitment(long use, long nullification, bool ambush, string nonce) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{use}:{nullification}:{ambush}:{nonce}")));
    private Dictionary<string,string> QizhengParameters(string branch, string value = "") => new() { ["program-action"] = "qizheng", ["branch"] = branch, ["value"] = value };
    private IReadOnlyList<PromptChoice> QizhengChoices(CardUseFrame use)
    {
        var r = use.Qizheng!; var choices = new List<PromptChoice>(); var responder = _players[r.Target];
        string Id(string branch) => $"qizheng.{use.Id}.{r.NullificationId}.{branch}";
        if (r.Stage == QizhengStage.Designation)
        {
            choices.Add(new(new(Id("ambush")), "暗中指定奇兵", [], [r.Target], QizhengParameters("designation", "ambush")));
            choices.Add(new(new(Id("regular")), "暗中指定正兵", [], [r.Target], QizhengParameters("designation", "regular")));
        }
        else if (r.Stage == QizhengStage.Response)
        {
            if (!IsWenjiResponseForbidden(use.Id, responder.Seat))
            foreach (var required in new[] { CardKind.Slash, CardKind.Dodge })
            {
                foreach (var card in GetResponseCards(responder, required).Where(card => CanUseCardAsResponse(responder, card, required)))
                {
                    var kind = GetEffectiveResponseKind(responder, card, required);
                    var parameters = QizhengParameters("response", kind.ToString());
                    parameters["response-card-kind"] = kind.ToString(); parameters["response"] = required == CardKind.Slash ? "slash" : "dodge";
                    choices.AddRange(CreateConversionChoiceVariants(responder, card, kind, true, Id($"respond.{kind}.{card.Id}"), $"打出【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [], parameters));
                }
                foreach (var selection in GetProgramMultiCardViewAsSelections(responder, required, true))
                {
                    var parameters = QizhengParameters("multi", required.ToString());
                    AddConversionParameters(parameters, selection.Source);
                    choices.Add(new(new(Id($"multi.{selection.Source.SkillId}.{selection.Source.BindingId}.{string.Join('-',selection.Cards.Select(c=>c.Id))}")), $"将{selection.Cards.Count}张牌当【{CardCatalog.Get(required).DisplayName}】打出", selection.Cards.Select(c => c.Id).ToArray(), [], parameters));
                }
                if (required == CardKind.Slash)
                    foreach (var pair in GetZhangbaSlashPairs(responder))
                        choices.Add(new(new(Id($"zhangba.{string.Join('-', pair.Select(c => c.Id))}")), "发动丈八蛇矛，将两张手牌当杀打出", pair.Select(c => c.Id).ToArray(), [], QizhengParameters("zhangba", CardKind.Slash.ToString())));
            }
            choices.Add(new(new(Id("pass")), "不打出牌", [], [], QizhengParameters("pass")));
        }
        else if (r.Stage == QizhengStage.Gain)
        {
            foreach (var slot in Enumerable.Range(0, GetHand(responder).Count))
                choices.Add(new(new(Id($"hand.{slot}")), $"获得一张手牌（牌位{slot+1}）", [], [r.Target], QizhengParameters("hand", slot.ToString(CultureInfo.InvariantCulture))));
            foreach (var card in GetEquipment(responder).Where(c => !IsForeignEquipmentDiscardPrevented(use.SourceSeat, c, CardLocation.Equipment(r.Target), OwnedCardMoveIntent.Obtain)))
                choices.Add(new(new(Id($"equipment.{card.Id}")), $"获得装备【{ToSnapshot(card).DisplayName}】", [card.Id], [r.Target], QizhengParameters("equipment", card.Id.ToString(CultureInfo.InvariantCulture))));
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishQizheng(CardUseFrame use)
    {
        var r = use.Qizheng!; var choices = QizhengChoices(use);
        if (choices.Count == 0) { FinishQizhengWithoutDamage(use); return; }
        var seat = r.Stage == QizhengStage.Response ? r.Target : use.SourceSeat;
        ReplaceRuntimeTop(use with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, r.Stage == QizhengStage.Designation ? "【奇正相生】请暗中指定奇兵或正兵。" : r.Stage == QizhengStage.Response ? "【奇正相生】你可以打出一张杀或闪，或不打出牌。" : "【奇正相生】请选择获得的一张牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [r.Target], use.SourceSeat, CardKind.QizhengXiangsheng)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, TargetSeat = r.Target };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private bool HasQizhengPrompt() => _resolutionStack.LastOrDefault() is CardUseFrame { Qizheng.Stage: QizhengStage.Designation or QizhengStage.Response or QizhengStage.Gain } && _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IncomingCard: CardKind.QizhengXiangsheng };
    private bool HasQizhengResponseContinuation(CardAttackHandle attack) =>
        _resolutionStack.LastOrDefault() is CardUseFrame { Qizheng: { Stage: QizhengStage.Response } r, Step: ResolutionFrameStep.AwaitingResponse } use &&
        use.Id == attack.ResolutionId && LeChengyanNativeEffectKind(use) == CardKind.QizhengXiangsheng && attack.EffectiveCardKind == CardKind.QizhengXiangsheng &&
        attack.SourceSeat == use.SourceSeat && attack.CardUserSeat == use.SourceSeat && attack.TargetSeat == r.Target && !attack.DamageRedirected && !attack.IsChainPropagation &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IncomingCard: CardKind.QizhengXiangsheng, IsPrivate: true } prompt &&
        prompt.PlayerSeat == r.Target && prompt.SourceSeat == use.SourceSeat && prompt.TargetSeat == r.Target &&
        AssistedChoicesEqual(prompt.Choices, QizhengChoices(use));
    [ProgramAnswer("qizheng")]
    private void AnswerQizheng(PromptChoice selected)
    {
        if (!HasQizhengPrompt() || _resolutionStack.LastOrDefault() is not CardUseFrame { Qizheng: { } r } use ||
            !AssistedChoicesEqual([selected], QizhengChoices(use).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Qizheng requires its current participant's exact private choice.");
        AssertQizheng(use); ClearPendingDecision(); var branch = selected.Parameters["branch"];
        if (r.Stage == QizhengStage.Designation)
        {
            var ambush = selected.Parameters["value"] == "ambush";
            var nonce = $"{_random.Next(int.MaxValue):X8}{_random.Next(int.MaxValue):X8}";
            var commitment = QizhengCommitment(use.Id, r.NullificationId, ambush, nonce);
            ReplaceRuntimeTop(use = use with { Qizheng = r with { Stage = QizhengStage.Response, Ambush = ambush, Nonce = nonce, Commitment = commitment } });
            AdvanceEventRulesAndQueueFact(new QizhengSecretCommittedEvent(use.Id, r.NullificationId, commitment));
            var card = GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true);
            var attack = new CardAttackHandle(this, use.Id, use.SourceSeat, r.Target, card, playedCardKind: CardKind.QizhengXiangsheng, physicalCards: GetCardUsePhysicalCards(use.Id)); ActiveCardAttack = attack;
            PublishQizheng(LifecycleCardUse(use.Id)!); return;
        }
        if (r.Stage == QizhengStage.Gain)
        {
            var target = _players[r.Target]; var card = branch == "hand" ? GetHand(target)[int.Parse(selected.Parameters["value"], CultureInfo.InvariantCulture)] : GetEquipment(target).Single(c => c.Id == int.Parse(selected.Parameters["value"], CultureInfo.InvariantCulture));
            var from = FindOwnedCardLocation(target, card);
            MoveCard(card, from, CardLocation.Hand(use.SourceSeat), new("card.effect.qizheng.obtain"), disclosure: from.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
            FinishQizhengWithoutDamage(use); return;
        }
        if (branch == "pass") { ResolveQizhengAfterResponse(use.Id); return; }
        var responder = _players[r.Target]; var kind = Enum.Parse<CardKind>(selected.Parameters["value"]);
        CaptureSelectedResponseConversion(selected);
        if (branch == "zhangba")
        {
            var pair = FindZhangbaSlashPair(responder, selected.Cards) ?? throw new InvalidOperationException("Qizheng lost its exact native Zhangba response materials.");
            var costs = pair.Select(card => new CardActionCost(card.Id, card.Kind, FindOwnedCardLocation(responder, card), CapturePhysicalCardColor(responder.Seat, card))).ToArray();
            ReplaceRuntimeTop(use = use with { Qizheng = r with { Stage = QizhengStage.PaidResponse, ResponseKind = kind } });
            AdvanceEventRulesAndQueueFact(new QizhengResponsePaidEvent(use.Id, r.NullificationId, kind));
            MoveZhangbaResponseCards(responder, pair, use.Id, use.SourceSeat); MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
            var attack = new CardAttackHandle(this, use.Id);
            if (TryBeginCardResponsePrograms(attack, responder, responder, null, use.SourceSeat, kind, costs, ProgramCardContinuation.QizhengResponse)) return;
            FinishZhangbaResponseCards(pair); ResolveQizhengAfterResponse(use.Id); return;
        }
        if (branch == "multi")
        {
            if (!TryReadConversionSource(selected.Parameters, out var source) || source is null) throw new InvalidOperationException("Qizheng multi-response lost its exact conversion.");
            var selection = FindProgramMultiCardViewAsSelection(responder, selected.Cards, kind, true, source)!;
            ReplaceRuntimeTop(use = use with { Qizheng = r with { Stage = QizhengStage.PaidResponse, ResponseKind = kind } });
            AdvanceEventRulesAndQueueFact(new QizhengResponsePaidEvent(use.Id, r.NullificationId, kind));
            var action = MoveProgramMultiCardResponse(responder, selection, use.Id, use.SourceSeat);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, kind);
            UpdateLifecycleCardUse(use.Id, f => f with { Qizheng = f.Qizheng! with { ResponseActionId = action.ActionId } });
            var attack = new CardAttackHandle(this, use.Id);
            if (TryBeginCommittedResponseUsePrograms(attack, action, ProgramCardContinuation.QizhengResponse) || TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [use.SourceSeat], ProgramCardContinuation.QizhengResponse)) return;
            ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.QizhengResponse); return;
        }
        var physical = GetResponseCards(responder, IsSlashCard(kind) ? CardKind.Slash : kind).Single(c => c.Id == selected.Cards.Single());
        if (TryBeginCardDeclaration(responder, physical, kind, PeekDeclarationConversion(responder, physical, kind, false), DeclarationReturn(CardDeclarationPurpose.QizhengResponse, responder.Seat, finalKind: kind), [use.SourceSeat])) return;
        PayQizhengSingleResponse(use.Id, responder, physical, kind);
    }
    private void PayQizhengSingleResponse(long id, CharacterState responder, Card card, CardKind kind)
    {
        var use = LifecycleCardUse(id)!; var r = use.Qizheng!;
        var conversion = GetSelectedResponseConversion(responder, card, kind);
        PrepareNativePublicPileResponsePayment(responder, card, kind, conversion);
        var cost = new CardActionCost(card.Id, card.Kind, FindOwnedCardLocation(responder, card), CapturePhysicalCardColor(responder.Seat, card));
        var suit = FreezeCompletedResponseUseSuit(responder, [card], kind);
        ReplaceRuntimeTop(use with { Qizheng = r with { Stage = QizhengStage.PaidResponse, ResponseKind = kind } });
        AdvanceEventRulesAndQueueFact(new QizhengResponsePaidEvent(id, r.NullificationId, kind));
        PaySingleCardResponse(card, responder, kind, conversion);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, kind);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(card.Id, responder.Seat, use.SourceSeat, kind));
        var attack = new CardAttackHandle(this, id);
        if (TryBeginCardResponsePrograms(attack, responder, responder, null, use.SourceSeat, kind, [cost], ProgramCardContinuation.QizhengResponse, conversion, suit)) return;
        FinishSingleCardResponse(card, conversion); ResolveQizhengAfterResponse(id);
    }
    private bool TryContinueQizhengResponse(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (continuation != ProgramCardContinuation.QizhengResponse) return false;
        if (LifecycleCardUse(attack.ResolutionId) is not { Qizheng: { Stage: QizhengStage.PaidResponse } r } use || action.ActorSeat != r.Target || action.OpponentSeat != use.SourceSeat || action.EffectiveKind != r.ResponseKind || action.ParentActionId != use.Action?.ActionId)
            throw new InvalidOperationException("Qizheng response lost its original action parent.");
        ResolveQizhengAfterResponse(use.Id); return true;
    }
    private void ResolveQizhengAfterResponse(long id)
    {
        var use = LifecycleCardUse(id)!; var r = use.Qizheng!;
        if (TryBeginMobileSimaRenjieQizheng(use)) return;
        AdvanceEventRulesAndQueueFact(new QizhengResolvedEvent(id, r.NullificationId, r.Target, r.Ambush, r.ResponseKind));
        var answered = r.Ambush ? r.ResponseKind is { } kind && IsSlashCard(kind) : r.ResponseKind == CardKind.Dodge;
        if (answered || !_players[r.Target].IsAlive || !r.Ambush && !_players[use.SourceSeat].IsAlive) { FinishQizhengWithoutDamage(use); return; }
        if (!r.Ambush)
        {
            ActiveCardAttack = null;
            ReplaceRuntimeTop(use = LifecycleCardUse(id)! with { Qizheng = r with { Stage = QizhengStage.Gain } }); PublishQizheng(use); return;
        }
        ReplaceRuntimeTop(use with { Qizheng = r with { Stage = QizhengStage.Damage } });
        var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }
    private void FinishQizhengWithoutDamage(CardUseFrame supplied)
    {
        var use = LifecycleCardUse(supplied.Id)!; ActiveCardAttack = null;
        UpdateLifecycleCardUse(use.Id, f => f with { Qizheng = null });
        var card = GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true);
        MoveFinishedTrickCard(use.Id, card); FinishCardUse(use.Id, card, CardKind.QizhengXiangsheng);
    }
    private bool TryCompleteQizhengAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { Qizheng: { } r } use) return false;
        if (r.Stage != QizhengStage.Damage) throw new InvalidOperationException("Qizheng completed before its exact damage return.");
        UpdateLifecycleCardUse(use.Id, f => f with { Qizheng = null }); return false;
    }
    [NativeFrameRoute(typeof(CardUseFrame), 51, typeof(QizhengRoutes), nameof(QizhengRoutes.HasReceipt))]
    private void ContinueQizheng(long id)
    { if (LifecycleCardUse(id) is { Qizheng.Stage: QizhengStage.Designation or QizhengStage.Response or QizhengStage.Gain } use) PublishQizheng(use); }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 30300)]
    private bool AdvanceQizhengAi()
    {
        if (!HasQizhengPrompt() || _players[_pendingDecision!.PlayerSeat].IsHuman) return false;
        var choices = _pendingDecision.Choices;
        var choice = _resolutionStack.LastOrDefault() is CardUseFrame { Qizheng.Stage: QizhengStage.Designation }
            ? choices[_random.Next(choices.Count)] : choices.FirstOrDefault(c => c.Parameters["branch"] != "pass") ?? choices[0];
        AnswerQizheng(choice); return true;
    }
    private bool HasQizhengProcessing(IReadOnlyList<Card> processing) => _resolutionStack.OfType<CardUseFrame>().Any(use => use.Qizheng is not null && processing.All(c => use.PhysicalCardIds?.Contains(c.Id) == true || use.Action?.PhysicalCards.Any(p => p.CardId == c.Id) == true));
    private void AssertQizheng(CardUseFrame use)
    {
        if (use.Qizheng is not { } r) return;
        if (LeChengyanNativeEffectKind(use) != CardKind.QizhengXiangsheng || use.Action?.ActionId != r.ActionId || !IsValidPlayerSeat(r.Target) ||
            r.TargetIndex != use.TargetIndex || r.EffectOrdinal != NativeExtraEffectRoundOrdinal(use.Id) || LeChengyanNativeTargets(use)[use.TargetIndex] != r.Target ||
            ProgramEventHistory<QizhengEffectStartedEvent>().Count(e => e == new QizhengEffectStartedEvent(use.Id, r.NullificationId, r.ActionId, r.Target, r.TargetIndex, r.EffectOrdinal)) != 1)
            throw new InvalidOperationException("Qizheng lost its original typed native effect receipt.");
        if (r.Stage != QizhengStage.Designation && (r.Nonce.Length != 16 || r.Commitment != QizhengCommitment(use.Id, r.NullificationId, r.Ambush, r.Nonce) ||
            ProgramEventHistory<QizhengSecretCommittedEvent>().Count(e => e.UseId == use.Id && e.NullificationId == r.NullificationId && e.Commitment == r.Commitment) != 1))
            throw new InvalidOperationException("Qizheng's private designation no longer matches its issued commitment.");
        if (r.ResponseKind is { } kind && (kind != CardKind.Dodge && !IsSlashCard(kind) || ProgramEventHistory<QizhengResponsePaidEvent>().Count(e => e.UseId == use.Id && e.NullificationId == r.NullificationId && e.Kind == kind) != 1))
            throw new InvalidOperationException("Qizheng lost its actual chosen response kind.");
    }
}
