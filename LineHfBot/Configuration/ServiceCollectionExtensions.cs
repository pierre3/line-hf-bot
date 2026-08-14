namespace LineHfBot.Configuration;

/// <summary>Options registration with startup validation (fail fast on missing secrets/required values).</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBotOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Required values are validated at startup so misconfiguration fails fast instead of
        // silently running with an empty channel secret (which would accept forged webhooks).
        services.AddOptions<LineOptions>()
            .Bind(configuration.GetSection(LineOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelSecret), "Line:ChannelSecret is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChannelAccessToken), "Line:ChannelAccessToken is required.")
            .ValidateOnStart();

        services.AddOptions<HuggingFaceOptions>()
            .Bind(configuration.GetSection(HuggingFaceOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "HuggingFace:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChatModel), "HuggingFace:ChatModel is required.")
            .ValidateOnStart();

        // These have sensible defaults, so binding is enough.
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.Section));
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.Section));

        return services;
    }
}
