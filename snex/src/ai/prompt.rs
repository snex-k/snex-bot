use crate::config::EmojiConfig;
use crate::db::{ChatMessage, UserFact};

/// Собирает финальный system prompt: личность бота + список доступных
/// эмодзи и гифок, которые модель может вставлять тегами вида
/// [emoji:funny] или [gif:excited].
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

/// Собирает контекст для отправки в Groq: историю последних сообщений
/// канала и известные факты о человеке, которому бот отвечает.
pub fn build_context_message(
    history: &[ChatMessage],
    facts: &[UserFact],
    current_author: &str,
    current_content: &str,
) -> String {
    let mut parts = Vec::new();

    if !facts.is_empty() {
        let facts_text = facts
            .iter()
            .map(|f| format!("- {}", f.fact))
            .collect::<Vec<_>>()
            .join("\n");
        parts.push(format!(
            "Известные факты о пользователе {current_author}:\n{facts_text}"
        ));
    }

    if !history.is_empty() {
        let history_text = history
            .iter()
            .map(|m| format!("{}: {}", m.author_name, m.content))
            .collect::<Vec<_>>()
            .join("\n");
        parts.push(format!("Недавняя переписка в канале:\n{history_text}"));
    }

    parts.push(format!("Новое сообщение от {current_author}: {current_content}"));

    parts.join("\n\n")
}
