use serenity::model::id::ChannelId;
use serenity::prelude::*;

pub async fn send_error(ctx: &Context, channel_id: ChannelId, message: &str) {
    let text = format!("**`ошибка: {message}`**");

    if let Err(err) = channel_id.say(&ctx.http, &text).await {
        tracing::error!("**не удалось отправить сообщение об ошибке: {err}**");
    }
}
