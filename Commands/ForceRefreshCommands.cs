﻿using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using WinstonBot.Attributes;

namespace WinstonBot.Commands
{
    [Command("force-refresh-commands", "Delete all applications commands and re-create them", DefaultPermission.AdminOnly)]
    public class ForceRefreshCommands : CommandBase
    {
        public async override Task HandleCommand(CommandContext context)
        {
            if (context.Channel is SocketGuildChannel channel)
            {
                Console.WriteLine("Clearing all commands for this bot from guild:");
                await channel.Guild.DeleteApplicationCommandsAsync();

                Console.WriteLine("Registering commands");
                await RegisterCommands(context.Client, channel.Guild);

                await context.RespondAsync("All commands refreshed", ephemeral: true);
            }
        }

        public static async Task RegisterCommands(DiscordSocketClient client, SocketGuild guild)
        {
            // Register the commands in all the guilds
            // NOTE: registering the same command will just update it, so we won't hit the 200 command create rate limit.

            // TODO: batch update breaks because we pass in more than 10 roles in the dict since there's 17 admin roles.
            //var adminRoles = guild.Roles.Where(role => role.Permissions.Administrator);
            var adminRoles = guild.Roles.Where(role => role.Id == 773757083904114689);

            Dictionary<string, ulong> appCommandIds = new();

            try
            {
                foreach (CommandInfo commandInfo in CommandHandler.CommandEntries.Values)
                {
                    Console.WriteLine($"Building command {commandInfo.Name}");
                    var commandBuilder = CommandBuilder.BuildSlashCommand(commandInfo);
                    SocketApplicationCommand appCommand = await guild.CreateApplicationCommandAsync(commandBuilder.Build());
                    commandInfo.AppCommandId = appCommand.Id;
                    appCommandIds.Add(commandInfo.Name, appCommand.Id);
                }
            }
            catch (ApplicationCommandException ex)
            {
                // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                var json = JsonConvert.SerializeObject(ex.Error, Formatting.Indented);

                // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                Console.WriteLine(json);
            }

            // Setup default command permissions
            var permDict = new Dictionary<ulong, ApplicationCommandPermission[]>();
            foreach (CommandInfo commandInfo in CommandHandler.CommandEntries.Values)
            {
                List<ApplicationCommandPermission> perms = new();
                if (commandInfo.DefaultPermission == DefaultPermission.AdminOnly)
                {
                    foreach (var role in adminRoles)
                    {
                        perms.Add(new ApplicationCommandPermission(role, true));
                    }
                }
                else
                {
                    perms.Add(new ApplicationCommandPermission(guild.EveryoneRole, true));
                }

                ulong id = appCommandIds[commandInfo.Name];
                permDict.Add(id, perms.ToArray());
            }

            await client.Rest.BatchEditGuildCommandPermissions(guild.Id, permDict);
        }
    }
}
