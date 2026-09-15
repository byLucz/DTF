# DiscordTelegramFrontier (DTF)

A pluggable bridge that reuses **existing Discord.Net commands** in Telegram — without rewriting the commands. Mark a command with `[Frontier]` and it becomes available in Telegram.

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
    o.Chat(telegramChatId, discordGuildId);      // TG chat → Discord guild
    o.User(telegramUserId, discordUserId);        // user link (for permissions)
});

// after the Discord client has started:
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
- `Context.Guild` / `Context.User` are taken live from Discord (mapped from config), so a command requires the Telegram user to be linked to a Discord id.

## Stack

- net10.0, Discord.Net 3.20.1, Telegram.Bot 22.6.0.
