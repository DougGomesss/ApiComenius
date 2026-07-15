using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Deposito.Orcamentos.Poc.Api.Enums;
using Deposito.Orcamentos.Poc.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "poc-front",
        policy =>
        {
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    );
});

builder.Services.AddHttpClient();

var app = builder.Build();

app.UseCors("poc-front");
app.UseDefaultFiles();
app.UseStaticFiles();

const string VERIFY_TOKEN = "deposito-poc-123";
const string DEPOSIT_ADDRESS = "R. João Amós Comenius, 181 - Jardim São Bernardo, São Paulo - SP, 04844-420";
const int MINIMUM_DELIVERY_DAYS = 2;
const string DEV_PHONE_BLOCK = "5511944433936";

const string DEV_PROMO_MESSAGE = """
🤖 Gostou do atendimento automático?

Sua loja também pode ter um assistente como este, atendendo seus clientes 24h por dia!

📲 Fale comigo: wa.me/5511980491930
Douglas — Desenvolvedor de Software e Automações
""";

var conversations = new ConcurrentDictionary<string, ConversationSession>();
var chatHistory = new ConcurrentDictionary<string, List<ChatMessage>>();
var budgets = new ConcurrentDictionary<Guid, Budget>();

// Webhook real da Meta
app.MapGet(
    "/whatsapp/webhook",
    (HttpRequest request) =>
    {
        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();

        Console.WriteLine("=== GET VERIFICAÇÃO WEBHOOK ===");
        Console.WriteLine($"hub.mode: {mode}");
        Console.WriteLine($"hub.verify_token: {token}");
        Console.WriteLine($"hub.challenge: {challenge}");

        if (mode == "subscribe" && token == VERIFY_TOKEN)
            return Results.Text(challenge, "text/plain");

        return Results.Unauthorized();
    }
);

app.MapPost(
    "/whatsapp/webhook",
    async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();

        Console.WriteLine("=== POST RECEBIDO DO WHATSAPP ===");
        Console.WriteLine(body);

        try
        {
            using var json = JsonDocument.Parse(body);

            var value = json
                .RootElement.GetProperty("entry")[0]
                .GetProperty("changes")[0]
                .GetProperty("value");

            if (!value.TryGetProperty("messages", out var messages))
                return Results.Ok();

            var message = messages[0];

            var messageType = message.GetProperty("type").GetString();

            if (messageType == "audio")
            {
                var fromAudio = OnlyNumbers(message.GetProperty("from").GetString() ?? "");
                
                var existingSession = conversations.GetOrAdd(
                    fromAudio,
                    number => new ConversationSession { Phone = number, Stage = ConversationStage.Started, UpdatedAt = DateTime.UtcNow }
                );

                var isPostFlow =
                    existingSession.Stage == ConversationStage.HumanSupport ||
                    existingSession.Stage == ConversationStage.Finished;

                if (isPostFlow)
                {
                    // Baixar e salvar o áudio para exibir no chat
                    var mediaId = message.GetProperty("audio").GetProperty("id").GetString();
                    var savedPath = await DownloadWhatsAppMediaAsync(httpClientFactory, mediaId!);

                    if (savedPath != null)
                        AddChatMessage(chatHistory, fromAudio, "audio", savedPath);

                    return Results.Ok();
                }

                var phoneNumberIdAudio = value
                    .GetProperty("metadata")
                    .GetProperty("phone_number_id")
                    .GetString();

                var replyText = """
                Não conseguimos processar mensagens de áudio durante o orçamento.

                O que prefere fazer?

                1 - Falar diretamente com um atendente
                2 - Digitar sua resposta
                """;

                await SendWhatsAppTextAsync(
                    httpClientFactory,
                    phoneNumberIdAudio!,
                    fromAudio,
                    replyText
                );

                var session = conversations.GetOrAdd(
                    fromAudio,
                    number => new ConversationSession
                    {
                        Phone = number,
                        Stage = ConversationStage.Started,
                        UpdatedAt = DateTime.UtcNow,
                    }
                );

                session.PendingAudioChoice = true;
                conversations[fromAudio] = session;

                return Results.Ok();
            }

            if (!message.TryGetProperty("text", out var textObject))
                return Results.Ok();

            var from = OnlyNumbers(message.GetProperty("from").GetString() ?? "");
            var text = textObject.GetProperty("body").GetString()?.Trim();

            var phoneNumberId = value
                .GetProperty("metadata")
                .GetProperty("phone_number_id")
                .GetString();

            if (
                string.IsNullOrWhiteSpace(from)
                || string.IsNullOrWhiteSpace(text)
                || string.IsNullOrWhiteSpace(phoneNumberId)
            )
                return Results.Ok();

            Console.WriteLine($"Mensagem recebida de {from}: {text}");
            Console.WriteLine($"Phone Number ID: {phoneNumberId}");

            AddChatMessage(chatHistory, from, "client", text);

            var conversation = conversations.GetOrAdd(
                from,
                number => new ConversationSession
                {
                    Phone = number,
                    Stage = ConversationStage.Started,
                    UpdatedAt = DateTime.UtcNow,
                }
            );

            if (
                conversation.Stage == ConversationStage.Finished
                || conversation.Stage == ConversationStage.Canceled
                || conversation.Stage == ConversationStage.Attended
            )
            {
                conversations.TryRemove(from, out _);
                conversation = conversations.GetOrAdd(
                    from,
                    number => new ConversationSession
                    {
                        Phone = number,
                        Stage = ConversationStage.Started,
                        UpdatedAt = DateTime.UtcNow,
                    }
                );
            }

            conversation.PhoneNumberId = phoneNumberId;

            var reply = ProcessMessage(conversation, text, budgets);

            conversation.UpdatedAt = DateTime.UtcNow;
            conversations[from] = conversation;

            if (!string.IsNullOrEmpty(reply.Message))
            {
                AddChatMessage(chatHistory, from, "bot", reply.Message);
                await SendWhatsAppTextAsync(httpClientFactory, phoneNumberId, from, reply.Message);
            }

            // Divulgação: só ao finalizar orçamento ou ao encaminhar para atendente
            if (conversation.Stage == ConversationStage.Finished
                || conversation.Stage == ConversationStage.HumanSupport)
            {
                await SendDevPromoAsync(httpClientFactory, phoneNumberId, from);
            }

            return Results.Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao processar webhook do WhatsApp:");
            Console.WriteLine(ex);

            return Results.Ok();
        }
    }
);

static async Task<string?> DownloadWhatsAppMediaAsync(
    IHttpClientFactory httpClientFactory,
    string mediaId
)
{
    var token = Environment.GetEnvironmentVariable("WHATSAPP_TOKEN");
    if (string.IsNullOrWhiteSpace(token)) return null;

    var client = httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var metaResponse = await client.GetAsync(
        $"https://graph.facebook.com/v21.0/{mediaId}"
    );

    if (!metaResponse.IsSuccessStatusCode) return null;

    var metaJson = await metaResponse.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(metaJson);
    var mediaUrl = doc.RootElement.GetProperty("url").GetString();
    if (string.IsNullOrWhiteSpace(mediaUrl)) return null;

    var audioResponse = await client.GetAsync(mediaUrl);
    if (!audioResponse.IsSuccessStatusCode) return null;

    var audioDir = Path.Combine(AppContext.BaseDirectory, "audio");
    Directory.CreateDirectory(audioDir);

    var fileName = $"{mediaId}.ogg";
    var filePath = Path.Combine(audioDir, fileName);

    await using var fs = File.Create(filePath);
    await audioResponse.Content.CopyToAsync(fs);

    return fileName;
}

static async Task SendWhatsAppTextAsync(
    IHttpClientFactory httpClientFactory,
    string phoneNumberId,
    string to,
    string message
)
{
    var token = Environment.GetEnvironmentVariable("WHATSAPP_TOKEN");

    Console.WriteLine("=== DEBUG TOKEN ===");
    Console.WriteLine(Environment.GetEnvironmentVariable("WHATSAPP_TOKEN"));

    if (string.IsNullOrWhiteSpace(token))
    {
        Console.WriteLine("WHATSAPP_TOKEN não configurado.");
        return;
    }

    var client = httpClientFactory.CreateClient();

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var payload = new
    {
        messaging_product = "whatsapp",
        to,
        type = "text",
        text = new { body = message },
    };

    var json = JsonSerializer.Serialize(payload);

    var response = await client.PostAsync(
        $"https://graph.facebook.com/v21.0/{phoneNumberId}/messages",
        new StringContent(json, Encoding.UTF8, "application/json")
    );

    var responseBody = await response.Content.ReadAsStringAsync();

    Console.WriteLine("=== RESPOSTA ENVIO WHATSAPP ===");
    Console.WriteLine(response.StatusCode);
    Console.WriteLine(responseBody);
}

static async Task SendDevPromoAsync(
    IHttpClientFactory httpClientFactory,
    string phoneNumberId,
    string to
)
{
    if (to == DEV_PHONE_BLOCK)
        return;

    await SendWhatsAppTextAsync(httpClientFactory, phoneNumberId, to, DEV_PROMO_MESSAGE);
}

app.MapPost(
    "/simulator/messages",
    (IncomingMessage request) =>
    {
        if (string.IsNullOrWhiteSpace(request.From))
            return Results.BadRequest("O campo 'from' é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest("O campo 'body' é obrigatório.");

        var phone = OnlyNumbers(request.From);
        var message = request.Body.Trim();

        AddChatMessage(chatHistory, phone, "client", message);

        if (request.Type == "audio")
        {
            var session = conversations.GetOrAdd(
                phone,
                number => new ConversationSession
                {
                    Phone = number,
                    Stage = ConversationStage.Started,
                    UpdatedAt = DateTime.UtcNow,
                }
            );

            session.PendingAudioChoice = true;
            conversations[phone] = session;

            var replyText = """
            Não conseguimos processar mensagens de áudio durante o orçamento.

            O que prefere fazer?

            1 - Falar diretamente com um atendente
            2 - Digitar sua resposta
            """;

            AddChatMessage(chatHistory, phone, "bot", replyText);
            return Results.Ok(new BotReply(phone, replyText));
        }

        var conversation = conversations.GetOrAdd(
            phone,
            number => new ConversationSession
            {
                Phone = number,
                Stage = ConversationStage.Started,
                UpdatedAt = DateTime.UtcNow,
            }
        );

        if (
            conversation.Stage == ConversationStage.Finished
            || conversation.Stage == ConversationStage.Canceled
            || conversation.Stage == ConversationStage.Attended
        )
        {
            conversations.TryRemove(phone, out _);
            conversation = conversations.GetOrAdd(
                phone,
                number => new ConversationSession
                {
                    Phone = number,
                    Stage = ConversationStage.Started,
                    UpdatedAt = DateTime.UtcNow,
                }
            );
        }

        var reply = ProcessMessage(conversation, message, budgets);

        conversation.UpdatedAt = DateTime.UtcNow;
        conversations[phone] = conversation;

        if (!string.IsNullOrEmpty(reply.Message))
        {
            AddChatMessage(chatHistory, phone, "bot", reply.Message);
            return Results.Ok(reply);
        }

        return Results.Ok();
    }
);

app.MapGet(
    "/budgets",
    () =>
    {
        return Results.Ok(budgets.Values.OrderByDescending(x => x.CreatedAt).ToList());
    }
);

app.MapGet(
    "/conversations",
    () =>
    {
        return Results.Ok(conversations.Values.OrderByDescending(x => x.UpdatedAt).ToList());
    }
);

app.MapGet(
    "/conversations/{phone}/messages",
    (string phone) =>
    {
        var normalized = OnlyNumbers(phone);
        if (chatHistory.TryGetValue(normalized, out var messages))
            return Results.Ok(messages.OrderBy(m => m.SentAt).ToList());

        return Results.Ok(new List<ChatMessage>());
    }
);

app.MapPost(
    "/conversations/{phone}/send",
    async (string phone, SendChatMessageRequest request, IHttpClientFactory httpClientFactory) =>
    {
        var normalized = OnlyNumbers(phone);

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest("A mensagem não pode estar vazia.");

        AddChatMessage(chatHistory, normalized, "attendant", request.Message.Trim());

        if (!conversations.TryGetValue(normalized, out var conv) || string.IsNullOrEmpty(conv.PhoneNumberId))
            return Results.BadRequest("Conversa não encontrada ou phoneNumberId ausente.");

        await SendWhatsAppTextAsync(httpClientFactory, conv.PhoneNumberId, normalized, request.Message.Trim());

        return Results.Ok();
    }
);

app.MapPatch(
    "/conversations/{phone}/attendance",
    (string phone, UpdateAttendanceRequest request) =>
    {
        var normalized = OnlyNumbers(phone);

        if (!conversations.TryGetValue(normalized, out var conversation))
            return Results.NotFound("Conversa não encontrada.");

        conversation.Stage = request.Attended
            ? ConversationStage.Attended
            : ConversationStage.HumanSupport;
        conversation.UpdatedAt = DateTime.UtcNow;

        conversations[normalized] = conversation;

        return Results.Ok(conversation);
    }
);

app.MapPatch(
    "/budgets/{id:guid}/status",
    (Guid id, UpdateBudgetStatusRequest request) =>
    {
        if (!budgets.TryGetValue(id, out var budget))
            return Results.NotFound("Orçamento não encontrado.");

        if (string.IsNullOrWhiteSpace(request.Status))
            return Results.BadRequest("Status é obrigatório.");

        budget.Status = request.Status.Trim();
        budget.UpdatedAt = DateTime.UtcNow;

        budgets[id] = budget;

        return Results.Ok(budget);
    }
);

app.MapPost(
    "/simulator/reset-all",
    () =>
    {
        conversations.Clear();
        budgets.Clear();

        return Results.Ok("Conversas e orçamentos apagados.");
    }
);

app.MapGet("/audio/{fileName}", (string fileName) =>
{
    var audioDir = Path.Combine(AppContext.BaseDirectory, "audio");
    var filePath = Path.Combine(audioDir, fileName);

    if (!File.Exists(filePath))
        return Results.NotFound();

    return Results.File(filePath, "audio/ogg");
});

app.MapFallbackToFile("index.html");
app.Run("http://0.0.0.0:5080");

static void AddChatMessage(
    ConcurrentDictionary<string, List<ChatMessage>> history,
    string phone,
    string from,
    string text
)
{
    var list = history.GetOrAdd(phone, _ => new List<ChatMessage>());
    lock (list)
    {
        list.Add(new ChatMessage
        {
            From = from,
            Text = text,
            SentAt = DateTime.UtcNow
        });
    }
}

static BotReply ProcessMessage(
    ConversationSession conversation,
    string message,
    ConcurrentDictionary<Guid, Budget> budgets
)
{
    if (conversation.Stage == ConversationStage.HumanSupport)
        return new BotReply(conversation.Phone, string.Empty);

    var saoPauloZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    var nowSaoPaulo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, saoPauloZone);

    if (nowSaoPaulo.DayOfWeek == DayOfWeek.Sunday)
        return new BotReply(
            conversation.Phone,
            "Não fazemos orçamentos no dia de domingo. Favor aguardar atendimento na segunda-feira."
        );

    if (conversation.PendingAudioChoice)
    {
        if (message == "1")
        {
            conversation.PendingAudioChoice = false;
            return SendToHuman(conversation);
        }

        if (message == "2")
        {
            conversation.PendingAudioChoice = false;
            return AskCurrentStage(conversation);
        }

        return new BotReply(
            conversation.Phone,
            """
            Opção inválida.

            O que prefere fazer?

            1 - Falar diretamente com um atendente
            2 - Digitar sua resposta
            """
        );
    }

    if (conversation.Stage == ConversationStage.Started)
    {
        conversation.Stage = ConversationStage.MenuSent;

        return new BotReply(
            conversation.Phone,
            """
            Olá! Sou o assistente do depósito.

            Escolha uma opção:

            1 - Fazer orçamento
            2 - Ver produtos/categorias
            3 - Falar com atendente
            """
        );
    }

    if (conversation.Stage == ConversationStage.MenuSent)
    {
        return message switch
        {
            "1" => AskProduct(conversation),
            "2" => ShowCategories(conversation),
            "3" => AskHumanName(conversation),
            _ => new BotReply(
                conversation.Phone,
                """
                Não entendi sua opção.

                Escolha uma opção:

                1 - Fazer orçamento
                2 - Ver produtos/categorias
                3 - Falar com atendente
                """
            ),
        };
    }

    if (conversation.Stage == ConversationStage.WaitingProduct)
    {
        conversation.PendingProduct = message;
        conversation.Stage = ConversationStage.ConfirmingProduct;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingProduct)
    {
        if (message == "1")
        {
            conversation.Product = conversation.PendingProduct;
            conversation.PendingProduct = null;
            conversation.Stage = ConversationStage.WaitingDeliveryType;

            return new BotReply(
                conversation.Phone,
                """
                Você prefere:

                1 - Retirada no depósito
                2 - Entrega
                """
            );
        }

        if (message == "2")
        {
            conversation.PendingProduct = null;
            conversation.Stage = ConversationStage.WaitingProduct;

            return new BotReply(
                conversation.Phone,
                "Tudo bem. Qual produto ou material você precisa?"
            );
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingDeliveryType)
    {
        if (message == "1")
        {
            conversation.DeliveryType = "Retirada";
            conversation.MinimumDeliveryDays = null;
            conversation.Stage = ConversationStage.AskingHasRegistration;

            return new BotReply(
                conversation.Phone,
                $"""
                Retire no seguinte endereço:

                {DEPOSIT_ADDRESS}

                Você já tem cadastro?

                1 - Sim
                2 - Não
                """
            );
        }

        if (message == "2")
        {
            conversation.DeliveryType = "Entrega";
            conversation.MinimumDeliveryDays = MINIMUM_DELIVERY_DAYS;
            conversation.Stage = ConversationStage.AskingHasRegistration;

            return new BotReply(
                conversation.Phone,
                $"""
                Entrega selecionada.

                Prazo mínimo para entrega: {MINIMUM_DELIVERY_DAYS} dias úteis.

                Você já tem cadastro?

                1 - Sim
                2 - Não
                """
            );
        }

        return new BotReply(
            conversation.Phone,
            """
            Opção inválida.

            Escolha:

            1 - Retirada no depósito
            2 - Entrega
            """
        );
    }

    if (conversation.Stage == ConversationStage.AskingHasRegistration)
    {
        if (message == "1")
        {
            conversation.HasRegistration = true;
            conversation.CustomerPhone = conversation.Phone;
            conversation.Stage = ConversationStage.WaitingRegisteredCustomerName;

            return new BotReply(
                conversation.Phone,
                "Perfeito. Informe o nome completo do cadastro."
            );
        }

        if (message == "2")
        {
            conversation.HasRegistration = false;
            conversation.Stage = ConversationStage.WaitingFullName;

            return new BotReply(conversation.Phone, "Informe seu nome completo.");
        }

        return new BotReply(
            conversation.Phone,
            """
            Opção inválida.

            Você já tem cadastro?

            1 - Sim
            2 - Não
            """
        );
    }

    if (conversation.Stage == ConversationStage.WaitingRegisteredCustomerName)
    {
        conversation.PendingCustomerName = message;
        conversation.Stage = ConversationStage.ConfirmingCustomerName;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingCustomerName)
    {
        if (message == "1")
        {
            conversation.CustomerName = conversation.PendingCustomerName;
            conversation.PendingCustomerName = null;
            conversation.Stage = ConversationStage.WaitingCustomerAddress;

            return new BotReply(conversation.Phone, "Informe seu endereço registrado.");
        }

        if (message == "2")
        {
            conversation.PendingCustomerName = null;
            conversation.Stage = ConversationStage.WaitingRegisteredCustomerName;

            return new BotReply(
                conversation.Phone,
                "Tudo bem. Informe o nome completo do cadastro."
            );
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingHumanName)
    {
        conversation.CustomerName = message.Trim();
        return SendToHuman(conversation);
    }

    if (conversation.Stage == ConversationStage.WaitingFullName)
    {
        conversation.PendingCustomerName = message;
        conversation.Stage = ConversationStage.ConfirmingFullName;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingFullName)
    {
        if (message == "1")
        {
            conversation.CustomerName = conversation.PendingCustomerName;
            conversation.PendingCustomerName = null;
            conversation.Stage = ConversationStage.WaitingCustomerAddress;

            return new BotReply(conversation.Phone, "Informe seu endereço completo.");
        }

        if (message == "2")
        {
            conversation.PendingCustomerName = null;
            conversation.Stage = ConversationStage.WaitingFullName;

            return new BotReply(conversation.Phone, "Tudo bem. Informe seu nome completo.");
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingCustomerAddress)
    {
        conversation.PendingCustomerAddress = message;
        conversation.Stage = ConversationStage.ConfirmingCustomerAddress;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingCustomerAddress)
    {
        if (message == "1")
        {
            conversation.CustomerAddress = conversation.PendingCustomerAddress;
            conversation.PendingCustomerAddress = null;

            if (conversation.HasRegistration == true)
            {
                conversation.Stage = ConversationStage.AskingContinueBudget;
                return AskContinueBudget(conversation);
            }

            conversation.Stage = ConversationStage.WaitingCustomerPhone;
            return new BotReply(conversation.Phone, "Informe seu telefone principal.");
        }

        if (message == "2")
        {
            conversation.PendingCustomerAddress = null;
            conversation.Stage = ConversationStage.WaitingCustomerAddress;

            var addressPrompt = conversation.HasRegistration == true
                ? "Tudo bem. Informe seu endereço registrado."
                : "Tudo bem. Informe seu endereço completo.";

            return new BotReply(conversation.Phone, addressPrompt);
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingCustomerPhone)
    {
        conversation.PendingCustomerPhone = message;
        conversation.Stage = ConversationStage.ConfirmingCustomerPhone;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingCustomerPhone)
    {
        if (message == "1")
        {
            conversation.CustomerPhone = conversation.PendingCustomerPhone;
            conversation.PendingCustomerPhone = null;
            conversation.Stage = ConversationStage.WaitingSecondPhone;

            return new BotReply(conversation.Phone, "Informe um segundo telefone para contato.");
        }

        if (message == "2")
        {
            conversation.PendingCustomerPhone = null;
            conversation.Stage = ConversationStage.WaitingCustomerPhone;

            return new BotReply(conversation.Phone, "Tudo bem. Informe seu telefone principal.");
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingSecondPhone)
    {
        conversation.PendingSecondPhone = message;
        conversation.Stage = ConversationStage.ConfirmingSecondPhone;

        return new BotReply(
            conversation.Phone,
            $"Você digitou: *{message}*\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.ConfirmingSecondPhone)
    {
        if (message == "1")
        {
            conversation.SecondPhone = conversation.PendingSecondPhone;
            conversation.PendingSecondPhone = null;
            conversation.Stage = ConversationStage.AskingContinueBudget;

            return AskContinueBudget(conversation);
        }

        if (message == "2")
        {
            conversation.PendingSecondPhone = null;
            conversation.Stage = ConversationStage.WaitingSecondPhone;

            return new BotReply(
                conversation.Phone,
                "Tudo bem. Informe um segundo telefone para contato."
            );
        }

        return new BotReply(
            conversation.Phone,
            "Opção inválida.\n\nEssa informação está correta?\n\n1 - Sim\n2 - Não"
        );
    }

    if (conversation.Stage == ConversationStage.AskingContinueBudget)
    {
        if (message == "1")
        {
            var budget = CreateBudget(conversation, "Pendente");
            budgets[budget.Id] = budget;

            conversation.Stage = ConversationStage.Finished;

            var hasRegistrationText = budget.HasRegistration ? "Sim" : "Não";

            return new BotReply(
                conversation.Phone,
                $"""
                Orçamento registrado com sucesso!

                Cliente: {budget.CustomerName}
                Possui cadastro: {hasRegistrationText}
                Lista de materiais: {budget.Product}
                Tipo: {budget.DeliveryType}
                Status: {budget.Status}

                Um atendente vai analisar e retornar em breve.
                """
            );
        }

        if (message == "2")
        {
            var budget = CreateBudget(conversation, "Cancelado pelo cliente");
            budgets[budget.Id] = budget;

            conversation.Stage = ConversationStage.Canceled;

            return new BotReply(
                conversation.Phone,
                """
                Tudo bem. O orçamento foi cancelado.

                Para realizar um novo orçamento, envie uma mensagem.
                """
            );
        }

        return new BotReply(
            conversation.Phone,
            """
            Opção inválida.

            Deseja continuar com o orçamento?

            1 - Sim
            2 - Não
            """
        );
    }

    return new BotReply(conversation.Phone, "Não consegui processar sua mensagem.");
}

static BotReply AskCurrentStage(ConversationSession conversation)
{
    return conversation.Stage switch
    {
        ConversationStage.Started or ConversationStage.MenuSent => new BotReply(
            conversation.Phone,
            """
            Olá! Sou o assistente do depósito.

            Escolha uma opção:

            1 - Fazer orçamento
            2 - Ver produtos/categorias
            3 - Falar com atendente
            """
        ),
        ConversationStage.WaitingProduct => new BotReply(
            conversation.Phone,
            "Qual produto ou material você precisa?"
        ),
        ConversationStage.WaitingFullName => new BotReply(
            conversation.Phone,
            "Informe seu nome completo."
        ),
        ConversationStage.WaitingRegisteredCustomerName => new BotReply(
            conversation.Phone,
            "Informe o nome completo do cadastro."
        ),
        ConversationStage.WaitingCustomerAddress => new BotReply(
            conversation.Phone,
            "Informe seu endereço completo."
        ),
        ConversationStage.WaitingCustomerPhone => new BotReply(
            conversation.Phone,
            "Informe seu telefone principal."
        ),
        ConversationStage.WaitingSecondPhone => new BotReply(
            conversation.Phone,
            "Informe um segundo telefone para contato."
        ),
        ConversationStage.WaitingHumanName => new BotReply(
            conversation.Phone,
            "Para encaminhar seu atendimento, informe seu nome completo."
        ),
        _ => new BotReply(conversation.Phone, "Por favor, envie sua resposta."),
    };
}

static BotReply AskHumanName(ConversationSession conversation)
{
    conversation.Stage = ConversationStage.WaitingHumanName;

    return new BotReply(
        conversation.Phone,
        "Para encaminhar seu atendimento, informe seu nome completo."
    );
}

static BotReply AskProduct(ConversationSession conversation)
{
    conversation.Stage = ConversationStage.WaitingProduct;

    return new BotReply(
        conversation.Phone,
        """
        Informe a lista de materiais com as quantidades.

        Exemplo:
        - 5 sacos de cimento
        - 2m³ de areia
        - 10 blocos

        Envie tudo em uma única mensagem.
        """
    );
}

static BotReply ShowCategories(ConversationSession conversation)
{
    conversation.Stage = ConversationStage.MenuSent;

    return new BotReply(
        conversation.Phone,
        """
        Trabalhamos com categorias como:

        - Cimento
        - Areia
        - Pedra
        - Blocos
        - Ferragens
        - Materiais hidráulicos
        - Materiais elétricos

        Para fazer um orçamento, digite 1.
        """
    );
}

static BotReply SendToHuman(ConversationSession conversation)
{
    conversation.Stage = ConversationStage.HumanSupport;

    return new BotReply(
        conversation.Phone,
        "Certo. Vou encaminhar sua conversa para um atendente."
    );
}

static BotReply AskContinueBudget(ConversationSession conversation)
{
    var hasRegistrationText = conversation.HasRegistration == true ? "Sim" : "Não";

    var deliveryInfo =
        conversation.DeliveryType == "Entrega"
            ? $"""
                Tipo de entrega: Entrega
                Prazo mínimo: {conversation.MinimumDeliveryDays} dias úteis
                """
            : $"""
                Tipo de entrega: Retirada
                Retirada no endereço: {DEPOSIT_ADDRESS}
                """;

    var customerInfo =
        conversation.HasRegistration == true
            ? $"""
                Cliente: {conversation.CustomerName}
                Possui cadastro: {hasRegistrationText}
                """
            : $"""
                Cliente: {conversation.CustomerName}
                Possui cadastro: {hasRegistrationText}
                Endereço informado: {conversation.CustomerAddress}
                Telefone principal: {conversation.CustomerPhone}
                Segundo telefone: {conversation.SecondPhone}
                """;

    return new BotReply(
        conversation.Phone,
        $"""
        Resumo do orçamento:

        {customerInfo}
        Lista de materiais:
        {conversation.Product}
        {deliveryInfo}

        Deseja continuar com o orçamento?

        1 - Sim
        2 - Não
        """
    );
}

static Budget CreateBudget(ConversationSession conversation, string status)
{
    return new Budget
    {
        Id = Guid.NewGuid(),
        CustomerName = conversation.CustomerName ?? "Cliente não informado",
        Phone = conversation.Phone,
        Product = conversation.Product ?? "Não informado",
        Quantity = string.Empty,
        DeliveryType = conversation.DeliveryType ?? "Não informado",
        DeliveryAddress =
            conversation.HasRegistration == true ? null : conversation.CustomerAddress,
        DepositAddress = conversation.DeliveryType == "Retirada" ? DEPOSIT_ADDRESS : null,
        HasRegistration = conversation.HasRegistration ?? false,
        CustomerAddress = conversation.CustomerAddress,
        CustomerPhone = conversation.CustomerPhone,
        SecondPhone = conversation.SecondPhone,
        MinimumDeliveryDays = conversation.MinimumDeliveryDays,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}

static string OnlyNumbers(string value)
{
    return new string(value.Where(char.IsDigit).ToArray());
}
