using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int[] DagongLegalTargets(int owner, bool ignoreDistance) => CurrentTableOrder().Select(s => _players[s]).Where(t =>
        CanUseVirtualSlashTarget(_players[owner], t, CardKind.Slash, Suit.None, effectiveColor: false, effectiveRank: 0, ignoreDistance: ignoreDistance, countsTowardLimit: false) &&
        !IsDirectedCardTargetProhibited(owner, t.Seat, CardKind.Slash) && !IsCardTargetProhibited(t, CardKind.Slash, Suit.None, false)).Select(t => t.Seat).ToArray();
    private static IEnumerable<int[]> DagongTargetSets(int[] legal, int max)
    {
        for (var bits = 1; bits < (1 << legal.Length); bits++)
        {
            var set = legal.Where((_, i) => (bits & (1 << i)) != 0).ToArray();
            if (set.Length <= max) yield return set;
        }
    }
    private PromptChoice ZhangFenChoice(ProgramSkillFrame f, string option, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var p = new Dictionary<string, string> { ["program-action"] = "anniversary-zhang-fen", ["option"] = option, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        if (extra is not null) foreach (var kv in extra) p.Add(kv.Key, kv.Value);
        return new(new($"zhang-fen.{f.Id}.{f.ZhangFen!.Stage}.{f.ZhangFen.Paid}.{option}"), label, cards ?? [], targets ?? [], p);
    }
    private IReadOnlyList<PromptChoice> ZhangFenChoices(ProgramSkillFrame f)
    {
        var r = f.ZhangFen!; var result = new List<PromptChoice>();
        if (r.Stage == ZhangFenStage.Upgrade)
        {
            if (HeldDagongChe(f.OwnerSeat)?.Id != r.CartId || DagongUpgrades(r.CartId).Length >= 5) return [];
            if (!DagongUpgrades(r.CartId).Any(e => e.Choice == "range")) result.Add(ZhangFenChoice(f, "range", "升级：此车的杀无视距离和防具"));
            result.Add(ZhangFenChoice(f, "target", "升级：此车的杀目标数+1"));
            result.Add(ZhangFenChoice(f, "discard", "升级：命中后的弃牌数+1"));
        }
        else if (r.Stage == ZhangFenStage.Targets)
        {
            foreach (var targets in DagongTargetSets(DagongLegalTargets(f.OwnerSeat, r.IgnoreRangeArmor), r.Count))
                result.Add(ZhangFenChoice(f, "targets:" + string.Join(',', targets), "视为对" + string.Join('、', targets.Select(s => _players[s].Name)) + "使用杀", targets: targets));
        }
        else if (r.Stage == ZhangFenStage.DiscardChoice && _players[r.TargetSeat].IsAlive && r.Paid < r.Count)
        {
            foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
            {
                var from = new CardLocation(zone, r.TargetSeat); var cards = _cardZones.CardsAt(from);
                for (var slot = 0; slot < cards.Count; slot++)
                {
                    var card = cards[slot];
                    if (IsForeignEquipmentDiscardPrevented(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard)) continue;
                    result.Add(ZhangFenChoice(f, $"discard:{zone}:{slot}", $"弃置{_players[r.TargetSeat].Name}{(zone == CardZoneKind.Hand ? "手牌" : "装备区")}第{slot + 1}张牌",
                        zone == CardZoneKind.Hand ? [] : [card.Id], [r.TargetSeat], new Dictionary<string, string> { ["source-zone"] = zone.ToString(), ["slot-index"] = slot.ToString(CultureInfo.InvariantCulture), ["card-owner-seat"] = r.TargetSeat.ToString(CultureInfo.InvariantCulture) }));
                }
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishZhangFen(ProgramSkillFrame f)
    {
        var choices = ZhangFenChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishZhangFen(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.ZhangFen!.Stage == ZhangFenStage.Upgrade ? "陷筑：选择本车的升级项目。" :
            f.ZhangFen.Stage == ZhangFenStage.Targets ? "大攻车：选择此杀的合法目标。" : "大攻车：选择受伤角色区域内的牌弃置。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = f.ZhangFen.Stage == ZhangFenStage.DiscardChoice, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-zhang-fen")] private void AnswerZhangFen(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangFen: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ZhangFenChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Zhang Fen rejected a changed cart, upgrade, native target set or opaque hand slot.");
        RequireZhangFen(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == ZhangFenStage.Upgrade)
        {
            var n = DagongUpgrades(r.CartId).Length + 1;
            AdvanceEventRulesAndQueueFact(new DagongCheUpgradedEvent(f.Id, r.Source, r.Hash, r.CartId, n, selected.Parameters["option"]));
            FinishZhangFen(f); return;
        }
        if (r.Stage == ZhangFenStage.Targets) { IssueDagongCheSlash(f, selected.Targets); return; }
        var zone = Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]); var slot = int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var from = new CardLocation(zone, r.TargetSeat); var card = _cardZones.CardsAt(from)[slot];
        if (IsForeignEquipmentDiscardPrevented(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard)) throw new InvalidOperationException("The selected field entity became non-discardable.");
        ReplaceRuntimeTop(f = f with { ZhangFen = r with { Stage = ZhangFenStage.DiscardChildren, CardId = card.Id, From = from, To = CardLocation.DiscardPile,
            Paid = r.Paid + 1, Before = _movementSequence, After = _movementSequence, MoveSequence = 0 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var invoice = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat, [card.Id], new("anniversary.dagong-che.discard"), f.Id);
        MoveCard(card, from, CardLocation.DiscardPile, new("anniversary.dagong-che.discard"), moved =>
        {
            var parent = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(parent with { ZhangFen = parent.ZhangFen! with { After = moved.Sequence, MoveSequence = moved.Sequence, To = moved.To } });
        }, disclosure: CardMovementDisclosure.FaceUp);
        CompleteNativeLogicalDiscardInvocation(invoice);
        ContinueZhangFen(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu, SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu,
        SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack, SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie)]
    private PromptChoice ZhangFenAi(PendingDecision p) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "range") ?? p.Choices[0];
}
