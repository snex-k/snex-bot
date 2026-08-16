// Команда /namestyle — панель управления стилем ника бота на сервере
// (шрифт, эффект, цвет/градиент), через недокументированный эндпоинт
// Discord PATCH /guilds/{guild_id}/members/@me.
//
// Стиль хранится на стороне Discord, а не у нас — переживает рестарт
// и офлайн бота, поэтому базы данных тут не нужно.

using System.Collections.Concurrent;
using System.Net.Http.Json;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Snex;

public static class NameStyleData
{
    public static readonly (byte Id, string Name)[] Fonts =
    [
        (1, "Bangers"),
        (2, "BioRhyme"),
        (3, "Sakura"),
        (4, "Chicle"),
        (5, "Compagnon"),
        (6, "MuseoModerno"),
        (7, "Neo-Castel"),
        (8, "Pixelify Sans"),
        (9, "Ribes"),
        (10, "Sinistre"),
        (11, "Default (GG Sans)"),
        (12, "Zilla Slab"),
    ];

    public static readonly (byte Id, string Name)[] Effects =
    [
        (1, "Solid"),
        (2, "Gradient"),
        (3, "Neon"),
        (4, "Toon"),
        (5, "Pop"),
        (6, "Shadow"),
    ];

    public static string FontName(byte id) => Fonts.FirstOrDefault(f => f.Id == id).Name ?? "?";
    public static string EffectName(byte id) => Effects.FirstOrDefault(e => e.Id == id).Name ?? "?";

    // Временное состояние выбора шрифта/эффекта по guild_id, пока
    // пользователь не введёт цвет(а) в модалке. Хранится в памяти —
    // переживать рестарт не нужно, это промежуточный шаг одной
    // настройки за один присест.
    public static readonly ConcurrentDictionary<ulong, (byte? FontId, byte? EffectId)> Pending = new();

    public static int HexToInt(string hex)
    {
        var cleaned = hex.Trim().TrimStart('#');
        return Convert.ToInt32(cleaned, 16);
    }

    public static async Task ApplyAsync(RestClient rest, ulong guildId, byte fontId, byte effectId, int[] colors)
    {
        var body = new
        {
            display_name_font_id = fontId,
            display_name_effect_id = effectId,
            display_name_colors = colors,
        };
        await PatchSelfMemberAsync(rest, guildId, body);
    }

    public static async Task ResetAsync(RestClient rest, ulong guildId)
    {
        var body = new
        {
            display_name_font_id = (byte?)null,
            display_name_effect_id = (byte?)null,
            display_name_colors = (int[]?)null,
        };
        await PatchSelfMemberAsync(rest, guildId, body);
    }

    private static async Task PatchSelfMemberAsync(RestClient rest, ulong guildId, object body)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", rest.Token?.RawToken ?? "");

        var response = await http.PatchAsJsonAsync(
            $"https://discord.com/api/v10/guilds/{guildId}/members/@me", body);
        response.EnsureSuccessStatusCode();
    }

    /// Собирает Components V2 сообщение с одним текстовым блоком —
    /// используется для всех служебных ответов namestyle (статусы, ошибки).
    public static InteractionMessageProperties TextV2(string text)
    {
        var container = new ComponentContainerProperties
        {
            new TextDisplayProperties(text),
        };

        return new InteractionMessageProperties
        {
            Flags = MessageFlags.Ephemeral | MessageFlags.IsComponentsV2,
            Components = [container],
        };
    }
}

public class NameStyleCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("namestyle", "Настроить стиль ника бота на этом сервере")]
    public async Task NamestyleAsync()
    {
        var fontMenu = new StringMenuProperties("namestyle_font_select")
            .WithPlaceholder("Выбери шрифт")
            .AddOptions(NameStyleData.Fonts
                .Select(f => new StringMenuSelectOptionProperties(f.Name, f.Id.ToString())));

        var effectMenu = new StringMenuProperties("namestyle_effect_select")
            .WithPlaceholder("Выбери эффект")
            .AddOptions(NameStyleData.Effects
                .Select(e => new StringMenuSelectOptionProperties(e.Name, e.Id.ToString())));

        var resetButton = new ButtonProperties("namestyle_reset_btn", "Сбросить стиль", ButtonStyle.Danger);

        var container = new ComponentContainerProperties
        {
            new TextDisplayProperties("### Стиль ника бота\n-# Выбери шрифт и эффект, затем укажи цвет(а)"),
            fontMenu,
            effectMenu,
            new ActionRowProperties().AddComponents(resetButton),
        };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Flags = MessageFlags.Ephemeral | MessageFlags.IsComponentsV2,
            Components = [container],
        }));
    }
}

public class NameStyleComponentModule : ComponentInteractionModule<StringMenuInteractionContext>
{
    [ComponentInteraction("namestyle_font_select")]
    public async Task FontSelectAsync()
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var fontId = byte.Parse(Context.SelectedValues[0]);

        NameStyleData.Pending.AddOrUpdate(
            guildId,
            (fontId, null),
            (_, existing) => (fontId, existing.EffectId));

        await RespondAsync(InteractionCallback.Message(
            NameStyleData.TextV2($"### Шрифт выбран: {NameStyleData.FontName(fontId)}\n-# Теперь выбери эффект")));
    }

    [ComponentInteraction("namestyle_effect_select")]
    public async Task EffectSelectAsync()
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var effectId = byte.Parse(Context.SelectedValues[0]);

        var state = NameStyleData.Pending.AddOrUpdate(
            guildId,
            (null, effectId),
            (_, existing) => (existing.FontId, effectId));

        if (state.FontId is not byte fontId)
        {
            await RespondAsync(InteractionCallback.Message(
                NameStyleData.TextV2("### Сначала выбери шрифт в select-меню выше")));
            return;
        }

        var needsTwo = effectId == 2; // Gradient
        var color1Input = new LabelProperties(
            "Цвет 1 (#hex)",
            new TextInputProperties("color1", TextInputStyle.Short)
                .WithPlaceholder("#FF69B4")
                .WithRequired(true)
                .WithMaxLength(7));

        var modal = new ModalProperties("namestyle_color_modal", "Цвет стиля ника")
            .AddComponents(color1Input);

        if (needsTwo)
        {
            var color2Input = new LabelProperties(
                "Цвет 2 (#hex) — для Gradient",
                new TextInputProperties("color2", TextInputStyle.Short)
                    .WithPlaceholder("#5865F2")
                    .WithRequired(true)
                    .WithMaxLength(7));
            modal.AddComponents(color2Input);
        }

        await RespondAsync(InteractionCallback.Modal(modal));
    }
}

public class NameStyleButtonModule : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("namestyle_reset_btn")]
    public async Task ResetAsync()
    {
        var guildId = Context.Interaction.GuildId!.Value;

        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        try
        {
            await NameStyleData.ResetAsync(Context.Client.Rest, guildId);
            NameStyleData.Pending.TryRemove(guildId, out _);
            await FollowupAsync(NameStyleData.TextV2("### Стиль сброшен"));
        }
        catch (Exception ex)
        {
            await FollowupAsync(NameStyleData.TextV2($"`ошибка: {ex.Message}`"));
        }
    }
}

public class NameStyleModalModule : ComponentInteractionModule<ModalInteractionContext>
{
    [ComponentInteraction("namestyle_color_modal")]
    public async Task ColorModalAsync()
    {
        var guildId = Context.Interaction.GuildId!.Value;

        if (!NameStyleData.Pending.TryGetValue(guildId, out var state)
            || state.FontId is not byte fontId
            || state.EffectId is not byte effectId)
        {
            await RespondAsync(InteractionCallback.Message(
                NameStyleData.TextV2("### Сессия настройки истекла, начни заново с `/namestyle`")));
            return;
        }

        var components = Context.Interaction.Data.Components;
        var color1Raw = components.OfType<TextInput>().FirstOrDefault(t => t.CustomId == "color1")?.Value ?? "";
        var color2Raw = components.OfType<TextInput>().FirstOrDefault(t => t.CustomId == "color2")?.Value ?? "";

        var colors = new List<int>();
        try
        {
            colors.Add(NameStyleData.HexToInt(color1Raw));
        }
        catch
        {
            await RespondAsync(InteractionCallback.Message(
                NameStyleData.TextV2("### Неверный формат цвета, используй `#rrggbb`")));
            return;
        }

        if (!string.IsNullOrWhiteSpace(color2Raw))
        {
            try
            {
                colors.Add(NameStyleData.HexToInt(color2Raw));
            }
            catch
            {
                await RespondAsync(InteractionCallback.Message(
                    NameStyleData.TextV2("### Неверный формат цвета, используй `#rrggbb`")));
                return;
            }
        }

        if (effectId == 2 && colors.Count != 2)
        {
            await RespondAsync(InteractionCallback.Message(
                NameStyleData.TextV2("### Для эффекта Gradient нужно указать 2 цвета")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        try
        {
            await NameStyleData.ApplyAsync(Context.Client.Rest, guildId, fontId, effectId, colors.ToArray());
            NameStyleData.Pending.TryRemove(guildId, out _);

            var colorText = colors.Count > 1 ? $"{color1Raw} → {color2Raw}" : color1Raw;
            var text = $"### Стиль применён\n" +
                       $"> **Шрифт:** {NameStyleData.FontName(fontId)}\n" +
                       $"> **Эффект:** {NameStyleData.EffectName(effectId)}\n" +
                       $"> **Цвет:** {colorText}";

            await FollowupAsync(NameStyleData.TextV2(text));
        }
        catch (Exception ex)
        {
            await FollowupAsync(NameStyleData.TextV2($"`ошибка: {ex.Message}`"));
        }
    }
}