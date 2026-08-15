using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;
using LineHfBot.Configuration;
using LineHfBot.State;
using Microsoft.Extensions.Options;

namespace LineHfBot.Line;

/// <summary>
/// Provisions the mode-switcher rich menu (one highlighted variant per mode) and keeps a user's
/// displayed menu in sync when the mode changes off-menu (e.g. the "back to chat" quick reply).
/// Provisioning is idempotent: menus already registered (detected by alias) are reused.
/// </summary>
public sealed class RichMenuManager
{
    private static readonly ChatMode[] Modes = [ChatMode.Chat, ChatMode.Image, ChatMode.Video];

    private readonly RichMenuClient? _client;
    private readonly bool _enabled;
    private readonly string _locale;
    private readonly ILogger<RichMenuManager> _logger;
    private readonly Dictionary<ChatMode, string> _menuIds = [];

    public RichMenuManager(IOptions<LineOptions> line, IOptions<AppOptions> app, ILogger<RichMenuManager> logger)
    {
        _logger = logger;
        _enabled = app.Value.RichMenuEnabled;
        _locale = string.Equals(app.Value.Locale, "ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
        _client = _enabled ? RichMenuClient.CreateWithStaticToken(line.Value.ChannelAccessToken) : null;
    }

    private static string Name(ChatMode m) => m.ToString().ToLowerInvariant();
    private static string AliasId(ChatMode m) => $"richmenu-{Name(m)}";

    /// <summary>Idempotently create the three rich menus + aliases and set the default (chat). Safe to call once at startup.</summary>
    public async Task ProvisionAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            _logger.LogInformation("Rich menu disabled (App:RichMenuEnabled=false); skipping provisioning.");
            return;
        }

        var aliases = await _client.Messaging.Api.V2.Bot.Richmenu.Alias.List.GetAsync(cancellationToken: ct);
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in aliases?.Aliases ?? [])
        {
            if (a.RichMenuAliasId is { } aid && a.RichMenuId is { } rid)
            {
                existing[aid] = rid;
            }
        }

        foreach (var mode in Modes)
        {
            if (existing.TryGetValue(AliasId(mode), out var existingId))
            {
                _menuIds[mode] = existingId;
                _logger.LogInformation("Rich menu for {Mode} already provisioned ({Id}).", mode, existingId);
                continue;
            }

            var menuId = await _client.CreateAsync(BuildRequest(mode), ct);
            if (string.IsNullOrEmpty(menuId))
            {
                _logger.LogError("Rich menu create returned no id for {Mode}; skipping.", mode);
                continue;
            }

            await _client.SetImageFromFileAsync(menuId, ImagePath(mode), ct);
            await _client.Messaging.Api.V2.Bot.Richmenu.Alias.PostAsync(
                new CreateRichMenuAliasRequest { RichMenuAliasId = AliasId(mode), RichMenuId = menuId },
                cancellationToken: ct);
            _menuIds[mode] = menuId;
            _logger.LogInformation("Provisioned rich menu for {Mode} ({Id}).", mode, menuId);
        }

        if (_menuIds.TryGetValue(ChatMode.Chat, out var chatId))
        {
            await _client.SetDefaultAsync(chatId, ct);
            _logger.LogInformation("Default rich menu set to chat ({Id}).", chatId);
        }
    }

    /// <summary>
    /// Re-link the user's rich menu to reflect a mode change that did not come from a menu tap
    /// (quick reply / slash command). Best-effort; the tab-switch action already handles menu taps.
    /// </summary>
    public async Task SyncUserMenuAsync(string userId, ChatMode mode, CancellationToken ct)
    {
        if (_client is null || string.IsNullOrEmpty(userId) || !_menuIds.TryGetValue(mode, out var id))
        {
            return;
        }
        try
        {
            await _client.LinkToUserAsync(userId, id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to sync rich menu for user (mode={Mode}): {Detail}", mode, ex.Message);
        }
    }

    private string ImagePath(ChatMode mode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "richmenu", _locale, $"richmenu-{Name(mode)}.png");
        if (!File.Exists(path) && _locale != "en")
        {
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "richmenu", "en", $"richmenu-{Name(mode)}.png");
        }
        return path;
    }

    // Every menu shares the same three tabs; only the image (highlight) differs.
    private RichMenuRequest BuildRequest(ChatMode active) => new()
    {
        Size = new RichMenuSize { Width = 2500, Height = 843 },
        Selected = false,
        Name = $"mode-{Name(active)}-{_locale}",
        ChatBarText = _locale == "ja" ? "メニュー" : "Menu",
        Areas =
        [
            Area(0, ChatMode.Chat),
            Area(1, ChatMode.Image),
            Area(2, ChatMode.Video),
        ],
    };

    private RichMenuArea Area(int column, ChatMode target) => new()
    {
        Bounds = new RichMenuBounds
        {
            X = column * 833,
            Y = 0,
            Width = column == 2 ? 834 : 833,
            Height = 843,
        },
        Action = new RichMenuSwitchAction
        {
            Type = "richmenuswitch",
            RichMenuAliasId = AliasId(target),
            Data = $"action=mode&value={Name(target)}",
            Label = TabLabel(target),
        },
    };

    private string TabLabel(ChatMode m) => (_locale, m) switch
    {
        ("ja", ChatMode.Chat) => "チャット",
        ("ja", ChatMode.Image) => "画像",
        ("ja", ChatMode.Video) => "動画",
        (_, ChatMode.Chat) => "Chat",
        (_, ChatMode.Image) => "Image",
        (_, _) => "Video",
    };
}
