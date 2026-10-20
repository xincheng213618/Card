using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record TousuiRequestReceipt(long OwnerFrameId, int InstructionIndex, SkillProgramEffectOp? RequestOp, int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash, PendingDecision OriginalDecision)
{
    private PendingDecision _original = RequestedDeckBasicFrame.FreezeDecision(OriginalDecision);
    public PendingDecision OriginalDecision { get => _original; init => _original = RequestedDeckBasicFrame.FreezeDecision(value); }

    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardConversionSource? RequestSource { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestGameplayHash { get; init; }
}

public sealed record TousuiRequestStartedEvent(long OwnerFrameId, int InstructionIndex, SkillProgramEffectOp? RequestOp, int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash, PromptId OriginalPromptId, long OriginalRevision, string OriginalDecisionHash) : IGameEvent;
public sealed record TousuiRequestMaterialSelectedEvent(long OwnerFrameId, PromptId OriginalPromptId, int ActorSeat, int Index, int CardId, CardLocation From) : IGameEvent;
public sealed record TousuiRequestFinishedEvent(long OwnerFrameId, int ActorSeat, bool Accepted, int MaterialCount) : IGameEvent;
public sealed record TousuiKeActionAcceptedEvent(long ProgramFrameId, int InstructionIndex, CardConversionSource RequestSource, string RequestGameplayHash, int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash, PromptId OriginalPromptId, int MaterialCount) : IGameEvent;
public sealed partial class GameEngine
{
    private const string TousuiRequestOpen = "tousui-request-open", TousuiRequestPick = "tousui-request-pick";
    private IEnumerable<CardConversionSource> TousuiSources(CharacterState actor) => GetSkillBindingShard(actor).ProgramInstances.Where(i => i.SkillId == ChengJiChengCuiComposition.Tousui).SelectMany(i => i.Program.ViewAs.Where(r => r.Id == "bottom-cost-slash" &&
            r.Condition.Evaluate(CreateSkillContext(actor))).Select(r => new CardConversionSource(i.SkillId, r.Id, actor.Seat, i.SkillInstanceId)));
    private IReadOnlyList<Card> TousuiRequestCards(CardConversionSource source)
    {
        if (!IsValidPlayerSeat(source.OwnerSeat) ||
            !_players[source.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) ||
            ViewAsRule(source) is not { CostDestination: SkillProgramCardDestination.DrawPileBottom } r ||
            IsCardUseForbidden(source.OwnerSeat, CardKind.Slash, CardActionType.Use))
            return [];
        var actor = _players[source.OwnerSeat];
        return TousuiPhysicalCards(actor).Where(c => !IsTurnHandCardRestricted(actor, c) &&
            !HasProgramCardIdentity(actor, c) &&
            !IsTurnPhysicalUseForbidden(actor.Seat, [c.Id])).DistinctBy(c => c.Id).OrderBy(c => c.Id).ToArray();
    }

    private IReadOnlyList<PromptChoice> TousuiProgramSlashChoices(ProgramSkillFrame f, int actorSeat, IReadOnlyList<int> targets, string? resultBind = null)
    {
        if (!IsValidPlayerSeat(actorSeat) ||
            !_players[actorSeat].IsAlive ||
            GetPausedPrivateOfferEffect(f) is not { } effect ||
            !TousuiProgramActor(f, effect.Op, actorSeat))
            return [];
        var exact = TousuiProgramTargets(f, actorSeat, effect.Op);
        var result = new List<PromptChoice>();
        foreach (var source in TousuiSources(_players[actorSeat]).Where(s => TousuiRequestCards(s).Count >= 1))
            foreach (var target in targets.Where(exact.Contains).Distinct())
            {
                if (target == actorSeat || !IsValidPlayerSeat(target) || !_players[target].IsAlive)
                    continue;
                var p = new Dictionary<string, string>
                {
                    {
                        "program-action",
                        TousuiRequestOpen
                    },
                    {
                        "frame-id",
                        f.Id.ToString(CultureInfo.InvariantCulture)
                    },
                    {
                        "actor-seat",
                        actorSeat.ToString(CultureInfo.InvariantCulture)
                    },
                    {
                        "target-seat",
                        target.ToString(CultureInfo.InvariantCulture)
                    }
                };
                AddConversionParameters(p, source);
                result.Add(new(new($"tousui.request.{f.Id}.{source.SkillInstanceId}.{source.BindingId}.{actorSeat}.{target}"), "发动【透髓】：私下选择至少一张手牌或装备牌当【杀】使用", [], [target], p));
            }

        return Array.AsReadOnly(result.ToArray());
    }

    private bool HasTousuiProgramSlash(CharacterState actor, IEnumerable<int> targets) => actor.IsAlive &&
            targets.Any(s => IsValidPlayerSeat(s) &&
            s != actor.Seat &&
            _players[s].IsAlive) &&
            TousuiSources(actor).Any(s => TousuiRequestCards(s).Count >= 1);
    private bool TousuiProgramActor(ProgramSkillFrame f, SkillProgramEffectOp op, int actor) => op == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment ? f.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicChoice } r &&
            r.ActorSeat == actor &&
            ValidKeBiNeng(f) : RoundOnceCardlessProgramActor(f, op, actor);
    private int[] TousuiProgramTargets(ProgramSkillFrame f, int actor, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment &&
            TousuiProgramActor(f, op, actor) ? _players.Where(p => p.IsAlive &&
            p.Seat != actor).Select(p => p.Seat).ToArray() : RoundOnceCardlessProgramTargets(f, actor, op);
    private IReadOnlyList<PromptChoice> TousuiBorrowedSlashChoices(BorrowedSwordHandle pending)
    {
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) ||
            !IsValidPlayerSeat(pending.WeaponOwnerSeat) ||
            !_players[pending.WeaponOwnerSeat].IsAlive ||
            !_players[pending.SlashTargetSeat].IsAlive)
            return [];
        return Array.AsReadOnly(TousuiSources(_players[pending.WeaponOwnerSeat]).Where(s => TousuiRequestCards(s).Count >= 1).Select(s =>
        {
            var p = new Dictionary<string, string>
            {
                {
                    "program-action",
                    TousuiRequestOpen
                },
                {
                    "response",
                    TousuiRequestOpen
                },
                {
                    "frame-id",
                    pending.ResolutionId.ToString(CultureInfo.InvariantCulture)
                },
                {
                    "actor-seat",
                    pending.WeaponOwnerSeat.ToString(CultureInfo.InvariantCulture)
                },
                {
                    "target-seat",
                    pending.SlashTargetSeat.ToString(CultureInfo.InvariantCulture)
                }
            };
            AddConversionParameters(p, s);
            return new PromptChoice(new($"tousui.borrowed.{pending.ResolutionId}.{s.SkillInstanceId}.{s.BindingId}"), "发动【透髓】：将至少一张手牌或装备牌当【杀】使用", [], [pending.SlashTargetSeat], p);
        }).ToArray());
    }

    private bool TryBeginTousuiBorrowedSlash(BorrowedSwordHandle pending, PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("response") != TousuiRequestOpen)
            return false;
        if (_pendingDecision is not { Kind: DecisionKind.RespondSlash } original ||
            !pending.AwaitingSlashChoice ||
            original.PlayerSeat != pending.WeaponOwnerSeat ||
            !TousuiBorrowedSlashChoices(pending).Any(c => AssistedChoicesEqual([c], [selected])) ||
            LifecycleCardUse(pending.ResolutionId) is not { TousuiRequest: null } parent ||
            _resolutionStack.LastOrDefault() is not ResponseWindowFrame response ||
            response.ParentFrameId != parent.Id)
            throw new InvalidOperationException("Tousui lost its original Borrowed Sword Slash-use request.");
        var source = RequireConversionSource(selected);
        var r = new TousuiRequestReceipt(parent.Id, 0, null, pending.WeaponOwnerSeat, pending.SlashTargetSeat, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, original);
        UpdateLifecycleCardUse(parent.Id, u => u with { TousuiRequest = r });
        BeginTousuiRequest(r);
        return true;
    }

    private IReadOnlyList<PromptChoice> TousuiQinglongSlashChoices(QinglongCrescentBladeHandle pending)
    {
        var attack = pending.Attack;
        var actor = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!SameAttackOwner(ActiveCardAttack, attack) || !CanUseQinglongCrescentBladeTarget(actor, target))
            return [];
        return Array.AsReadOnly(TousuiSources(actor).Where(s => TousuiRequestCards(s).Count >= 1).Select(s =>
        {
            var p = new Dictionary<string, string>
            {
                {
                    "program-action",
                    TousuiRequestOpen
                },
                {
                    "action",
                    TousuiRequestOpen
                },
                {
                    "frame-id",
                    attack.ResolutionId.ToString(CultureInfo.InvariantCulture)
                },
                {
                    "actor-seat",
                    actor.Seat.ToString(CultureInfo.InvariantCulture)
                },
                {
                    "target-seat",
                    target.Seat.ToString(CultureInfo.InvariantCulture)
                }
            };
            AddConversionParameters(p, s);
            return new PromptChoice(new($"tousui.qinglong.{attack.ResolutionId}.{s.SkillInstanceId}.{s.BindingId}"), "发动【透髓】：选择至少一张牌追杀", [], [target.Seat], p);
        }).ToArray());
    }

    private bool TryBeginTousuiQinglongSlash(QinglongCrescentBladeHandle pending, PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("action") != TousuiRequestOpen)
            return false;
        var attack = pending.Attack;
        if (_pendingDecision is not { Kind: DecisionKind.QinglongCrescentBlade, IsPrivate: true } original ||
            !SameContinuationOwner(ActiveQinglongCrescentBlade, pending) ||
            original.PlayerSeat != attack.SourceSeat ||
            !TousuiQinglongSlashChoices(pending).Any(c => AssistedChoicesEqual([c], [selected])) ||
            LifecycleCardUse(attack.ResolutionId) is not { TousuiRequest: null } parent ||
            _resolutionStack.LastOrDefault()?.Id != parent.Id)
            throw new InvalidOperationException("Tousui lost its exact native Qinglong Slash-use request.");
        var source = RequireConversionSource(selected);
        var r = new TousuiRequestReceipt(parent.Id, 0, null, attack.SourceSeat, attack.TargetSeat, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, original);
        UpdateLifecycleCardUse(parent.Id, u => u with { TousuiRequest = r });
        BeginTousuiRequest(r);
        return true;
    }

    [ProgramAnswer(TousuiRequestOpen)]
    private void OpenTousuiProgramRequest(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TousuiRequest: null } f ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } original ||
            GetPausedPrivateOfferEffect(f) is not { } effect ||
            selected.Targets is not [var target] ||
            !TousuiProgramSlashChoices(f, original.PlayerSeat, TousuiProgramTargets(f, original.PlayerSeat, effect.Op)).Any(c => AssistedChoicesEqual([c], [selected])) ||
            !original.Choices.Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Tousui lost its exact program-request actor, instruction or legal published frontier.");
        var source = RequireConversionSource(selected);
        var r = new TousuiRequestReceipt(f.Id, f.InstructionIndex, effect.Op, original.PlayerSeat, target, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, original)
        {
            RequestSource = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            RequestGameplayHash = f.GameplayHash
        };
        ReplaceRuntimeTop(f with { TousuiRequest = r });
        BeginTousuiRequest(r);
    }

    private void BeginTousuiRequest(TousuiRequestReceipt r)
    {
        AdvanceEventRulesAndQueueFact(new TousuiRequestStartedEvent(r.OwnerFrameId, r.InstructionIndex, r.RequestOp, r.ActorSeat, r.TargetSeat, r.Source, r.GameplayHash, r.OriginalDecision.PromptId, r.OriginalDecision.Revision, TousuiOriginalDecisionHash(r.OriginalDecision)));
        PublishTousuiRequest(r);
    }

    private static string TousuiOriginalDecisionHash(PendingDecision decision) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(decision))));
    private TousuiRequestReceipt? CurrentTousuiRequest() => _resolutionStack.LastOrDefault() switch
    {
        ProgramSkillFrame { TousuiRequest: { } r } => r,
        CardUseFrame { TousuiRequest: { } r } => r,
        ResponseWindowFrame response when LifecycleCardUse(response.ParentFrameId) is { TousuiRequest: { } r } => r,
        _ => null
    };
    private bool ValidTousuiRequest(TousuiRequestReceipt r)
    {
        if (r.OwnerFrameId <= 0 ||
            r.Source.OwnerSeat != r.ActorSeat ||
            r.GameplayHash != _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash ||
            ViewAsRule(r.Source) is not { CostDestination: SkillProgramCardDestination.DrawPileBottom } ||
            r.OriginalDecision.PlayerSeat != r.ActorSeat ||
            r.Selected.Distinct().Count() != r.Selected.Count ||
            r.Selected.Any(id => !TousuiRequestCards(r.Source).Any(c => c.Id == id)) ||
            ProgramEventHistory<TousuiRequestStartedEvent>().Count(e => e.OwnerFrameId == r.OwnerFrameId &&
            e.InstructionIndex == r.InstructionIndex &&
            e.RequestOp == r.RequestOp &&
            e.ActorSeat == r.ActorSeat &&
            e.TargetSeat == r.TargetSeat &&
            e.Source == r.Source &&
            e.GameplayHash == r.GameplayHash &&
            e.OriginalPromptId == r.OriginalDecision.PromptId &&
            e.OriginalRevision == r.OriginalDecision.Revision &&
            e.OriginalDecisionHash == TousuiOriginalDecisionHash(r.OriginalDecision)) != 1 ||
            !r.OriginalDecision.ValidTargetSeats.Contains(r.TargetSeat) ||
            !r.OriginalDecision.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == TousuiRequestOpen &&
            c.Parameters.GetValueOrDefault("frame-id") == r.OwnerFrameId.ToString(CultureInfo.InvariantCulture) &&
            c.Parameters.GetValueOrDefault("actor-seat") == r.ActorSeat.ToString(CultureInfo.InvariantCulture) &&
            c.Targets.SequenceEqual([r.TargetSeat]) &&
            TryReadConversionSource(c.Parameters, out var s) &&
            s == r.Source))
            return false;
        var selected = ProgramEventHistory<TousuiRequestMaterialSelectedEvent>().Where(e => e.OwnerFrameId == r.OwnerFrameId &&
            e.OriginalPromptId == r.OriginalDecision.PromptId).OrderBy(e => e.Index).ToArray();
        if (selected.Length != r.Selected.Count ||
            !selected.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, selected.Length)) ||
            !selected.Select(e => e.CardId).SequenceEqual(r.Selected) ||
            selected.Any(e => e.ActorSeat != r.ActorSeat ||
            e.From != _cardZones.GetLocation(e.CardId)))
            return false;
        if (r.RequestOp is { } op)
            return r.OriginalDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { TousuiRequest: { } stored } f &&
            stored == r &&
            f.Id == r.OwnerFrameId &&
            f.InstructionIndex == r.InstructionIndex &&
            f.GameplayHash == r.RequestGameplayHash &&
            r.OriginalDecision.SourceSeat == f.OwnerSeat &&
            r.OriginalDecision.TargetSeat == r.ActorSeat &&
            r.OriginalDecision.SkillPrompt?.SkillId == f.SkillId &&
            r.RequestSource == new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) &&
            GetPausedPrivateOfferEffect(f)?.Op == op &&
            TousuiProgramActor(f, op, r.ActorSeat) &&
            TousuiProgramTargets(f, r.ActorSeat, op).Contains(r.TargetSeat);
        if (r.OriginalDecision.Kind == DecisionKind.QinglongCrescentBlade)
            return r.OriginalDecision.IsPrivate &&
            ActiveQinglongCrescentBlade is { } q &&
            SameAttackOwner(ActiveCardAttack, q.Attack) &&
            q.Attack.ResolutionId == r.OwnerFrameId &&
            q.Attack.SourceSeat == r.ActorSeat &&
            q.Attack.TargetSeat == r.TargetSeat &&
            r.OriginalDecision.SourceSeat == r.ActorSeat &&
            r.OriginalDecision.TargetSeat == r.TargetSeat &&
            r.OriginalDecision.IncomingCard == q.Attack.EffectiveCardKind &&
            r.OriginalDecision.SkillPrompt is null &&
            _resolutionStack.LastOrDefault() is CardUseFrame { TousuiRequest: { } native } u &&
            u.Id == r.OwnerFrameId &&
            native == r;
        return r.OriginalDecision is { Kind: DecisionKind.RespondSlash, RequiredCardKind: CardKind.Slash, IncomingCard: CardKind.BorrowedSword } &&
            ActiveBorrowedSword is { AwaitingSlashChoice: true } borrowed &&
            borrowed.ResolutionId == r.OwnerFrameId &&
            borrowed.WeaponOwnerSeat == r.ActorSeat &&
            borrowed.SlashTargetSeat == r.TargetSeat &&
            r.OriginalDecision.SourceSeat == borrowed.SourceSeat &&
            r.OriginalDecision.TargetSeat == r.TargetSeat &&
            r.OriginalDecision.SkillPrompt is null &&
            LifecycleCardUse(r.OwnerFrameId)?.TousuiRequest == r &&
            _resolutionStack.LastOrDefault() is ResponseWindowFrame response &&
            response.ParentFrameId == r.OwnerFrameId;
    }

    private bool TousuiSelectedRequestLegal(TousuiRequestReceipt r)
    {
        if (r.Selected.Count < 1 || FindProgramMultiCardViewAsSelection(_players[r.ActorSeat], r.Selected, CardKind.Slash, false, r.Source) is not { } selection)
            return false;
        var actor = _players[r.ActorSeat];
        var target = _players[r.TargetSeat];
        var cards = selection.Cards;
        var suit = PhysicalGroupSuit(actor, cards);
        var color = PhysicalGroupColor(actor, cards);
        if (r.RequestOp is null)
            return r.OriginalDecision.Kind == DecisionKind.QinglongCrescentBlade ? CanUseQinglongCrescentBladeTarget(actor, target, CardKind.Slash, color, false, suit, physicalCardIds: r.Selected) : IsLegalBorrowedSwordSlashTarget(actor, target, CardKind.Slash, suit, false, color, physicalCardIds: r.Selected);
        var f = GetActiveProgramFrame(r.OwnerFrameId);
        var ignore = r.RequestOp is SkillProgramEffectOp.UseHejiAtCompletedSingleTarget or SkillProgramEffectOp.RequestSlashByTarget or SkillProgramEffectOp.RequestSlashByNearest ||
            r.RequestOp == SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner &&
            f.FixedTargetSlash!.IgnoreDistance;
        if (r.RequestOp == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment &&
            !CanSpendSlashUse(actor, target, IgnoresProgramSlashLimit(actor, r.Source), CardKind.Slash, physicalCardIds: r.Selected))
            return false;
        return CanUseSlashTarget(actor, target, cards[0], r.Source, CardKind.Slash, ignoreDistance: ignore, noEffectiveRank: true, physicalCardIds: r.Selected) &&
            !HasBeneficiarySuitShield(actor.Seat, target.Seat, suit) &&
            !IsCardTargetProhibited(target, CardKind.Slash, suit, color) &&
            (ignore ||
            AssistedSlashPaymentHasRange(actor, target.Seat, cards));
    }

    private IReadOnlyList<PromptChoice> TousuiRequestPickChoices(TousuiRequestReceipt r)
    {
        if (!ValidTousuiRequest(r))
            throw new InvalidOperationException("Tousui private material selection lost its exact unchanged request.");
        var choices = new List<PromptChoice>();
        foreach (var c in TousuiRequestCards(r.Source).Where(c => !r.Selected.Contains(c.Id)))
            choices.Add(new(new($"tousui.{r.OwnerFrameId}.card.{c.Id}"), "选择【" + c.DisplayName + "】", [c.Id], [], new Dictionary<string, string> { { "program-action", TousuiRequestPick }, { "branch", "card" } }));
        if (TousuiSelectedRequestLegal(r))
            choices.Add(new(new($"tousui.{r.OwnerFrameId}.finish"), "将所选的至少一张牌当【杀】使用", [], [r.TargetSeat], new Dictionary<string, string> { { "program-action", TousuiRequestPick }, { "branch", "finish" } }));
        choices.Add(new(new($"tousui.{r.OwnerFrameId}.cancel"), "返回原用牌请求", [], [], new Dictionary<string, string> { { "program-action", TousuiRequestPick }, { "branch", "cancel" } }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishTousuiRequest(TousuiRequestReceipt r)
    {
        var choices = TousuiRequestPickChoices(r);
        var skill = _contentRegistry.GetSkill(r.Source.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.ActorSeat, "透髓：私下选择至少一张手牌或装备牌，然后确认使用；不会提前移动或公开未支付的牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [r.TargetSeat], r.ActorSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices,
            SkillPrompt = new(r.Source.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[r.ActorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private void StoreTousuiRequest(TousuiRequestReceipt? r, long id)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame f && f.Id == id)
            ReplaceRuntimeTop(f with { TousuiRequest = r });
        else
            UpdateLifecycleCardUse(id, u => u with { TousuiRequest = r });
    }

    [ProgramAnswer(TousuiRequestPick)]
    private void AnswerTousuiRequest(PromptChoice selected)
    {
        if (CurrentTousuiRequest() is not { } r ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p ||
            p.PlayerSeat != r.ActorSeat ||
            !TousuiRequestPickChoices(r).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Tousui private selection requires its real published actor and exact card identity.");
        var branch = selected.Parameters["branch"];
        if (branch == "card")
        {
            var id = selected.Cards.Single();
            AdvanceEventRulesAndQueueFact(new TousuiRequestMaterialSelectedEvent(r.OwnerFrameId, r.OriginalDecision.PromptId, r.ActorSeat, r.Selected.Count, id, _cardZones.GetLocation(id)));
            r = r with
            {
                Selected = r.Selected.Append(id).ToArray()
            };
            StoreTousuiRequest(r, r.OwnerFrameId);
            PublishTousuiRequest(r);
            return;
        }

        if (branch == "finish" && !TousuiSelectedRequestLegal(r))
            throw new InvalidOperationException("Tousui's final original target or complete physical payment became illegal.");
        StoreTousuiRequest(null, r.OwnerFrameId);
        _pendingDecision = branch == "cancel" ? r.OriginalDecision with
        {
            PromptId = CreatePromptId()
        }

        : r.OriginalDecision;
        _status = _players[r.ActorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new TousuiRequestFinishedEvent(r.OwnerFrameId, r.ActorSeat, branch == "finish", branch == "finish" ? r.Selected.Count : 0));
        if (branch == "cancel")
        {
            AdvanceRulesAndPublishState();
            return;
        }

        var selection = FindProgramMultiCardViewAsSelection(_players[r.ActorSeat], r.Selected, CardKind.Slash, false, r.Source) ?? throw new InvalidOperationException("Tousui lost its exact selected native payment.");
        if (r.RequestOp is null)
        {
            if (r.OriginalDecision.Kind == DecisionKind.QinglongCrescentBlade)
                BeginQinglongCrescentBladeFollowup(ActiveQinglongCrescentBlade!, selection.Cards[0], CardKind.Slash, r.ActorSeat, selection.Cards, conversionSource: r.Source);
            else
                ResolveBorrowedSwordProgramMultiCardSlashChoice(ActiveBorrowedSword!, selection);
            return;
        }

        var f = GetActiveProgramFrame(r.OwnerFrameId);
        var effect = GetPausedPrivateOfferEffect(f)!;
        if (r.RequestOp == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment)
        {
            var action = new LegalAction(LegalActionKind.Slash, selection.Cards[0].Id, r.TargetSeat, "透髓：使用【杀】", CardKind.Slash, TargetSeats: [r.TargetSeat], MinCardCount: r.Selected.Count, MaxCardCount: r.Selected.Count)
            {
                ConversionSource = r.Source,
                SelectableCardIds = r.Selected
            };
            AdvanceEventRulesAndQueueFact(new TousuiKeActionAcceptedEvent(f.Id, f.InstructionIndex, r.RequestSource!, r.RequestGameplayHash!, r.ActorSeat, r.TargetSeat, r.Source, r.GameplayHash, r.OriginalDecision.PromptId, r.Selected.Count));
            var k = f.KeBiNengPingduan!;
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, KeBiNengPingduan = k with { BasicActions = k.BasicActions.Append(action).ToArray(), SelectedBasicAction = action, Stage = KeBiNengPingduanStage.BasicIssuing } });
            BeginKeBiNengPingduanBasicUse(f, action);
            return;
        }

        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.RequestOp == SkillProgramEffectOp.RequestSlashByTarget)
            CommitProgramChoiceResult(f.Id, effect.ResultBind!, RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption, r.ActorSeat, "透髓：使用【杀】。");
        else if (r.RequestOp == SkillProgramEffectOp.RequestSlashAgainstChosenTarget)
        {
            ReplaceRuntimeTop(f = f with { AssistedSlashRequest = null });
            CommitProgramChoiceResult(f.Id, effect.ResultBind!, "used-slash", r.ActorSeat, "透髓：使用【杀】。");
        }
        else if (r.RequestOp == SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner)
            ReplaceRuntimeTop(f = f with { FixedTargetSlash = f.FixedTargetSlash!with { Issued = true } });
        else if (r.RequestOp == SkillProgramEffectOp.RequestLegalSlashByNearest)
            ReplaceRuntimeTop(f = f with { NearestLegalSlashRequest = null });
        if (r.RequestOp == SkillProgramEffectOp.UseHejiAtCompletedSingleTarget)
        {
            PrepareTousuiWuJingNative(f, selection);
            f = GetActiveProgramFrame(f.Id);
        }

        if (IsSayingSlashRequest(f))
        {
            PrepareSayingSuppliedSlash(f, r.ActorSeat, r.ActorSeat, r.TargetSeat, CardKind.Slash, selection.Cards, r.Source);
            f = GetActiveProgramFrame(f.Id);
        }

        if (IsChanshuangSlashRequest(f))
        {
            PrepareChanshuangSuppliedSlash(f, r.ActorSeat, r.ActorSeat, r.TargetSeat, CardKind.Slash, selection.Cards, r.Source);
            f = GetActiveProgramFrame(f.Id);
        }

        ResolveSlashCore(_players[r.ActorSeat], _players[r.TargetSeat], selection.Cards[0], CardKind.Slash, r.ActorSeat, physicalCards: selection.Cards, countsTowardSlashLimit: false, conversionSource: r.Source, programSkillCardUseFrameId: f.Id);
    }

    private void PrepareTousuiWuJingNative(ProgramSkillFrame f, ProgramMultiCardViewAsSelection selection)
    {
        if (!IsWuJingHejiRequest(f))
            return;
        if (selection.Cards.Count < 1 ||
            !IsTousuiSource(selection.Source) ||
            !WuJingCanUse(f.OwnerSeat, f.WuJing!.TargetSeat, selection.Cards[0], CardKind.Slash, selection.Source, selection.Cards.Select(c => c.Id).ToArray()) ||
            ProgramEventHistory<TousuiRequestFinishedEvent>().Count(e => e.OwnerFrameId == f.Id &&
            e.ActorSeat == f.OwnerSeat &&
            e.Accepted &&
            e.MaterialCount == selection.Cards.Count) != 1)
            throw new InvalidOperationException("Heji Tousui requires its actual accepted original material request.");
        ReplaceRuntimeTop(f with { WuJing = f.WuJing with { Stage = WuJingStage.IssuingUse, Selection = new(CardKind.Slash, false, false) { CardIds = selection.Cards.Select(c => c.Id).ToArray(), Conversions = [selection.Source] }, Before = _movementSequence, After = _movementSequence } });
    }

    private bool HasTousuiRequestPrompt() => CurrentTousuiRequest() is { } r &&
            _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
            p.PlayerSeat == r.ActorSeat &&
            p.SkillPrompt?.SkillId == r.Source.SkillId &&
            p.Choices.All(c => c.Parameters.GetValueOrDefault("program-action") == TousuiRequestPick) &&
            ValidTousuiRequest(r);
    private PendingDecision? TousuiNativeOriginalDecision(long ownerId) => HasTousuiRequestPrompt() &&
            CurrentTousuiRequest() is { RequestOp: null } r &&
            r.OwnerFrameId == ownerId ? r.OriginalDecision : null;
    private PendingDecision TousuiProgramNativeDecision(PendingDecision p) => ZonghuNativeDecision(HasTousuiRequestPrompt() &&
            CurrentTousuiRequest() is { RequestOp: not null } r &&
            p.PlayerSeat == r.ActorSeat ? r.OriginalDecision : p);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32203)]
    private void AssertTousuiRequestState(ProgramSkillFrame f)
    {
        if (f.TousuiRequest is { } r && (!ValidTousuiRequest(r) || !HasTousuiRequestPrompt() || r.OwnerFrameId != f.Id))
            throw new InvalidOperationException("Tousui program request lost its owning instruction, original private frontier or ordered private selections.");
    }

    private void AssertTousuiRequests()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
            AssertTousuiRequestState(f);
        foreach (var f in _resolutionStack.OfType<CardUseFrame>())
        {
            if (f.TousuiRequest is { } r && (!ValidTousuiRequest(r) || !HasTousuiRequestPrompt() || r.OwnerFrameId != f.Id || r.RequestOp is not null))
                throw new InvalidOperationException("Tousui native request lost its exact Borrowed Sword/Qinglong owner, original frontier or selected entity invoices.");
            if (f.Tousui is not null && !TousuiUseProof(f))
                throw new InvalidOperationException("Tousui native use lost its original material count, conversion source or final actor chain.");
        }
    }

    private bool HasAcceptedTousuiKeAction(ProgramSkillFrame f, LegalAction action)
    {
        if (f.KeBiNengPingduan is not { } r ||
            action is not { Kind: LegalActionKind.Slash, PlayedCardKind: CardKind.Slash, ConversionSource: { } source } ||
            !IsTousuiSource(source) ||
            source.OwnerSeat != r.ActorSeat ||
            action.TargetSeats is not [var target] ||
            action.MinCardCount < 1 ||
            action.MaxCardCount != action.MinCardCount ||
            action.SelectableCardIds.Count != action.MinCardCount ||
            action.SelectableCardIds.Distinct().Count() != action.MinCardCount ||
            action.CardId != action.SelectableCardIds[0] ||
            ProgramEventHistory<TousuiKeActionAcceptedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray() is not [var accepted] ||
            accepted.InstructionIndex != f.InstructionIndex ||
            accepted.RequestSource != r.Source ||
            accepted.RequestGameplayHash != f.GameplayHash ||
            accepted.ActorSeat != r.ActorSeat ||
            accepted.TargetSeat != target ||
            accepted.Source != source ||
            accepted.GameplayHash != _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash ||
            accepted.MaterialCount != action.MinCardCount ||
            ProgramEventHistory<TousuiRequestStartedEvent>().Count(e => e.OwnerFrameId == f.Id &&
            e.RequestOp == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment &&
            e.ActorSeat == r.ActorSeat &&
            e.TargetSeat == target &&
            e.Source == source &&
            e.OriginalPromptId == accepted.OriginalPromptId) != 1 ||
            ProgramEventHistory<TousuiRequestFinishedEvent>().Count(e => e.OwnerFrameId == f.Id &&
            e.ActorSeat == r.ActorSeat &&
            e.Accepted &&
            e.MaterialCount == accepted.MaterialCount) != 1)
            return false;
        var materials = ProgramEventHistory<TousuiRequestMaterialSelectedEvent>().Where(e => e.OwnerFrameId == f.Id &&
            e.OriginalPromptId == accepted.OriginalPromptId).OrderBy(e => e.Index).ToArray();
        return materials.Length == accepted.MaterialCount &&
            materials.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, materials.Length)) &&
            materials.Select(e => e.CardId).SequenceEqual(action.SelectableCardIds) &&
            materials.All(e => e.ActorSeat == r.ActorSeat &&
            e.From.OwnerSeat == r.ActorSeat &&
            e.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain);
    }

    private bool TryChooseTousuiAi(PendingDecision p, out PromptChoice choice)
    {
        choice = null !;
        if (!HasTousuiRequestPrompt() || CurrentTousuiRequest() is not { } r || p.PlayerSeat != r.ActorSeat)
            return false;
        choice = p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "finish") ?? p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[r.ActorSeat])).ThenBy(c => c.Cards[0]).FirstOrDefault() ?? p.Choices.Single(c => c.Parameters["branch"] == "cancel");
        return true;
    }
}
