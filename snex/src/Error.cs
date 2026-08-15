using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Snex;

public static class ErrorMessage
{
    public static async Task SendAsync(GatewayClient client, ulong channelId, string message)
    {
        var botUser = client.Cache.User!;
        var avatarUrl = botUser.GetAvatarUrl()?.ToString() ?? botUser.DefaultAvatarUrl.ToString();

        var section = new ComponentSectionProperties(
            new ComponentSectionThumbnailProperties(new ComponentMediaProperties(avatarUrl)),
            [new TextDisplayProperties($"`ошибка: {message}`")]);

        var container = new ComponentContainerProperties([section])
        {
            AccentColor = new Color(0xE74C3C),
        };

        var properties = new MessageProperties
        {
            Flags = MessageFlags.IsComponentsV2,
            Components = [container],
        };

        try
        {
            await client.Rest.SendMessageAsync(channelId, properties);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"не удалось отправить error-компонент: {ex.Message}");
        }
    }
}
