namespace CardGame.Core;
internal sealed record MouZhangHePlayer(int Seat,bool Alive,bool Human,int HandCount,int FieldCount,int AllCardCount);
internal sealed record MouZhangHeMovement(long Sequence,int CardId,CardLocation From,CardLocation To,string Reason);
internal interface IMouZhangHeFacts:IProgramSelectionQuery
{
    int Turn {get;}
    int TurnOwner {get;}
    bool Finished {get;}
    IReadOnlyList<int> Seats {get;}
    MouZhangHePlayer Player(int seat);
    SkillProgram? Program(string id);
    IEnumerable<T> History<T>() where T:IGameEvent;
    IReadOnlyList<MouZhangHeMovement> Movements {get;}
    CardLocation Location(int cardId);
    bool ExactStart(ProgramSkillFrame frame,SkillProgramEffectOp operation);
    bool NativeParent(ProgramSkillFrame frame);
    IReadOnlyList<MouZhangHeCard> Discardable(int owner);
    IReadOnlyList<MouZhangHeCard> FieldCards {get;}
    bool CanMoveField(int cardId,int destination);
    IReadOnlyList<PromptChoice> HandChoices(ProgramSkillFrame frame,int target);
    int ResolveHandChoice(PromptChoice choice,int subject);
    int KeepValue(int owner,int cardId);
    bool Friendly(int owner,int target);
    long MovementSequence {get;}
    ProgramPhaseSchedule? Schedule {get;}
    bool FirstNativeChild(ProgramSkillFrame frame,ResolutionFrame child);
}
internal interface IMouZhangHeNative:IProgramRuntimeOperations
{
    void PublishState();
    bool Drain(ProgramSkillFrame frame);
    void Finish(ProgramSkillFrame frame);
    void Discard(ProgramSkillFrame frame,MouZhangHeCard card);
    void Obtain(ProgramSkillFrame frame,int target,int cardId);
    void MoveField(ProgramSkillFrame frame,int cardId,int target);
    void ApplySkips(ProgramSkillFrame frame,IReadOnlyList<SkillProgramTurnPhase> phases);
    void OpenSkipWindow(MouZhangHeSkipEntitlement entitlement,ProgramActualPhaseSkipContext nativeReturn);
    void ResumeSkip(ProgramLifecycleTriggerWindowFrame window);
    void AdvanceLifecycleCursor(ProgramLifecycleTriggerWindowFrame window);
    bool BeginExtraPlay(ProgramSkillFrame frame);
    void ResumeExtraPlay(ProgramPhaseSchedule schedule,bool cancel);
    bool AdvanceObserverChildren();
}
