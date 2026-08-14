use crate::config::EmojiConfig;

pub struct ParsedResponse {
    pub text: String,
    pub gif_urls: Vec<String>,
}

pub fn parse_response(raw: &str, emojis: &EmojiConfig) -> ParsedResponse {
    let mut text = raw.to_string();
    let mut gif_urls = Vec::new();

    for (key, emoji) in &emojis.emoji {
        let tag = format!("[emoji:{key}]");
        text = text.replace(&tag, emoji);
    }

    for (key, url) in &emojis.gifs {
        let tag = format!("[gif:{key}]");
        if text.contains(&tag) {
            gif_urls.push(url.clone());
            text = text.replace(&tag, "").trim().to_string();
        }
    }

    ParsedResponse { text, gif_urls }
}
