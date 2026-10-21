namespace CardGame.Core;
internal interface IMouXuShengFacts : IProgramSelectionQuery
{
    int Turn { get; }
    int TurnOwner { get; }
    long MovementSequence { get; }
    bool Finished { get; }
    bool Alive(int seat);
    bool Human(int seat);
    string CardLabel(int id);
    bool Friendly(int owner, int other);
    int KeepValue(int owner, int id);
    SkillProgram? Program(string id);
    IEnumerable<T> History<T>() where T : IGameEvent;
    MouXuShengUse? Use(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    bool DamageParent(ProgramSkillFrame frame);
    bool SourcedDamage(ProgramSkillWindowContext context, int owner);
    IReadOnlyList<MouXuShengCard> Hand(int seat);
    bool Discardable(int chooser, MouXuShengCard card);
    CardLocation Location(int id);
}
internal interface IMouXuShengNative : IProgramRuntimeOperations
{
    void PublishState();
    void Finish(ProgramSkillFrame frame);
    bool Drain(ProgramSkillFrame frame);
    void Boost(ProgramSkillFrame frame);
    void Discard(ProgramSkillFrame frame);
    void Claim(ProgramSkillFrame frame, IReadOnlyList<int> ids);
}
