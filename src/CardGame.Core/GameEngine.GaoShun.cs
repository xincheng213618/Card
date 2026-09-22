namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XianzhenSkillId = "classic:xianzhen";
    private const string XianzhenLossUsageId = "loss";
    private const string XianzhenWinUsagePrefix = "win.target-";


    private bool UsesFormalGaoShun =>
        HasClassicGeneralPackage(new Version(1, 83, 0));

    private static string GetXianzhenWinUsageId(int targetSeat) =>
        $"{XianzhenWinUsagePrefix}{targetSeat}";

    private bool HasXianzhenWonAgainst(CharacterState source, int targetSeat) =>
        UsesFormalGaoShun &&
        HasRuntimeSkill(source, XianzhenSkillId) &&
        _skillRuntimeState.GetUsage(
            source.Seat,
            XianzhenSkillId,
            GetXianzhenWinUsageId(targetSeat),
            SkillUsageScope.Turn) > 0;

    private bool HasXianzhenWon(CharacterState source) =>
        _players.Any(target => target.Seat != source.Seat &&
            HasXianzhenWonAgainst(source, target.Seat));

    private bool HasXianzhenLost(CharacterState source) =>
        UsesFormalGaoShun &&
        HasRuntimeSkill(source, XianzhenSkillId) &&
        _skillRuntimeState.GetUsage(
            source.Seat,
            XianzhenSkillId,
            XianzhenLossUsageId,
            SkillUsageScope.Turn) > 0;

    private bool IsXianzhenTarget(CharacterState source, CharacterState target) =>
        HasXianzhenWonAgainst(source, target.Seat);

    private void BeginXianzhenPindian(CharacterState source, int sourceCardId, int opponentSeat, long frameId)
    {
        var opponent = _players[opponentSeat];
        if (!UsesFormalGaoShun || !HasRuntimeSkill(source, XianzhenSkillId) ||
            !GetHand(source).Any(card => card.Id == sourceCardId) ||
            !opponent.IsAlive || opponent.Seat == source.Seat || GetHand(opponent).Count == 0 ||
            source.UsedActiveSkillKinds.Contains(SkillKind.Xianzhen))
            throw new InvalidOperationException("The selected Xianzhen Pindian is no longer legal.");
        source.UsedActiveSkillKinds.Add(SkillKind.Xianzhen);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        BeginSharedPindian(frameId, new(XianzhenSkillId, "陷阵", "陷阵", "选择拼点牌"),
            source.Seat, opponentSeat, sourceCardId, SkillKind.Xianzhen);
    }

    private void CompleteXianzhenPindian(long frameId, PindianResult result)
    {
        var source = _players[result.SourceSeat];
        var usageId = result.SourceWon ? GetXianzhenWinUsageId(result.OpponentSeat) : XianzhenLossUsageId;
        if (!_skillRuntimeState.TryConsumeUsage(source.Seat, XianzhenSkillId, usageId, SkillUsageScope.Turn, limit: 1))
            throw new InvalidOperationException("Xianzhen tried to record its turn result twice.");
        QueueGameEvent(new SkillUsageConsumedEvent(source.Seat, XianzhenSkillId, usageId, SkillUsageScope.Turn, Count: 1));
        QueueGameEvent(new XianzhenResolvedEvent(frameId, source.Seat, result.OpponentSeat, result.SourceWon));
        AddLog("SkillResolved", result.SourceWon
            ? $"{source.Name} 赢得【陷阵】拼点，本回合对该目标的用牌无距离限制、杀无次数限制且无视防具。"
            : $"{source.Name} 未赢得【陷阵】拼点，本回合不能使用杀。", source.Seat, result.OpponentSeat);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(frameId, source.Seat, SkillKind.Xianzhen,
            ActiveSkillEffectKind.PindianForSlashBonus));
        PopResolutionFrame(frameId, ResolutionFrameKind.ActiveSkill);
    }
}
