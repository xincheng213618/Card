using System.Globalization;

namespace CardGame.Core;

/// <summary>
/// Placed-card declared uses (Guhuo): the owner commits one hand card, announces
/// any basic card or ordinary trick, every other living character without
/// Chanyuan may question in seat order, and the flip either voids the use
/// (false declaration) or grants Chanyuan to the doubter (true declaration).
/// The placed card stays hidden in the owner hand until it is flipped or played.
/// </summary>
public sealed partial class GameEngine
{
    private const string ChanyuanSkillId = "classic:chanyuan";
    private const string GuhuoDeclaredBindingId = "guhuo-declared";

    private SkillProgramStepOutcome UseProgramPlacedCardAsDeclared(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.OwnerSeat != frame.OwnerSeat ||
            active.SkillId != frame.SkillId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id)
            throw new InvalidOperationException(
                "A placed-card declared use requires the current active program activation.");
        var owner = _players[frame.OwnerSeat];
        if (frame.SelectedCardIds.Count != 1)
            throw new InvalidOperationException(
                "A placed-card declared use requires exactly one placed hand card.");
        var options = BuildGuhuoDeclarationOptions(owner);
        if (options.Count == 0)
            throw new InvalidOperationException(
                "No declared card is currently legal for the placed hand card.");
        var skill = _contentRegistry!.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            owner.Seat,
            $"【{skill.Name}】已扣置一张手牌，请声明要视为使用的牌。",
            active.SelectedCardIds,
            options.SelectMany(option => option.TargetSeats).Distinct().Order().ToArray(),
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            SkillPrompt = new(active.SkillId, skill.Name, $"{skill.Name} · 声明牌名", skill.Description),
            Choices = options.Select(option => CreateGuhuoDeclarationChoice(active, option)).ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<ProgramOrdinaryTrickUseOption> BuildGuhuoDeclarationOptions(CharacterState owner)
    {
        var options = new List<ProgramOrdinaryTrickUseOption>();
        void Add(
            CardKind declaredKind,
            LegalActionKind actionKind,
            string description,
            IReadOnlyList<int>? targetSeats = null,
            int? targetCardId = null,
            CardKind? requiredCardKind = null)
        {
            var targets = targetSeats?.ToArray() ?? [];
            var targetPart = targets.Length == 0 ? "none" : string.Join('-', targets);
            var cardPart = targetCardId?.ToString(CultureInfo.InvariantCulture) ?? "none";
            options.Add(new ProgramOrdinaryTrickUseOption(
                new ChoiceId($"guhuo.{declaredKind}.targets-{targetPart}.card-{cardPart}"),
                declaredKind,
                actionKind,
                Array.AsReadOnly(targets),
                targetCardId,
                requiredCardKind,
                description));
        }

        // 基本牌声明集：杀、桃、酒。【闪】只在响应窗口打出，【火】【雷】杀属军争牌，
        // 都不在经典蛊惑的声明范围内；杀目标合法性复用虚拟杀的同一谓词（含次数与距离）。
        foreach (var target in _players.Where(player =>
                     player.IsAlive && CanUseVirtualSlashTarget(owner, player, CardKind.Slash)))
            Add(CardKind.Slash, LegalActionKind.Slash,
                $"当【杀】对 {target.Name} 使用", [target.Seat]);
        if (!IsCardUseForbidden(owner.Seat, CardKind.Peach, CardActionType.Use) && owner.Hp < owner.MaxHp)
            Add(CardKind.Peach, LegalActionKind.Peach, "当【桃】使用：回复一点体力");
        if (!IsCardUseForbidden(owner.Seat, CardKind.Alcohol, CardActionType.Use) &&
            !owner.UsedPlayPhaseAlcoholThisTurn)
            Add(CardKind.Alcohol, LegalActionKind.Alcohol, "当【酒】使用：下一张【杀】伤害+1");

        // 普通锦囊声明集与目标合法性完全复用全部手牌当普通锦囊的既有选项构建。
        options.AddRange(BuildProgramOrdinaryTrickUseOptions(owner));
        return Array.AsReadOnly(options.ToArray());
    }

    private static PromptChoice CreateGuhuoDeclarationChoice(
        ProgramSkillFrame frame,
        ProgramOrdinaryTrickUseOption option) =>
        new(
            option.Id,
            option.Description,
            [],
            option.TargetSeats,
            new Dictionary<string, string>
            {
                ["program-action"] = "guhuo-declare",
                ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                ["card-kind"] = option.EffectiveCardKind.ToString(),
                ["action-kind"] = option.ActionKind.ToString(),
                ["target-card-id"] = option.TargetCardId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["required-card-kind"] = option.RequiredCardKind?.ToString() ?? string.Empty
            });

    private void ResolveProgramGuhuoDeclarationChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Guhuo declaration lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (paused.Op != SkillProgramEffectOp.UsePlacedCardAsDeclared ||
            selected.Parameters.GetValueOrDefault("program-action") != "guhuo-declare" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(CultureInfo.InvariantCulture))
            throw new InvalidOperationException(
                "The Guhuo declaration does not match its suspended program instruction.");

        var owner = _players[frame.OwnerSeat];
        var option = BuildGuhuoDeclarationOptions(owner)
            .SingleOrDefault(candidate => candidate.Id == selected.Id) ??
            throw new InvalidOperationException("The Guhuo declaration is no longer legal.");
        if (!selected.Targets.SequenceEqual(option.TargetSeats))
            throw new InvalidOperationException(
                "The Guhuo declaration targets changed while the choice was pending.");

        ClearPendingDecision();
        _resolutionStack[^1] = frame with
        {
            DeclaredCardUse = ProgramDeclaredCardUse.Create(
                option.EffectiveCardKind,
                option.ActionKind,
                option.TargetSeats,
                option.TargetCardId,
                option.RequiredCardKind,
                NextGuhuoDoubtSeat(frame, frame.OwnerSeat))
        };
        var declaredName = CardCatalog.Get(option.EffectiveCardKind).DisplayName;
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 扣置一张手牌，声明当【{declaredName}】使用。",
            owner.Seat,
            option.TargetSeats.FirstOrDefault(-1));
        QueueGameEvent(new ProgramCardDeclaredEvent(
            frame.Id,
            frame.SkillId,
            frame.OwnerSeat,
            option.EffectiveCardKind,
            Array.AsReadOnly(GuhuoDoubtCandidates(frame).Select(player => player.Seat).ToArray())));
        ContinueGuhuoDoubtWindow(frame.Id);
    }

    private IEnumerable<CharacterState> GuhuoDoubtCandidates(ProgramSkillFrame frame) =>
        Enumerable.Range(1, _playerCount - 1)
            .Select(offset => _players[(frame.OwnerSeat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                             !player.SkillGrants.EffectiveSkillIds.Contains(ChanyuanSkillId));

    private int NextGuhuoDoubtSeat(ProgramSkillFrame frame, int afterSeat)
    {
        foreach (var offset in Enumerable.Range(1, _playerCount - 1))
        {
            var seat = (afterSeat + offset) % _playerCount;
            var player = _players[seat];
            if (player.IsAlive && !player.SkillGrants.EffectiveSkillIds.Contains(ChanyuanSkillId))
                return seat;
        }

        return -1;
    }

    private void ContinueGuhuoDoubtWindow(long frameId)
    {
        var frame = _resolutionStack.OfType<ProgramSkillFrame>()
                        .SingleOrDefault(item => item.Id == frameId) ??
            throw new InvalidOperationException("The Guhuo doubt window lost its program frame.");
        var declared = frame.DeclaredCardUse ??
            throw new InvalidOperationException("The Guhuo doubt window lost its declaration.");
        var cursor = declared.DoubtCursorSeat;
        while (cursor >= 0)
        {
            var candidate = _players[cursor];
            if (candidate.IsAlive &&
                !candidate.SkillGrants.EffectiveSkillIds.Contains(ChanyuanSkillId))
                break;
            cursor = NextGuhuoDoubtSeat(frame, cursor);
        }

        if (cursor < 0)
        {
            ResolveGuhuoDeclaredUse(frameId);
            return;
        }

        if (declared.DoubtCursorSeat != cursor)
        {
            _resolutionStack[^1] = frame with
            {
                DeclaredCardUse = declared with { DoubtCursorSeat = cursor }
            };
            frame = (ProgramSkillFrame)_resolutionStack[^1];
        }

        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var declaredName = CardCatalog.Get(declared.DeclaredKind).DisplayName;
        var passId = new ChoiceId("guhuo-doubt-pass");
        var doubtId = new ChoiceId("guhuo-doubt");
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            cursor,
            $"{_players[frame.OwnerSeat].Name} 声明将扣置的手牌当【{declaredName}】使用，你是否质疑？",
            [],
            [],
            SourceSeat: frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            SkillPrompt = new(frame.SkillId, skill.Name,
                $"{skill.Name} · 质疑决策", skill.Description),
            Choices =
            [
                new PromptChoice(
                    doubtId,
                    "质疑：翻开此牌；为假则此牌作废，为真则你获得【缠怨】。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "guhuo-doubt",
                        ["decision"] = "doubt",
                        ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture)
                    }),
                new PromptChoice(
                    passId,
                    "不质疑。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "guhuo-doubt",
                        ["decision"] = "pass",
                        ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture)
                    })
            ]
        };
        _status = _players[cursor].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramGuhuoDoubtChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Guhuo doubt answer lost its program frame.");
        var declared = frame.DeclaredCardUse ??
            throw new InvalidOperationException("The Guhuo doubt answer lost its declaration.");
        if (selected.Parameters.GetValueOrDefault("program-action") != "guhuo-doubt" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("decision") is not ("doubt" or "pass") ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision)
            throw new InvalidOperationException(
                "The Guhuo doubt answer does not match its pending prompt.");
        var doubterSeat = decision.PlayerSeat;
        if (doubterSeat != declared.DoubtCursorSeat)
            throw new InvalidOperationException("The Guhuo doubt answer arrived out of order.");

        ClearPendingDecision();
        if (selected.Parameters.GetValueOrDefault("decision") != "doubt")
        {
            AddLog(
                "SkillChoice",
                $"{_players[doubterSeat].Name} 不质疑于吉的声明。",
                frame.OwnerSeat,
                doubterSeat);
            _resolutionStack[^1] = frame with
            {
                DeclaredCardUse = declared with
                {
                    DoubtCursorSeat = NextGuhuoDoubtSeat(frame, doubterSeat)
                }
            };
            ContinueGuhuoDoubtWindow(frame.Id);
            return;
        }

        FlipGuhuoPlacedCard(frame.Id, doubterSeat);
    }

    private void FlipGuhuoPlacedCard(long frameId, int doubterSeat)
    {
        var frame = _resolutionStack.OfType<ProgramSkillFrame>()
                        .SingleOrDefault(item => item.Id == frameId) ??
            throw new InvalidOperationException("The Guhuo flip lost its program frame.");
        var declared = frame.DeclaredCardUse ??
            throw new InvalidOperationException("The Guhuo flip lost its declaration.");
        var owner = _players[frame.OwnerSeat];
        var doubter = _players[doubterSeat];
        var placedCardId = frame.SelectedCardIds.Single();
        var location = _cardZones.GetLocation(placedCardId);
        var placed = _cardZones.CardsAt(location).SingleOrDefault(card => card.Id == placedCardId);
        if (location.OwnerSeat != owner.Seat || location.Zone != CardZoneKind.Hand || placed is null)
        {
            CancelProgramBindingAndCleanup(
                GetActiveProgramFrame(frameId), "扣置的手牌已离开其手牌，蛊惑剩余结算取消。");
            return;
        }

        var isTrue = placed.Kind == declared.DeclaredKind;
        // 翻开只公开牌面，不移动牌：作废路径直接进弃牌堆，成立路径按声明的
        // 使用从手牌正常进入结算，避免出现无活动结算的 Processing 中间态。
        QueueGameEvent(new ProgramGuhuoCardFlippedEvent(
            frameId,
            frame.SkillId,
            frame.OwnerSeat,
            doubterSeat,
            placedCardId,
            declared.DeclaredKind,
            placed.Kind,
            isTrue));
        var declaredName = CardCatalog.Get(declared.DeclaredKind).DisplayName;
        AddLog(
            "SkillTriggered",
            isTrue
                ? $"{doubter.Name} 质疑翻开扣置牌：确为【{declaredName}】，{doubter.Name} 获得技能【缠怨】。"
                : $"{doubter.Name} 质疑翻开扣置牌：并非【{declaredName}】，此牌作废。",
            frame.OwnerSeat,
            doubterSeat);
        if (!isTrue)
        {
            VoidGuhuoPlacedCard(frameId, placed, location);
            return;
        }

        AcquireRuntimeSkills(doubter, frame.SkillId, [ChanyuanSkillId]);
        ResolveGuhuoDeclaredUse(frameId);
    }

    private void VoidGuhuoPlacedCard(long frameId, Card placed, CardLocation location)
    {
        var frame = _resolutionStack.OfType<ProgramSkillFrame>()
                        .SingleOrDefault(item => item.Id == frameId) ??
            throw new InvalidOperationException("The Guhuo void lost its program frame.");
        MoveCard(
            placed,
            location,
            CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.GuhuoVoid"));
        ContinueProgramSkill(frameId);
    }

    private void ResolveGuhuoDeclaredUse(long frameId)
    {
        var frame = _resolutionStack.OfType<ProgramSkillFrame>()
                        .SingleOrDefault(item => item.Id == frameId) ??
            throw new InvalidOperationException("The declared-card use lost its program frame.");
        var declared = frame.DeclaredCardUse ??
            throw new InvalidOperationException("The declared-card use lost its declaration.");
        var owner = _players[frame.OwnerSeat];
        var placedCardId = frame.SelectedCardIds.Single();
        var location = _cardZones.GetLocation(placedCardId);
        var placed = _cardZones.CardsAt(location).SingleOrDefault(card => card.Id == placedCardId);
        if (location.OwnerSeat != owner.Seat || location.Zone != CardZoneKind.Hand || placed is null)
        {
            CancelProgramBindingAndCleanup(
                GetActiveProgramFrame(frameId), "扣置的手牌已离开其手牌，蛊惑剩余结算取消。");
            return;
        }

        var source = new CardConversionSource(
            frame.SkillId,
            GuhuoDeclaredBindingId,
            owner.Seat,
            frame.SkillInstanceId);
        var declaredName = CardCatalog.Get(declared.DeclaredKind).DisplayName;
        if (!owner.IsAlive)
        {
            CancelProgramBindingAndCleanup(
                GetActiveProgramFrame(frameId), "扣置者已失效，蛊惑剩余结算已取消。");
            return;
        }

        switch (declared.ActionKind)
        {
            case LegalActionKind.Slash:
                var target = _players.SingleOrDefault(player =>
                    player.Seat == declared.TargetSeats.FirstOrDefault(-1)) ??
                    throw new InvalidOperationException("The declared Slash target no longer exists.");
                if (!CanUseVirtualSlashTarget(owner, target, declared.DeclaredKind))
                    throw new InvalidOperationException(
                        "The declared Slash target is no longer legal.");
                ResolveSlashCore(
                    owner,
                    target,
                    placed,
                    declared.DeclaredKind,
                    owner.Seat,
                    physicalCards: [placed],
                    conversionSource: source);
                return;
            case LegalActionKind.Peach:
                ResolvePeach(owner, placed);
                return;
            case LegalActionKind.Alcohol:
                // 与 ResolveAlcohol 相同的承诺语义；无需 stillLegal 复验，因为
                // 蛊惑的声明使用不走普通出牌的合法动作表。
                var alcoholResolution = BeginCardUse(placed, owner.Seat, [],
                    playedCardKind: CardKind.Alcohol, conversionSource: source);
                MoveCard(placed, location, CardLocation.Processing, CardMoveReasons.Use);
                owner.UsedPlayPhaseAlcoholThisTurn = true;
                BeginSimpleCardUse(alcoholResolution, new(placed.Id, SimpleCardUseEffect.Alcohol));
                return;
            default:
                var resolutionId = BeginCardUse(
                    placed,
                    owner.Seat,
                    declared.TargetSeats,
                    declared.DeclaredKind,
                    physicalCardIds: [placedCardId],
                    conversionSource: source);
                MoveCard(placed, location, CardLocation.Processing, CardMoveReasons.Use);
                QueueGameEvent(new ProgramViewAsConvertedEvent(
                    resolutionId,
                    frame.SkillId,
                    GuhuoDeclaredBindingId,
                    owner.Seat,
                    [placedCardId],
                    declared.DeclaredKind,
                    IsUse: true,
                    declared.TargetSeats));
                AddLog(
                    "SkillTriggered",
                    $"{owner.Name} 将扣置的手牌当【{declaredName}】使用。",
                    owner.Seat,
                    declared.TargetSeats.FirstOrDefault(-1));
                BeginJizhiOrNullificationWindow(
                    resolutionId,
                    placed,
                    owner.Seat,
                    declared.TargetSeats,
                    declared.ActionKind,
                    declared.TargetCardId,
                    declared.RequiredCardKind,
                    declared.DeclaredKind);
                return;
        }
    }
}
