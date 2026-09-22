using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace YtpdWeb.Api.Bot;

// Thin wrapper over the Bot API's HTTP surface, pointed at the self-hosted
// server (Telegram:ApiBaseUrl) rather than api.telegram.org - see
// TelegramOptions for why. Only the handful of methods this bot needs.
public class TelegramClient(HttpClient http, IOptions<TelegramOptions> options)
{
    // The local Bot API server rejects `"field": null` outright ("Bad
    // Request: object expected as reply markup") instead of treating it
    // like an omitted field the way api.telegram.org does - confirmed
    // live. Every optional field here (reply_markup, callback text, ...)
    // needs to disappear from the JSON entirely when unset, not serialize
    // as null.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TelegramOptions _opts = options.Value;
    private string BaseUrl => $"{_opts.ApiBaseUrl.TrimEnd('/')}/bot{_opts.BotToken}";

    public Task SendMessageAsync(long chatId, string text, TgInlineKeyboardMarkup? keyboard = null, CancellationToken ct = default) =>
        PostAsync("sendMessage", new { chat_id = chatId, text, reply_markup = keyboard, parse_mode = "HTML" }, ct);

    public Task EditMessageTextAsync(long chatId, long messageId, string text, TgInlineKeyboardMarkup? keyboard = null, CancellationToken ct = default) =>
        PostAsync("editMessageText", new { chat_id = chatId, message_id = messageId, text, reply_markup = keyboard, parse_mode = "HTML" }, ct);

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default) =>
        PostAsync("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, ct);

    // `path` is a local filesystem path visible to the telegram-bot-api
    // container itself (shared volume - see docker-compose.yml), not a
    // path on this container.
    //
    // The docs for --local mode describe passing local paths directly (as
    // a file:// URI or even a bare path) as an optimization that skips a
    // real upload. Tried both, live, against this server
    // (aiogram/telegram-bot-api): every variant gets parsed as a remote
    // URL and rejected ("invalid file HTTP URL specified" / "URL host is
    // empty") - this server build doesn't actually support it, docs
    // notwithstanding. A genuine multipart upload works (confirmed live:
    // real file delivered, audio metadata intact), so that's what this
    // does - one extra copy over the docker-internal network to
    // telegram-bot-api, not over the real internet, so still fast.
    public async Task SendDocumentByPathAsync(long chatId, string path, string caption, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString()), "chat_id");
        form.Add(new StringContent(caption), "caption");

        await using var stream = File.OpenRead(path);
        using var fileContent = new StreamContent(stream);
        form.Add(fileContent, "document", Path.GetFileName(path));

        var resp = await http.PostAsync($"{BaseUrl}/sendDocument", form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var responseBody = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Telegram sendDocument failed ({(int)resp.StatusCode}): {responseBody}");
        }
    }

    public Task SetWebhookAsync(string url, string secretToken, CancellationToken ct = default) =>
        PostAsync("setWebhook", new { url, secret_token = secretToken }, ct);

    private async Task PostAsync(string method, object body, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync($"{BaseUrl}/{method}", body, JsonOptions, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var responseBody = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Telegram {method} failed ({(int)resp.StatusCode}): {responseBody}");
        }
    }
}
