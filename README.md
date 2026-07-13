# ApiComenius

API (.NET 8 / Minimal API) para atendimento via WhatsApp do Depósito Comenius, com geração de orçamentos e suporte a um painel de atendimento.

## Funcionalidades

- Webhook de integração com a API do WhatsApp (Meta), incluindo verificação e recebimento de mensagens.
- Fluxo conversacional guiado (bot) para coleta de produto, quantidade, tipo de entrega e dados do cliente, gerando um orçamento ao final.
- Simulador de mensagens (`/simulator/messages`) para testar o fluxo sem depender do WhatsApp real.
- Download/armazenamento de áudios recebidos via WhatsApp.
- Endpoints para o painel de atendimento: listagem de conversas e orçamentos, histórico de mensagens por telefone, envio manual de mensagens pelo atendente e controle de status (orçamento/atendimento).

## Estrutura do projeto

```
Program.cs      Configuração da aplicação e endpoints (Minimal API)
Models/         Records e classes de modelo (ConversationSession, Budget, ChatMessage, requests, etc.)
Enums/          Enums do domínio (ConversationStage)
wwwroot/        Build gerado pelo Vite (frontend)
```

## Principais endpoints

| Método | Rota | Descrição |
|---|---|---|
| GET  | `/whatsapp/webhook` | Verificação do webhook da Meta |
| POST | `/whatsapp/webhook` | Recebimento de mensagens do WhatsApp |
| POST | `/simulator/messages` | Envio de mensagem simulada ao bot |
| POST | `/simulator/reset-all` | Limpa conversas e orçamentos (ambiente de teste) |
| GET  | `/budgets` | Lista os orçamentos |
| PATCH| `/budgets/{id}/status` | Atualiza o status de um orçamento |
| GET  | `/conversations` | Lista as conversas ativas |
| GET  | `/conversations/{phone}/messages` | Histórico de mensagens de uma conversa |
| POST | `/conversations/{phone}/send` | Envia mensagem manual do atendente |
| PATCH| `/conversations/{phone}/attendance` | Atualiza status de atendimento humano |
| GET  | `/audio/{fileName}` | Recupera um áudio recebido |

## Executando localmente

```bash
dotnet restore
dotnet run
```

A API sobe com CORS liberado para `http://localhost:5173` (frontend em desenvolvimento) e serve o build estático em `wwwroot` por padrão.

## Requisitos

- .NET 8 SDK
