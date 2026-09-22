namespace YtpdWeb.Api.Models;

// A user the owner has approved via the bot's /adduser command (see
// TelegramController). Separate from Telegram:AllowedChatIds (env config,
// see TelegramOptions) which is the fixed set of *admins* - the only ones
// who can run /adduser/removeuser/users in the first place. This table is
// the mutable, self-service list everyone else gets added to.
public class TelegramAllowedUser
{
    public long ChatId { get; set; }
    public string? Label { get; set; }
    public long AddedByChatId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
