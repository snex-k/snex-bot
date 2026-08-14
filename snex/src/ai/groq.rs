use serde::{Deserialize, Serialize};

const GROQ_URL: &str = "https://api.groq.com/openai/v1/chat/completions";
const MODEL: &str = "openai/gpt-oss-120b";

#[derive(Serialize)]
struct ChatMessage {
    role: String,
    content: String,
}

#[derive(Serialize)]
struct ChatRequest {
    model: String,
    messages: Vec<ChatMessage>,
}

#[derive(Deserialize)]
struct ChatResponse {
    choices: Vec<Choice>,
}

#[derive(Deserialize)]
struct Choice {
    message: ResponseMessage,
}

#[derive(Deserialize)]
struct ResponseMessage {
    content: String,
}

pub struct GroqClient {
    api_key: String,
    http: reqwest::Client,
}

impl GroqClient {
    pub fn new(api_key: String) -> Self {
        Self {
            api_key,
            http: reqwest::Client::new(),
        }
    }

    /// Отправляет system prompt + сообщение пользователя, возвращает
    /// сырой текст ответа модели (с тегами [emoji:...]/[gif:...] внутри).
    pub async fn ask(&self, system_prompt: &str, user_message: &str) -> anyhow::Result<String> {
        let body = ChatRequest {
            model: MODEL.to_string(),
            messages: vec![
                ChatMessage {
                    role: "system".to_string(),
                    content: system_prompt.to_string(),
                },
                ChatMessage {
                    role: "user".to_string(),
                    content: user_message.to_string(),
                },
            ],
        };

        let response = self
            .http
            .post(GROQ_URL)
            .bearer_auth(&self.api_key)
            .json(&body)
            .send()
            .await?
            .error_for_status()?
            .json::<ChatResponse>()
            .await?;

        let text = response
            .choices
            .into_iter()
            .next()
            .map(|c| c.message.content)
            .unwrap_or_default();

        Ok(text)
    }
}
