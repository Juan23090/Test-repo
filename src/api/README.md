# EMA Backend API

This directory contains the backend HTTP API that powers the Blazor application.

**Stack:** Azure Functions (C#)

## Prerequisites

- .NET 9 SDK/runtime
- Azure Functions Core Tools v4 (`func`)
- `local.settings.json` with:
	- `AzureWebJobsStorage`
	- `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`
	- `SqlConnectionString`

## Run locally

```bash
cd src/api/EMA.Api
func start
```
