﻿using Discord;
using WinstonBot.Data;
using WinstonBot.Attributes;
using Microsoft.Extensions.Logging;
using Discord.WebSocket;

namespace WinstonBot.Commands.HostPvm
{
    [Command(
    "host-pvm",
    "Create a new pvm event",
    actions: new Type[] {
        typeof(ChooseRoleAction),
        typeof(CompleteTeamAction),
    })]
    [ScheduableCommand]
    public class HostPvmCommand : CommandBase
    {
        [CommandOption("boss", "The boss to create an event for.", dataProvider: typeof(BossChoiceDataProvider))]
        public long BossIndex { get; set; }

        [CommandOption("message", "An optional message to display.", required: false)]
        public string? Message { get; set; }

        public HostPvmCommand(ILogger logger) : base(logger)
        {
        }

        public async override Task HandleCommand(CommandContext context)
        {
            var entry = BossData.Entries[BossIndex];

            Embed embed;
            MessageComponent component;
            var roles = Helpers.GetRuntimeRoles();
            Helpers.BuildSignup(roles, entry, context.Guild, out embed, out component);

            await context.RespondAsync(text:Message, embed: embed, component: component);
        }
    }
}
