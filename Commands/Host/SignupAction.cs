﻿using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using WinstonBot.Services;
using WinstonBot.Attributes;
using WinstonBot.Data;
using Microsoft.Extensions.Logging;

namespace WinstonBot.Commands
{
    [Action("pvm-team-signup")]
    internal class SignupAction : ActionBase
    {
        public static string ActionName = "pvm-team-signup";
        private static string PvmRulesChannelMention = "<#884181220462841866>";

        [ActionParam]
        public long BossIndex { get; set; }

        private BossData.Entry BossEntry => BossData.Entries[BossIndex];

        public SignupAction(ILogger logger) : base(logger)
        {
        }

        public override async Task HandleAction(ActionContext context)
        {
            // Re-grab the message as it may have been modified by a concurrent action.
            var message = await context.Channel.GetMessageAsync(context.Message.Id);
            if (!message.Embeds.Any())
            {
                await context.RespondAsync("Message is missing the embed. Please re-create the host message (and don't delete the embed this time)", ephemeral: true);
                return;
            }

            var guild = ((SocketGuildChannel)message.Channel).Guild;
            var configService = context.ServiceProvider.GetRequiredService<ConfigService>();
            List<ulong> rolesForBoss = new();
            if (configService.Configuration.GuildEntries[guild.Id].RolesNeededForBoss.TryGetValue(BossEntry.CommandName, out rolesForBoss))
            {
                if (!Utility.DoesUserHaveAnyRequiredRole((SocketGuildUser)context.User, rolesForBoss))
                {
                    await context.RespondAsync(
                        $"You must have one of the following roles to sign up:\n{Utility.JoinRoleMentions(guild, rolesForBoss)}\n" +
                        $"Please see {PvmRulesChannelMention}.", ephemeral: true);
                    return;
                }
            }

            // Check if the user is suspended from pvm events
            var eventSuspensionService = context.ServiceProvider.GetRequiredService<EventControl>();
            if (eventSuspensionService.IsUserSuspended((SocketGuildUser)context.User))
            {
                var suspensionInfo = eventSuspensionService.GetSuspensionInfo((SocketGuildUser)context.User);
                await context.RespondAsync(
                    $"You are currently suspended from PvM events until {Discord.TimestampTag.FromDateTime(suspensionInfo.Value.Expiry)} for reason: {suspensionInfo.Value.Reason}",
                    ephemeral: true);
                return;
            }

            var currentEmbed = message.Embeds.First();

            var names = HostHelpers.ParseMentionsStringToMentionList(currentEmbed.Description);
            var ids = HostHelpers.ParseMentionListToIdList(names);
            if (ids.Contains(context.User.Id))
            {
                Logger.LogDebug($"{context.User.Mention} {context.Message.Id} is already signed up: ignoring.");
                await context.RespondAsync("You're already signed up.", ephemeral: true);
                return;
            }

            if ((ids.Count + 1) > HostHelpers.MaxSignupsAllowed)
            {
                Logger.LogDebug($"Failed to sign up user {context.User.Mention}: Already at the maximum allowed sign up count.");
                await context.RespondAsync("The max number of sign ups has been reached.", ephemeral: true);
                return;
            }

            Logger.LogInformation($"{context.User.Mention} has signed up for {context.Message.Id}!");

            names.Add(context.User.Mention);

            //The call to UpdateAsync().Wait() instead of await UpdateAsync() is on purpose so it doesn't release the semaphore until the message is updated.
            //Don't listen to VS crying about it :>
            context.UpdateAsync(msgProps =>
            {
                msgProps.Embed = HostHelpers.BuildSignupEmbed(BossIndex, names);
                msgProps.Components = HostHelpers.BuildSignupButtons(BossIndex, HostHelpers.CalculateNumTeams(BossIndex, names.Count));
            }).Wait();
        }
    }
}
