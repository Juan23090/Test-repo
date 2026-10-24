# EMA Connector Builder

Console app built from the "Create a connector" flowchart. GPT 5.6 Luna and Claude Sonnet 5.5 each write a
connector plus unit tests; Opus 5.5 (blind judge) approves one or writes its own; the result is run in a
temporary project and the output is shown.

Flow 0 tests the ChMS credentials/URL. Flow 1 (after answering `y`) creates a connector that follows
`schemas/AgnosticSchema.json` using `schemas/RockSchema.json`. Execution.Order 1 = unit tests, 2 = the connector.

## Secrets (JSON user secrets, never in the repo)

```
dotnet user-secrets set "ChMS:Url" "https://your-rock-sandbox" --project src/ConnectorBuilder
dotnet user-secrets set "ChMS:Username" "..."        # or ChMS:ApiKey
dotnet user-secrets set "ChMS:Password" "..."
dotnet user-secrets set "Models:OpenAiKey" "..."
dotnet user-secrets set "Models:AnthropicKey" "..."
```
Missing values are asked on screen. The ChMS password/API key is never sent to a model.

## Run

`dotnet run --project src/ConnectorBuilder` (needs the .NET 8 SDK; it also runs the generated code with `dotnet run`).

Demo stand-ins: "Notify EMA team" = red console message + `output/ema_notifications.log`;
"store in DB" = `output/connector_results.json`.
