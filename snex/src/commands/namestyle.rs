use std::collections::HashMap;
use std::sync::Mutex;

use once_cell::sync::Lazy;
use serde_json::json;
use serenity::builder::{
    CreateActionRow, CreateInputText, CreateInteractionResponse,
    CreateInteractionResponseMessage, CreateModal, CreateSelectMenu, CreateSelectMenuKind,
    CreateSelectMenuOption,
};
use serenity::http::Http;
use serenity::model::application::{ComponentInteraction, ModalInteraction};
use serenity::model::id::GuildId;
use serenity::model::input_text::InputTextStyle;

pub const FONTS: &[(u8, &str)] = &[
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

pub const EFFECTS: &[(u8, &str)] = &[
    (1, "Solid"),
    (2, "Gradient"),
    (3, "Neon"),
    (4, "Toon"),
    (5, "Pop"),
    (6, "Shadow"),
];

fn font_name(id: u8) -> &'static str {
    FONTS.iter().find(|(k, _)| *k == id).map(|(_, v)| *v).unwrap_or("?")
}

fn effect_name(id: u8) -> &'static str {
    EFFECTS.iter().find(|(k, _)| *k == id).map(|(_, v)| *v).unwrap_or("?")
}

#[derive(Default, Clone, Copy)]
struct PendingStyle {
    font_id: Option<u8>,
    effect_id: Option<u8>,
}

static PENDING_STYLE: Lazy<Mutex<HashMap<u64, PendingStyle>>> =
    Lazy::new(|| Mutex::new(HashMap::new()));

fn hex_to_int(hex: &str) -> anyhow::Result<u32> {
    let cleaned = hex.trim().trim_start_matches('#');
    u32::from_str_radix(cleaned, 16).map_err(|_| anyhow::anyhow!("не hex-цвет"))
}

async fn apply_name_style(
    http: &Http,
    guild_id: GuildId,
    font_id: u8,
    effect_id: u8,
    colors: Vec<u32>,
) -> anyhow::Result<()> {
    let route = format!("/guilds/{guild_id}/members/@me");
    let body = json!({
        "display_name_font_id": font_id,
        "display_name_effect_id": effect_id,
        "display_name_colors": colors,
    });
    http.client()
        .patch(format!("https://discord.com/api/v10{route}"))
        .header("Authorization", format!("Bot {}", http.token()))
        .json(&body)
        .send()
        .await?
        .error_for_status()?;
    Ok(())
}

async fn reset_name_style(http: &Http, guild_id: GuildId) -> anyhow::Result<()> {
    let route = format!("/guilds/{guild_id}/members/@me");
    let body = json!({
        "display_name_font_id": null,
        "display_name_effect_id": null,
        "display_name_colors": null,
    });
    http.client()
        .patch(format!("https://discord.com/api/v10{route}"))
        .header("Authorization", format!("Bot {}", http.token()))
        .json(&body)
        .send()
        .await?
        .error_for_status()?;
    Ok(())
}

/// Строит компоненты панели /namestyle: два select-меню (шрифт, эффект)
/// и кнопку сброса.
pub fn build_namestyle_panel() -> Vec<CreateActionRow> {
    let font_options: Vec<CreateSelectMenuOption> = FONTS
        .iter()
        .map(|(id, name)| CreateSelectMenuOption::new(*name, id.to_string()))
        .collect();

    let effect_options: Vec<CreateSelectMenuOption> = EFFECTS
        .iter()
        .map(|(id, name)| CreateSelectMenuOption::new(*name, id.to_string()))
        .collect();

    let font_select = CreateSelectMenu::new(
        "namestyle_font_select",
        CreateSelectMenuKind::String { options: font_options },
    )
    .placeholder("Выбери шрифт");

    let effect_select = CreateSelectMenu::new(
        "namestyle_effect_select",
        CreateSelectMenuKind::String { options: effect_options },
    )
    .placeholder("Выбери эффект");

    let reset_button = serenity::builder::CreateButton::new("namestyle_reset_btn")
        .label("Сбросить стиль")
        .style(serenity::model::application::ButtonStyle::Danger);

    vec![
        CreateActionRow::SelectMenu(font_select),
        CreateActionRow::SelectMenu(effect_select),
        CreateActionRow::Buttons(vec![reset_button]),
    ]
}

/// Обрабатывает select-меню и кнопки панели /namestyle.
/// Возвращает true, если взаимодействие было обработано этим модулем.
pub async fn handle_namestyle_component(
    http: &Http,
    interaction: &ComponentInteraction,
) -> anyhow::Result<bool> {
    let Some(guild_id) = interaction.guild_id else {
        return Ok(false);
    };
    let custom_id = interaction.data.custom_id.as_str();

    match custom_id {
        "namestyle_font_select" => {
            let serenity::model::application::ComponentInteractionDataKind::StringSelect { values } =
                &interaction.data.kind
            else {
                return Ok(false);
            };
            let font_id: u8 = values.first().and_then(|v| v.parse().ok()).unwrap_or(11);

            {
                let mut state = PENDING_STYLE.lock().unwrap();
                let entry = state.entry(guild_id.get()).or_default();
                entry.font_id = Some(font_id);
            }

            let response = CreateInteractionResponseMessage::new()
                .content(format!(
                    "### Шрифт выбран: {}\n-# Теперь выбери эффект",
                    font_name(font_id)
                ))
                .ephemeral(true);
            interaction
                .create_response(http, CreateInteractionResponse::Message(response))
                .await?;
            Ok(true)
        }

        "namestyle_effect_select" => {
            let serenity::model::application::ComponentInteractionDataKind::StringSelect { values } =
                &interaction.data.kind
            else {
                return Ok(false);
            };
            let effect_id: u8 = values.first().and_then(|v| v.parse().ok()).unwrap_or(1);

            let font_id = {
                let mut state = PENDING_STYLE.lock().unwrap();
                let entry = state.entry(guild_id.get()).or_default();
                entry.effect_id = Some(effect_id);
                entry.font_id
            };

            let Some(font_id) = font_id else {
                let response = CreateInteractionResponseMessage::new()
                    .content("### Сначала выбери шрифт в select-меню выше")
                    .ephemeral(true);
                interaction
                    .create_response(http, CreateInteractionResponse::Message(response))
                    .await?;
                return Ok(true);
            };

            let needs_two = effect_id == 2; // Gradient
            let mut inputs = vec![CreateInputText::new(
                InputTextStyle::Short,
                "Цвет 1 (#hex)",
                "color1",
            )
            .placeholder("#FF69B4")
            .required(true)
            .max_length(7)];

            if needs_two {
                inputs.push(
                    CreateInputText::new(InputTextStyle::Short, "Цвет 2 (#hex) — для Gradient", "color2")
                        .placeholder("#5865F2")
                        .required(true)
                        .max_length(7),
                );
            }

            let modal = CreateModal::new(
                format!("namestyle_color_modal_{font_id}_{effect_id}"),
                "Цвет стиля ника",
            )
            .components(
                inputs
                    .into_iter()
                    .map(CreateActionRow::InputText)
                    .collect(),
            );

            interaction
                .create_response(http, CreateInteractionResponse::Modal(modal))
                .await?;
            Ok(true)
        }

        "namestyle_reset_btn" => {
            interaction.defer_ephemeral(http).await?;
            match reset_name_style(http, guild_id).await {
                Ok(()) => {
                    PENDING_STYLE.lock().unwrap().remove(&guild_id.get());
                    interaction
                        .create_followup(
                            http,
                            serenity::builder::CreateInteractionResponseFollowup::new()
                                .content("### Стиль сброшен")
                                .ephemeral(true),
                        )
                        .await?;
                }
                Err(err) => {
                    interaction
                        .create_followup(
                            http,
                            serenity::builder::CreateInteractionResponseFollowup::new()
                                .content(format!("`ошибка: {err}`"))
                                .ephemeral(true),
                        )
                        .await?;
                }
            }
            Ok(true)
        }

        _ => Ok(false),
    }
}

/// Обрабатывает отправку модалки с цветом(-ами).
pub async fn handle_namestyle_modal(
    http: &Http,
    interaction: &ModalInteraction,
) -> anyhow::Result<bool> {
    let Some(guild_id) = interaction.guild_id else {
        return Ok(false);
    };
    let custom_id = interaction.data.custom_id.as_str();

    if !custom_id.starts_with("namestyle_color_modal_") {
        return Ok(false);
    }

    let parts: Vec<&str> = custom_id
        .trim_start_matches("namestyle_color_modal_")
        .split('_')
        .collect();
    let (Some(font_str), Some(effect_str)) = (parts.first(), parts.get(1)) else {
        return Ok(true);
    };
    let font_id: u8 = font_str.parse().unwrap_or(11);
    let effect_id: u8 = effect_str.parse().unwrap_or(1);

    let mut color1_raw = String::new();
    let mut color2_raw = String::new();

    for row in &interaction.data.components {
        for component in &row.components {
            if let serenity::model::application::ActionRowComponent::InputText(input) = component {
                if let Some(value) = &input.value {
                    match input.custom_id.as_str() {
                        "color1" => color1_raw = value.clone(),
                        "color2" => color2_raw = value.clone(),
                        _ => {}
                    }
                }
            }
        }
    }

    let mut colors = Vec::new();
    match hex_to_int(&color1_raw) {
        Ok(c) => colors.push(c),
        Err(_) => {
            let response = CreateInteractionResponseMessage::new()
                .content("### Неверный формат цвета, используй `#rrggbb`")
                .ephemeral(true);
            interaction
                .create_response(http, CreateInteractionResponse::Message(response))
                .await?;
            return Ok(true);
        }
    }
    if !color2_raw.trim().is_empty() {
        match hex_to_int(&color2_raw) {
            Ok(c) => colors.push(c),
            Err(_) => {
                let response = CreateInteractionResponseMessage::new()
                    .content("### Неверный формат цвета, используй `#rrggbb`")
                    .ephemeral(true);
                interaction
                    .create_response(http, CreateInteractionResponse::Message(response))
                    .await?;
                return Ok(true);
            }
        }
    }

    if effect_id == 2 && colors.len() != 2 {
        let response = CreateInteractionResponseMessage::new()
            .content("### Для эффекта Gradient нужно указать 2 цвета")
            .ephemeral(true);
        interaction
            .create_response(http, CreateInteractionResponse::Message(response))
            .await?;
        return Ok(true);
    }

    interaction.defer_ephemeral(http).await?;

    match apply_name_style(http, guild_id, font_id, effect_id, colors).await {
        Ok(()) => {
            PENDING_STYLE.lock().unwrap().remove(&guild_id.get());
            let text = format!(
                "### Стиль применён\n> **Шрифт:** {}\n> **Эффект:** {}\n> **Цвет:** {}{}",
                font_name(font_id),
                effect_name(effect_id),
                color1_raw,
                if color2_raw.trim().is_empty() {
                    String::new()
                } else {
                    format!(" → {color2_raw}")
                }
            );
            interaction
                .create_followup(
                    http,
                    serenity::builder::CreateInteractionResponseFollowup::new()
                        .content(text)
                        .ephemeral(true),
                )
                .await?;
        }
        Err(err) => {
            interaction
                .create_followup(
                    http,
                    serenity::builder::CreateInteractionResponseFollowup::new()
                        .content(format!("`ошибка: {err}`"))
                        .ephemeral(true),
                )
                .await?;
        }
    }

    Ok(true)
}
