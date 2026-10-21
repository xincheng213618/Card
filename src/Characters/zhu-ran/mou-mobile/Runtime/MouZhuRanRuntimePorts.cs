namespace CardGame.Core;
internal sealed record MouZhuRanPlayer(int Seat,bool Alive,bool Human,bool Wounded);
internal interface IMouZhuRanFacts:IProgramSelectionQuery
{
    int Turn {get;}
    bool Finished {get;}
    ActualDiscardRecoveryPhaseKey? Phase {get;}
    IReadOnlyList<int> Seats {get;}
    IReadOnlyList<int> ActionOrder(int from,IEnumerable<int> seats);
    MouZhuRanPlayer Player(int seat);
    IEnumerable<T> History<T>()where T:IGameEvent;
    SkillProgram? Program(string skill);
    bool ExactStart(ProgramSkillFrame frame,SkillProgramEffectOp operation);
    bool NativeParent(ProgramSkillFrame frame);
    IReadOnlyList<MouZhuRanCard> Discardable(int seat);
    int KeepValue(int seat,int cardId);
    string CardLabel(int cardId);
    bool Friendly(int owner,int target);
    long MovementSequence {get;}
}
internal interface IMouZhuRanNative:IProgramRuntimeOperations
{
    void PublishState();
    bool Drain(ProgramSkillFrame frame);
    void Finish(ProgramSkillFrame frame);
    void Discard(ProgramSkillFrame frame,int payer,IReadOnlyList<MouZhuRanCard> cards);
    void Damage(ProgramSkillFrame frame,int target);
}
