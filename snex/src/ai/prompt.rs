use crate::config::EmojiConfig;

pub fn build_system_prompt(personality: &str, emojis: &EmojiConfig) -> String {
    let emoji_list = emojis
        .emoji
        .keys()
        .map(|k| format!("[emoji:{k}]"))
        .collect::<Vec<_>>()
        .join(", ");

    let gif_list = emojis
        .gifs
        .keys()
        .map(|k| format!("[gif:{k}]"))
        .collect::<Vec<_>>()
        .join(", ");

    format!(
        "{personality}\n\n\
        Доступные эмодзи (вставляй тегом прямо в текст): {emoji_list}\n\
        Доступные гифки (вставляй тегом, если хочешь отправить гифку): {gif_list}\n\n\
        Пример использования: \"это было неожиданно [emoji:surprised]\""
    )
}
