mod ai;
mod commands;
mod config;
mod db;
mod discord;
mod utils;

use axum::{routing::get, Router};
use serenity::prelude::*;

use ai::groq::GroqClient;
use ai::prompt::build_system_prompt;
use config::Config;
use db::Database;
use discord::handler::Handler;

/// Render (free Web Service) требует открытый порт, иначе убивает процесс
/// по таймауту. Боту порт не нужен, поэтому поднимаем фиктивный сервер,
/// который просто отвечает "ok" на любой запрос.
async fn run_dummy_server() {
    let port = std::env::var("PORT").unwrap_or_else(|_| "10000".to_string());
    let app = Router::new().route("/", get(|| async { "ok" }));
    let listener = tokio::net::TcpListener::bind(format!("0.0.0.0:{port}"))
        .await
        .expect("не удалось забиндить порт");

    tracing::info!("Dummy HTTP-сервер слушает на порту {port}");
    axum::serve(listener, app).await.expect("dummy server упал");
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();

    let cfg = Config::load()?;
    let system_prompt = build_system_prompt(&cfg.personality, &cfg.emojis);
    let groq = GroqClient::new(cfg.groq_api_key.clone());
    let discord_token = cfg.discord_token.clone();

    tracing::info!("Подключаюсь к базе данных...");
    let db = Database::connect(&cfg.database_url).await?;

    let handler = Handler {
        config: cfg,
        groq,
        system_prompt,
        db,
    };

    let intents = GatewayIntents::GUILD_MESSAGES
        | GatewayIntents::MESSAGE_CONTENT
        | GatewayIntents::GUILDS;

    let mut client = Client::builder(&discord_token, intents)
        .event_handler(handler)
        .await?;

    tracing::info!("Snex запускается...");

    // Discord-бот и фиктивный HTTP-сервер работают параллельно.
    tokio::select! {
        result = client.start() => {
            if let Err(err) = result {
                tracing::error!("Discord клиент упал: {err}");
            }
        }
        _ = run_dummy_server() => {}
    }

    Ok(())
}
