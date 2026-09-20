using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly FrontierUpdatePipeline _pipeline;

        private ITelegramBotClient _tg;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly object _stopLock = new();
        private readonly ConditionalWeakTable<ICommandContext, FailureState> _failures = new();
        private readonly ConditionalWeakTable<ITelegramBotClient, BotIdentity> _identities = new();
        private Task _receiver;
        private volatile bool _subscribed;
        private volatile bool _disposed;

        public UpdateType[] AllowedUpdates => (_opts.AllowedUpdates is { Length: 0 }
                ? Enum.GetValues<UpdateType>().Where(type => type != UpdateType.Unknown)
                : _opts.AllowedUpdates ?? new[] { UpdateType.Message })
            .Concat(_pipeline.UpdateTypes)
            .Distinct().ToArray();

        public FrontierService(DiscordSocketClient discord, CommandService commands, IServiceProvider services, FrontierOptions opts,
            ITelegramBotClient telegramClient = null)
        {
            _discord = discord;
            _commands = commands;
            _services = services;
            _opts = opts;
            _tg = telegramClient;
            _pipeline = new FrontierUpdatePipeline(services, DispatchTerminalAsync);
        }

        public async Task StartAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tg == null && string.IsNullOrWhiteSpace(_opts.TelegramToken))
                return;
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_receiver != null && !_receiver.IsCompleted)
                    return;
                EnsureDiscordBridge();
                _tg ??= new TelegramBotClient(_opts.TelegramToken);
                lock (_stopLock)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
                await GetBotUsernameAsync(_tg, _cts.Token).ConfigureAwait(false);
                _receiver = _tg.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync,
                    new ReceiverOptions { AllowedUpdates = AllowedUpdates }, _cts.Token);
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
                lock (_stopLock)
                {
                    _cts?.Dispose();
                    _cts = null;
                }
                _lifecycle.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_stopLock) _disposed = true;
            try { await StopAsync().ConfigureAwait(false); }
            finally
            {
                lock (_stopLock)
                {
                    if (_subscribed) _commands.CommandExecuted -= HandleCommandExecutedAsync;
                    _subscribed = false;
                }
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
            => await ProcessUpdateAsync(bot, update, ct).ConfigureAwait(false);

        public async Task<bool> ProcessUpdateAsync(ITelegramBotClient bot, Update update,
            CancellationToken cancellationToken = default, string botUsername = null)
        {
            ArgumentNullException.ThrowIfNull(bot);
            ArgumentNullException.ThrowIfNull(update);
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDiscordBridge();
            botUsername ??= await GetBotUsernameAsync(bot, cancellationToken).ConfigureAwait(false);
            await using var scope = _services.CreateAsyncScope();
            var context = new FrontierUpdateContext(bot, update, botUsername, scope.ServiceProvider, cancellationToken);
            if (context.AddressedToAnotherBot) return false;
            try
            {
                return await _pipeline.InvokeAsync(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ReportError(ex);
                if (_opts.UpdateErrorHandler != null)
                {
                    await _opts.UpdateErrorHandler(context, ex).ConfigureAwait(false);
                    return true;
                }
                try
                {
                    if (context.CallbackQuery != null)
                        await context.AnswerCallbackAsync("Не удалось выполнить действие.", showAlert: true).ConfigureAwait(false);
                    else if (context.Message != null)
                        await context.ReplyAsync("Не удалось выполнить команду.").ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception replyError) { ReportError(replyError); }
                return true;
            }

        }

        private async Task<bool> DispatchTerminalAsync(FrontierUpdateContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (_opts.UpdateHandler != null && await _opts.UpdateHandler(context.Bot, context.Update, context.BotUsername, context.CancellationToken).ConfigureAwait(false))
                return true;
            return _opts.EnableDiscordCommands && await ExecuteDiscordCommandAsync(context).ConfigureAwait(false);
        }

        private async Task<bool> ExecuteDiscordCommandAsync(FrontierUpdateContext telegram)
        {
            var bot = telegram.Bot;
            var update = telegram.Update;
            var msg = update.Message;
            if (msg?.Text is not string raw)
                return false;

            var text = raw.TrimStart();
            if (text.StartsWith("/"))
            {
                text = text.Substring(1);
                var end = 0;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                var mention = text.IndexOf('@', 0, end);
                if (mention >= 0)
                {
                    if (!string.Equals(text.Substring(mention + 1, end - mention - 1), telegram.BotUsername, StringComparison.OrdinalIgnoreCase))
                        return false;
                    text = text.Remove(mention, end - mention);
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!_opts.ChatToGuild.TryGetValue(msg.Chat.Id, out var guildId))
                guildId = _opts.DefaultGuildId;

            var guild = guildId != 0 ? _discord.GetGuild(guildId) : null;

            SocketGuildUser user = null;
            if (guild is not null && msg.From is { } from && _opts.UserToDiscord.TryGetValue(from.Id, out var discordId))
                user = guild.GetUser(discordId);

            var channel = new FrontierProxyChannel(bot, msg.Chat.Id, _discord.CurrentUser,
                messageThreadId: msg.MessageThreadId, businessConnectionId: msg.BusinessConnectionId);
            var context = DiscordContextFactory.Create(_discord, guild, channel, user);

            var matches = _commands.Search(context, text);
            if (!matches.IsSuccess || matches.Commands.Count == 0)
                return false;
            if (matches.Commands.Any(match => !match.Command.Attributes.Any(a => a is FrontierAttribute or FrontierAsImageAttribute)))
                return false;

            _failures.Add(context, new FailureState());

            var validation = await _commands.ValidateAndGetBestMatch(matches, context, _services).ConfigureAwait(false);
            if (validation is not MatchResult selection || !selection.IsSuccess || !selection.Match.HasValue)
            {
                await ReportFailureAsync(context, validation).ConfigureAwait(false);
                return true;
            }
            if (selection.Pipeline is not ParseResult parsed || !parsed.IsSuccess)
            {
                await ReportFailureAsync(context, selection.Pipeline).ConfigureAwait(false);
                return true;
            }

            var command = selection.Match.Value.Command;
            channel.SetRenderMode(command.Attributes.Any(a => a is FrontierAsImageAttribute));
            var result = await command.ExecuteAsync(context, parsed, _services).ConfigureAwait(false);
            await ReportFailureAsync(context, result).ConfigureAwait(false);
            return true;
        }

        private void EnsureDiscordBridge()
        {
            if (!_opts.EnableDiscordCommands || _subscribed) return;
            lock (_stopLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_opts.EnableDiscordCommands) return;
                if (_discord == null || _commands == null)
                    throw new InvalidOperationException("Register DiscordSocketClient and CommandService, or set EnableDiscordCommands to false.");
                if (_subscribed) return;
                DiscordContextFactory.Validate();
                _commands.CommandExecuted += HandleCommandExecutedAsync;
                _subscribed = true;
            }
        }

        private async Task<string> GetBotUsernameAsync(ITelegramBotClient bot, CancellationToken ct)
        {
            var identity = _identities.GetValue(bot, _ => new BotIdentity());
            if (identity.Loaded) return identity.Username;
            await identity.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!identity.Loaded)
                {
                    identity.Username = (await bot.GetMe(cancellationToken: ct).ConfigureAwait(false)).Username;
                    identity.Loaded = true;
                }
                return identity.Username;
            }
            finally { identity.Gate.Release(); }
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
            if (result.IsSuccess || context.Channel is not FrontierProxyChannel channel || !_failures.TryGetValue(context, out var failure))
                return;
            if (Interlocked.Exchange(ref failure.Reported, 1) != 0)
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

        private sealed class BotIdentity
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public string Username;
            public volatile bool Loaded;
        }
    }
}
