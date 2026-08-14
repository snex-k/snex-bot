mod ai;
mod config;
mod discord;
mod utils;

use serenity::prelude::*;

use ai::groq::GroqClient;
use ai::prompt::build_system_prompt;
use config::Config;
use discord::handler::Handler;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();

    let cfg = Config::load()?;
    let system_prompt = build_system_prompt(&cfg.personality, &cfg.emojis);
    let groq = GroqClient::new(cfg.groq_api_key.clone());
    let discord_token = cfg.discord_token.clone();

    let handler = Handler {
        config: cfg,
        groq,
        system_prompt,
    };

    let intents = GatewayIntents::GUILD_MESSAGES
        | GatewayIntents::MESSAGE_CONTENT
        | GatewayIntents::GUILDS;

    let mut client = Client::builder(&discord_token, intents)
        .event_handler(handler)
        .await?;

    tracing::info!("Snex запускается...");
    client.start().await?;

    Ok(())
}
