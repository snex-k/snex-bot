import discord
from discord import app_commands
from google import genai
from google.genai import types
import json
import urllib.parse
import re
import os
import datetime
import unicodedata
import asyncio

TOKEN = os.environ.get("TOKEN")
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")

# --- Google Gemini Client ---
gemini_client = genai.Client(api_key=GEMINI_API_KEY)

intents = discord.Intents.default()
intents.message_content = True
intents.members = True
client = discord.Client(intents=intents)
tree = app_commands.CommandTree(client)

histories: dict[int, list[dict]] = {}
user_thread: dict[int, int] = {}
allowed_channels: dict[int, list[int]] = {}

# --- System prompts ---
SYSTEM_PROMPT_RU = (
    "Тебя зовут Мико. Твой разработчик — Кими. "
    "Ты просто собеседник. Общаешься естественно и неформально, как живой человек в Discord. "
    "Отвечай коротко, понятно и по теме. Не пиши слишком длинные сообщения. "
    "Не используй списки, заголовки и официальный стиль. "
    "Общайся на 'ты'. Всегда отвечай только на русском языке. "
    "Пиши легко, с эмоциями и вайбом обычного чата. "
    "Если пользователь отправил картинку, гифку или видео — сначала отреагируй именно на них. "
    "Не выдумывай детали, которых нет. "
    "Не уходи в странные размышления или философию. "
    "Имя пользователя используй только при первом приветствии. "
    "В конце каждого сообщения добавляй эмодзи  "
    "Всегда оборачивай ответ в **жирный текст**. "
    "Если пользователь прямо просит сгенерировать изображение — используй generate_image. "
    "Для generate_image всегда пиши prompt на английском языке."
)

SYSTEM_PROMPT_EN = (
    "Your name is Miko. Your developer is Kimi. "
    "You are just a conversation partner. You chat naturally and casually, like a real person in Discord. "
    "Reply shortly, clearly, and to the point. Don't write overly long messages. "
    "Don't use lists, titles, or an overly formal tone. "
    "Address the user casually. Always reply only in English. "
    "Write with emotion and the vibe of a normal chat. "
    "If the user sends an image, gif, or video — react to it first. "
    "Don't make up details that aren't there. "
    "Don't drift into weird thoughts or philosophy. "
    "Use the user's name only on the first greeting. "
    "Add the emoji  at the end of every message. "
    "Always wrap your entire reply in **bold text**. "
    "If the user clearly asks to generate an image — use generate_image. "
    "For generate_image always write the prompt in English."
)

MODEL = "gemini-1.5-flash"


def detect_language(text: str) -> str:
    cyrillic = sum(1 for c in text if unicodedata.category(c) in ("Ll", "Lu") and "CYRILLIC" in unicodedata.name(c, ""))
    latin = sum(1 for c in text if unicodedata.category(c) in ("Ll", "Lu") and "LATIN" in unicodedata.name(c, ""))
    if cyrillic > latin:
        return "Russian"
    return "English"


# --- Gemini Function Declaration for Image Generation ---
generate_image_tool = types.Tool(function_declarations=[
    types.FunctionDeclaration(
        name="generate_image",
        description="Generate an image. Always pass the prompt parameter with a detailed English description of what to draw.",
        parameters=types.Schema(
            type=types.Type.OBJECT,
            properties={
                "prompt": types.Schema(
                    type=types.Type.STRING,
                    description="Detailed description of the image in English. Required."
                )
            },
            required=["prompt"]
        )
    )
])


async def execute_tool(tool_name: str, args: dict):
    if tool_name == "generate_image":
        prompt = args.get("prompt", "beautiful scenery")
        encoded = urllib.parse.quote(prompt)
        seed = abs(hash(prompt + str(datetime.datetime.now().minute))) % 99999
        url = (
            f"https://image.pollinations.ai/prompt/{encoded}"
            f"?width=1024&height=1024&nologo=true&seed={seed}&enhance=true"
        )
        try:
            import aiohttp
            async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=45)) as s:
                async with s.get(url) as r:
                    if r.status == 200:
                        image_bytes = await r.read()
                        print(f"[IMG] Downloaded {len(image_bytes)} bytes")
                        return f"IMAGE_BYTES:{url}", image_bytes
        except Exception as e:
            print(f"[IMG] Download failed: {e}")
            return f"IMAGE:{url}", None
    return "Неизвестное действие.", None


async def gemini_request(system_prompt: str, history: list, use_tools: bool = True):
    """Send a request to Gemini with conversation history."""
    config_args = {
        "system_instruction": system_prompt,
        "max_output_tokens": 1024,
    }
    if use_tools:
        config_args["tools"] = [generate_image_tool]

    config = types.GenerateContentConfig(**config_args)

    # Convert history to Gemini Content format
    contents = []
    for msg in history:
        role = "user" if msg["role"] == "user" else "model"
        content = msg.get("content", "")
        if content:
            contents.append(types.Content(
                role=role,
                parts=[types.Part.from_text(text=content)]
            ))

    try:
        response = await gemini_client.aio.models.generate_content(
            model=MODEL,
            contents=contents,
            config=config,
        )
        return response
    except Exception as e:
        print(f"[Gemini Error] {e}")
        raise


async def ai(uid: int, text: str, username: str = "пользователь", force_lang: str = None):
    try:
        lang = force_lang or detect_language(text)
        histories[uid].append({"role": "user", "content": text})

        base_prompt = SYSTEM_PROMPT_RU if lang == "Russian" else SYSTEM_PROMPT_EN
        name_label = "Имя пользователя" if lang == "Russian" else "User's name"
        system_with_user = base_prompt + f"\n{name_label}: {username}."

        # Slice last 30 messages for context
        context = histories[uid][-30:]

        # First call — with tools
        response = await gemini_request(system_with_user, context, use_tools=True)

        image_url = None
        image_bytes = None
        reply = None

        # Check if model wants to call a function
        if response.candidates:
            candidate = response.candidates[0]
            if candidate.content.parts:
                first_part = candidate.content.parts[0]

                if first_part.function_call:
                    fn_call = first_part.function_call
                    fn_name = fn_call.name
                    fn_args = {}
                    for k, v in fn_call.args.items():
                        if isinstance(v, str):
                            fn_args[k] = v
                        else:
                            fn_args[k] = str(v)

                    print(f"[Gemini] Function call: {fn_name}({fn_args})")

                    # Add model's function call message to history
                    histories[uid].append({
                        "role": "assistant",
                        "content": None,
                        "function_call": {"name": fn_name, "arguments": json.dumps(fn_args)}
                    })

                    # Execute the tool
                    tool_result, img_data = await execute_tool(fn_name, fn_args)
                    if tool_result.startswith("IMAGE"):
                        image_url = tool_result.split(":", 1)[1] if ":" in tool_result else tool_result
                        image_bytes = img_data
                        tool_result = "Картинка сгенерирована!"

                    # Add tool result to history
                    histories[uid].append({
                        "role": "tool",
                        "content": tool_result
                    })

                    # Second call — without tools to get final text
                    context2 = histories[uid][-30:]
                    response2 = await gemini_request(system_with_user, context2, use_tools=False)

                    if response2.candidates and response2.candidates[0].content.parts:
                        reply = response2.candidates[0].content.parts[0].text
                    else:
                        reply = "Готово!"
                else:
                    # Normal text response
                    reply = first_part.text
            else:
                reply = "..."

        if not reply:
            reply = "**...**"

        # Clean up the reply
        reply = re.sub(r'<\|[^>]+\|>', '', reply)
        reply = re.sub(r'generate_image\s*[=:]\s*\{[^}]*\}', '', reply, flags=re.DOTALL)
        reply = reply.strip() or "**...**"
        if not reply.startswith("**") and not reply.endswith("**"):
            reply = f"**{reply}**"

        histories[uid].append({"role": "assistant", "content": reply})
        return reply, image_url, image_bytes

    except Exception as e:
        print(f"Ошибка в ai(): {e}")
        return f"Ошибка: {e}", None, None


async def send_v2(
    channel_id: int,
    uid: int,
    text: str,
    username: str,
    avatar_url: str,
    reply_to: int = None,
    image_url: str = None,
    image_bytes: bytes = None
):
    import aiohttp

    inner = [
        {
            "type": 9,
            "components": [{"type": 10, "content": f"# **{username}**"}],
            "accessory": {"type": 11, "media": {"url": avatar_url}},
        },
        {"type": 14, "divider": True, "spacing": 1},
        {"type": 10, "content": text},
    ]

    use_file = image_bytes is not None
    if use_file:
        inner.append({
            "type": 12,
            "items": [{"media": {"url": "attachment://image.jpg"}, "description": "Сгенерированное изображение"}]
        })
    elif image_url:
        inner.append({
            "type": 12,
            "items": [{"media": {"url": image_url}, "description": "Сгенерированное изображение"}]
        })

    inner.append({
        "type": 1,
        "components": [
            {
                "type": 2,
                "style": 2,
                "custom_id": f"ask_end_{uid}",
                "emoji": {"name": "cross", "id": "1504024178494410865", "animated": False}
            }
        ]
    })

    payload: dict = {
        "flags": 32768,
        "components": [{"type": 17, "components": inner}]
    }

    if reply_to:
        payload["message_reference"] = {"message_id": str(reply_to), "fail_if_not_exists": False}

    headers = {"Authorization": f"Bot {TOKEN}"}
    async with aiohttp.ClientSession() as s:
        if use_file:
            form = aiohttp.FormData()
            form.add_field("payload_json", json.dumps(payload), content_type="application/json")
            form.add_field("files[0]", image_bytes, filename="image.jpg", content_type="image/jpeg")
            async with s.post(
                f"https://discord.com/api/v10/channels/{channel_id}/messages",
                headers=headers,
                data=form
            ) as r:
                data = await r.json()
                status = r.status
        else:
            headers["Content-Type"] = "application/json"
            async with s.post(
                f"https://discord.com/api/v10/channels/{channel_id}/messages",
                headers=headers,
                data=json.dumps(payload)
            ) as r:
                data = await r.json()
                status = r.status

        if status not in (200, 201):
            print(f"Discord ошибка: {data}")
            raise RuntimeError(str(data))
        return int(data["id"])


async def send_error_v2(channel_id: int, text: str, reply_to: int = None):
    import aiohttp

    payload: dict = {
        "flags": 32768,
        "components": [
            {
                "type": 17,
                "accent_color": 0xFF4444,
                "components": [
                    {"type": 10, "content": f"<:error:1504479091577983016> **{text}**"}
                ]
            }
        ]
    }
    if reply_to:
        payload["message_reference"] = {"message_id": str(reply_to), "fail_if_not_exists": False}
    headers = {"Authorization": f"Bot {TOKEN}", "Content-Type": "application/json"}
    async with aiohttp.ClientSession() as s:
        await s.post(
            f"https://discord.com/api/v10/channels/{channel_id}/messages",
            headers=headers,
            data=json.dumps(payload)
        )


async def show_end_confirmation(uid: int, interaction: discord.Interaction):
    import aiohttp

    payload = {
        "type": 4,
        "data": {
            "flags": 32768 | 64,
            "components": [
                {
                    "type": 17,
                    "components": [
                        {"type": 10, "content": "**Ты точно хочешь завершить диалог?**"},
                        {
                            "type": 1,
                            "components": [
                                {
                                    "type": 2,
                                    "style": 2,
                                    "custom_id": f"confirm_end_{uid}",
                                    "emoji": {"name": "checkmark", "id": "1504023759101886607", "animated": False}
                                },
                                {
                                    "type": 2,
                                    "style": 2,
                                    "custom_id": f"cancel_end_{uid}",
                                    "emoji": {"name": "cross", "id": "1504024178494410865", "animated": False}
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    }
    headers = {"Authorization": f"Bot {TOKEN}", "Content-Type": "application/json"}
    async with aiohttp.ClientSession() as s:
        await s.post(
            f"https://discord.com/api/v10/interactions/{interaction.id}/{interaction.token}/callback",
            headers=headers,
            data=json.dumps(payload)
        )


async def end_dialog(uid: int, interaction: discord.Interaction):
    channel = interaction.channel
    is_miko_thread = isinstance(channel, discord.Thread) and channel.name.startswith("miko ·")
    thread_id = user_thread.get(uid)
    in_memory = thread_id and interaction.channel_id == thread_id

    if not in_memory and not is_miko_thread:
        await interaction.response.send_message("Диалог не найден.", ephemeral=True)
        return

    await interaction.response.send_message("**Чат завершён. Ветка удаляется...**")
    user_thread.pop(uid, None)
    histories.pop(uid, None)
    try:
        await channel.delete()
    except Exception as e:
        print(f"Ошибка удаления ветки: {e}")


def is_channel_allowed(guild_id: int, channel_id: int) -> bool:
    channels = allowed_channels.get(guild_id)
    if not channels:
        return True
    return channel_id in channels


@client.event
async def on_interaction(interaction: discord.Interaction):
    if interaction.type == discord.InteractionType.component:
        custom_id = interaction.data.get("custom_id", "")

        if custom_id.startswith("ask_end_"):
            try:
                uid = int(custom_id.split("ask_end_")[1])
            except ValueError:
                return
            if interaction.user.id != uid:
                await interaction.response.send_message("Это не твой диалог!", ephemeral=True)
                return
            await show_end_confirmation(uid, interaction)

        elif custom_id.startswith("confirm_end_"):
            try:
                uid = int(custom_id.split("confirm_end_")[1])
            except ValueError:
                return
            if interaction.user.id != uid:
                await interaction.response.send_message("Это не твой диалог!", ephemeral=True)
                return
            await end_dialog(uid, interaction)

        elif custom_id.startswith("cancel_end_"):
            await interaction.response.send_message("**Отменено.**", ephemeral=True)


@tree.command(name="miko", description="Чат с ai")
async def miko(interaction: discord.Interaction):
    uid = interaction.user.id
    guild_id = interaction.guild_id
    display_name = interaction.user.display_name

    await interaction.response.defer(ephemeral=True)

    if not is_channel_allowed(guild_id, interaction.channel_id):
        channels = allowed_channels.get(guild_id, [])
        mentions = []
        for ch_id in channels:
            ch = interaction.guild.get_channel(ch_id)
            if ch:
                mentions.append(ch.mention)
        mention_str = ", ".join(mentions) if mentions else "нужный канал"
        await interaction.followup.send(
            f" **Команду** `/miko` **можно использовать только в: {mention_str}**",
            ephemeral=True
        )
        return

    existing_thread_id = user_thread.get(uid)
    if existing_thread_id:
        existing_thread = interaction.guild.get_channel(existing_thread_id)
        if existing_thread:
            await interaction.followup.send(
                f" **У тебя уже есть активный чат: {existing_thread.mention}**\n"
                f"Заверши его прежде чем начать новый.",
                ephemeral=True
            )
            return
        else:
            user_thread.pop(uid, None)
            histories.pop(uid, None)

    histories[uid] = []

    thread = await interaction.channel.create_thread(
        name=f"miko · {display_name}",
        type=discord.ChannelType.private_thread,
        invitable=False,
    )
    await thread.add_user(interaction.user)
    user_thread[uid] = thread.id

    await interaction.followup.send(
        f" **Твой чат: {thread.mention}**",
        ephemeral=True
    )

    greeting, image_url, image_bytes = await ai(
        uid,
        f"Поприветствуй меня коротко, моё имя {display_name}.",
        username=display_name,
        force_lang="Russian"
    )
    await send_v2(
        thread.id, uid, greeting,
        display_name,
        interaction.user.display_avatar.url,
        image_url=image_url,
        image_bytes=image_bytes
    )


@tree.command(name="setchannel", description="Добавить/убрать канал для miko (макс 3, admin)")
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

    if channel_id in ch_list:
        ch_list.remove(channel_id)
        if not ch_list:
            del allowed_channels[guild_id]
            await interaction.response.send_message(
                "<:checkmark:1504023759101886607> **Канал убран. Теперь** `/miko` **работает везде.**",
                ephemeral=True
            )
        else:
            mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
            await interaction.response.send_message(
                f"<:checkmark:1504023759101886607> **Канал убран.**\nАктивные каналы: {', '.join(mentions)}",
                ephemeral=True
            )
        return

    if len(ch_list) >= 3:
        mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
        await interaction.response.send_message(
            f"<:cross:1504024178494410865> **Достигнут лимит (3 канала).**\n"
            f"Текущие: {', '.join(mentions)}\n"
            f"Введи `/setchannel` в одном из них чтобы убрать.",
            ephemeral=True
        )
        return

    ch_list = allowed_channels[guild_id]

    if channel_id in ch_list:
        ch_list.remove(channel_id)
        if not ch_list:
            del allowed_channels[guild_id]
            await interaction.response.send_message(
                "<:checkmark:1504023759101886607> **Канал убран. Теперь** `/miko` **работает везде.**",
                ephemeral=True
            )
        else:
            mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
            await interaction.response.send_message(
                f"<:checkmark:1504023759101886607> **Канал убран.**\nАктивные каналы: {', '.join(mentions)}",
                ephemeral=True
            )
        return

    if len(ch_list) >= 3:
        mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
        await interaction.response.send_message(
            f"<:cross:1504024178494410865> **Достигнут лимит (3 канала).**\n"
            f"Текущие: {', '.join(mentions)}\n"
            f"Введи `/setchannel` в одном из них чтобы убрать.",
            ephemeral=True
        )
        return

    ch_list.append(channel_id)
    mentions = [interaction.guild.get_channel(c).mention for c in ch_list if interaction.guild.get_channel(c)]
    await interaction.response.send_message(
        f"<:checkmark:1504023759101886607> **Канал добавлен!**\nАктивные каналы: {', '.join(mentions)}",
        ephemeral=True
    )


@client.event
async def on_message(message: discord.Message):
    if message.author.bot:
        return
    uid = message.author.id
    channel = message.channel
    thread_id = user_thread.get(uid)
    if not thread_id or message.channel.id != thread_id:
        if (
            isinstance(channel, discord.Thread) and
            channel.name == f"miko · {message.author.display_name}"
        ):
            user_thread[uid] = channel.id
            if uid not in histories:
                histories[uid] = []
        else:
            return
    display_name = message.author.display_name
    async with message.channel.typing():
        try:
            reply, image_url, image_bytes = await ai(uid, message.content, username=display_name)
        except Exception as e:
            await send_error_v2(message.channel.id, f"Ошибка: {e}", reply_to=message.id)
            return
        try:
            await send_v2(
                message.channel.id, uid, reply,
                display_name,
                message.author.display_avatar.url,
                reply_to=message.id,
                image_url=image_url,
                image_bytes=image_bytes
            )
        except Exception as e:
            print(f"Ошибка отправки: {e}")


@client.event
async def on_ready():
    await client.change_presence(status=discord.Status.idle)
    for guild in client.guilds:
        await tree.sync(guild=guild)
    await tree.sync()
    print(f"Запущен: {client.user}")


client.run(TOKEN)
