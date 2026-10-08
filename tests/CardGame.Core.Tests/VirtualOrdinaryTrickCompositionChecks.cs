using System.Text.Json;
using CardGame.Core;

internal static class VirtualOrdinaryTrickCompositionChecks
{
    private const string SkillId = "fixture:virtual-trick-composition";
    private const string SelectAndContest = """
        {"op":"selectTarget","target":"owner","targetKind":"otherLivingWithHand"},
        {"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"},
        """;
    private const string WinningUse = """
        {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"drawTwo","condition":{"kind":"pindianWon","sourceBind":"contest"}}
        """;
    private const string LosingUse = """
        {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"dismantlement","condition":{"kind":"pindianNotWon","sourceBind":"contest"}}
        """;

    public static void FrozenBranchesAndParticipantResourcesAreValidated()
    {
        _ = Load(SelectAndContest + WinningUse + "," + LosingUse);
        foreach (var invalid in new[]
        {
            WinningUse.Replace("\"drawTwo\"", "\"duel\""),
            WinningUse.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            WinningUse.Replace("\"actorRef\":{\"kind\":\"owner\"}", "\"actorRef\":{\"kind\":\"actor\"}"),
            WinningUse.Replace("\"targetRef\":{\"kind\":\"owner\"}", "\"targetRef\":{\"kind\":\"selectedTarget\"}"),
            LosingUse.Replace("\"actorRef\":{\"kind\":\"selectedTarget\"}", "\"actorRef\":{\"kind\":\"owner\"}"),
            WinningUse.Replace("\"sourceBind\":\"contest\"", "\"sourceBind\":\"unknown-result\""),
            WinningUse.Replace("\"pindianWon\"", "\"ownTurn\""),
            WinningUse.Replace("\"outputKind\":\"drawTwo\"", "\"outputKind\":\"drawTwo\",\"sourceBind\":\"material\""),
            WinningUse.Replace("\"outputKind\":\"drawTwo\"", "\"outputKind\":\"drawTwo\",\"useCardActionWindows\":false")
        }) Reject(SelectAndContest + invalid);
        Reject(LosingUse.Replace("\"condition\":{\"kind\":\"pindianNotWon\",\"sourceBind\":\"contest\"}",
            "\"condition\":{\"kind\":\"always\"}"));
        Reject(SelectAndContest + WinningUse, "turnEnding");

        var owner = new PlayerSkillContext(0, 3, 3, 2, TurnPhase.Play, IsOwnTurn: true);
        var other = new PlayerSkillContext(1, 4, 4, 2, TurnPhase.Play);
        var effects = Load(SelectAndContest + WinningUse + "," + LosingUse).Triggers.Single().Effects
            .Where(effect => effect.Op == SkillProgramEffectOp.UseVirtualOrdinaryTrick).ToArray();
        var publicFacts = new ProgramAiPublicContext(2, SelectedTarget: other);
        var unknown = ProgramCompositionAi.Estimate(effects, owner, publicContext: publicFacts);
        var pending = ProgramCompositionAi.Estimate(effects, owner, publicContext: publicFacts with
        {
            PindianWon = _ => false,
            PindianOutcome = _ => null
        });
        var won = ProgramCompositionAi.Estimate(effects, owner, publicContext: publicFacts with
        {
            PindianOutcome = bind => bind == "contest" ? true : null
        });
        var notWon = ProgramCompositionAi.Estimate(effects, owner, publicContext: publicFacts with
        {
            PindianOutcome = bind => bind == "contest" ? false : null
        });
        Require(won.Hint.OwnerDraw == 2 && won.Score > 0d && notWon.Hint.OwnerDraw == 0 && notWon.Score < 0d,
            "A frozen win must price only self DrawTwo; a frozen non-win must price only opponent Dismantlement.");
        Require(unknown.Score == pending.Score && unknown.Score == (won.Score + notWon.Score) / 2d,
            "A not-yet-frozen contest must retain a public branch prior, even if the older boolean callback defaults false.");
        var emptyOwner = owner with { HandCount = 0 };
        Require(ProgramCompositionAi.Estimate(effects, emptyOwner, publicContext: publicFacts with { PindianOutcome = _ => false }).Score == 0d &&
            ProgramCompositionAi.Estimate(effects, emptyOwner, publicContext: publicFacts with { PindianOutcome = _ => false, EquipmentCardCount = 1 }).Score < 0d,
            "The loss estimate must use public eligible cards and must include known equipment when the hand is empty.");
        var reciprocal = Load(SelectAndContest + """
            {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"selectedTarget"},"outputKind":"drawTwo"},
            {"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"dismantlement"}
            """).Triggers.Single().Effects.Where(effect => effect.Op == SkillProgramEffectOp.UseVirtualOrdinaryTrick).ToArray();
        var reciprocalEstimate = ProgramCompositionAi.Estimate(reciprocal, owner, publicContext: publicFacts);
        Require(reciprocalEstimate.Hint.OwnerDraw == 0 && reciprocalEstimate.Hint.TargetDraw == 2 &&
            reciprocalEstimate.Hint.TargetValueAdjustment < 0d,
            "Participant references must price the actual other recipient, rather than awarding the owner an opponent draw.");
    }

    private static SkillProgram Load(string effects, string window = "playEnding") => SkillProgramCatalog.Load($$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{SkillId}}","revision":1,
        "triggers":[{"id":"run","window":"{{window}}","subject":"owner","optional":true,"effects":[{{effects}}]}]}]}
        """, JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [SkillId] = new { name = "虚拟锦囊组合", description = "共享能力检查" } }
        })).Programs[SkillId];

    private static void Reject(string effects, string window = "playEnding")
    {
        try { _ = Load(effects, window); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("Invalid skill program", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("An invalid virtual ordinary-trick resource or participant contract was accepted.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
