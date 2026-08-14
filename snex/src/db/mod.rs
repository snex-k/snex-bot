use sqlx::{postgres::PgPoolOptions, PgPool};

#[derive(Debug, Clone)]
pub struct ChatMessage {
    pub author_name: String,
    pub content: String,
}

#[derive(Debug, Clone)]
pub struct UserFact {
    pub fact: String,
}

#[derive(Clone)]
pub struct Database {
    pool: PgPool,
}

impl Database {
    pub async fn connect(database_url: &str) -> anyhow::Result<Self> {
        let pool = PgPoolOptions::new()
            .max_connections(5)
            .connect(database_url)
            .await?;

        Ok(Self { pool })
    }
  
    pub async fn save_message(
        &self,
        channel_id: &str,
        author_id: &str,
        author_name: &str,
        content: &str,
    ) -> anyhow::Result<()> {
        sqlx::query(
            "insert into messages (channel_id, author_id, author_name, content) \
             values ($1, $2, $3, $4)",
        )
        .bind(channel_id)
        .bind(author_id)
        .bind(author_name)
        .bind(content)
        .execute(&self.pool)
        .await?;

        Ok(())
    }

    pub async fn recent_messages(
        &self,
        channel_id: &str,
        limit: i64,
    ) -> anyhow::Result<Vec<ChatMessage>> {
        let rows: Vec<(String, String)> = sqlx::query_as(
            "select author_name, content from messages \
             where channel_id = $1 \
             order by created_at desc \
             limit $2",
        )
        .bind(channel_id)
        .bind(limit)
        .fetch_all(&self.pool)
        .await?;

        Ok(rows
            .into_iter()
            .rev()
            .map(|(author_name, content)| ChatMessage {
                author_name,
                content,
            })
            .collect())
    }

    pub async fn save_fact(&self, user_id: &str, fact: &str) -> anyhow::Result<()> {
        sqlx::query("insert into user_facts (user_id, fact) values ($1, $2)")
            .bind(user_id)
            .bind(fact)
            .execute(&self.pool)
            .await?;

        Ok(())
    }

    pub async fn user_facts(&self, user_id: &str, limit: i64) -> anyhow::Result<Vec<UserFact>> {
        let rows: Vec<(String,)> = sqlx::query_as(
            "select fact from user_facts \
             where user_id = $1 \
             order by created_at desc \
             limit $2",
        )
        .bind(user_id)
        .bind(limit)
        .fetch_all(&self.pool)
        .await?;

        Ok(rows.into_iter().map(|(fact,)| UserFact { fact }).collect())
    }
}
