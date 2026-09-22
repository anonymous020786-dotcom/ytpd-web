namespace YtpdWeb.Api.Bot;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    // Empty/unset disables the bot entirely - TelegramController checks this.
    public string BotToken { get; set; } = "";

    // Telegram sends this back in the X-Telegram-Bot-Api-Secret-Token header
    // on every webhook call; anything else gets rejected. Without this, a
    // spoofed POST could crawl straight past the Tunnel to the job queue.
    public string WebhookSecret { get; set; } = "";

    // The self-hosted telegram-bot-api server (see docker-compose.yml), not
    // api.telegram.org - the local server lifts the 50MB upload cap to 2GB
    // and lets us hand it a local file path directly instead of re-uploading
    // bytes it can already read off the shared volume.
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    // Comma-separated numeric chat IDs. Empty means "not configured yet" -
    // the bot replies to anyone with their own chat ID instead of doing
    // anything, so the owner can copy it in on first run.
    public string AllowedChatIds { get; set; } = "";
}
