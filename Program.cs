using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("poc-front", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();

var app = builder.Build();

app.UseCors("poc-front");
app.UseDefaultFiles();
app.UseStaticFiles();

const string VERIFY_TOKEN = "deposito-poc-123";
const string DEPOSIT_ADDRESS = "Rua Exemplo, 123 - Bairro - São Paulo/SP";
const int MINIMUM_DELIVERY_DAYS = 2;

var conversations = new ConcurrentDictionary<string, ConversationSession>();
var budgets = new ConcurrentDictionary<Guid, Budget>();


// Webhook real da Meta
app.MapGet("/whatsapp/webhook", (HttpRequest request) =>
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
});

app.MapPost("/whatsapp/webhook", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    Console.WriteLine("=== POST RECEBIDO DO WHATSAPP ===");
    Console.WriteLine(body);

    try
    {
        using var json = JsonDocument.Parse(body);

        var value = json.RootElement
            .GetProperty("entry")[0]
            .GetProperty("changes")[0]
            .GetProperty("value");

        // Quando for status de mensagem enviada, não vem "messages". Então ignora.
        if (!value.TryGetProperty("messages", out var messages))
            return Results.Ok();

        var message = messages[0];

        // Por enquanto trata só texto.
        if (!message.TryGetProperty("text", out var textObject))
            return Results.Ok();

        var from = OnlyNumbers(message.GetProperty("from").GetString() ?? "");
        var text = textObject.GetProperty("body").GetString()?.Trim();

        var phoneNumberId = value
            .GetProperty("metadata")
            .GetProperty("phone_number_id")
            .GetString();

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phoneNumberId))
            return Results.Ok();

        Console.WriteLine($"Mensagem recebida de {from}: {text}");
        Console.WriteLine($"Phone Number ID: {phoneNumberId}");

        if (text.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            conversations.TryRemove(from, out _);

            await SendWhatsAppTextAsync(
                httpClientFactory,
                phoneNumberId,
                from,
                "Conversa reiniciada. Envie 'Olá' para começar novamente."
            );

            return Results.Ok();
        }

        var conversation = conversations.GetOrAdd(from, number => new ConversationSession
        {
            Phone = number,
            Stage = ConversationStage.Started,
            UpdatedAt = DateTime.UtcNow
        });

        var reply = ProcessMessage(conversation, text, budgets);

        conversation.UpdatedAt = DateTime.UtcNow;
        conversations[from] = conversation;

        await SendWhatsAppTextAsync(
            httpClientFactory,
            phoneNumberId,
            from,
            reply.Message
        );

        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Erro ao processar webhook do WhatsApp:");
        Console.WriteLine(ex);

        return Results.Ok();
    }
});

static async Task SendWhatsAppTextAsync(
    IHttpClientFactory httpClientFactory,
    string phoneNumberId,
    string to,
    string message)
{
    var token = Environment.GetEnvironmentVariable("WHATSAPP_TOKEN");

    if (string.IsNullOrWhiteSpace(token))
    {
        Console.WriteLine("WHATSAPP_TOKEN não configurado.");
        return;
    }

    var client = httpClientFactory.CreateClient();

    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

    var payload = new
    {
        messaging_product = "whatsapp",
        to,
        type = "text",
        text = new
        {
            body = message
        }
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

// Simulador da conversa
app.MapPost("/simulator/messages", (IncomingMessage request) =>
{
    if (string.IsNullOrWhiteSpace(request.From))
        return Results.BadRequest("O campo 'from' é obrigatório.");

    if (string.IsNullOrWhiteSpace(request.Body))
        return Results.BadRequest("O campo 'body' é obrigatório.");

    var phone = OnlyNumbers(request.From);
    var message = request.Body.Trim();

    if (message.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        conversations.TryRemove(phone, out _);

        return Results.Ok(new BotReply(
            phone,
            "Conversa reiniciada. Envie 'Olá' para começar novamente."
        ));
    }

    var conversation = conversations.GetOrAdd(phone, number => new ConversationSession
    {
        Phone = number,
        Stage = ConversationStage.Started,
        UpdatedAt = DateTime.UtcNow
    });

    var reply = ProcessMessage(conversation, message, budgets);

    conversation.UpdatedAt = DateTime.UtcNow;
    conversations[phone] = conversation;

    return Results.Ok(reply);
});

// Lista orçamentos
app.MapGet("/budgets", () =>
{
    return Results.Ok(
        budgets.Values
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
    );
});

// Lista conversas
app.MapGet("/conversations", () =>
{
    return Results.Ok(
        conversations.Values
            .OrderByDescending(x => x.UpdatedAt)
            .ToList()
    );
});

// Atualiza status do orçamento
app.MapPatch("/budgets/{id:guid}/status", (Guid id, UpdateBudgetStatusRequest request) =>
{
    if (!budgets.TryGetValue(id, out var budget))
        return Results.NotFound("Orçamento não encontrado.");

    if (string.IsNullOrWhiteSpace(request.Status))
        return Results.BadRequest("Status é obrigatório.");

    budget.Status = request.Status.Trim();
    budget.UpdatedAt = DateTime.UtcNow;

    budgets[id] = budget;

    return Results.Ok(budget);
});

// Limpa tudo para testar de novo
app.MapPost("/simulator/reset-all", () =>
{
    conversations.Clear();
    budgets.Clear();

    return Results.Ok("Conversas e orçamentos apagados.");
});
app.MapFallbackToFile("index.html");
app.Run("http://0.0.0.0:5080");
static BotReply ProcessMessage(
    ConversationSession conversation,
    string message,
    ConcurrentDictionary<Guid, Budget> budgets)
{
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
            "3" => SendToHuman(conversation),
            _ => new BotReply(
                conversation.Phone,
                """
                Não entendi sua opção.

                Escolha uma opção:

                1 - Fazer orçamento
                2 - Ver produtos/categorias
                3 - Falar com atendente
                """
            )
        };
    }

    if (conversation.Stage == ConversationStage.WaitingProduct)
    {
        conversation.Product = message;
        conversation.Stage = ConversationStage.WaitingQuantity;

        return new BotReply(
            conversation.Phone,
            "Qual a quantidade aproximada?"
        );
    }

    if (conversation.Stage == ConversationStage.WaitingQuantity)
    {
        conversation.Quantity = message;
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

            return new BotReply(
                conversation.Phone,
                "Informe seu nome completo."
            );
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
        conversation.CustomerName = message;
        conversation.Stage = ConversationStage.AskingContinueBudget;

        return AskContinueBudget(conversation);
    }

    if (conversation.Stage == ConversationStage.WaitingFullName)
    {
        conversation.CustomerName = message;
        conversation.Stage = ConversationStage.WaitingCustomerAddress;

        return new BotReply(
            conversation.Phone,
            "Informe seu endereço completo."
        );
    }

    if (conversation.Stage == ConversationStage.WaitingCustomerAddress)
    {
        conversation.CustomerAddress = message;
        conversation.Stage = ConversationStage.WaitingCustomerPhone;

        return new BotReply(
            conversation.Phone,
            "Informe seu telefone principal."
        );
    }

    if (conversation.Stage == ConversationStage.WaitingCustomerPhone)
    {
        conversation.CustomerPhone = message;
        conversation.Stage = ConversationStage.WaitingSecondPhone;

        return new BotReply(
            conversation.Phone,
            "Informe um segundo telefone para contato."
        );
    }

    if (conversation.Stage == ConversationStage.WaitingSecondPhone)
    {
        conversation.SecondPhone = message;
        conversation.Stage = ConversationStage.AskingContinueBudget;

        return AskContinueBudget(conversation);
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
    Produto: {budget.Product}
    Quantidade: {budget.Quantity}
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

                Caso precise novamente, envie "reset" e comece uma nova simulação.
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

    if (conversation.Stage == ConversationStage.HumanSupport)
    {
        return new BotReply(
            conversation.Phone,
            "Sua conversa já foi marcada para atendimento humano. Um atendente continuará o contato."
        );
    }

    if (conversation.Stage == ConversationStage.Finished)
    {
        return new BotReply(
            conversation.Phone,
            """
            Você já possui um orçamento registrado.

            Para iniciar uma nova simulação, envie "reset".
            """
        );
    }

    if (conversation.Stage == ConversationStage.Canceled)
    {
        return new BotReply(
            conversation.Phone,
            """
            Este orçamento foi cancelado.

            Para iniciar novamente, envie "reset".
            """
        );
    }

    return new BotReply(
        conversation.Phone,
        "Não consegui processar sua mensagem."
    );
}

static BotReply AskProduct(ConversationSession conversation)
{
    conversation.Stage = ConversationStage.WaitingProduct;

    return new BotReply(
        conversation.Phone,
        "Qual produto ou material você precisa?"
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

    var deliveryInfo = conversation.DeliveryType == "Entrega"
        ? $"""
          Tipo de entrega: Entrega
          Prazo mínimo: {conversation.MinimumDeliveryDays} dias úteis
          """
        : $"""
          Tipo de entrega: Retirada
          Retirada no endereço: {DEPOSIT_ADDRESS}
          """;

    var customerInfo = conversation.HasRegistration == true
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
        Produto: {conversation.Product}
        Quantidade: {conversation.Quantity}
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
        Quantity = conversation.Quantity ?? "Não informado",
        DeliveryType = conversation.DeliveryType ?? "Não informado",
        DeliveryAddress = conversation.HasRegistration == true
    ? null
    : conversation.CustomerAddress,
        DepositAddress = conversation.DeliveryType == "Retirada" ? DEPOSIT_ADDRESS : null,
        HasRegistration = conversation.HasRegistration ?? false,
        CustomerAddress = conversation.CustomerAddress,
        CustomerPhone = conversation.CustomerPhone,
        SecondPhone = conversation.SecondPhone,
        MinimumDeliveryDays = conversation.MinimumDeliveryDays,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

static string OnlyNumbers(string value)
{
    return new string(value.Where(char.IsDigit).ToArray());
}

public record IncomingMessage(string From, string Body);

public record BotReply(string To, string Message);

public record UpdateBudgetStatusRequest(string Status);

public class ConversationSession
{
    public string Phone { get; set; } = string.Empty;
    public ConversationStage Stage { get; set; }

    public string? Product { get; set; }
    public string? Quantity { get; set; }

    public string? DeliveryType { get; set; }
    public string? DeliveryAddress { get; set; }
    public int? MinimumDeliveryDays { get; set; }

    public bool? HasRegistration { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerPhone { get; set; }
    public string? SecondPhone { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public class Budget
{
    public Guid Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public string Product { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;

    public string DeliveryType { get; set; } = string.Empty;
    public string? DeliveryAddress { get; set; }
    public string? DepositAddress { get; set; }
    public int? MinimumDeliveryDays { get; set; }

    public bool HasRegistration { get; set; }

    public string? CustomerAddress { get; set; }
    public string? CustomerPhone { get; set; }
    public string? SecondPhone { get; set; }

    public string Status { get; set; } = "Pendente";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum ConversationStage
{
    Started = 0,
    MenuSent = 1,

    WaitingProduct = 2,
    WaitingQuantity = 3,
    WaitingDeliveryType = 4,
    WaitingDeliveryAddress = 5,

    AskingHasRegistration = 6,

    WaitingRegisteredCustomerName = 7,

    WaitingFullName = 8,
    WaitingCustomerAddress = 9,
    WaitingCustomerPhone = 10,
    WaitingSecondPhone = 11,

    AskingContinueBudget = 12,

    HumanSupport = 13,
    Finished = 14,
    Canceled = 15
}