using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
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
        private readonly InteractionService _interactions;
        private readonly FrontierButtons _buttons;

        private ITelegramBotClient _tg;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly object _stopLock = new();
        private readonly ConditionalWeakTable<ICommandContext, FailureState> _failures = new();
        private readonly ConditionalWeakTable<ITelegramBotClient, BotIdentity> _identities = new();
        private Task _receiver;
        private volatile bool _subscribed;
        private volatile bool _disposed;
        private static readonly TimeSpan PollingReportInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan PollingRetryDelay = TimeSpan.FromSeconds(5);
        private const int PollingTimeoutSeconds = 50;
        private readonly object _pollingLock = new();
        private DateTimeOffset _pollingReportedAt = DateTimeOffset.MinValue;
        private int _pollingSuppressed;

        public UpdateType[] AllowedUpdates => (_opts.AllowedUpdates is { Length: 0 }
                ? Enum.GetValues<UpdateType>().Where(type => type != UpdateType.Unknown)
                : _opts.AllowedUpdates ?? new[] { UpdateType.Message })
            .Concat(_pipeline.UpdateTypes)
            .Concat(ButtonsAvailable ? new[] { UpdateType.CallbackQuery } : Array.Empty<UpdateType>())
            .Distinct().ToArray();

        private bool ButtonsAvailable => _opts.EnableDiscordCommands && _interactions != null;

        public ITelegramBotClient Bot
        {
            get
            {
                if (_tg != null || string.IsNullOrWhiteSpace(_opts.TelegramToken))
                    return _tg;
                lock (_stopLock)
                    return _tg ??= new TelegramBotClient(new TelegramBotClientOptions(_opts.TelegramToken) { RetryCount = _opts.RetryCount },
                        FrontierHttp.Create(TimeSpan.FromSeconds(PollingTimeoutSeconds + 30)));
            }
        }

        public FrontierService(DiscordSocketClient discord, CommandService commands, IServiceProvider services, FrontierOptions opts,
            ITelegramBotClient telegramClient = null, InteractionService interactions = null)
        {
            _discord = discord;
            _commands = commands;
            _services = services;
            _opts = opts;
            _tg = telegramClient;
            _interactions = interactions;
            _buttons = new FrontierButtons(opts.ButtonLifetime);
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
                var bot = Bot;
                lock (_stopLock)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
                await GetBotUsernameAsync(bot, _cts.Token).ConfigureAwait(false);
                _receiver = PollAsync(bot, _cts.Token);
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
                _buttons.Dispose();
                lock (_stopLock)
                {
                    if (_subscribed) _commands.CommandExecuted -= HandleCommandExecutedAsync;
                    if (_subscribed && _interactions != null) _interactions.InteractionExecuted -= HandleInteractionExecutedAsync;
                    _subscribed = false;
                }
            }
        }

        private async Task PollAsync(ITelegramBotClient bot, CancellationToken ct)
        {
            var offset = 0;
            var allowed = AllowedUpdates;

            while (!ct.IsCancellationRequested)
            {
                Update[] updates;
                try
                {
                    updates = await bot.GetUpdates(offset, 100, PollingTimeoutSeconds, allowed, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    await HandleErrorAsync(bot, ex, ct).ConfigureAwait(false);
                    try { await Task.Delay(PollingRetryDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                lock (_pollingLock)
                {
                    _pollingReportedAt = DateTimeOffset.MinValue;
                    _pollingSuppressed = 0;
                }

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    try { await ProcessUpdateAsync(bot, update, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { ReportError(ex); }
                }
            }
        }

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
            FrontierUsers.Remember(update.Message ?? update.ChannelPost);
            if (context.AddressedToAnotherBot) return false;
            try
            {
                return await _pipeline.InvokeAsync(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (FrontierCallbacks.IsStale(ex))
            {
                return true;
            }
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
            if (!_opts.EnableDiscordCommands)
                return false;
            return context.CallbackQuery != null
                ? await ExecuteDiscordComponentAsync(context).ConfigureAwait(false)
                : await ExecuteDiscordCommandAsync(context).ConfigureAwait(false);
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
            else if (!_opts.AcceptsPlainCommand(msg.Chat))
                return false;
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!_opts.ChatToGuild.TryGetValue(msg.Chat.Id, out var guildId))
                guildId = _opts.DefaultGuildId;

            var guild = guildId != 0 ? _discord.GetGuild(guildId) : null;

            var user = LinkedUser(guild, msg.From);

            var channel = new FrontierProxyChannel(bot, msg.Chat.Id, _discord.CurrentUser,
                messageThreadId: msg.MessageThreadId, businessConnectionId: msg.BusinessConnectionId)
            {
                Resolver = FrontierMentions.Resolver(_discord, guild, _opts)
            };
            var context = DiscordContextFactory.Create(_discord, guild, channel, user);
            FrontierUsers.Attach(context, msg, guild, _opts);

            var matches = _commands.Search(context, text);
            if (!matches.IsSuccess || matches.Commands.Count == 0)
                return false;
            if (matches.Commands.Any(match => !match.Command.Attributes.Any(a => a is FrontierAttribute or FrontierAsImageAttribute)))
                return false;

            _failures.Add(context, new FailureState());

            var validation = await _commands.ValidateAndGetBestMatch(matches, context, _services).ConfigureAwait(false);
            if (validation is not MatchResult selection || !selection.IsSuccess || !selection.Match.HasValue)
            {
                await ReportFailureAsync(context, validation, (validation as MatchResult)?.Match?.Command ?? matches.Commands[0].Command).ConfigureAwait(false);
                return true;
            }
            if (selection.Pipeline is not Discord.Commands.ParseResult parsed || !parsed.IsSuccess)
            {
                await ReportFailureAsync(context, selection.Pipeline, selection.Match.Value.Command).ConfigureAwait(false);
                return true;
            }

            var command = selection.Match.Value.Command;
            channel.SetRenderMode(command.Attributes.Any(a => a is FrontierAsImageAttribute));
            if (ButtonsAvailable && command.Attributes.Any(a => a is FrontierAttribute { Buttons: true } or FrontierAsImageAttribute { Buttons: true }))
                channel.EnableButtons(_buttons);
            var result = await command.ExecuteAsync(context, parsed, _services).ConfigureAwait(false);
            await ReportFailureAsync(context, result, command).ConfigureAwait(false);
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
                if (_interactions != null) _interactions.InteractionExecuted += HandleInteractionExecutedAsync;
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
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                return Task.CompletedTask;

            int suppressed;
            lock (_pollingLock)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _pollingReportedAt < PollingReportInterval)
                {
                    _pollingSuppressed++;
                    return Task.CompletedTask;
                }
                _pollingReportedAt = now;
                suppressed = _pollingSuppressed;
                _pollingSuppressed = 0;
            }

            ReportError(suppressed == 0 ? ex
                : new InvalidOperationException($"Telegram polling error, {suppressed} similar errors suppressed", ex));
            return Task.CompletedTask;
        }

        private Task HandleCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, Discord.Commands.IResult result)
            => ReportFailureAsync(context, result, command);

        private async Task<bool> ExecuteDiscordComponentAsync(FrontierUpdateContext telegram)
        {
            var callback = telegram.CallbackQuery;
            if (_interactions == null || callback.Data is not { } data || !data.StartsWith(FrontierButtons.Prefix, StringComparison.Ordinal))
                return false;

            var bot = telegram.Bot;
            var ct = telegram.CancellationToken;

            if (!_buttons.TryGet(data, out var entry) || callback.Message is not { } source
                || entry.Message.ProxyChannel.ChatId != source.Chat.Id || !entry.Message.ProxyChannel.UsesClient(bot))
            {
                await FrontierCallbacks.AnswerAsync(bot, callback.Id, data == FrontierButtons.Prefix ? null : "Кнопка устарела, вызови команду заново.",
                    false, ct).ConfigureAwait(false);
                return true;
            }

            if (!_opts.ChatToGuild.TryGetValue(source.Chat.Id, out var guildId))
                guildId = _opts.DefaultGuildId;

            var guild = guildId != 0 ? _discord.GetGuild(guildId) : null;
            IUser user = LinkedUser(guild, callback.From) ?? (IUser)FrontierUsers.Proxy(callback.From, callback.From.Username);

            var interaction = new FrontierComponentInteraction(entry.Message,
                new FrontierComponentData(entry.CustomId, entry.Type, entry.Values), user, guild,
                _discord.CurrentUser?.Id ?? 0, callback.From.LanguageCode,
                (text, alert) => FrontierCallbacks.AnswerAsync(bot, callback.Id, text, alert, ct));
            var context = new FrontierInteractionContext(_discord, guild, entry.Message.ProxyChannel, user, interaction);

            Discord.Interactions.IResult result;
            try
            {
                result = await _interactions.ExecuteCommandAsync(context, _services).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = Discord.Interactions.ExecuteResult.FromError(ex);
            }

            if (!result.IsSuccess)
                await FinishInteractionAsync(context, result).ConfigureAwait(false);
            return true;
        }

        private SocketUser LinkedUser(SocketGuild guild, User from)
        {
            if (from is null || !_opts.UserToDiscord.TryGetValue(from.Id, out var discordId))
                return null;
            return guild is not null ? guild.GetUser(discordId) : _discord.GetUser(discordId);
        }

        private Task HandleInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, Discord.Interactions.IResult result)
            => context is FrontierInteractionContext frontier ? FinishInteractionAsync(frontier, result) : Task.CompletedTask;

        private async Task FinishInteractionAsync(FrontierInteractionContext context, Discord.Interactions.IResult result)
        {
            if (!context.TryFinish())
                return;

            if (result is Discord.Interactions.ExecuteResult { Exception: { } stale } && FrontierCallbacks.IsStale(stale))
                return;

            var userError = result.Error is InteractionCommandError.UnknownCommand or InteractionCommandError.UnmetPrecondition
                or InteractionCommandError.ConvertFailed or InteractionCommandError.BadArgs or InteractionCommandError.ParseFailed;
            if (!result.IsSuccess && !userError)
                ReportError(result is Discord.Interactions.ExecuteResult { Exception: { } exception }
                    ? exception : new InvalidOperationException(result.ErrorReason));

            try
            {
                if (result.IsSuccess || context.Interaction.HasResponded)
                    await context.Interaction.AnswerAsync().ConfigureAwait(false);
                else
                    await context.Interaction.AnswerAsync(result.Error switch
                    {
                        InteractionCommandError.UnknownCommand => "Эта кнопка не работает в Telegram.",
                        InteractionCommandError.UnmetPrecondition when !string.IsNullOrWhiteSpace(result.ErrorReason) => result.ErrorReason,
                        _ => "Не удалось выполнить действие."
                    }, alert: true).ConfigureAwait(false);
            }
            catch (Exception ex) when (!FrontierCallbacks.IsStale(ex)) { ReportError(ex); }
            catch (Exception) { }
        }

        private async Task ReportFailureAsync(ICommandContext context, Discord.Commands.IResult result, Optional<CommandInfo> command = default)
        {
            if (result.IsSuccess || context.Channel is not FrontierProxyChannel channel || !_failures.TryGetValue(context, out var failure))
                return;
            if (Interlocked.Exchange(ref failure.Reported, 1) != 0)
                return;
            var userError = result.Error is CommandError.ParseFailed or CommandError.ObjectNotFound
                or CommandError.BadArgCount or CommandError.UnmetPrecondition
                && !string.IsNullOrWhiteSpace(result.ErrorReason);
            if (!userError)
                ReportError(result is Discord.Commands.ExecuteResult execution && execution.Exception != null
                    ? execution.Exception : new InvalidOperationException(result.ErrorReason));
            var reply = userError ? result.ErrorReason : "Не удалось выполнить команду.";
            try
            {
                if (_opts.CommandErrorHandler != null)
                    await _opts.CommandErrorHandler(command, context, result).ConfigureAwait(false);
                else
                    await ((IMessageChannel)channel).SendMessageAsync(reply).ConfigureAwait(false);
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
