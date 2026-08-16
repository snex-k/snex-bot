using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Snex;

public class Program
{
    public static async Task Main(string[] args)
    {
        var config = Config.Load();
        var systemPrompt = Prompt.BuildSystemPrompt(config.Personality, config.Emojis);
        var groq = new Groq(config.GroqApiKey);
        var db = await Database.ConnectAsync(config.DatabaseUrl);
        var messageHandler = new Handler(config, groq, systemPrompt, db);

        // Render ограничивает число inotify-инстансов на контейнер, а
        // ASP.NET Core по умолчанию следит за файлами конфигурации через
        // FileSystemWatcher — упираемся в лимит. Мы не используем
        // appsettings.json (все настройки через .env), поэтому отключаем
        // отслеживание изменений конфигурации целиком.
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder:reloadConfigOnChange", "false");

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDiscordGateway(options =>
        {
            options.Token = config.DiscordToken;
            options.Intents = GatewayIntents.GuildMessages
                | GatewayIntents.MessageContent
                | GatewayIntents.Guilds;
        });

        builder.Services
            .AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>()
            .AddComponentInteractions<StringMenuInteraction, StringMenuInteractionContext>()
            .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>()
            .AddComponentInteractions<ModalInteraction, ModalInteractionContext>();

        // Render (free Web Service) требует открытый порт, иначе убивает
        // процесс по таймауту. Боту порт не нужен, поэтому поднимаем
        // минимальный endpoint, который просто отвечает "ok".
        var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        var app = builder.Build();
        app.MapGet("/", () => "ok");

        var client = app.Services.GetRequiredService<GatewayClient>();
        client.MessageCreate += async message => await messageHandler.HandleAsync(message, client);

        app.AddModules(typeof(Program).Assembly);

        IHost host = app;
        host.UseGatewayEventHandlers();

        // Регистрируем /namestyle и другие slash-команды в Discord.
        // По гильдиям — команды появляются мгновенно, в отличие от
        // глобальной регистрации, которая может занять до часа.
        var commandService = app.Services.GetRequiredService<ApplicationCommandService<ApplicationCommandContext>>();
        client.Ready += async readyEventArgs =>
        {
            foreach (var guildId in readyEventArgs.GuildIds)
            {
                await commandService.RegisterCommandsAsync(client.Rest, client.Id, guildId);
            }
            Console.WriteLine($"Slash-команды зарегистрированы на {readyEventArgs.GuildIds.Count} серверах");
        };

        Console.WriteLine("Snex запускается...");
        await app.RunAsync();
    }
}