use rand::Rng;
use serenity::async_trait;
use serenity::builder::CreateCommand;
use serenity::model::application::Interaction;
use serenity::model::channel::Message;
use serenity::model::gateway::Ready;
use serenity::prelude::*;

use crate::ai::groq::GroqClient;
use crate::ai::prompt::build_context_message;
use crate::commands::namestyle::{
    build_namestyle_panel, handle_namestyle_component, handle_namestyle_modal,
};
use crate::config::Config;
use crate::db::Database;
use crate::discord::error::send_error;
use crate::discord::responder::parse_response;

const HISTORY_LIMIT: i64 = 15;
const FACTS_LIMIT: i64 = 10;

pub struct Handler {
    pub config: Config,
    pub groq: GroqClient,
    pub system_prompt: String,
    pub db: Database,
}

#[async_trait]
impl EventHandler for Handler {
    async fn ready(&self, ctx: Context, ready: Ready) {
        tracing::info!("{} на связи", ready.user.name);

        for guild in &ready.guilds {
            let command = CreateCommand::new("namestyle")
                .description("Настроить стиль ника бота на этом сервере");
            if let Err(err) = guild.id.create_command(&ctx.http, command).await {
                tracing::error!("не удалось зарегистрировать /namestyle на {}: {err}", guild.id);
            }
        }
    }

    async fn interaction_create(&self, ctx: Context, interaction: Interaction) {
        match interaction {
            Interaction::Command(cmd) if cmd.data.name == "namestyle" => {
                let response = serenity::builder::CreateInteractionResponseMessage::new()
                    .components(build_namestyle_panel())
                    .ephemeral(true);
                if let Err(err) = cmd
                    .create_response(
                        &ctx.http,
                        serenity::builder::CreateInteractionResponse::Message(response),
                    )
                    .await
                {
                    tracing::error!("не удалось открыть панель /namestyle: {err}");
                }
            }
            Interaction::Component(component) => {
                if let Err(err) = handle_namestyle_component(&ctx.http, &component).await {
                    tracing::error!("ошибка обработки компонента namestyle: {err}");
                }
            }
            Interaction::Modal(modal) => {
                if let Err(err) = handle_namestyle_modal(&ctx.http, &modal).await {
                    tracing::error!("ошибка обработки модалки namestyle: {err}");
                }
            }
            _ => {}
        }
    }

    async fn message(&self, ctx: Context, msg: Message) {
        if msg.author.bot {
            return;
        }

        let channel_id = msg.channel_id.to_string();
        let author_id = msg.author.id.to_string();
        let author_name = msg.author.name.clone();


        if let Err(err) = self
            .db
            .save_message(&channel_id, &author_id, &author_name, &msg.content)
            .await
        {
            tracing::error!("не удалось сохранить сообщение: {err}");
        }

        let bot_id = ctx.cache.current_user().id;
        let is_mentioned = msg.mentions_user_id(bot_id);

        if !is_mentioned {
            let roll: f64 = rand::thread_rng().gen();
            if roll > self.config.random_reply_chance {
                return;
            }
        }

        // Подтягиваем историю канала и известные факты о человеке,
        // чтобы модель понимала контекст, а не отвечала в вакууме.
        let history = self
            .db
            .recent_messages(&channel_id, HISTORY_LIMIT)
            .await
            .unwrap_or_default();
        let facts = self
            .db
            .user_facts(&author_id, FACTS_LIMIT)
            .await
            .unwrap_or_default();

        let context_message =
            build_context_message(&history, &facts, &author_name, &msg.content);

        let raw_reply = match self.groq.ask(&self.system_prompt, &context_message).await {
            Ok(reply) => reply,
            Err(err) => {
                tracing::error!("Groq API ошибка: {err}");
                send_error(&ctx, msg.channel_id, "не смог получить ответ от ИИ").await;
                return;
            }
        };

        let parsed = parse_response(&raw_reply, &self.config.emojis);

        if !parsed.text.is_empty() {
            if let Err(err) = msg.channel_id.say(&ctx.http, &parsed.text).await {
                tracing::error!("не удалось отправить сообщение: {err}");
                send_error(&ctx, msg.channel_id, "не удалось отправить сообщение").await;
            }
        }

        for gif_url in parsed.gif_urls {
            if let Err(err) = msg.channel_id.say(&ctx.http, &gif_url).await {
                tracing::error!("не удалось отправить гифку: {err}");
            }
        }


        match self.groq.extract_fact(&msg.content).await {
            Ok(Some(fact)) => {
                if let Err(err) = self.db.save_fact(&author_id, &fact).await {
                    tracing::error!("не удалось сохранить факт: {err}");
                }
            }
            Ok(None) => {}
            Err(err) => tracing::error!("Groq API ошибка при извлечении факта: {err}"),
        }
    }
}
