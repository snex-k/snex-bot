use serenity::builder::{
    CreateAttachment, CreateMessage, CreateContainer, CreateSection,
    CreateSectionAccessory, CreateSectionComponent, CreateTextDisplay, CreateThumbnail,
    CreateUnfurledMediaItem,
};
use serenity::model::id::ChannelId;
use serenity::prelude::*;

pub async fn send_error_embed(ctx: &Context, channel_id: ChannelId, message: &str) {
    let avatar_url = ctx.cache.current_user().face();

    let section = CreateSection::new(
        vec![CreateSectionComponent::TextDisplay(CreateTextDisplay::new(
            format!("⚠️ **Что-то пошло не так**\n{message}"),
        ))],
        CreateSectionAccessory::Thumbnail(CreateThumbnail::new(
            CreateUnfurledMediaItem::new(avatar_url),
        )),
    );

    let container = CreateContainer::new(vec![CreateSectionComponent::Section(section)])
        .accent_color(0xE74C3C);

    let builder = CreateMessage::new().components(vec![container.into()]);

    if let Err(err) = channel_id.send_message(&ctx.http, builder).await {
        tracing::error!("не удалось отправить error embed: {err}");
    }
}
