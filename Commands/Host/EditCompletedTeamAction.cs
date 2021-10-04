﻿using Discord;
using Discord.WebSocket;

namespace WinstonBot.Commands
{
    internal class EditCompletedTeamAction : IAction
    {
        public static string ActionName = "pvm-edit-team";
        public string Name => ActionName;
        public long RoleId => throw new NotImplementedException();

        public async Task HandleAction(ActionContext actionContext)
        {
            var context = (HostActionContext)actionContext;

            var component = context.Component;
            if (!component.Message.Embeds.Any())
            {
                await component.RespondAsync("Message is missing the embed. Please re-create the host message (and don't delete the embed this time)", ephemeral: true);
                return;
            }

            if (!context.TryMarkMessageForEdit(component.Message.Id))
            {
                await component.RespondAsync("This team is already being edited by someone else.", ephemeral: true);
                return;
            }

            var currentEmbed = component.Message.Embeds.First();
            var selectedNames = HostHelpers.ParseNamesToList(currentEmbed.Description);
            if (selectedNames.Count == 0)
            {
                await component.RespondAsync("Not enough people signed up.", ephemeral: true);
                return;
            }

            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var allNames = new List<string>();
            if (context.OriginalSignupsForMessage.ContainsKey(component.Message.Id))
            {
                allNames = Utility.ConvertUserIdListToMentions(guild, context.OriginalSignupsForMessage[component.Message.Id]);
            }
            else
            {
                Console.WriteLine($"[EditCompletedTeamAction] Failed to find message data for {component.Message.Id}. Cannot retrieve original names.");
            }

            List<string> unselectedNames = allNames
                .Where(name => !selectedNames.Contains(name))
                .ToList();

            await component.Message.ModifyAsync(msgProps =>
            {
                msgProps.Content = "Host is finalizing the team, fuck off."; // todo
                msgProps.Embed = Utility.CreateBuilderForEmbed(currentEmbed)
                .WithFooter($"Being edited by {component.User.Username}")
                .Build();
                msgProps.Components = new ComponentBuilder()
                    .WithButton("Edit", $"{EditCompletedTeamAction.ActionName}_{context.BossIndex}", ButtonStyle.Danger, disabled: true)
                    .Build();
            });

            await component.User.SendMessageAsync("Confirm or edit the team." +
                "\nClick the buttons to change who is selected to go." +
                "\nOnce you're done click Confirm Team." +
                "\nYou may continue making changes after you confirm the team by hitting confirm again." +
                "\nOnce you're finished making changes you can dismiss this message.",
                embed: HostHelpers.BuildTeamSelectionEmbed(guild.Id, component.Channel.Id, component.Message.Id, true, selectedNames),
                component: HostHelpers.BuildTeamSelectionComponent(guild, context.BossIndex, selectedNames, unselectedNames));

            await component.DeferAsync();
        }
    }
}
