using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace YtpdWeb.Api.Bot;

// Thin wrapper over the Bot API's HTTP surface, pointed at the self-hosted
// server (Telegram:ApiBaseUrl) rather than api.telegram.org - see
// TelegramOptions for why. Only the handful of methods this bot needs.
public class TelegramClient(HttpClient http, IOptions<TelegramOptions> options)
{
    private readonly TelegramOptions _opts = options.Value;
    private string BaseUrl => $"{_opts.ApiBaseUrl.TrimEnd('/')}/bot{_opts.BotToken}";

    public Task SendMessageAsync(long chatId, string text, TgInlineKeyboardMarkup? keyboard = null, CancellationToken ct = default) =>
        PostAsync("sendMessage", new { chat_id = chatId, text, reply_markup = keyboard, parse_mode = "HTML" }, ct);

    public Task EditMessageTextAsync(long chatId, long messageId, string text, CancellationToken ct = default) =>
        PostAsync("editMessageText", new { chat_id = chatId, message_id = messageId, text, parse_mode = "HTML" }, ct);

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default) =>
        PostAsync("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, ct);

    // `path` is a local filesystem path visible to the telegram-bot-api
    // container itself (shared volume - see docker-compose.yml), not a
    // path on this container. Sent as a file:// URI, which only a --local
    // server understands - api.telegram.org would reject this as garbage.
    public Task SendDocumentByPathAsync(long chatId, string path, string caption, CancellationToken ct = default) =>
        PostAsync("sendDocument", new { chat_id = chatId, document = "file://" + path, caption }, ct);

    public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct = default) =>
        PostAsync("setWebhook", new { url, secret_token = secretToken }, ct);

    private async Task PostAsync(string method, object body, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync($"{BaseUrl}/{method}", body, ct);
        resp.EnsureSuccessStatusCode();
    }
}
