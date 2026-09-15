using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DiscordTelegramFrontier
{
    public sealed class FrontierService
    {
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly FrontierOptions _opts;

        private TelegramBotClient _tg;
        private HashSet<string> _allowed;
        private CancellationTokenSource _cts;

        public FrontierService(DiscordSocketClient discord, CommandService commands, IServiceProvider services, FrontierOptions opts)
        {
            _discord = discord;
            _commands = commands;
            _services = services;
            _opts = opts;
        }

        public Task StartAsync()
        {
            if (string.IsNullOrWhiteSpace(_opts.TelegramToken))
                return Task.CompletedTask;

            _allowed = DiscoverFrontierAliases();
            _tg = new TelegramBotClient(_opts.TelegramToken);
            _cts = new CancellationTokenSource();
            _tg.StartReceiving(HandleUpdateAsync, HandleErrorAsync,
                new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message } }, _cts.Token);

            return Task.CompletedTask;
        }

        public void Stop() => _cts?.Cancel();

        private HashSet<string> DiscoverFrontierAliases()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in _commands.Commands)
                if (cmd.Attributes.Any(a => a is FrontierAttribute))
                    foreach (var alias in cmd.Aliases)
                        set.Add(alias);
            return set;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            var msg = update.Message;
            if (msg?.Text is not string raw)
                return;

            var text = raw.TrimStart();
            if (text.StartsWith("/"))
                text = text.Substring(1);

            var name = text.Split(new[] { ' ' }, 2)[0];
            if (!_allowed.Contains(name))
                return;

            if (!_opts.ChatToGuild.TryGetValue(msg.Chat.Id, out var guildId))
                guildId = _opts.DefaultGuildId;

            var guild = guildId != 0 ? _discord.GetGuild(guildId) : null;

            SocketGuildUser user = null;
            if (guild is not null && msg.From is { } from && _opts.UserToDiscord.TryGetValue(from.Id, out var discordId))
                user = guild.GetUser(discordId);

            var channel = new FrontierProxyChannel(bot, msg.Chat.Id);
            var context = (SocketCommandContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketCommandContext));
            SetField(context, "Client", _discord);
            SetField(context, "Guild", guild);
            SetField(context, "Channel", channel);
            SetField(context, "User", user);
            SetField(context, "Message", null);

            await _commands.ExecuteAsync(context, text, _services);
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct) => Task.CompletedTask;

        private static void SetField(object target, string propertyName, object value)
        {
            var field = typeof(SocketCommandContext)
                .GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}
