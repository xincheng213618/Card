using System.Reflection;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class DecisionContextChecks
{
    public static void Semantics()
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var snapshot = Program.Engine(vm).CreateSnapshot(0);
        // These presentation-only variants cover different meanings of the optional source/target fields.
        var prompt = new PendingDecision(DecisionKind.RescueDying, 0, "AI 2 进入濒死状态。", [], [], SourceSeat: 1) { TargetSeat = 1 };
        var context = DecisionContext.From(snapshot with { PendingDecision = prompt })!;
        Program.Assert(context.SourceSeat is null && context.TargetSeat == 1 && context.TargetLabel == "等待救援",
            "Rescue source must not be presented as the attacker.");
        Program.Assert(context.Title.Contains(snapshot.Players[1].GeneralName) && !context.Description.Contains("AI 2"),
            "Public general and seat names must identify the rescue target.");
        var draw = prompt with
        {
            Kind = DecisionKind.Nullification,
            SourceSeat = 1,
            TargetSeat = null,
            IncomingCard = CardKind.DrawTwo,
            Prompt = "上一张【无懈可击】使【无中生有】暂时失效，是否继续使用【无懈可击】？"
        };
        context = DecisionContext.From(snapshot with { PendingDecision = draw })!;
        Program.Assert(context.TargetSeat == 1 && !context.Title.Contains("群体") && context.Description == draw.Prompt,
            "DrawTwo must identify its beneficiary and preserve the counter-Nullification explanation.");
        var group = draw with { IncomingCard = CardKind.PeachGarden };
        context = DecisionContext.From(snapshot with { PendingDecision = group })!;
        Program.Assert(context.TargetSeat is null && context.Title.Contains("群体效果"), "A group window must not imply the responder is its only target.");
        var counter = group with { Choices = [CounterChoice("1")] };
        context = DecisionContext.From(snapshot with { PendingDecision = counter })!;
        Program.Assert(context.ResponseStatusTitle == "等待你的无懈 · 第2层" && context.HasResponseStatus && context.Description == counter.Prompt,
            "The persistent counterspell heading must use the current public chain depth without replacing the engine's actual effect explanation.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = counter with { Choices = [CounterChoice("0")] } })!
            .ResponseStatusTitle == "等待你的无懈 · 第1层", "The initial public counterspell window must name its first layer.");
        foreach (var invalid in new[] { "-1", "2147483647", "not-a-depth", "" })
            Program.Assert(DecisionContext.From(snapshot with { PendingDecision = counter with { Choices = [CounterChoice(invalid)] } })!
                .ResponseStatusTitle == "等待你的无懈", "Unusable public parameters must not invent or overflow a response layer.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = counter with { Choices = [CounterChoice("1"), CounterChoice("2")] } })!
            .ResponseStatusTitle == "等待你的无懈", "Conflicting public response layers must not silently pick one choice.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = counter with { Choices = [] } })!
            .ResponseStatusTitle == "等待你的无懈", "Missing response parameters must not infer a layer from a prompt description or ChoiceId.");
        var duel = prompt with { Kind = DecisionKind.RespondSlash, IncomingCard = CardKind.Duel, SourceSeat = 2, TargetSeat = null };
        context = DecisionContext.From(snapshot with { PendingDecision = duel })!;
        Program.Assert(context.SourceSeat == 2 && context.TargetSeat == 0, "Duel must use the current opposing responder, not the active turn seat.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = duel with { PlayerSeat = 3 } }) is null,
            "Another player's private prompt was projected into the local context.");
        Program.Assert(DecisionContext.From(snapshot with { PendingDecision = null }) is null, "Resolved prompts must clear context.");

        // Presentation-only prompt variants: the actor is distinct from both
        // source and effect target, and has no card/choice data to infer from.
        var projectStatus = typeof(MainViewModel).GetMethod("ResponseStatusFor", BindingFlags.Static | BindingFlags.NonPublic)!;
        (DecisionKind Kind, CardKind? Required, string Status)[] statuses =
        [
            (DecisionKind.RespondDodge, null, "待打闪"), (DecisionKind.RespondDodge, CardKind.Slash, "待打杀"),
            (DecisionKind.RespondSlash, null, "待打杀"), (DecisionKind.RespondSlash, CardKind.Dodge, "待打闪"),
            (DecisionKind.RescueDying, null, "待救援"), (DecisionKind.Nullification, null, "待无懈"),
            (DecisionKind.FireAttackReveal, null, "待展示"), (DecisionKind.FireAttackDiscard, null, "待弃牌"),
            (DecisionKind.PlayCard, null, ""), (DecisionKind.DiscardCards, null, ""),
            (DecisionKind.SelectGeneral, null, ""), (DecisionKind.ProgramTrigger, null, "")
        ];
        foreach (var item in statuses)
        {
            var response = prompt with { Kind = item.Kind, PlayerSeat = snapshot.HumanSeat, SourceSeat = 2, TargetSeat = 1, RequiredCardKind = item.Required };
            var view = snapshot with { PendingDecision = response };
            var responseContext = DecisionContext.From(view);
            Program.Assert(responseContext?.HasResponseStatus == (item.Status.Length > 0) || responseContext is null && item.Status.Length == 0,
                "Only the current supported response prompt may show a persistent response heading.");
            if (item.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)
                Program.Assert(responseContext!.ResponseStatusTitle == (item.Status == "待打闪" ? "等待你打出【闪】" : "等待你打出【杀】"),
                    "The persistent heading must follow the prompt's public required card kind rather than its nominal response kind.");
            foreach (var seat in snapshot.Players.Select(player => player.Seat))
                Program.Assert(Status(view, seat) == (seat == snapshot.HumanSeat ? item.Status : string.Empty),
                    "Only the public prompt's exact human responder may receive a persistent response status, regardless of effect roles.");
            var foreign = view with { PendingDecision = response with { PlayerSeat = 3 } };
            Program.Assert(snapshot.Players.All(player => Status(foreign, player.Seat).Length == 0),
                "A foreign private prompt must not project even a persistent responder status.");
        }
        Program.Assert(snapshot.Players.All(player => Status(snapshot with { PendingDecision = null }, player.Seat).Length == 0),
            "Resolved prompts must clear every persistent responder status.");
        foreach (var alias in new[]
        {
            prompt with { Kind = DecisionKind.RespondDodge, Prompt = "是否质疑？", Choices =
                [new(new ChoiceId("question"), "质疑", [], [], new Dictionary<string, string> { ["declaration-answer"] = "challenge" })] },
            prompt with { Kind = DecisionKind.RespondSlash, RequiredCardKind = CardKind.Slash, Prompt = "先弃置一张牌。", Choices =
                [new(new ChoiceId("cost"), "弃置", [], [], new Dictionary<string, string> { ["response"] = "faction-request-cost" })] }
        })
        {
            var view = snapshot with { PendingDecision = alias };
            var aliasContext = DecisionContext.From(view)!;
            Program.Assert(!aliasContext.HasResponseStatus && aliasContext.Description == alias.Prompt &&
                view.Players.All(player => Status(view, player.Seat).Length == 0),
                "Declaration challenges and paid faction requests reuse response kinds but must not claim that a Slash or Dodge is required.");
        }
        string Status(GameSnapshot view, int seat) => (string)projectStatus.Invoke(null, [view, seat])!;
        static PromptChoice CounterChoice(string depth) => new(new ChoiceId("arbitrary-presentation-id"), "公开响应", [], [],
            new Dictionary<string, string> { ["response"] = "nullification", ["chain-depth"] = depth });
    }

    public static void VerifyLive(MainViewModel vm)
    {
        var engine = Program.Engine(vm);
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0, true));
        var view = engine.CreateSnapshot(0);
        var expected = DecisionContext.From(view);
        Program.Assert(vm.CurrentDecisionContext == expected, "Displayed context does not match the actual private prompt.");
        Program.Assert(expected == DecisionContext.From(engine.CreateSnapshot(0, true)), "Hidden identities or opponent hands changed response context.");
        foreach (var seat in vm.Seats)
        {
            var label = expected?.TargetSeat == seat.Seat ? expected.TargetLabel : expected?.SourceSeat == seat.Seat ? "效果来源" : string.Empty;
            Program.Assert(seat.DecisionRoleLabel == label, "A seat retained a stale effect marker.");
            var response = view.PendingDecision;
            var questionOrCost = response is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } &&
                response.Choices.Any(choice => choice.Parameters.GetValueOrDefault("declaration-answer") is "pass" or "challenge" ||
                    choice.Parameters.GetValueOrDefault("response") == "faction-request-cost");
            var status = response is not null && !questionOrCost && response.PlayerSeat == view.HumanSeat && response.PlayerSeat == seat.Seat
                ? response.Kind switch
                {
                    DecisionKind.RespondDodge or DecisionKind.RespondSlash =>
                        (response.RequiredCardKind ?? (response.Kind == DecisionKind.RespondDodge ? CardKind.Dodge : CardKind.Slash)) switch
                        { CardKind.Dodge => "待打闪", CardKind.Slash => "待打杀", _ => string.Empty },
                    DecisionKind.RescueDying => "待救援", DecisionKind.Nullification => "待无懈",
                    DecisionKind.FireAttackReveal => "待展示", DecisionKind.FireAttackDiscard => "待弃牌", _ => string.Empty
                } : string.Empty;
            Program.Assert(seat.ResponseStatusText == status && seat.HasResponseStatus == (status.Length > 0) &&
                seat.IsCurrent == (seat.Seat == view.CurrentSeat && view.Status != EngineStatus.Completed),
                "The live persistent responder status must follow only the safe current prompt without changing the active turn seat.");
        }
        Program.Assert(state == SnapshotJson.Serialize(engine.CreateSnapshot(0, true)), "Reading response context changed the match.");
    }
}
