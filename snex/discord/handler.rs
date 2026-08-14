use serenity::async_trait;
use serenity::model::channel::Message;
use serenity::prelude::*;

use crate::ai::groq::GroqClient;
use crate::config::Config;
use crate::discord::responder::parse_response;

pub struct Handler {
    pub config: Config,
    pub groq: GroqClient,
    pub system_prompt: String,
}

#[async_trait]
impl EventHandler for Handler {
    async fn message(&self, ctx: Context, msg: Message) {
        // Не отвечаем сами себе и другим ботам
        if msg.author.bot {
            return;
        }

        // Пока что реагируем только на упоминание бота.
        // Дальше сюда можно добавить: рандомный шанс ответить,
        // белый список каналов, ключевые слова и т.д.
        let bot_id = ctx.cache.current_user().id;
        if !msg.mentions_user_id(bot_id) {
            return;
        }

        let raw_reply = match self.groq.ask(&self.system_prompt, &msg.content).await {
            Ok(reply) => reply,
            Err(err) => {
                tracing::error!("Groq API ошибка: {err}");
                return;
            }
        };

        let parsed = parse_response(&raw_reply, &self.config.emojis);

        if !parsed.text.is_empty() {
            if let Err(err) = msg.channel_id.say(&ctx.http, &parsed.text).await {
                tracing::error!("не удалось отправить сообщение: {err}");
            }
        }

        for gif_url in parsed.gif_urls {
            if let Err(err) = msg.channel_id.say(&ctx.http, &gif_url).await {
                tracing::error!("не удалось отправить гифку: {err}");
            }
        }
    }
}
