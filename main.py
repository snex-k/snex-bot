import discord
from discord import app_commands
import aiohttp
import json
import urllib.parse
import re
import os

TOKEN = os.environ.get("TOKEN")
GROQ_API_KEY = os.environ.get("GROQ_API_KEY")

intents = discord.Intents.default()
intents.message_content = True
intents.members = True
client = discord.Client(intents=intents)
tree = app_commands.CommandTree(client)

histories: dict[int, list[dict]] = {}
user_thread: dict[int, int] = {}
allowed_channels: dict[int, list[int]] = {}  # guild_id -> список каналов (макс 3)

SYSTEM_PROMPT = (
    "Ты дружелюбный друг, общаешься как живой человек, неформально и просто. "
    "Отвечай коротко и по делу, без лишней воды. Не используй списки и заголовки. "
    "Пиши как в обычном чате. "
    "ВАЖНО: Всегда отвечай на том же языке, на котором написал пользователь. "
    "ВАЖНО: Всегда оборачивай весь свой ответ в жирный текст — используй ** с обеих сторон. "
    "Если пользователь просит нарисовать или сгенерировать картинку/изображение — "
    "используй инструмент generate_image с описанием на английском."
    "Не допускай ошибок!"
)

TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "generate_image",
            "description": "Сгенерировать изображение по текстовому описанию",
            "parameters": {
                "type": "object",
                "properties": {
                    "prompt": {
                        "type": "string",
                        "description": "Описание изображения на английском языке"
                    }
                },
                "required": ["prompt"]
            }
        }
    }
]


async def execute_tool(tool_name: str, args: dict):
    if tool_name == "generate_image":
        prompt = args.get("prompt", "beautiful scenery")
        encoded = urllib.parse.quote(prompt)
        url = (
            f"https://image.pollinations.ai/prompt/{encoded}"
            f"?width=1024&height=1024&nologo=true&seed={abs(hash(prompt)) % 99999}"
        )
        return f"IMAGE:{url}"
    return "Неизвестное действие."


MODELS = [
    "llama-3.3-70b-versatile",
    "llama-3.1-70b-versatile",
    "llama3-70b-8192",
    "llama-3.1-8b-instant",
    "mixtral-8x7b-32768",
    "gemma2-9b-it",
]

async def groq_request(messages, tools=None):
    payload_base: dict = {"messages": messages, "max_tokens": 1024}
    if tools:
        payload_base["tools"] = tools
        payload_base["tool_choice"] = "auto"
    headers = {
        "Authorization": f"Bearer {GROQ_API_KEY}",
        "Content-Type": "application/json"
    }
    last_error = "Неизвестная ошибка"
    for model in MODELS:
        try:
            payload = {**payload_base, "model": model}
            async with aiohttp.ClientSession() as s:
                async with s.post(
                    "https://api.groq.com/openai/v1/chat/completions",
                    headers=headers,
                    data=json.dumps(payload)
                ) as r:
                    status = r.status
                    data = await r.json()

            # Проверка по статусу
            if status == 429:
                print(f"[429] Лимит на {model}, следующая...")
                continue

            # Проверка по тексту ошибки
            if "choices" not in data:
                err_msg = data.get("error", {}).get("message", "")
                last_error = err_msg or str(data)
                print(f"[ERR] {model}: {last_error}")
                continue

            print(f"[OK] Использую модель: {model}")
            return data["choices"][0]["message"]

        except Exception as e:
            last_error = str(e)
            print(f"[EXC] {model}: {e}")
            continue

    raise RuntimeError(f"Все модели недоступны. Последняя ошибка: {last_error}")


async def ai(uid: int, text: str):
    histories[uid].append({"role": "user", "content": text})
    messages = [{"role": "system", "content": SYSTEM_PROMPT}] + histories[uid]
    msg = await groq_request(messages, tools=TOOLS)

    image_url = None

    if msg.get("tool_calls"):
        histories[uid].append(msg)
        results = []
        for call in msg["tool_calls"]:
            fn = call["function"]["name"]
            args = json.loads(call["function"]["arguments"])
            result = await execute_tool(fn, args)
            if result.startswith("IMAGE:"):
                image_url = result[6:]
                result = "Картинка сгенерирована!"
            results.append(result)
            histories[uid].append({
                "role": "tool",
                "tool_call_id": call["id"],
                "content": result
            })
        messages2 = [{"role": "system", "content": SYSTEM_PROMPT}] + histories[uid]
        final = await groq_request(messages2)
        reply = final.get("content") or "Готово!"
    else:
        reply = msg.get("content", "...")

        # Запасной парсер — если модель написала вызов тексто
        match = re.search(r'"prompt"\s*:\s*"([^"]+)"', reply)
        if match:
            prompt = match.group(1)
            result = await execute_tool("generate_image", {"prompt": prompt})
            if result.startswith("IMAGE:"):
                image_url = result[6:]
            # Убираем весь мусор с function call (любой формат)
            reply = re.sub(r'\{[^{}]*"prompt"[^{}]*\}', '', reply, flags=re.DOTALL)
            reply = re.sub(r'</?function[^>]*>', '', reply)
            reply = re.sub(r'generate_image\s*=?\s*', '', reply)
            reply = re.sub(r'\s{2,}', ' ', reply).strip()
            if not reply:
                reply = "**Держи!**"

    histories[uid].append({"role": "assistant", "content": reply})
    return reply, image_url


async def send_v2(
    channel_id: int,
    uid: int,
    text: str,
    username: str,
    avatar_url: str,
    reply_to: int = None,
    image_url: str = None
):
    inner = [
        {
            "type": 9,
            "components": [{"type": 10, "content": f"# **{username}**"}],
            "accessory": {"type": 11, "media": {"url": avatar_url}},
        },
        {"type": 14, "divider": True, "spacing": 1},
        {"type": 10, "content": text},
    ]

    if image_url:
        inner.append({
            "type": 12,
            "items": [
                {
                    "media": {"url": image_url},
                    "description": "Сгенерированное изображение"
                }
            ]
        })

    # Разделитель + серая кнопка только с эмодзи
    inner.append({
        "type": 1,
        "components": [
            {
                "type": 2,
                "style": 2,  # серая (Secondary)
                "custom_id": f"end_dialog_{uid}",
                "emoji": {
                    "name": "cross",
                    "id": "1504024178494410865",
                    "animated": False
                }
            }
        ]
    })

    payload: dict = {
        "flags": 32768,
        "components": [
            {
                "type": 17,
                "components": inner
            }
        ]
    }

    if reply_to:
        payload["message_reference"] = {
            "message_id": str(reply_to),
            "fail_if_not_exists": False
        }

    headers = {
        "Authorization": f"Bot {TOKEN}",
        "Content-Type": "application/json"
    }
    async with aiohttp.ClientSession() as s:
        async with s.post(
            f"https://discord.com/api/v10/channels/{channel_id}/messages",
            headers=headers,
            data=json.dumps(payload)
        ) as r:
            data = await r.json()
            if r.status not in (200, 201):
                print(f"Discord ошибка: {data}")
                raise RuntimeError(str(data))
            return int(data["id"])


def is_channel_allowed(guild_id: int, channel_id: int) -> bool:
    channels = allowed_channels.get(guild_id)
    if not channels:
        return True  # если каналы не заданы — разрешено везде
    return channel_id in channels


async def end_dialog(uid: int, interaction: discord.Interaction):
    thread_id = user_thread.get(uid)
    if not thread_id or interaction.channel_id != thread_id:
        await interaction.response.send_message("Диалог не найден.", ephemeral=True)
        return
    await interaction.response.send_message("**Чат завершён. Ветка удаляется...**")
    user_thread.pop(uid, None)
    histories.pop(uid, None)
    channel = interaction.channel
    if channel:
        try:
            await channel.delete()
        except Exception as e:
            print(f"Ошибка удаления ветки: {e}")


@client.event
async def on_interaction(interaction: discord.Interaction):
    if interaction.type == discord.InteractionType.component:
        custom_id = interaction.data.get("custom_id", "")
        if custom_id.startswith("end_dialog_"):
            try:
                uid = int(custom_id.split("end_dialog_")[1])
            except ValueError:
                return
            if interaction.user.id != uid:
                await interaction.response.send_message(
                    "Ты не можешь завершить чужой диалог!", ephemeral=True
                )
                return
            await end_dialog(uid, interaction)


@tree.command(name="kimo", description="Чат с ИИ")
async def kimo(interaction: discord.Interaction):
    uid = interaction.user.id
    guild_id = interaction.guild_id

    await interaction.response.defer(ephemeral=True)

    # Проверка разрешённых каналов
    if not is_channel_allowed(guild_id, interaction.channel_id):
        channels = allowed_channels.get(guild_id, [])
        mentions = []
        for ch_id in channels:
            ch = interaction.guild.get_channel(ch_id)
            if ch:
                mentions.append(ch.mention)
        mention_str = ", ".join(mentions) if mentions else "нужный канал"
        await interaction.followup.send(
            f"<a:kaito:1504034420720533538> **Команду /kimo можно использовать только в: {mention_str}**",
            ephemeral=True
        )
        return

    # Если у пользователя уже есть активная ветка — не создаём новую
    existing_thread_id = user_thread.get(uid)
    if existing_thread_id:
        existing_thread = interaction.guild.get_channel(existing_thread_id)
        if existing_thread:
            await interaction.followup.send(
                f"<a:kaito:1504034420720533538> **У тебя уже есть активный чат: {existing_thread.mention}**\n"
                f"Заверши его прежде чем начать новый.",
                ephemeral=True
            )
            return
        else:
            # Ветка была удалена вручную — очищаем
            user_thread.pop(uid, None)
            histories.pop(uid, None)

    histories[uid] = []

    thread = await interaction.channel.create_thread(
        name=f"kimo · {interaction.user.display_name}",
        type=discord.ChannelType.private_thread,
        invitable=False,
    )
    await thread.add_user(interaction.user)
    user_thread[uid] = thread.id

    await interaction.followup.send(
        f"<a:kaito:1504034420720533538> **Твой чат: {thread.mention}**",
        ephemeral=True
    )

    greeting, image_url = await ai(uid, "Поприветствуй меня коротко на русском языке!")
    await send_v2(
        thread.id, uid, greeting,
        interaction.user.display_name,
        interaction.user.display_avatar.url,
        image_url=image_url
    )


@tree.command(name="setchannel", description="Добавить/убрать канал для /kimo (макс 3, только для админов)")
async def setchannel(interaction: discord.Interaction):
    if not interaction.user.guild_permissions.administrator:
        await interaction.response.send_message(
            "<:cross:1504024178494410865> **У тебя нет прав.**", ephemeral=True
        )
        return

    guild_id = interaction.guild_id
    channel_id = interaction.channel_id

    if guild_id not in allowed_channels:
        allowed_channels[guild_id] = []

    ch_list = allowed_channels[guild_id]

    # Если канал уже в списке — убираем (toggle)
    if channel_id in ch_list:
        ch_list.remove(channel_id)
        if not ch_list:
            del allowed_channels[guild_id]
            await interaction.response.send_message(
                "<:checkmark:1504023759101886607> **Этот канал убран. Теперь /kimo работает везде.**",
                ephemeral=True
            )
        else:
            mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
            await interaction.response.send_message(
                f"<:checkmark:1504023759101886607> **Канал убран из списка.**\n"
                f"Активные каналы: {', '.join(mentions)}",
                ephemeral=True
            )
        return

    # Если уже 3 — отказываем
    if len(ch_list) >= 3:
        mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
        await interaction.response.send_message(
            f"<:cross:1504024178494410865> **Достигнут лимит (3 канала).**\n"
            f"Текущие каналы: {', '.join(mentions)}\n"
            f"Введи `/setchannel` в одном из них чтобы убрать его.",
            ephemeral=True
        )
        return

    # Добавляем канал
    ch_list.append(channel_id)
    mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
    await interaction.response.send_message(
        f"<:checkmark:1504023759101886607> **Канал добавлен!**\n"
        f"Активные каналы: {', '.join(mentions)}",
        ephemeral=True
    )


@client.event
async def on_message(message: discord.Message):
    if message.author.bot:
        return
    uid = message.author.id
    thread_id = user_thread.get(uid)
    if not thread_id or message.channel.id != thread_id:
        return

    async with message.channel.typing():
        try:
            reply, image_url = await ai(uid, message.content)
        except Exception as e:
            print(f"Ошибка ИИ: {e}")
            await message.reply(f"Ошибка: {e}")
            return

    try:
        await send_v2(
            message.channel.id, uid, reply,
            message.author.display_name,
            message.author.display_avatar.url,
            reply_to=message.id,
            image_url=image_url
        )
    except Exception as e:
        print(f"Ошибка отправки: {e}")


@client.event
async def on_ready():
    await client.change_presence(
        status=discord.Status.do_not_disturb
    )
    for guild in client.guilds:
        await tree.sync(guild=guild)
    await tree.sync()
    print(f"Запущен: {client.user}")


client.run(TOKEN)