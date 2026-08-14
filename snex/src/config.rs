use serde::Deserialize;
use std::collections::HashMap;
use std::fs;

#[derive(Debug, Deserialize)]
pub struct EmojiConfig {
    pub emoji: HashMap<String, String>,
    pub gifs: HashMap<String, String>,
}

pub struct Config {
    pub discord_token: String,
    pub groq_api_key: String,
    pub personality: String,
    pub emojis: EmojiConfig,
    pub random_reply_chance: f64,
    pub database_url: String,
}

impl Config {
    pub fn load() -> anyhow::Result<Self> {
        dotenvy::dotenv().ok();

        let discord_token = std::env::var("DISCORD_TOKEN")
            .map_err(|_| anyhow::anyhow!("DISCORD_TOKEN не найден в .env"))?;
        let groq_api_key = std::env::var("GROQ_API_KEY")
            .map_err(|_| anyhow::anyhow!("GROQ_API_KEY не найден в .env"))?;

        let personality = fs::read_to_string("config/personality.txt")
            .map_err(|_| anyhow::anyhow!("не найден config/personality.txt"))?;

        let emojis_raw = fs::read_to_string("config/emojis.toml")
            .map_err(|_| anyhow::anyhow!("не найден config/emojis.toml"))?;
        let emojis: EmojiConfig = toml::from_str(&emojis_raw)?;

        let random_reply_chance = std::env::var("RANDOM_REPLY_CHANCE")
            .ok()
            .and_then(|v| v.parse::<f64>().ok())
            .unwrap_or(0.15);

        let database_url = std::env::var("DATABASE_URL")
            .map_err(|_| anyhow::anyhow!("DATABASE_URL не найден в .env"))?;

        Ok(Config {
            discord_token,
            groq_api_key,
            personality,
            emojis,
            random_reply_chance,
            database_url,
        })
    }
}
