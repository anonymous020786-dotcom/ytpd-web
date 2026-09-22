using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YtpdWeb.Api.Bot;
using YtpdWeb.Api.Data;
using YtpdWeb.Api.Models;
using YtpdWeb.Api.Services;

namespace YtpdWeb.Api.Controllers;

// Telegram webhooks carry no JWT - Telegram itself has no way to hold one -
// so this endpoint is [AllowAnonymous] and instead checks the secret token
// Telegram echoes back in a header (set via setWebhook, see TelegramClient).
//
// Two-tier access control:
//   - Telegram:AllowedChatIds (env config, fixed) - admins. The only ones
//     who can run /adduser, /removeuser, /users.
//   - TelegramAllowedUser (DB table, mutable) - everyone an admin has
//     approved via /adduser. This is what "add users from my Telegram
//     account" without a redeploy actually means: admins manage this list
//     live from inside a chat instead of editing .env and redeploying.
// Anyone can reach this endpoint; only chat IDs in one of the two above
// get a real response instead of "here's your chat ID, ask an admin."
[ApiController]
[AllowAnonymous]
[Route("api/bot")]
public class TelegramController(
    TelegramClient telegram,
    IOptions<TelegramOptions> telegramOptions,
    YoutubeResolverService resolver,
    PendingSelectionCache pending,
    DownloadQueue queue,
    AppDbContext db,
    IOptions<StorageOptions> storageOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramController> logger
) : ControllerBase
{
    private readonly TelegramOptions _opts = telegramOptions.Value;
    private readonly StorageOptions _storage = storageOptions.Value;

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] TgUpdate update, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken))
            return Ok(); // bot not configured - silently accept so Telegram stops retrying

        if (Request.Headers["X-Telegram-Bot-Api-Secret-Token"] != _opts.WebhookSecret)
            return Unauthorized();

        try
        {
            if (update.Message is not null)
                await HandleMessageAsync(update.Message, ct);
            else if (update.CallbackQuery is not null)
                await HandleCallbackAsync(update.CallbackQuery, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed handling Telegram update {UpdateId}", update.UpdateId);
        }

        return Ok();
    }

    private async Task HandleMessageAsync(TgMessage message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text?.Trim() ?? "";
        var isAdmin = IsAdmin(chatId);
        var isAdminCommand = text.StartsWith("/adduser") || text.StartsWith("/removeuser") || text.StartsWith("/users");

        if (isAdminCommand)
        {
            if (!isAdmin)
            {
                await telegram.SendMessageAsync(chatId, "🔒 Only admins can manage users.", ct: ct);
                return;
            }
            await HandleAdminCommandAsync(chatId, text, ct);
            return;
        }

        if (!isAdmin && !await IsApprovedAsync(chatId, ct))
        {
            await telegram.SendMessageAsync(chatId,
                $"🔒 This bot isn't open to this chat yet.\nAsk an admin to run <code>/adduser {chatId}</code>.", ct: ct);
            return;
        }

        if (text.StartsWith("/start"))
        {
            await telegram.SendMessageAsync(chatId,
                "👋 Paste a YouTube video link and I'll offer you a format to download.\n\n" +
                "Playlists and channels aren't supported here - use the web app for those: https://ytpd.videodownloaders.cloud", ct: ct);
            return;
        }

        ResolveResponseDto resolved;
        try
        {
            resolved = await resolver.ResolveAsync(text, ct);
        }
        catch (Exception)
        {
            await telegram.SendMessageAsync(chatId, "⚠️ Couldn't recognize that as a YouTube video link.", ct: ct);
            return;
        }

        if (resolved.Kind != "Video")
        {
            await telegram.SendMessageAsync(chatId,
                $"📃 That's a {resolved.Kind.ToLowerInvariant()} ({resolved.Items.Count} videos) - this bot only handles single videos.\n" +
                "Use the web app for playlists/channels: https://ytpd.videodownloaders.cloud", ct: ct);
            return;
        }

        var video = resolved.Items[0];
        var token = pending.Add(new PendingVideo(video.VideoId, video.Title, video.Author, text, DateTimeOffset.UtcNow));
        logger.LogInformation("DIAG pending token {Token}", token);

        var keyboard = new TgInlineKeyboardMarkup(new List<List<TgInlineKeyboardButton>>
        {
            new()
            {
                new TgInlineKeyboardButton("🎬 MP4 (video)", $"dl:{token}:mp4"),
                new TgInlineKeyboardButton("🎵 MP3 (audio)", $"dl:{token}:mp3"),
                new TgInlineKeyboardButton("🎧 M4A (audio)", $"dl:{token}:m4a"),
            },
        });

        await telegram.SendMessageAsync(chatId, $"🎥 <b>{System.Net.WebUtility.HtmlEncode(video.Title)}</b>\n{System.Net.WebUtility.HtmlEncode(video.Author)}\n\nPick a format:", keyboard, ct);
    }

    private async Task HandleCallbackAsync(TgCallbackQuery callback, CancellationToken ct)
    {
        await telegram.AnswerCallbackQueryAsync(callback.Id, ct: ct);

        if (callback.Message is null) return;
        var chatId = callback.Message.Chat.Id;
        var messageId = callback.Message.MessageId;

        if (!IsAdmin(chatId) && !await IsApprovedAsync(chatId, ct)) return;

        var parts = (callback.Data ?? "").Split(':');
        if (parts.Length != 3 || parts[0] != "dl" || !pending.TryGet(parts[1], out var video))
        {
            await telegram.EditMessageTextAsync(chatId, messageId, "⌛ This link expired - paste the URL again.", ct);
            return;
        }

        var formatCode = parts[2];
        var format = formatCode switch
        {
            "mp4" => DownloadFormat.Mp4,
            "mp3" => DownloadFormat.Mp3,
            "m4a" => DownloadFormat.M4a,
            _ => DownloadFormat.Mp4,
        };

        var job = new DownloadJob
        {
            SourceUrl = video.SourceUrl,
            Format = format,
            Quality = "best",
            EmbedMetadata = true,
        };
        var item = new DownloadJobItem
        {
            JobId = job.Id,
            Job = job,
            VideoId = video.VideoId,
            Title = video.Title,
            Author = video.Author,
        };
        job.Items = [item];

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(item.Id, ct);

        await telegram.EditMessageTextAsync(chatId, messageId, $"⏳ Downloading <b>{System.Net.WebUtility.HtmlEncode(video.Title)}</b>...", ct);

        // The webhook request scope ends the moment this method returns (it
        // already answered Telegram above) - this needs its own scope to
        // keep polling and eventually send the finished file.
        _ = Task.Run(() => TrackAndDeliverAsync(item.Id, chatId, messageId, video.Title));
    }

    private async Task TrackAndDeliverAsync(Guid itemId, long chatId, long messageId, string title)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastReportedProgress = -1.0;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(3000);

            var item = await scopedDb.JobItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
            if (item is null) return;

            if (item.Status is JobItemStatus.Completed)
            {
                var path = System.IO.Path.Combine(_storage.DownloadsPath, item.JobId.ToString(), item.OutputFileName!);
                try
                {
                    await telegram.SendDocumentByPathAsync(chatId, path, title);
                    await telegram.EditMessageTextAsync(chatId, messageId, $"✅ Sent <b>{System.Net.WebUtility.HtmlEncode(title)}</b>.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send finished file for job item {ItemId}", itemId);
                    await telegram.EditMessageTextAsync(chatId, messageId, $"⚠️ Downloaded but couldn't send the file (it may exceed Telegram's 2GB limit).");
                }
                return;
            }

            if (item.Status is JobItemStatus.Failed)
            {
                await telegram.EditMessageTextAsync(chatId, messageId, $"❌ Failed: {System.Net.WebUtility.HtmlEncode(item.ErrorMessage ?? "unknown error")}");
                return;
            }

            if (item.Status is JobItemStatus.Cancelled)
            {
                await telegram.EditMessageTextAsync(chatId, messageId, "🚫 Cancelled.");
                return;
            }

            var roundedProgress = Math.Round(item.Progress, 1);
            if (roundedProgress != lastReportedProgress)
            {
                lastReportedProgress = roundedProgress;
                await telegram.EditMessageTextAsync(chatId, messageId,
                    $"⏳ {item.Status} <b>{System.Net.WebUtility.HtmlEncode(title)}</b>... {roundedProgress:P0}");
            }
        }

        await telegram.EditMessageTextAsync(chatId, messageId, "⌛ Timed out waiting for this download.");
    }

    private async Task HandleAdminCommandAsync(long adminChatId, string text, CancellationToken ct)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];

        if (command == "/users")
        {
            var users = await db.TelegramAllowedUsers.AsNoTracking().OrderBy(u => u.AddedAt).ToListAsync(ct);
            var admins = string.Join(", ", _opts.AllowedChatIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var approved = users.Count == 0
                ? "(none)"
                : string.Join("\n", users.Select(u => $"  <code>{u.ChatId}</code>{(string.IsNullOrEmpty(u.Label) ? "" : $" - {System.Net.WebUtility.HtmlEncode(u.Label)}")}"));
            await telegram.SendMessageAsync(adminChatId, $"👑 Admins: {admins}\n\n✅ Approved users:\n{approved}", ct: ct);
            return;
        }

        if (parts.Length < 2 || !long.TryParse(parts[1], out var targetChatId))
        {
            await telegram.SendMessageAsync(adminChatId, $"Usage: <code>{command} &lt;chat_id&gt;</code>", ct: ct);
            return;
        }

        if (command == "/adduser")
        {
            var label = parts.Length > 2 ? string.Join(' ', parts[2..]) : null;
            var existing = await db.TelegramAllowedUsers.FindAsync([targetChatId], ct);
            if (existing is null)
            {
                db.TelegramAllowedUsers.Add(new TelegramAllowedUser { ChatId = targetChatId, Label = label, AddedByChatId = adminChatId });
                await db.SaveChangesAsync(ct);
            }

            await telegram.SendMessageAsync(adminChatId, $"✅ Added <code>{targetChatId}</code>.", ct: ct);

            // Best-effort: only works if that chat has messaged the bot
            // before (Telegram won't let a bot message a chat cold).
            try { await telegram.SendMessageAsync(targetChatId, "🎉 You've been approved to use this bot. Send /start to begin.", ct: ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Couldn't notify newly-approved chat {ChatId}", targetChatId); }
            return;
        }

        if (command == "/removeuser")
        {
            var existing = await db.TelegramAllowedUsers.FindAsync([targetChatId], ct);
            if (existing is not null)
            {
                db.TelegramAllowedUsers.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            await telegram.SendMessageAsync(adminChatId, $"🗑️ Removed <code>{targetChatId}</code>.", ct: ct);
        }
    }

    private bool IsAdmin(long chatId)
    {
        var admins = _opts.AllowedChatIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return admins.Contains(chatId.ToString());
    }

    private Task<bool> IsApprovedAsync(long chatId, CancellationToken ct) =>
        db.TelegramAllowedUsers.AnyAsync(u => u.ChatId == chatId, ct);
}
