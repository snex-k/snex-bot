using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace Snex;

public class Program
{
    public static async Task Main(string[] args)
    {
        var config = Config.Load();
        var systemPrompt = Prompt.BuildSystemPrompt(config.Personality, config.Emojis);
        var groq = new Groq(config.GroqApiKey);
        var db = Database.Connect(config.DatabaseUrl);
        var messageHandler = new Handler(config, groq, systemPrompt, db);

        Environment.SetEnvironmentVariable("DOTNET_hostBuilder:reloadConfigOnChange", "false");

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDiscordGateway(options =>
        {
            options.Token = config.DiscordToken;
            options.Intents = GatewayIntents.GuildMessages
                | GatewayIntents.MessageContent
                | GatewayIntents.Guilds;
        });
        
        var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        var client = app.Services.GetRequiredService<GatewayClient>();
        client.MessageCreate += async message => await messageHandler.HandleAsync(message, client);

        Console.WriteLine("Snex запускается...");
        await app.RunAsync();
    }
}
