﻿using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinstonBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace WinstonBot.MessageHandlers
{
    public class AoDMessageHandlers
    {
        public static readonly EmoteDatabase.IEmoteDefinition AoDEmote = new EmoteDatabase.CustomEmoteDefinition() { Name = "winstonface" };
        public static readonly EmoteDatabase.IEmoteDefinition CompleteEmoji = new EmoteDatabase.EmojiDefinition() { Name = "\u2705" };

        // TODO: if we want to be able to leave off from where we were if the bot restarts we probably need to serialize the state we were in.
        private enum State
        {
            WaitForQueueCompletion,
            ConfirmTeamSelection,
        }

        public class QueueCompleted : BaseMessageHandler
        {
            public QueueCompleted(Commands.CommandContext context) : base(context)
            {
            }

            public override async Task<bool> ReactionAdded(IUserMessage message, ISocketMessageChannel channel, SocketReaction reaction)
            {
                // 1. for queued, determine who should go based on our rules
                // 2. send result to the specific channel
                // 3. complete reaction will finalize the team

                if (reaction.Emote.Name != CompleteEmoji.Name)
                {
                    return false;
                }

                var emoteDB = ServiceProvider.GetRequiredService<EmoteDatabase>();
                var client = ServiceProvider.GetRequiredService<DiscordSocketClient>();

                var aodEmote = emoteDB.Get(client, AoDEmote);

                List<IUser> userReactions = new List<IUser>();
                IAsyncEnumerable<IReadOnlyCollection<IUser>> reactionUsers = message.GetReactionUsersAsync(aodEmote, 10);
                await foreach (var users in reactionUsers)
                {
                    foreach (IUser user in users)
                    {
                        if (!user.IsBot)
                        {
                            userReactions.Add(user);
                        }
                    }
                }

                foreach (IUser user in userReactions)
                {
                    Console.WriteLine(user.Username);
                }

                var names = userReactions.Select(user => user.Mention);

                if (names.Count() == 0)
                {
                    Console.WriteLine("No one signed up, cannot complete the group.");
                    return false;
                }

                var configService = ServiceProvider.GetRequiredService<ConfigService>();
                SocketTextChannel teamConfirmationChannel = Context.Guild.GetTextChannel(configService.Configuration.TeamConfirmationChannelId);
                if (teamConfirmationChannel == null)
                {
                    await channel.SendMessageAsync("Failed to find team confirmation channel. Please use config set teamconfirmationchannel <channel> to set it.");
                    return false;
                }

                var newMessage = await teamConfirmationChannel.SendMessageAsync("Pending Team is: " + String.Join(' ', names));

                ServiceProvider.GetRequiredService<MessageDatabase>().AddMessage(newMessage.Id, new TeamConfirmation(Context, channel));

                var completeEmote = emoteDB.Get(client, CompleteEmoji);
                await newMessage.AddReactionAsync(completeEmote);
                return true;
            }
        }

        public class TeamConfirmation : BaseMessageHandler
        {
            private ISocketMessageChannel _channelToSendFinalTeamTo;

            public TeamConfirmation(Commands.CommandContext context, ISocketMessageChannel channelToSendFinalTeamTo) : base(context)
            {
                _channelToSendFinalTeamTo = channelToSendFinalTeamTo;
            }

            public override async Task<bool> ReactionAdded(IUserMessage message, ISocketMessageChannel channel, SocketReaction reaction)
            {
                if (reaction.Emote.Name != CompleteEmoji.Name)
                {
                    return false;
                }

                var client = ServiceProvider.GetRequiredService<DiscordSocketClient>();
                List<string> names = message.MentionedUserIds.Select(userId => client.GetUser(userId).Mention).ToList();

                // Send team to main channel
                await _channelToSendFinalTeamTo.SendMessageAsync("Team confirmed: " + String.Join(' ', names));

                // Log team in DB
                // Create cancelation/deletion handler (remove team from DB and send a team confirmation again so they can edit).
                return true;
            }

            public override async Task<bool> MessageRepliedTo(SocketUserMessage messageParam)
            {
                // parse out new team and set this handler as the handler for the new message
                List<string> newNames = messageParam.MentionedUsers.Select(user => user.Mention).ToList();

                var emoteDB = ServiceProvider.GetRequiredService<EmoteDatabase>();
                var client = ServiceProvider.GetRequiredService<DiscordSocketClient>();

                var configService = ServiceProvider.GetRequiredService<ConfigService>();
                SocketTextChannel teamConfirmationChannel = Context.Guild.GetTextChannel(configService.Configuration.TeamConfirmationChannelId);
                if (teamConfirmationChannel == null)
                {
                    await messageParam.Channel.SendMessageAsync("Failed to find team confirmation channel. Please use config set teamconfirmationchannel <channel> to set it.");
                    return false;
                }

                var newMessage = await teamConfirmationChannel.SendMessageAsync("Revised Team is: " + String.Join(' ', newNames));

                ServiceProvider.GetRequiredService<MessageDatabase>().AddMessage(newMessage.Id, new TeamConfirmation(Context, _channelToSendFinalTeamTo));

                await newMessage.AddReactionAsync(emoteDB.Get(client, CompleteEmoji));
                return true;
            }
        }
    }
}
