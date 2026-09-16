using System;
using System.Diagnostics;
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
    public sealed class FrontierService : IAsyncDisposable
    {
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly FrontierOptions _opts;

        private ITelegramBotClient _tg;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly object _stopLock = new();
        private readonly ConditionalWeakTable<ICommandContext, FailureState> _failures = new();
        private Task _receiver;
        private string _username;

        public FrontierService(DiscordSocketClient discord, CommandService commands, IServiceProvider services, FrontierOptions opts,
            ITelegramBotClient telegramClient = null)
        {
            _discord = discord;
            _commands = commands;
            _services = services;
            _opts = opts;
            _tg = telegramClient;
        }

        public async Task StartAsync()
        {
            if (_tg == null && string.IsNullOrWhiteSpace(_opts.TelegramToken))
                return;
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_receiver != null && !_receiver.IsCompleted)
                    return;
                _tg ??= new TelegramBotClient(_opts.TelegramToken);
                lock (_stopLock)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
                _username = (await _tg.GetMe(cancellationToken: _cts.Token).ConfigureAwait(false)).Username;
                _commands.CommandExecuted -= HandleCommandExecutedAsync;
                _commands.CommandExecuted += HandleCommandExecutedAsync;
                _receiver = _tg.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync,
                    new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message } }, _cts.Token);
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        public void Stop()
        {
            lock (_stopLock) _cts?.Cancel();
        }

        public async Task StopAsync()
        {
            Stop();
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                Stop();
                if (_receiver != null)
                {
                    try { await _receiver.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
                    finally { _receiver = null; }
                }
            }
            finally
            {
                _commands.CommandExecuted -= HandleCommandExecutedAsync;
                lock (_stopLock)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
                _lifecycle.Release();
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            var msg = update.Message;
            if (msg?.Text is not string raw)
                return;

            var text = raw.TrimStart();
            if (text.StartsWith("/"))
            {
                text = text.Substring(1);
                var end = 0;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                var mention = text.IndexOf('@', 0, end);
                if (mention >= 0)
                {
                    if (!string.Equals(text.Substring(mention + 1, end - mention - 1), _username, StringComparison.OrdinalIgnoreCase))
                        return;
                    text = text.Remove(mention, end - mention);
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!_opts.ChatToGuild.TryGetValue(msg.Chat.Id, out var guildId))
                guildId = _opts.DefaultGuildId;

            var guild = guildId != 0 ? _discord.GetGuild(guildId) : null;

            SocketGuildUser user = null;
            if (guild is not null && msg.From is { } from && _opts.UserToDiscord.TryGetValue(from.Id, out var discordId))
                user = guild.GetUser(discordId);

            var channel = new FrontierProxyChannel(bot, msg.Chat.Id, _discord.CurrentUser);
            var context = (SocketCommandContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketCommandContext));
            SetField(context, "Client", _discord);
            SetField(context, "Guild", guild);
            SetField(context, "Channel", channel);
            SetField(context, "User", user);
            SetField(context, "Message", null);

            var matches = _commands.Search(context, text);
            if (!matches.IsSuccess || matches.Commands.Count == 0)
                return;
            if (matches.Commands.Any(match => !match.Command.Attributes.Any(a => a is FrontierAttribute or FrontierAsImageAttribute)))
                return;

            var validation = await _commands.ValidateAndGetBestMatch(matches, context, _services).ConfigureAwait(false);
            if (validation is not MatchResult selection || !selection.IsSuccess || !selection.Match.HasValue)
            {
                await ReportFailureAsync(context, validation).ConfigureAwait(false);
                return;
            }
            if (selection.Pipeline is not ParseResult parsed || !parsed.IsSuccess)
            {
                await ReportFailureAsync(context, selection.Pipeline).ConfigureAwait(false);
                return;
            }

            var command = selection.Match.Value.Command;
            channel.SetRenderMode(command.Attributes.Any(a => a is FrontierAsImageAttribute));
            var result = await command.ExecuteAsync(context, parsed, _services).ConfigureAwait(false);
            await ReportFailureAsync(context, result).ConfigureAwait(false);
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
        {
            if (!(ex is OperationCanceledException && ct.IsCancellationRequested))
                ReportError(ex);
            return Task.CompletedTask;
        }

        private Task HandleCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
            => ReportFailureAsync(context, result);

        private async Task ReportFailureAsync(ICommandContext context, IResult result)
        {
            if (result.IsSuccess || context.Channel is not FrontierProxyChannel channel || !channel.UsesClient(_tg))
                return;
            if (Interlocked.Exchange(ref _failures.GetValue(context, _ => new FailureState()).Reported, 1) != 0)
                return;
            ReportError(result is ExecuteResult execution && execution.Exception != null
                ? execution.Exception : new InvalidOperationException(result.ErrorReason));
            try
            {
                await ((IMessageChannel)channel).SendMessageAsync("Не удалось выполнить команду.").ConfigureAwait(false);
            }
            catch (Exception ex) { ReportError(ex); }
        }

        private void ReportError(Exception ex)
        {
            if (_opts.ErrorHandler == null)
                Trace.TraceError(ex.ToString());
            else
            {
                try { _opts.ErrorHandler(ex); }
                catch (Exception handlerError) { Trace.TraceError(handlerError.ToString()); }
            }
        }

        private sealed class FailureState { public int Reported; }

        private static void SetField(object target, string propertyName, object value)
        {
            var field = typeof(SocketCommandContext)
                .GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(typeof(SocketCommandContext).FullName, propertyName);
            field.SetValue(target, value);
        }
    }
}
