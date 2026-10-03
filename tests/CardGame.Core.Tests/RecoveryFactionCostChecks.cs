using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class RecoveryPaidWinnerChecks
{
    private const string FactionCostMode = "identity:classic-paid-faction-recovery";
    private const string FactionWinningObserver = "fixture:paid-faction-winning-hp";

    public static void QinwangSilverLionCostPrecedesProviders()
    {
        foreach (var branch in new[] { "keep", "redirect", "legacy", "winner", "target-death" })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(),
                new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
                new StandardClassicGeneralPackage(), new FactionCostFixture(branch));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = FactionCostMode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
            }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, "fixture:paid-faction-owner", game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
            Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats,
                game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion);
            var ownerHp = game.State.Players[0].Hp;
            var ownerHand = game.State.Players[0].HandCount;
            var recipientHp = game.State.Players[1].Hp;
            var target = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Rebel).Seat;
            Accept(game, new UseProgramSkillCommand(0, "classic:qinwang", "request-shu-slash", [], [target],
                game.Revision, Prompt(game)!.PromptId));
            Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-request-cost"));
            Replay(game, registry);
            Answer(game, c => c.Cards.SequenceEqual([armor.Id]));
            if (branch != "legacy")
            {
                Reach(game, p => p.Kind == DecisionKind.RecoveryReplacement);
                var replacement = game.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
                var producer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == replacement.ParentFrameId);
                Require(producer.SkillId == "classic:qinwang" &&
                    producer.PaidFactionRequestCostRecovery is { CostCardId: var paidId } && paidId == armor.Id &&
                    replacement.Return.Continuation == PostEventContinuation.FactionRequestCost &&
                    game.State.Players[0].Hp == ownerHp && game.State.Players[0].HandCount == ownerHand &&
                    game.CardMovements.Count(m => m.CardId == armor.Id && m.Reason.Value == "program.faction-request.cost") == 1,
                    "The real Qinwang equipment cost pauses on its exact paid request before HP, provider selection or repeated payment.");
                Require(branch == "keep" || replacement.Attempt.Candidates.Any(c => c.OwnerSeat == 1 && c.SkillId == "boundary:jiuyuan"),
                    "A real Weidi projection offers the lord's exact Jiuyuan instance to the recovering Wu requester.");
                Replay(game, registry);
                Answer(game, c => branch == "keep" ? c.Parameters.GetValueOrDefault("recovery-action") == "keep" :
                    c.Parameters.GetValueOrDefault("recovery-action") == "redirect" && c.Targets.SequenceEqual([1]));
            }
            if (branch is "winner" or "target-death")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == FactionWinningObserver);
                Replay(game, registry);
                Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Reach(game, p => p.SkillPrompt?.SkillId == FactionWinningObserver &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
                Replay(game, registry);
                var lossTarget = branch == "winner" ? 0 : target;
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([lossTarget]));
                if (branch == "target-death")
                {
                    Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
                    Require(!game.State.Players[target].IsAlive && game.State.Winner == Winner.None &&
                        game.State.Players[0].IsAlive && game.State.Players[0].HandCount == ownerHand + 1 &&
                        game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0 &&
                        !game.Events.Any(e => e.Payload is CardUsedEvent { CardKind: CardKind.Slash }) &&
                        !game.CardMovements.Any(m => m.Reason.Value == "program.faction-request.provider-reward") &&
                        game.CardMovements.Count(m => m.CardId == armor.Id && m.Reason.Value == "program.faction-request.cost") == 1,
                        "A real HP child kills the frozen target while another antagonist survives; recovery returns and the paid request cancels without a stale provider prompt or attack.");
                    Replay(game, registry);
                    continue;
                }
                Complete(game);
                Require(game.State.Status == EngineStatus.Completed && game.State.Winner == Winner.Rebels &&
                    !game.State.Players[0].IsAlive && game.ResolutionStack.Count == 0 &&
                    game.Events.Any(e => e.Payload is ProgramSkillHpLostEvent lost && lost.SkillId == FactionWinningObserver && lost.TargetSeat == 0) &&
                    !game.Events.Any(e => e.Payload is FactionSlashResolvedEvent { Succeeded: true }) &&
                    !game.Events.Any(e => e.Payload is DamageAppliedEvent) &&
                    !game.CardMovements.Any(m => m.Reason.Value is "skill-program.recovery-replacement.reward" or "program.faction-request.provider-reward") &&
                    game.CardMovements.Count(m => m.CardId == armor.Id && m.Reason.Value == "program.faction-request.cost") == 1,
                    "The HP observer's real winning death drains the paid request without provider response, attack, reward or second cost.");
                Replay(game, registry);
                continue;
            }
            Reach(game, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 1 &&
                p.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
            Require(game.ResolutionStack.All(f => f.PaidFactionRequestCostRecovery is null) &&
                !game.ResolutionStack.OfType<RecoveryReplacementFrame>().Any() &&
                game.State.Players[0].Hp == ownerHp + (branch == "redirect" ? 0 : 1) &&
                game.State.Players[1].Hp == recipientHp + (branch == "redirect" ? 1 : 0) &&
                game.State.Players[0].HandCount == ownerHand + (branch == "redirect" ? 1 : 0),
                "Keep, projected redirect and the unchanged no-policy path finish recovery before the first actual provider prompt.");
            Replay(game, registry);
            // AI response commands use the engine's native response path.
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Require(game.Events.Count(e => e.Payload is CardUsedEvent { CardKind: CardKind.Slash, SourceSeat: 0 }) == 1 &&
                game.CardMovements.Count(m => m.Reason.Value == "program.faction-request.provider-reward") == 1 &&
                game.CardMovements.Count(m => m.CardId == armor.Id && m.Reason.Value == "program.faction-request.cost") == 1 &&
                game.State.ProcessingCardCount == 0 && game.ResolutionStack.Count == 0,
                "The resumed request accepts an actual red-card Wusheng provider, finishes one real Slash, and never repays the armor.");
            Replay(game, registry);
        }
    }

    private sealed class FactionCostFixture(string branch) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-paid-faction-recovery", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (branch is "winner" or "target-death")
            {
                var catalog = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                    {"id":"{{FactionWinningObserver}}","revision":1,"triggers":[
                    {"id":"winning-hp","window":"afterHpRecovered","subject":"owner","optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"continue-win","options":[{"id":"continue"}]},
                    {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
                    {"op":"loseHp","target":"selectedTarget","amount":20}]}]}]}
                    """, JsonSerializer.Serialize(new
                    {
                        schemaVersion = 3,
                        skills = new Dictionary<string, object>
                        {
                            [FactionWinningObserver] = new { name = "请求胜负观察", description = "真实回复子链令来源失去体力。",
                                optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                        }
                    }));
                builder.AddSkill(new(FactionWinningObserver, "请求胜负观察", "真实来源死亡")
                { Program = catalog.Programs[FactionWinningObserver] });
            }
            builder.AddGeneral(new("fixture:paid-faction-owner", "吴主公", "supporter", "classic:qinwang", "wu", 6,
                branch == "legacy" ? ["classic:mashu"] : ["boundary:jiuyuan", "classic:mashu"]) { InitialHp = 4 });
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:paid-faction-target-{index}", "蜀伪帝", "supporter", "classic:weidi", "shu", 6,
                    branch is "winner" or "target-death" ? ["classic:wusheng", FactionWinningObserver] : ["classic:wusheng"]) { InitialHp = 2 });
            builder.AddDeck(new("fixture:paid-faction-deck", "固定红色装备", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                new ContentDeckPhysicalCard("classic:silver-lion", Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new(FactionCostMode, "请求支付回复", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:paid-faction-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:paid-faction-owner", "fixture:paid-faction-target-1", "fixture:paid-faction-target-2", "fixture:paid-faction-target-3"]));
        }
    }
}
