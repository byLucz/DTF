# DiscordTelegramFrontier (DTF)

Reuses existing Discord.Net prefix commands in Telegram. Keep
`ModuleBase<SocketCommandContext>` and mark portable commands with `[Frontier]`.

## How it works

DTF runs a Telegram client (long-polling) in the same process as the Discord bot. On an incoming Telegram command it builds a proxy `ICommandContext` (real `Client` + `Guild`, a proxy channel) and calls `CommandService.ExecuteAsync` — the exact same command logic. The command's output (`ReplyAsync` / `Context.Channel.SendMessageAsync`) is intercepted by the proxy channel and rendered to Telegram (Embed → HTML text + photo).

```
TG update → FrontierService → proxy ICommandContext → CommandService.ExecuteAsync
          → command calls ReplyAsync(embed) → FrontierProxyChannel → Telegram.Bot.SendMessage
```

## Wiring (host)

```csharp
services.AddFrontier(o =>
{
    o.TelegramToken = "<bot token>";
    o.Chat(telegramChatId, discordGuildId);
    o.User(telegramUserId, discordUserId);
    o.ErrorHandler = ex => Console.Error.WriteLine(ex);
});

await provider.GetRequiredService<FrontierService>().StartAsync();
```

Mark a command:
```csharp
[Frontier]
[Command("weather")]
public async Task Weather(params string[] city) { ... await ReplyAsync(embed: e.Build()); }
```

## Limitations (MVP)

- Only text/data commands that reply via `ReplyAsync` / `Context.Channel.SendMessageAsync` are portable.
- Not supported: voice/music, reactions, interactive-button callbacks, commands that cast the channel to `SocketTextChannel`.
- Mappings are optional. Stateless commands (e.g. an API lookup) work in any chat with no mapping. Commands that read `Context.Guild` need a chat→guild map (or `DefaultGuild`); commands with permission checks (`Context.User`) need the Telegram user linked to a Discord id — otherwise `Guild`/`User` are null and such commands degrade or deny.

## Stack

- net10.0, Discord.Net 3.20.1, Telegram.Bot 22.6.0.
