using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// =====================================================================
//  HOW TO READ THE COMMENTS IN THIS FILE
//    IMPORTANT:    a key idea of the program (worth mentioning to a client)
//    HOW IT WORKS: explains a piece of C# syntax that looks difficult
// =====================================================================

class Program
{
    // ---------- SETTINGS: everything you may want to change ----------

    const string GptName = "GPT 5.6 Luna";
    const string GptModel = "gpt-5.6-luna";

    const string SonnetName = "Claude Sonnet 5.5";
    const string SonnetModel = "claude-sonnet-5-5";

    const string OpusName = "Opus 5.5 (evaluator)";
    const string OpusModel = "claude-opus-5-5";

    const int MaxAnswerTokens = 8000;   // limit for each of the two connector answers
    const int MaxJudgeTokens = 16000;   // limit for the evaluator's reply
    const int RunTimeoutSeconds = 180;  // how long the generated code may run (build included)
    const string OutputFolder = "output";

    // Files the program writes into OutputFolder.
    const string NotifyLog = "ema_notifications.log";      // stand-in for "Notify EMA team"
    const string ResultStore = "connector_results.json";    // stand-in for "store the result in the DB"

    // Instructions for the two subordinate models.
    const string SystemPrompt =
        "You are a senior C# developer. Reply with ONE JSON object and nothing else " +
        "(no markdown fences, no extra text).";

    // IMPORTANT: this is the "socket" from the diagram. The program fixes the shape of the code
    // first, so whatever the AI writes (the plug) always fits and can be run the same way.
    // The same text is written to Contract.cs in the temporary project that runs the code.
    const string ContractSource = """
        public record ConnectorSettings(string Url, string Username, string Password, string ApiKey);
        public record ConnectorResult(bool Success, int StatusCode, string Message, string Data);
        public record TestOutcome(string Name, bool Passed, string Detail);

        public interface IConnector
        {
            Task<ConnectorResult> FetchAsync(ConnectorSettings settings);
        }

        public interface IConnectorTests
        {
            Task<List<TestOutcome>> RunAsync(IConnector connector, ConnectorSettings settings);
        }
        """;

    // The rules every generated answer must follow.
    const string AnswerShape = """
        Reply with ONE JSON object in exactly this shape:
        {
          "files": [
            { "name": "Connector.cs",      "content": "full C# source of class Connector : IConnector" },
            { "name": "ConnectorTests.cs", "content": "full C# source of class ConnectorTests : IConnectorTests" }
          ],
          "notes": "short assumptions"
        }
        Rules:
        - Target .NET 8 with implicit usings. Use only the .NET base libraries (System.Net.Http, System.Text.Json).
        - Do NOT redeclare the contract types below; they already exist. Public classes must be named
          exactly Connector and ConnectorTests.
        - Never hard-code a URL, user, password or key. Read them from the ConnectorSettings argument.
        - Never throw for a failed connection: return ConnectorResult with Success = false, the
          HTTP status code and a short message. Use a 20 second timeout on every HttpClient.
        - ConnectorTests.RunAsync runs the unit tests by hand (no test framework) and returns one
          TestOutcome per check.

        THE CONTRACT (already in the project):
        """;

    // 0.2 and 1.2: the evaluator's instructions. It must reply in JSON so the program can read it.
    const string JudgePrompt = """
        You are a senior evaluator. You receive a user's ORIGINAL REQUEST and candidate answers labelled
        ANSWER A, ANSWER B, and so on, written by different AI models. Each answer is a JSON object with
        two C# files: Connector.cs and ConnectorTests.cs.
        Decide between two options:
        1. "select": one candidate is already good (it would compile, follows the contract, and its tests are meaningful). Choose it.
        2. "synthesize": none is good enough, or combining them is clearly better. Write your own two files.

        Reply with ONE JSON object and nothing else (no markdown fences, no extra text):
        {
          "decision": "select" or "synthesize",
          "chosen": "A" or "B" when decision is select, otherwise "",
          "reasoning": "two or three sentences explaining the decision",
          "files": [ { "name": "Connector.cs", "content": "..." }, { "name": "ConnectorTests.cs", "content": "..." } ]
        }
        Use "files" only when decision is synthesize; when you select, use [].
        """;

    // ---------- SHARED STATE ----------

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // Session token totals: model name -> [input tokens, output tokens].
    static readonly Dictionary<string, int[]> sessionTokens = new();

    // One record per flow run, kept in memory and saved at the end if the user asks.
    static readonly List<RunRecord> history = new();

    static readonly string historyFile = $"connector_history_{DateTime.Now:yyyyMMdd_HHmmss}.json";

    // =================================================================
    //  MAIN: the flow of the diagram, top to bottom
    // =================================================================
    static async Task Main()
    {
        Console.WriteLine("=== EMA Connector Builder ===\n");

        // ---------- STEP 1: JSON USER SECRETS ----------
        // IMPORTANT: URL, passwords and API keys come from the "user secrets" JSON file on this
        // PC, so they are never in the source code or in the repository. Set them once with:
        //   dotnet user-secrets set "ChMS:Url" "https://..." --project src/ConnectorBuilder
        // HOW IT WORKS: AddUserSecrets reads that JSON file; config["ChMS:Url"] reads one value.
        // A value that is missing is asked on screen instead (typed keys stay hidden).
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string chmsUrl = config["ChMS:Url"] ?? Ask("ChMS system URL: ");
        string chmsUser = config["ChMS:Username"] ?? Ask("ChMS username (blank if using an API key): ");
        string chmsPassword = config["ChMS:Password"] ?? (chmsUser == "" ? "" : ReadSecret("ChMS password: "));
        string chmsApiKey = config["ChMS:ApiKey"] ?? (chmsUser != "" ? "" : ReadSecret("ChMS API key: "));

        string openAiKey = config["Models:OpenAiKey"] ?? ReadSecret("OpenAI API key (blank to skip): ");
        string claudeKey = config["Models:AnthropicKey"] ?? ReadSecret("Anthropic API key: ");
        string workspaceId = config["Models:AnthropicWorkspaceId"] ?? "";

        // Opus is the evaluator, so the program cannot work without this key.
        if (string.IsNullOrWhiteSpace(claudeKey))
        {
            Console.WriteLine("\nThe Anthropic key is required (Opus is the evaluator). Exiting.");
            return;
        }

        if (string.IsNullOrWhiteSpace(chmsUrl))
        {
            Console.WriteLine("\nThe ChMS URL is required. Exiting.");
            return;
        }

        // The two schemas at the top of the diagram. The ChMS one defaults to Rock.
        string agnosticSchema = ReadSchema(config["Schemas:Agnostic"] ?? "schemas/AgnosticSchema.json");
        string chmsSchema = ReadSchema(config["Schemas:ChMS"] ?? "schemas/RockSchema.json");

        var settings = new ConnectorSettings(chmsUrl.TrimEnd('/'), chmsUser, chmsPassword, chmsApiKey);
        var keys = new Keys(openAiKey, claudeKey, workspaceId);

        // ---------- FLOW 0.1 / 0.2: TEST THE CONNECTION ----------
        // The password is NOT put in the prompt. The models only know the URL and the user name.
        string testRequest =
            $"Create me a connector that can currently test these credentials and connection " +
            $"(user: \"{(chmsUser == "" ? "(none, uses an API key)" : chmsUser)}\", URL: \"{settings.Url}\"). " +
            "The password or API key is supplied at run time in ConnectorSettings. " +
            "FetchAsync should only prove the connection works (one small GET request). " +
            "Create the unit tests for it too.\n\n" +
            "ChMS API description:\n" + chmsSchema;

        RunRecord test = await RunFlow("Connection test", testRequest, "connectorForTesting.cs", "Test.Connector.cs",
                                       keys, settings);
        history.Add(test);

        // ---------- "DO YOU WANT TO MAKE A CONNECTOR?" ----------
        Console.Write("\nDo you want to make a connector? (y/n): ");
        string answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "n";

        if (answer == "y" || answer == "yes")
        {
            // ---------- FLOW 1.1 / 1.2: CREATE A CONNECTOR ----------
            string createRequest =
                "Create me a connector that follows the AgnosticSchema below and uses the ChMS API " +
                "described in the RockSchema below. FetchAsync must read donors and their donations from the " +
                "ChMS and return them in Data as one JSON text that matches the AgnosticSchema " +
                "(an object with \"donors\" and \"donations\" arrays). Also write the tests that check " +
                "the connector returns something we expect (basic tests: did we get back data, " +
                "does every donation have an id, a donor id and an amount).\n\n" +
                "AgnosticSchema:\n" + agnosticSchema + "\n\nRockSchema:\n" + chmsSchema;

            RunRecord create = await RunFlow("Create connector", createRequest, "Solution.connector.cs", "Test.Connector.cs",
                                             keys, settings);
            history.Add(create);
        }

        // ---------- END PROGRAM ----------
        ShowTokens();

        Console.Write("\nSave the run history locally? (y/n): ");
        string saveReply = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";

        if (saveReply == "y" || saveReply == "yes")
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(historyFile, JsonSerializer.Serialize(history, options));
            Console.WriteLine($"History saved to {Path.GetFullPath(historyFile)}");
        }

        Console.WriteLine("\nGoodbye.");
    }

    // =================================================================
    //  ONE FLOW: ask two models, judge, run, notify or show the output
    //  (the diagram uses this same shape twice: 0.1/0.2 and 1.1/1.2)
    // =================================================================
    static async Task<RunRecord> RunFlow(string flowName, string request, string connectorFileName,
                                         string testFileName, Keys keys, ConnectorSettings settings)
    {
        Console.WriteLine($"\n################ {flowName.ToUpperInvariant()} ################");

        // ---------- ASK TWO MODELS AT THE SAME TIME ----------
        // IMPORTANT: calling an async method starts it right away, so both requests run together
        // and Task.WhenAll waits for the slower one instead of adding the two times.
        string prompt = request + "\n\n" + AnswerShape + "\n" + ContractSource;

        Task<ModelResult> gptTask = AskOpenAi(keys.OpenAi, prompt);
        Task<ModelResult> sonnetTask = AskClaude(SonnetName, SonnetModel, SystemPrompt,
                                                 MaxAnswerTokens, keys.Claude, keys.Workspace, prompt);
        ModelResult[] results = await Task.WhenAll(gptTask, sonnetTask);

        foreach (ModelResult r in results)
            AddTokens(r);

        // ---------- PARSE THE RESPONSES ("parse on this" in the diagram) ----------
        // IMPORTANT: an answer only counts if it parses AND has both files. A broken answer
        // is left out here, so one bad model never stops the flow.
        var good = new List<Candidate>();

        foreach (ModelResult r in results)
        {
            if (!r.Ok)
                continue;

            if (TryParseSolution(r.Text, out Solution? solution, out string problem))
                good.Add(new Candidate(r, solution!));
            else
                Console.WriteLine($"[{r.Model}] answer rejected: {problem}");
        }

        if (good.Count == 0)
        {
            Notify(flowName, "No model produced a usable connector, so nothing was evaluated.");
            return new RunRecord(DateTime.Now, flowName, results.ToList(), "none", null, "", false, "No usable answer", new List<string>());
        }

        // ---------- JUDGE (OPUS), BLIND ----------
        // IMPORTANT: the judge sees "ANSWER A/B", never the model names, so it cannot favour one.
        var judgeInput = new StringBuilder();
        judgeInput.AppendLine("ORIGINAL REQUEST:");
        judgeInput.AppendLine(request);

        for (int i = 0; i < good.Count; i++)
        {
            // HOW IT WORKS: 'A' + 0 is 'A', 'A' + 1 is 'B'. (char) turns the number back into a letter.
            char letter = (char)('A' + i);
            judgeInput.AppendLine($"\n===== ANSWER {letter} =====");
            judgeInput.AppendLine(good[i].Result.Text);
        }

        Console.WriteLine();
        ModelResult judge = await AskClaude(OpusName, OpusModel, JudgePrompt,
                                            MaxJudgeTokens, keys.Claude, keys.Workspace, judgeInput.ToString());
        AddTokens(judge);

        Solution finalSolution;
        string decision;
        string? chosenModel = null;
        string reasoning = "";

        Verdict? verdict = judge.Ok ? ParseVerdict(judge.Text) : null;

        // Approve: use the chosen answer word for word.
        // Disapprove: use the files Opus wrote. If Opus failed or its files are unusable,
        // fall back to the first good candidate rather than losing the run.
        if (verdict != null && verdict.Decision == "select" && verdict.Chosen.Length == 1 &&
            char.ToUpperInvariant(verdict.Chosen[0]) - 'A' is int index && index >= 0 && index < good.Count)
        {
            finalSolution = good[index].Solution;
            chosenModel = good[index].Result.Model;
            decision = "approve";
            reasoning = verdict.Reasoning;
            Console.WriteLine($"Judge approved answer {verdict.Chosen.ToUpperInvariant()} (written by {chosenModel}).");
        }
        else if (verdict != null && verdict.Decision == "synthesize" &&
                 TryBuildSolution(verdict.Files, out Solution? own))
        {
            finalSolution = own!;
            decision = "disapprove, Opus wrote its own";
            reasoning = verdict.Reasoning;
            Console.WriteLine("Judge disapproved both answers and wrote its own solution.");
        }
        else
        {
            finalSolution = good[0].Solution;
            chosenModel = good[0].Result.Model;
            decision = "fallback (judge unusable)";
            reasoning = judge.Ok ? "The evaluator's reply could not be used." : judge.Text;
            Console.WriteLine($"The evaluator could not decide ({decision}); using the answer from {chosenModel}.");
        }

        if (reasoning != "")
            Console.WriteLine($"Reasoning: {reasoning}");

        // ---------- SAVE THE FILES, THEN EXECUTION.ORDER ----------
        var savedFiles = new List<string>();
        string? p1 = SaveFile(testFileName, finalSolution.TestsSource);
        string? p2 = SaveFile(connectorFileName, finalSolution.ConnectorSource);
        if (p1 != null) savedFiles.Add(p1);
        if (p2 != null) savedFiles.Add(p2);

        // IMPORTANT: Execution.Order. 1 = Test.Connector (the unit tests), 2 = the connector itself.
        Console.WriteLine("\nExecution.Order 1: Test.Connector   |   Execution.Order 2: " + connectorFileName);
        RunReport? report = await ExecuteSolution(finalSolution, settings);

        // ---------- DID THE CONNECTOR ITSELF FAIL? ----------
        bool connectorFailed = report == null || report.Connector == null || !report.Connector.Success;
        string outcome;

        if (connectorFailed)
        {
            outcome = report == null
                ? "The generated code did not build or did not report a result."
                : report.Connector == null
                    ? $"The connector crashed: {report.ConnectorError}"
                    : $"The connector failed: HTTP {report.Connector.StatusCode} {report.Connector.Message}";

            // Yes -> Notify EMA team (and store the failure, "I can store the result somewhere in the DB").
            Notify(flowName, outcome);
        }
        else
        {
            // No -> Execute, then show the output to the user.
            outcome = "Connector succeeded.";
        }

        ShowOutput(report);

        StoreResult(flowName, !connectorFailed, outcome, chosenModel, savedFiles, report);

        return new RunRecord(DateTime.Now, flowName, results.ToList(), decision, chosenModel, reasoning,
                             !connectorFailed, outcome, savedFiles);
    }

    // =================================================================
    //  RUNNING THE GENERATED CODE
    // =================================================================

    // IMPORTANT: the AI's code is compiled and run in a small temporary project by `dotnet run`,
    // in its own process. The ChMS login is handed over through environment variables only at
    // that moment, so it is never sent to the AI and never written to disk.
    // SECURITY: the code was written by an AI, so run this only on a machine you trust.
    static async Task<RunReport?> ExecuteSolution(Solution solution, ConnectorSettings settings)
    {
        string dir = Path.Combine(Path.GetTempPath(), "connector_run_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "Run.csproj"), RunProjectFile);
            File.WriteAllText(Path.Combine(dir, "Contract.cs"), ContractSource);
            File.WriteAllText(Path.Combine(dir, "Harness.cs"), HarnessSource);
            File.WriteAllText(Path.Combine(dir, "Connector.cs"), solution.ConnectorSource);
            File.WriteAllText(Path.Combine(dir, "ConnectorTests.cs"), solution.TestsSource);

            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = dir
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--nologo");
            psi.Environment["CHMS_URL"] = settings.Url;
            psi.Environment["CHMS_USERNAME"] = settings.Username;
            psi.Environment["CHMS_PASSWORD"] = settings.Password;
            psi.Environment["CHMS_APIKEY"] = settings.ApiKey;

            Console.WriteLine("Building and running the generated code...");

            using Process process = Process.Start(psi)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RunTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Console.WriteLine($"The generated code did not finish in {RunTimeoutSeconds} seconds and was stopped.");
                return null;
            }

            string output = await stdout + await stderr;

            // The harness prints one line starting with @@REPORT@@ followed by JSON.
            foreach (string line in output.Split('\n'))
            {
                if (line.StartsWith("@@REPORT@@"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<RunReport>(line.Substring("@@REPORT@@".Length), options);
                }
            }

            // No report: most likely a compile error. Show the first lines so the cause is visible.
            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine("Build/run output:");
            foreach (string line in lines.Take(15))
                Console.WriteLine("  " + line.TrimEnd());
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not run the generated code: {ex.Message}");
            return null;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { /* temp folder, ignore */ }
        }
    }

    const string RunProjectFile = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    // The part of the temporary project that I wrote: runs order 1, then order 2, prints a report.
    const string HarnessSource = """
        using System.Text.Json;

        static class Harness
        {
            static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

            static async Task Main()
            {
                var settings = new ConnectorSettings(Env("CHMS_URL"), Env("CHMS_USERNAME"), Env("CHMS_PASSWORD"), Env("CHMS_APIKEY"));

                // Execution.Order 1: Test.Connector
                List<TestOutcome> tests = new();
                string testError = "";
                try { tests = await new ConnectorTests().RunAsync(new Connector(), settings).WaitAsync(TimeSpan.FromSeconds(90)); }
                catch (Exception ex) { testError = ex.Message; }

                // Execution.Order 2: the connector itself
                ConnectorResult? result = null;
                string connectorError = "";
                try { result = await new Connector().FetchAsync(settings).WaitAsync(TimeSpan.FromSeconds(60)); }
                catch (Exception ex) { connectorError = ex.Message; }

                Console.WriteLine("@@REPORT@@" + JsonSerializer.Serialize(new { tests, testError, connector = result, connectorError }));
            }
        }
        """;

    // =================================================================
    //  OUTPUT, NOTIFY, STORE
    // =================================================================

    static void ShowOutput(RunReport? report)
    {
        Console.WriteLine("\n================ OUTPUT ================");

        if (report == null)
        {
            Console.WriteLine("(no result to show)");
            return;
        }

        Console.WriteLine("Unit tests (Execution.Order 1):");
        if (report.TestError != "")
            Console.WriteLine($"  The test run crashed: {report.TestError}");
        foreach (TestOutcome t in report.Tests)
            Console.WriteLine($"  [{(t.Passed ? "PASS" : "FAIL")}] {t.Name} {t.Detail}");

        Console.WriteLine("Connector (Execution.Order 2):");
        if (report.Connector != null)
        {
            Console.WriteLine($"  Success: {report.Connector.Success}   HTTP: {report.Connector.StatusCode}");
            Console.WriteLine($"  Message: {report.Connector.Message}");
            string data = report.Connector.Data ?? "";
            Console.WriteLine("  Data   : " + (data.Length > 1500 ? data.Substring(0, 1500) + "..." : data));
        }
        else
        {
            Console.WriteLine($"  Crashed: {report.ConnectorError}");
        }
    }

    // "Notify EMA team". In the demo this is a red message plus a line in a log file.
    // Replace the body with an email or Teams call when the real channel is known.
    static void Notify(string flowName, string reason)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[NOTIFY EMA TEAM] {flowName}: {reason}");
        Console.ResetColor();

        try
        {
            Directory.CreateDirectory(OutputFolder);
            File.AppendAllText(Path.Combine(OutputFolder, NotifyLog),
                               $"{DateTime.Now:s}\t{flowName}\t{reason.Replace('\n', ' ')}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write the notification log: {ex.Message}");
        }
    }

    // "I can store the result somewhere in the DB". In the demo: one JSON file with a growing list.
    static void StoreResult(string flowName, bool success, string outcome, string? model,
                            List<string> files, RunReport? report)
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            string path = Path.Combine(OutputFolder, ResultStore);

            var all = File.Exists(path)
                ? JsonSerializer.Deserialize<List<StoredResult>>(File.ReadAllText(path)) ?? new List<StoredResult>()
                : new List<StoredResult>();

            all.Add(new StoredResult(DateTime.Now, flowName, success, outcome, model, files, report));
            File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[result stored] {Path.GetFullPath(path)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not store the result: {ex.Message}");
        }
    }

    // =================================================================
    //  CALLING THE MODELS
    // =================================================================

    // HOW IT WORKS: "async Task<ModelResult>" means "this method takes time (it waits for the
    // internet) and will deliver a ModelResult later". That is what lets two run at the same time.
    static async Task<ModelResult> AskOpenAi(string apiKey, string prompt)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine($"[{GptName}] skipped (no API key)");
            return new ModelResult(GptName, "No API key provided.", 0, 0, false);
        }

        var body = new
        {
            model = GptModel,
            instructions = SystemPrompt,
            input = new[] { new { role = "user", content = prompt } },
            reasoning = new { effort = "low" },
            max_output_tokens = MaxAnswerTokens
        };

        Console.WriteLine($"[{GptName}] asking...");

        // IMPORTANT: try/catch is why the program never crashes on a network problem.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(body);

            using var response = await http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Failed(GptName, json);

            return ParseOpenAi(json);
        }
        catch (Exception ex)
        {
            return Failed(GptName, ex.Message);
        }
    }

    // Used twice: for Sonnet (one of the two writers) and for Opus (the evaluator).
    static async Task<ModelResult> AskClaude(string name, string model, string system,
        int maxTokens, string apiKey, string workspaceId, string prompt)
    {
        var body = new
        {
            model = model,
            system = system,
            max_tokens = maxTokens,
            messages = new[] { new { role = "user", content = prompt } }
        };

        Console.WriteLine($"[{name}] asking...");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            if (!string.IsNullOrWhiteSpace(workspaceId))
                request.Headers.Add("anthropic-workspace-id", workspaceId);

            request.Content = JsonContent.Create(body);

            using var response = await http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return Failed(name, json);

            return ParseClaude(name, json);
        }
        catch (Exception ex)
        {
            return Failed(name, ex.Message);
        }
    }

    static ModelResult Failed(string name, string error)
    {
        string reason = error.Length > 400 ? error.Substring(0, 400) + "..." : error;
        Console.WriteLine($"[{name}] FAILED: {reason}");
        return new ModelResult(name, $"Error: {error}", 0, 0, false);
    }

    // =================================================================
    //  READING THE REPLIES
    // =================================================================

    // OpenAI text lives in: output[] -> type "message" -> content[] -> type "output_text" -> text
    static ModelResult ParseOpenAi(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();

        if (doc.RootElement.TryGetProperty("output", out JsonElement output))
        {
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "message")
                    continue;

                foreach (JsonElement part in item.GetProperty("content").EnumerateArray())
                {
                    if (part.GetProperty("type").GetString() == "output_text")
                        sb.Append(part.GetProperty("text").GetString());
                }
            }
        }

        var tokens = ReadTokens(doc.RootElement, "input_tokens", "output_tokens");
        return Finished(GptName, sb.ToString(), tokens.Input, tokens.Output);
    }

    // Claude text lives in: content[] -> type "text" -> text
    static ModelResult ParseClaude(string name, string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();

        foreach (JsonElement block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
                sb.Append(block.GetProperty("text").GetString());
        }

        var tokens = ReadTokens(doc.RootElement, "input_tokens", "output_tokens");
        return Finished(name, sb.ToString(), tokens.Input, tokens.Output);
    }

    // HOW IT WORKS: "(int Input, int Output)" is a tuple: the method returns two numbers at once.
    static (int Input, int Output) ReadTokens(JsonElement root, string inputName, string outputName)
    {
        int input = 0;
        int output = 0;

        if (root.TryGetProperty("usage", out JsonElement usage))
        {
            if (usage.TryGetProperty(inputName, out JsonElement i))
                input = i.GetInt32();

            if (usage.TryGetProperty(outputName, out JsonElement o))
                output = o.GetInt32();
        }

        return (input, output);
    }

    static ModelResult Finished(string name, string text, int inputTokens, int outputTokens)
    {
        if (text == "")
        {
            Console.WriteLine($"[{name}] FAILED (empty answer)");
            return new ModelResult(name, "No answer returned.", inputTokens, outputTokens, false);
        }

        Console.WriteLine($"[{name}] done  (in: {inputTokens}, out: {outputTokens} tokens)");
        return new ModelResult(name, text, inputTokens, outputTokens, true);
    }

    // Cuts the JSON object out of a reply. Models sometimes wrap JSON in ``` fences,
    // so we cut from the first { to the last }.
    static string? ExtractJson(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        return start < 0 || end <= start ? null : raw.Substring(start, end - start + 1);
    }

    // Reads {"files":[{name, content}, ...]} and checks that both files are there.
    static bool TryParseSolution(string raw, out Solution? solution, out string problem)
    {
        solution = null;
        problem = "";

        try
        {
            string? json = ExtractJson(raw);
            if (json == null)
            {
                problem = "no JSON object found";
                return false;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            var files = ReadFiles(doc.RootElement);

            if (!TryBuildSolution(files, out solution))
            {
                problem = "the JSON must contain Connector.cs and ConnectorTests.cs";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            problem = "invalid JSON (" + ex.Message + ")";
            return false;
        }
    }

    static List<OutputFile> ReadFiles(JsonElement root)
    {
        var files = new List<OutputFile>();

        if (root.TryGetProperty("files", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement f in list.EnumerateArray())
            {
                string name = GetText(f, "name");
                if (name != "")
                    files.Add(new OutputFile(name, GetText(f, "content")));
            }
        }

        return files;
    }

    // Finds the connector and the tests among the files by what they contain, not only by name.
    static bool TryBuildSolution(List<OutputFile> files, out Solution? solution)
    {
        OutputFile? tests = files.FirstOrDefault(f => f.Content.Contains("IConnectorTests"));
        OutputFile? connector = files.FirstOrDefault(f => f != tests && f.Content.Contains("IConnector"));

        solution = tests != null && connector != null ? new Solution(connector.Content, tests.Content) : null;
        return solution != null;
    }

    // Converts the evaluator's JSON reply into a Verdict. Returns null if it is not valid JSON.
    static Verdict? ParseVerdict(string raw)
    {
        try
        {
            string? json = ExtractJson(raw);
            if (json == null)
                return null;

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            return new Verdict(GetText(root, "decision").ToLowerInvariant(), GetText(root, "chosen"),
                               GetText(root, "reasoning"), ReadFiles(root));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Safely reads a text property: returns "" if it is missing or is not text.
    static string GetText(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";

        return "";
    }

    // =================================================================
    //  FILES, TOKENS, KEYS
    // =================================================================

    static string ReadSchema(string path)
    {
        string full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

        if (File.Exists(full))
            return File.ReadAllText(full);

        Console.WriteLine($"Schema file not found: {full} (continuing without it)");
        return "(schema not provided)";
    }

    // Saves one file into ./output. Returns the full path, or null if it could not be saved.
    static string? SaveFile(string name, string content)
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);

            // IMPORTANT: security. The name is fixed by this program, but we still never overwrite:
            // Solution.connector.cs -> Solution.connector_1.cs -> Solution.connector_2.cs ...
            string path = Path.Combine(OutputFolder, name);
            int counter = 1;

            while (File.Exists(path))
            {
                string numbered = Path.GetFileNameWithoutExtension(name) + "_" + counter + Path.GetExtension(name);
                path = Path.Combine(OutputFolder, numbered);
                counter++;
            }

            File.WriteAllText(path, content);
            Console.WriteLine($"[file saved] {Path.GetFullPath(path)}");
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save file '{name}': {ex.Message}");
            return null;
        }
    }

    static void AddTokens(ModelResult result)
    {
        if (!sessionTokens.ContainsKey(result.Model))
            sessionTokens[result.Model] = new int[2];

        sessionTokens[result.Model][0] += result.InputTokens;
        sessionTokens[result.Model][1] += result.OutputTokens;
    }

    static void ShowTokens()
    {
        Console.WriteLine("\n---------------- TOKENS (session total) ------");
        int total = 0;

        foreach (KeyValuePair<string, int[]> pair in sessionTokens)
        {
            Console.WriteLine($"  {pair.Key,-22} in: {pair.Value[0],7}   out: {pair.Value[1],7}");
            total += pair.Value[0] + pair.Value[1];
        }

        Console.WriteLine($"  {"TOTAL",-22} {total} tokens");
    }

    static string Ask(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? "";
    }

    static string ReadSecret(string prompt)
    {
        Console.Write(prompt);

        // ReadKey crashes when input is redirected, so fall back to ReadLine
        if (Console.IsInputRedirected)
            return Console.ReadLine()?.Trim() ?? "";

        var value = new StringBuilder();

        while (true)
        {
            // IMPORTANT: this is how a key stays hidden. intercept: true stops the key
            // from appearing on screen. We print * instead.
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
                Console.Write("*");
            }
        }

        return value.ToString().Trim();
    }
}

// =====================================================================
//  DATA TYPES: small containers that carry information around
// =====================================================================

// The contract types, mirrored here so this program can read the harness report.
record ConnectorSettings(string Url, string Username, string Password, string ApiKey);
record ConnectorResult(bool Success, int StatusCode, string Message, string Data);
record TestOutcome(string Name, bool Passed, string Detail);

// What the harness prints at the end of a run.
record RunReport(List<TestOutcome> Tests, string TestError, ConnectorResult? Connector, string ConnectorError);

record Keys(string OpenAi, string Claude, string Workspace);

// One model's reply to one call.
record ModelResult(string Model, string Text, int InputTokens, int OutputTokens, bool Ok);

// A file written by a model.
record OutputFile(string Name, string Content);

// The two C# files that make a solution.
record Solution(string ConnectorSource, string TestsSource);

// An answer that parsed correctly, with the model that wrote it.
record Candidate(ModelResult Result, Solution Solution);

// The evaluator's decision, after its JSON has been read.
record Verdict(string Decision, string Chosen, string Reasoning, List<OutputFile> Files);

// One complete flow run. The history is a list of these.
record RunRecord(DateTime Time, string Flow, List<ModelResult> Answers, string Decision,
                 string? ChosenModel, string Reasoning, bool ConnectorSucceeded, string Outcome,
                 List<string> SavedFiles);

// One row of output/connector_results.json (the stand-in for the database).
record StoredResult(DateTime Time, string Flow, bool Success, string Outcome, string? Model,
                    List<string> Files, RunReport? Report);
