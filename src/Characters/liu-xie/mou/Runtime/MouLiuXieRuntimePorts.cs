namespace CardGame.Core;
internal interface IMouLiuXieFacts : IProgramSelectionQuery
{
    int Turn { get; }
    int CurrentSeat { get; }
    int PlayerCount { get; }
    int TableStepSeat(int from, int offset);
    long MovementSequence { get; }
    bool Finished { get; }
    ActualDiscardRecoveryPhaseKey? Phase { get; }
    bool Alive(int seat);
    bool Human(int seat);
    bool Qun(int seat);
    bool LordQualified(int seat);
    bool Friendly(int owner, int target);
    string Name(int seat);
    string CardLabel(int id);
    int HandCount(int seat);
    IReadOnlyList<int> Discardable(int seat);
    int KeepValue(int seat, int id);
    SkillProgram? Program(string skill);
    IEnumerable<T> History<T>() where T : IGameEvent;
    bool ExactStart(ProgramSkillFrame frame);
    bool ChenshengParent(ProgramSkillFrame frame);
}
internal interface IMouLiuXieNative : IProgramRuntimeOperations
{
    void PublishState();
    void Finish(ProgramSkillFrame frame);
    bool Drain(ProgramSkillFrame frame);
    void Discard(ProgramSkillFrame frame);
    void Damage(ProgramSkillFrame frame, int target);
}
