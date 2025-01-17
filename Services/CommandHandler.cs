﻿using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Reflection;
using WinstonBot.Commands;
using WinstonBot.Services;
using WinstonBot.Attributes;
using Discord.Addons.Hosting;
using Microsoft.Extensions.Logging;
using Discord.Addons.Hosting.Util;
using Microsoft.Extensions.Hosting;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace WinstonBot
{
    public class CommandOptionInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string PropertyName { get; set; }
        public Type Type { get; set; }
        public bool Required { get; set; }
        public Type? ChoiceProviderType { get; set; }
    }

    public class ActionOptionInfo
    {
        public PropertyInfo Property { get; set; }
    }

    public class ActionInfo
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public List<ActionOptionInfo>? Options { get; set; }
    }

    public class CommandInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DefaultPermission DefaultPermission { get; set; }
        public Type Type { get; set; }
        public List<CommandOptionInfo> Options { get; set; }
        public Dictionary<string, ActionInfo>? Actions { get; set; }

        public ulong AppCommandId { get; set; }
    }

    public class SubCommandInfo : CommandInfo
    {
        public Type ParentCommandType { get; set; }
        public bool HasDynamicSubCommands { get; set; }
    }

    // Mirrors SocketSlashCommandDataOption
    public class CommandDataOption
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public ApplicationCommandOptionType Type { get; set; }
        public List<CommandDataOption>? Options { get; set; }
    }

    public class CommandHandler : DiscordClientService
    {
        private IServiceProvider _services;

        private static Dictionary<string, CommandInfo> _commandEntries = new();
        private static List<SubCommandInfo> _subCommandEntries = new();
        private static Dictionary<string, ActionInfo> _actionEntries = new();
        private static HashSet<string> _allowedCommands = new();

        public static IReadOnlyDictionary<string, CommandInfo> CommandEntries => new ReadOnlyDictionary<string, CommandInfo>(_commandEntries);
        public static IReadOnlyCollection<SubCommandInfo> SubCommandEntries => new ReadOnlyCollection<SubCommandInfo>(_subCommandEntries);
        public static IReadOnlyDictionary<string, ActionInfo> ActionEntries => new ReadOnlyDictionary<string, ActionInfo>(_actionEntries);

        private static readonly HashSet<Type> ValidOptionTypes = new()
        {
            typeof(string),
            typeof(long),
            typeof(bool),
            typeof(double),
            typeof(SocketGuildUser),
            typeof(SocketGuildChannel),
            typeof(SocketRole)
        };

        private class OptionTypeException : Exception
        {
            public OptionTypeException(PropertyInfo info)
                : base($"Invalid option type {info.PropertyType} on property {info.Name} in {info.DeclaringType}") { }
        }

        public CommandHandler(DiscordSocketClient client, ILogger<CommandHandler> logger, IServiceProvider services, IConfiguration configuration)
            : base(client, logger)
        {
            logger.LogInformation($"Running WinstonBot Version: {Assembly.GetEntryAssembly().GetName().Version}");

            _services = services;

            // Optional config option to only register certain commands. This avoid polluting the slash command table
            // with all commands when we're only testing certain ones.
            var allowedCommands = configuration.GetSection("allowed_commands");
            if (allowedCommands != null)
            {
                foreach (var command in allowedCommands.GetChildren())
                {
                    _allowedCommands.Add(command.Value);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await LoadCommands();

            await Client.WaitForReadyAsync(cancellationToken);
            await InstallCommandsAsync();
        }

        private async Task LoadCommands()
        {
            Logger.LogInformation("Loading commands...");

            List<CommandOptionInfo> GetOptions(TypeInfo info)
            {
                return info.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance)
                    .Where(prop => prop.GetCustomAttribute<CommandOptionAttribute>() != null)
                    .Select(prop =>
                    {
                        if (!ValidOptionTypes.Contains(prop.PropertyType))
                        {
                            throw new OptionTypeException(prop);
                        }

                        var optionInfo = prop.GetCustomAttribute<CommandOptionAttribute>();
                        return new CommandOptionInfo()
                        {
                            Name = optionInfo.Name,
                            Description = optionInfo.Description,
                            PropertyName = prop.Name,
                            Required = optionInfo.Required,
                            Type = prop.PropertyType,
                            ChoiceProviderType = optionInfo.ChoiceDataProvider
                        };
                    })
                    .ToList();
            }

            Dictionary<string, ActionInfo>? GetActions(IEnumerable<Type>? actionTypes)
            {
                if (actionTypes == null)
                {
                    return null;
                }

                return actionTypes.Select(t =>
                {
                    var att = t.GetCustomAttribute<ActionAttribute>();
                    if (att == null)
                    {
                        throw new ArgumentException($"Expected ActionAttribute on type {t.Name}");
                    }
                    return _actionEntries[att.Name];
                })
                .ToDictionary(a => a.Name, a => a);
            }

            var assembly = Assembly.GetEntryAssembly();

            // Build the list of actions first
            foreach (TypeInfo typeInfo in assembly.DefinedTypes)
            {
                var actionAttribute = typeInfo.GetCustomAttribute<ActionAttribute>();
                if (actionAttribute != null)
                {
                    var actionInfo = new ActionInfo()
                    {
                        Name = actionAttribute.Name,
                        Type = typeInfo,
                        Options = typeInfo.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance)
                            .Where(prop => prop.GetCustomAttribute<ActionParamAttribute>() != null)
                            .Select(prop => new ActionOptionInfo() { Property = prop })
                            .ToList()
                    };

                    if (!_actionEntries.TryAdd(actionAttribute.Name, actionInfo))
                    {
                        throw new Exception($"Tried to register duplicate action: {actionAttribute.Name}");
                    }
                }
            }

            // Load commands
            foreach (TypeInfo typeInfo in assembly.DefinedTypes)
            {
                var commandAttribute = typeInfo.GetCustomAttribute<CommandAttribute>();
                if (commandAttribute != null && ShouldLoadCommand(commandAttribute.Name))
                {
                    var commandInfo = new CommandInfo()
                    {
                        Name = commandAttribute.Name,
                        Description = commandAttribute.Description,
                        DefaultPermission = commandAttribute.DefaultPermission,
                        Type = typeInfo,
                        Options = GetOptions(typeInfo),
                        Actions = GetActions(commandAttribute.Actions)
                    };

                    if (!_commandEntries.TryAdd(commandAttribute.Name, commandInfo))
                    {
                        throw new Exception($"Tried to register duplicate command: {commandAttribute.Name}");
                    }
                }

                var subCommandAttribute = typeInfo.GetCustomAttribute<SubCommandAttribute>();
                if (subCommandAttribute != null)
                {
                    if (subCommandAttribute.ParentCommand.GetCustomAttribute<SubCommandAttribute>() == null &&
                        subCommandAttribute.ParentCommand.GetCustomAttribute<CommandAttribute>() == null)
                    {
                        throw new Exception($"ParentCommand for type {typeInfo.Name} must have either a Command or SubCommand attribute");
                    }

                    CommandAttribute parentCommand = GetParentCommand(subCommandAttribute);

                    var subCommandInfo = new SubCommandInfo()
                    {
                        Name = subCommandAttribute.Name,
                        Description = subCommandAttribute.Description,
                        Type = typeInfo,
                        ParentCommandType = subCommandAttribute.ParentCommand,
                        Options = GetOptions(typeInfo),
                        Actions = GetActions(subCommandAttribute.Actions),
                        HasDynamicSubCommands = subCommandAttribute.HasDynamicSubCommands,
                        DefaultPermission = subCommandAttribute.DefaultPermissionOverride != null
                            ? subCommandAttribute.DefaultPermissionOverride.Value
                            : parentCommand.DefaultPermission
                    };

                    _subCommandEntries.Add(subCommandInfo);
                }
            }
        }

        private bool ShouldLoadCommand(string name)
        {
            return !_allowedCommands.Any() || _allowedCommands.Contains(name);
        }

        private CommandAttribute GetParentCommand(SubCommandAttribute subCommand)
        {
            CommandAttribute? parentCommand = subCommand.ParentCommand.GetCustomAttribute<CommandAttribute>();
            if (parentCommand == null)
            {
                return GetParentCommand(subCommand.ParentCommand.GetCustomAttribute<SubCommandAttribute>());
            }
            return parentCommand;
        }

        private async Task InstallCommandsAsync()
        {
            // Hook the MessageReceived event into our command handler
            Client.ButtonExecuted += HandleButtonExecuted;
            Client.InteractionCreated += HandleInteractionCreated;

            foreach (SocketGuild guild in Client.Guilds)
            {
                Logger.LogInformation($"Registering commands for guild: {guild.Name}");

                await ForceRefreshCommands.RegisterCommands(Client, guild, Logger);

                Logger.LogInformation($"Finished Registering commands for guild: {guild.Name}");
            }
        }

        private async Task HandleInteractionCreated(SocketInteraction arg)
        {
            var configService = _services.GetRequiredService<ConfigService>();
            var slashCommand = arg as SocketSlashCommand;
            if (slashCommand == null)
            {
                return;
            }

            if (!_commandEntries.ContainsKey(slashCommand.CommandName))
            {
                return;
            }

            var command = _commandEntries[slashCommand.CommandName];

            if (arg.Channel is SocketGuildChannel guildChannel)
            {
                var user = (SocketGuildUser)arg.User;
                var requiredRoleIds = GetRequiredRolesForCommand(configService, guildChannel.Guild, command.Name);
                if (!Utility.DoesUserHaveAnyRequiredRole(user, requiredRoleIds))
                {
                    await arg.RespondAsync($"You must have one of the following roles to use this command: {Utility.JoinRoleMentions(guildChannel.Guild, requiredRoleIds)}.", ephemeral: true);
                    return;
                }
            }

            // Allow the command to build a custom context if desired.
            var createContextFunction = Utility.GetInheritedStaticMethod(command.Type, CommandBase.CreateContextName);
            var context = createContextFunction?.Invoke(null, new object[] { Client, slashCommand, _services }) as CommandContext;
            if (context == null)
            {
                throw new ArgumentNullException($"Failed to create context for command {command.Name}");
            }

            // Translate the options to our own serializable version
            var options = BuildCommandDataOptions(slashCommand.Data.Options);

            Logger.LogDebug($"{arg.User.Username}#{arg.User.Discriminator} initiated command {command.Name} {CommandOptionsToString(options)}");

            await ExecuteCommand(command, context, options, Logger, _services);
        }

        private static void InjectCommandPropertyValues(
            CommandInfo commandInfo,
            CommandBase commandInstance,
            IEnumerable<CommandDataOption>? dataOptions,
            ILogger logger)
        {
            if (dataOptions == null)
            {
                return;
            }

            HashSet<string> setProperties = new();
            foreach (var optionData in dataOptions)
            {
                // Find the metadata for this option (defined by the CommandOptionAttribute)
                CommandOptionInfo? optionInfo = commandInfo.Options.Find(op => op.Name == optionData.Name);
                if (optionInfo == null)
                {
                    logger.LogError($"Could not find option {optionData.Name} for command {commandInfo.Name}");
                    continue;
                }

                PropertyInfo? property = commandInfo.Type.GetProperty(optionInfo.PropertyName);
                if (property == null)
                {
                    throw new Exception($"Failed to get property {optionInfo.PropertyName} from type {commandInfo.Type}");
                }

                // Set the value of the property on the command class with the value passed in by the user.
                setProperties.Add(optionData.Name);
                property.SetValue(commandInstance, optionData.Value);
            }

            var requiredParamsNotSet = commandInfo.Options
                .Where(opt => opt.Required && !setProperties.Contains(opt.Name))
                .Select(opt => opt.Name);
            if (requiredParamsNotSet.Any())
            {
                throw new ArgumentException($"Missing required arguments for command {commandInfo.Name}: {String.Join(',', requiredParamsNotSet)}");
            }
        }

        public static async Task ExecuteCommand(
            CommandInfo command,
            CommandContext context,
            IEnumerable<CommandDataOption>? dataOptions,
            ILogger logger,
            IServiceProvider services)
        {

            CommandBase? commandInstance = null;
            CommandInfo commandInfo = command;
            // Subcommands can be nested within other subcommands, so traverse downwards until we find the lowest level subcommand.
            // This will give us the info for the actual subcommand we need to run and the options for that subcommand.
            var subCommandResult = FindDeepestSubCommand(command, dataOptions);
            if (subCommandResult != null)
            {
                commandInfo = subCommandResult.Value.Key;
                dataOptions = subCommandResult.Value.Value;

                var commandLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger(commandInfo.Type);
                commandInstance = Activator.CreateInstance(commandInfo.Type, new object[] { commandLogger }) as CommandBase;
            }
            else
            {
                var commandLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger(command.Type);
                commandInstance = Activator.CreateInstance(command.Type, new object[] { commandLogger }) as CommandBase;
            }

            if (commandInstance == null)
            {
                throw new Exception($"Failed to construct command {command.Type}");
            }

            if (dataOptions != null)
            {
                // If the first option is a subcommand that means that subcommand isn't defined as a class with the SubCommand attribute.
                // In these cases we allow the parent to handle this subcommand.
                if (dataOptions.Count() == 1 &&
                    dataOptions.First().Type == ApplicationCommandOptionType.SubCommand)
                {
                    if (!commandInstance.WantsToHandleSubCommands)
                    {
                        throw new Exception($"Unhandled SubCommand: {dataOptions.First().Name}, Parent: {commandInfo.Name}");
                    }

                    logger.LogInformation($"SubCommand {commandInfo.Name} is handling itself.");
                    await commandInstance.HandleSubCommand(context, commandInfo, dataOptions);
                    return;
                }

                InjectCommandPropertyValues(commandInfo, commandInstance, dataOptions, logger);
            }

            logger.LogInformation($"Command {command.Name} handling interaction");

            try
            {
                await commandInstance.HandleCommand(context);
            }
            catch (InvalidCommandArgumentException ex)
            {
                logger.LogError($"Invalid Command Argument: {ex.Message}");
                await context.RespondAsync($"Invalid Command Argument: {ex.Message}", ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error running command {command.Name}: {ex}");
#if !DEBUG
                await context.SendMessageAsync(embed: BuildErrorEmbed(ex, context.User, $"Command {command.Name} threw an exception.", CommandOptionsToString(dataOptions)));
#endif
            }
        }

        private Dictionary<ulong, Semaphore> _messageSemaphores = new();

        private async Task HandleButtonExecuted(SocketMessageComponent component)
        {
            // For now just assume actions are unique per command.
            // If we want to change this in the future we'll have to implement the interaction service properly.
            CommandInfo? interactionOwner = null;
            ActionInfo? action = null;
            foreach (var pair in _commandEntries)
            {
                if (pair.Value.Actions == null)
                {
                    continue;
                }

                foreach ((string name, ActionInfo info) in pair.Value.Actions)
                {
                    if (component.Data.CustomId.StartsWith(name))
                    {
                        action = info;
                        interactionOwner = pair.Value;
                        break;
                    }
                }
            }

            if (interactionOwner == null || action == null)
            {
                Logger.LogError($"No action found for interaction: {component.Data.CustomId}");
                return;
            }

            // If this was executed in a guild channel check the user has permission to use it.
            if (component.Channel is SocketGuildChannel guildChannel)
            {
                var user = (SocketGuildUser)component.User;
                // How do we know what command this action belongs to if an action can belong to multiple commands?
                // should we encode something in the customid?
                var configService = _services.GetRequiredService<ConfigService>();
                var requiredRoleIds = GetRequiredRolesForAction(configService, guildChannel.Guild, interactionOwner.Name, action.Name);
                if (!Utility.DoesUserHaveAnyRequiredRole(user, requiredRoleIds))
                {
                    await component.RespondAsync($"You must have one of the following roles to do this action: {Utility.JoinRoleMentions(guildChannel.Guild, requiredRoleIds)}.", ephemeral: true);
                    return;
                }
            }
            
            var actionLogger = _services.GetRequiredService<ILoggerFactory>().CreateLogger(action.Type);

            ActionBase? actionInstance = Activator.CreateInstance(action.Type, new object[] { actionLogger }) as ActionBase;
            if (actionInstance == null)
            {
                throw new Exception($"Failed to construct action {action.Type}");
            }

            var tokens = component.Data.CustomId.Split('_');
            if (tokens.Length > 1)
            {
                // Skip the first token which is the action name
                tokens = tokens.TakeLast(tokens.Length - 1).ToArray();
                if (action.Options?.Count != tokens.Length)
                {
                    throw new Exception($"Action option mismatch. Got {tokens.Length}, expected {action.Options?.Count}");
                }

                for (int i = 0; i < tokens.Length; ++i)
                {
                    ActionOptionInfo optionInfo = action.Options[i];

                    // TODO: add type reader support.
                    object value = null;
                    if (optionInfo.Property.PropertyType == typeof(string))
                    {
                        value = tokens[i];
                    }
                    else if (optionInfo.Property.PropertyType == typeof(long))
                    {
                        value = long.Parse(tokens[i]);
                    }
                    else if (optionInfo.Property.PropertyType == typeof(ulong))
                    {
                        value = ulong.Parse(tokens[i]);
                    }
                    else
                    {
                        throw new ArgumentException($"Unsupported action option type: {optionInfo.Property.PropertyType}");
                    }

                    optionInfo.Property.SetValue(actionInstance, value);
                }
            }

            var createContextFunction = Utility.GetInheritedStaticMethod(interactionOwner.Type, CommandBase.CreateActionContextName);
            var context = createContextFunction?.Invoke(null, new object[] { Client, component, _services, interactionOwner.Name }) as ActionContext;
            if (context == null)
            {
                throw new ArgumentNullException($"Failed to create action context for {interactionOwner.Name}:{action.Name}");
            }

            Semaphore sem;
            lock (_messageSemaphores)
            {
                if (!_messageSemaphores.TryGetValue(component.Message.Id, out sem))
                {
                    sem = new Semaphore(1, 1);
                    _messageSemaphores.Add(component.Message.Id, sem);
                }
            }

            Logger.LogDebug($"{component.User.Username}#{component.User.Discriminator} initiated action {action.Name}");

            Task.Run(async () =>
            {
                try
                {
                    sem.WaitOne();
                    Logger.LogDebug($"{action.Name} executing");
                    await actionInstance.HandleAction(context);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error running action {action.Name}: {ex}");
#if !DEBUG
                    await component.Channel.SendMessageAsync(embed: BuildErrorEmbed(ex, component.User, $"Error running action {action.Name}"));
#endif
                }
                finally
                {
                    Logger.LogDebug($"{action.Name} Releasing semaphore");
                    sem.Release();
                }

            }).Forget();
        }

        static KeyValuePair<SubCommandInfo, IEnumerable<CommandDataOption>?>? FindDeepestSubCommand(CommandInfo parent,
            IEnumerable<CommandDataOption>? options)
        {
            if (options == null)
            {
                return null;
            }

            foreach (var optionData in options)
            {
                if (optionData.Type == Discord.ApplicationCommandOptionType.SubCommandGroup ||
                    optionData.Type == Discord.ApplicationCommandOptionType.SubCommand)
                {
                    string subCommandName = optionData.Name;
                    SubCommandInfo? info = _subCommandEntries.Find(sub => sub.Name == subCommandName && sub.ParentCommandType == parent.Type);

                    if (info != null)
                    {
                        var result = FindDeepestSubCommand(info, optionData.Options);
                        if (result != null)
                        {
                            return result;
                        }

                        return new(info, optionData.Options);
                    }
                }
            }
            return null;
        }

        public static List<CommandDataOption>? BuildCommandDataOptions(IReadOnlyCollection<SocketSlashCommandDataOption>? options)
        {
            if (options == null)
            {
                return null;
            }

            return options.Select(opt => new CommandDataOption()
            {
                Name = opt.Name,
                Value = opt.Value,
                Type = opt.Type,
                Options = BuildCommandDataOptions(opt.Options)
            }).ToList();
        }

        private IEnumerable<ulong> GetRequiredRolesForCommand(ConfigService configService, SocketGuild guild, string commandName)
        {
            if (configService.Configuration.GuildEntries.ContainsKey(guild.Id))
            {
                var commands = configService.Configuration.GuildEntries[guild.Id].Commands;
                if (commands.ContainsKey(commandName))
                {
                    return commands[commandName].Roles;
                }
            }
            return new List<ulong>();
        }

        private IEnumerable<ulong> GetRequiredRolesForAction(ConfigService configService, SocketGuild guild, string commandName, string actionName)
        {
            if (configService.Configuration.GuildEntries.ContainsKey(guild.Id))
            {
                var commandRoles = configService.Configuration.GuildEntries[guild.Id].Commands;
                if (commandRoles.ContainsKey(commandName))
                {
                    var command = commandRoles[commandName];
                    if (command.ActionRoles.ContainsKey(actionName))
                    {
                        return command.ActionRoles[actionName];
                    }
                }
            }
            return new List<ulong>();
        }

        private static string CommandOptionsToString(IEnumerable<CommandDataOption>? options)
        {
            void OptionsToStringInternal(IEnumerable<CommandDataOption>? opts, StringBuilder builder)
            {
                if (opts == null) return;

                foreach (CommandDataOption op in opts)
                {
                    if (op.Type != ApplicationCommandOptionType.SubCommand && op.Type != ApplicationCommandOptionType.SubCommandGroup)
                    {
                        builder.Append($"{op.Name}:{op.Value} ");
                    }
                    else if (op.Options != null)
                    {
                        builder.Append($"{op.Name} ");
                        OptionsToStringInternal(op.Options, builder);
                    }
                }
            }

            StringBuilder builder = new();
            OptionsToStringInternal(options, builder);
            return builder.ToString();
        }

        private static Embed BuildErrorEmbed(Exception ex, IUser user, string title, string? args = null)
        {
            EmbedBuilder builder = new();
            var exceptionMessage = ex.ToString();

            string message = $"**Exception:** {ex.Message}\n" +
                $"{(args != null ? $"**Args:** {args}\n" : "")}";

            // max message length is 4096
            if (message.Length + exceptionMessage.Length > 4096)
            {
                // 6 chars needed for the ```
                var callstackSpace = Math.Min(4096 - (message.Length + 6), exceptionMessage.Length);
                exceptionMessage = exceptionMessage.Substring(0, callstackSpace);
            }

            message += $"```{exceptionMessage}```";

            builder.WithTitle(title)
                .WithAuthor(user)
                .WithDescription(message);
                //.WithImageUrl("https://images-ext-2.discordapp.net/external/I0FROsQesBipYVjLKEyGYrwVJgeTnqR5_yr3jT2Z0Fw/https/media.discordapp.net/attachments/892631133219590174/902676707088146523/2e7175697eab40b392acf06d02002004cat-with-loading-sign-on-head.jpg");
            return builder.Build();
        }
    }
}
