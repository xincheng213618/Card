namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginTianyiPindian(CharacterState source, int sourceCardId, int opponentSeat, long frameId)
    {
        var opponent = _players[opponentSeat];
        if (!UsesFormalTaishiCi || !HasRuntimeSkill(source, SkillKind.Tianyi) ||
            !GetHand(source).Any(card => card.Id == sourceCardId) ||
            !opponent.IsAlive || opponent.Seat == source.Seat || GetHand(opponent).Count == 0 ||
            source.UsedActiveSkillKinds.Contains(SkillKind.Tianyi))
            throw new InvalidOperationException("The selected Tianyi Pindian is no longer legal.");
        source.UsedActiveSkillKinds.Add(SkillKind.Tianyi);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        BeginSharedPindian(frameId, new("classic:tianyi", "天义", "天义", "选择拼点牌"),
            source.Seat, opponentSeat, sourceCardId, SkillKind.Tianyi);
    }

    private void CompleteTianyiPindian(long frameId, PindianResult result)
    {
        var source = _players[result.SourceSeat];
        source.TianyiWonThisTurn = result.SourceWon;
        source.TianyiLostThisTurn = !result.SourceWon;
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(frameId, source.Seat, SkillKind.Tianyi,
            ActiveSkillEffectKind.PindianForSlashBonus));
        PopResolutionFrame(frameId, ResolutionFrameKind.ActiveSkill);
    }
}
