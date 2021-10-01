﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using WinstonBot.Services;
using WinstonBot.Data;
using System.Diagnostics;
using WinstonBot.Commands;
using Discord.Net;
using Newtonsoft.Json;

namespace WinstonBot
{
    public class CommandHandler
    {
        public IEnumerable<ICommand> Commands => _commands;

        private readonly DiscordSocketClient _client;
        private IServiceProvider _services;
        private List<ICommand> _commands;

        public CommandHandler(IServiceProvider services, DiscordSocketClient client)
        {
            _client = client;
            _services = services;

            _commands = new List<ICommand>()
            {
                new HostPvmSignup(),
                new ConfigCommand(this) // not great but will do for now.
            };
        }

        public async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            _client.ButtonExecuted += HandleButtonExecuted;
            _client.InteractionCreated += HandleInteractionCreated;

            // Register the commands in all the guilds
            // NOTE: registering the same command will just update it, so we won't hit the 200 command create rate limit.
            foreach (SocketGuild guild in _client.Guilds)
            {
                var adminRoles = guild.Roles.Where(role => role.Permissions.Administrator);

                try
                {
                    foreach (ICommand command in _commands)
                    {
                        Console.WriteLine($"Registering command {command.Name}.");
                        SocketApplicationCommand appCommand = await guild.CreateApplicationCommandAsync(command.BuildCommand());
                        if (appCommand == null)
                        {
                            Console.WriteLine($"Failed to register command: {command.Name}");
                            continue;
                        }

                        command.AppCommandId = appCommand.Id;
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
                foreach (ICommand command in _commands)
                {
                    List<ApplicationCommandPermission> perms = new();
                    if (command.DefaultPermission == ICommand.Permission.AdminOnly)
                    {
                        foreach (var role in adminRoles)
                        {
                            perms.Add(new ApplicationCommandPermission(role, true));
                        }
                    }

                    permDict.Add(command.AppCommandId, perms.ToArray());
                }

                await _client.Rest.BatchEditGuildCommandPermissions(guild.Id, permDict);
            }
        }

        private async Task HandleInteractionCreated(SocketInteraction arg)
        {
            if (arg is SocketSlashCommand slashCommand)
            {
                foreach (ICommand command in _commands)
                {
                    if (command.Name == slashCommand.Data.Name)
                    {
                        var context = new Commands.CommandContext(_client, slashCommand, _services);
                        await command.HandleCommand(context);
                        return;
                    }
                }
            }
        }

        private async Task HandleButtonExecuted(SocketMessageComponent component)
        {
            foreach (ICommand command in _commands)
            {
                foreach (IAction action in command.Actions)
                {
                    if (component.Data.CustomId.StartsWith(action.Name))
                    {
                        // TODO: action could define params and we could parse them in the future.
                        // wouldn't work with the interface though.
                        await action.HandleAction(component);
                        return;
                    }
                }
            }
        }
    }
}
