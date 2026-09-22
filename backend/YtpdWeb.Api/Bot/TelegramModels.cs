using System.Text.Json.Serialization;

namespace YtpdWeb.Api.Bot;

// Minimal subset of the Bot API's types - only the fields this bot actually
// reads or sends. Hand-rolled instead of a NuGet client library because the
// self-hosted local server (see docker-compose.yml) needs a plain-string
// local-file-path value in `document`/`video`, which most client libraries
// don't model (they assume api.telegram.org's usual upload-or-file_id shape).

public record TgUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TgMessage? Message,
    [property: JsonPropertyName("callback_query")] TgCallbackQuery? CallbackQuery
);

public record TgMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("chat")] TgChat Chat,
    [property: JsonPropertyName("text")] string? Text
);

public record TgChat(
    [property: JsonPropertyName("id")] long Id
);

public record TgCallbackQuery(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("message")] TgMessage? Message,
    [property: JsonPropertyName("data")] string? Data
);

public record TgInlineKeyboardButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData
);

public record TgInlineKeyboardMarkup(
    [property: JsonPropertyName("inline_keyboard")] List<List<TgInlineKeyboardButton>> InlineKeyboard
);
