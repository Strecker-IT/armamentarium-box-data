using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>A GitHub answer that is not a success. Names the request and the status, never the token.</summary>
public sealed class GitHubException(string message, int? status = null) : Exception(message)
{
    /// <summary>The HTTP status GitHub answered with; null when no answer came.</summary>
    public int? Status { get; } = status;
}

/// <summary>
/// This repository through the GitHub REST API (ADR 0003 §3): reads the table, lists the open scraper pull requests,
/// opens a pull request and, with <c>--auto-merge</c>, merges it (ADR 0004). The token, when present, goes only into the
/// Authorization header. A merge GitHub refuses with 405 is tried again after <paramref name="mergeRetry"/> (five seconds by
/// default), <see cref="MergeAttempts"/> times in all, each try again named in <paramref name="log"/> (ADR 0004 Addendum
/// ARMAM-603).
/// </summary>
public sealed class GitHubRepository(HttpClient http, string? token, TimeSpan? mergeRetry = null, Action<string>? log = null)
    : IRepository
{
    public const string Repository = "Strecker-IT/armamentarium-box-data";
    public const string BranchPrefix = "scraper/";
    public const int MergeAttempts = 3;

    // GitHub refuses a pull request text over 65,536 characters with 422 (measured: the backfill of 491 gaps, 2026-10-21);
    // the rest is a plain safety margin.
    public const int MaxBodyLength = 60_000;

    private const string File = "box-contents.json";
    private static readonly Uri Api = new("https://api.github.com/");

    /// <summary>
    /// The text of a pull request as GitHub takes it: whole up to <see cref="MaxBodyLength"/>, else cut after the last line
    /// that fits, with how many lines were left out; the table in the commit holds every change (ADR 0004 Addendum
    /// ARMAM-642).
    /// </summary>
    public static string BodyOf(string body)
    {
        if (body.Length <= MaxBodyLength)
        {
            return body;
        }

        var lines = body.TrimEnd('\n').Split('\n');
        var kept = new StringBuilder();
        var count = 0;
        // Room for the closing line.
        while (count < lines.Length && kept.Length + lines[count].Length + 1 <= MaxBodyLength - 200)
        {
            kept.Append(lines[count++]).Append('\n');
        }

        return kept.Append('\n')
            .Append($"_… {lines.Length - count} more line(s) left out: GitHub takes at most 65,536 characters here; the diff holds every change._\n")
            .ToString();
    }

    public Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken) => GetFileAsync(File, cancellationToken);

    public async Task<string?> GetAliasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await GetFileAsync(Aliases.File, cancellationToken)).Text;
        }
        catch (GitHubException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<(string Text, string Sha)> GetFileAsync(string path, CancellationToken cancellationToken)
    {
        using var file = await SendAsync(HttpMethod.Get, $"repos/{Repository}/contents/{path}?ref=main", null, cancellationToken);
        var sha = file.RootElement.GetProperty("sha").GetString()!;
        // Over 1 MB the contents API answers with encoding "none" and no content (the table passed it on 2026-10-22); the
        // blob API gives the same file up to 100 MB (ADR 0003 Addendum ARMAM-655).
        if (file.RootElement.TryGetProperty("encoding", out var encoding) && encoding.GetString() == "none")
        {
            using var blob = await SendAsync(HttpMethod.Get, $"repos/{Repository}/git/blobs/{sha}", null, cancellationToken);
            return (Decode(blob.RootElement), sha);
        }

        return (Decode(file.RootElement), sha);
    }

    private static string Decode(JsonElement file) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(file.GetProperty("content").GetString()!.Replace("\n", string.Empty)));

    public async Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken)
    {
        using var pulls = await SendAsync(HttpMethod.Get, $"repos/{Repository}/pulls?state=open&per_page=100", null, cancellationToken);
        return pulls.RootElement.EnumerateArray()
            .Select(p => p.GetProperty("head").GetProperty("ref").GetString())
            .OfType<string>()
            .Where(r => r.StartsWith(BranchPrefix, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<int> ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
    {
        string mainSha;
        using (var main = await SendAsync(HttpMethod.Get, $"repos/{Repository}/git/ref/heads/main", null, cancellationToken))
        {
            mainSha = main.RootElement.GetProperty("object").GetProperty("sha").GetString()!;
        }

        (await SendAsync(HttpMethod.Post, $"repos/{Repository}/git/refs", new JsonObject
        {
            ["ref"] = $"refs/heads/{proposal.Branch}",
            ["sha"] = mainSha,
        }, cancellationToken)).Dispose();

        // From here on the branch exists. Should the commit or the pull request fail, the branch goes again: a branch
        // without a pull request would only block the next proposal of the same box.
        try
        {
            (await SendAsync(HttpMethod.Put, $"repos/{Repository}/contents/{File}", new JsonObject
            {
                ["message"] = proposal.Title,
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(proposal.Table)),
                ["sha"] = tableSha,
                ["branch"] = proposal.Branch,
            }, cancellationToken)).Dispose();

            using var pull = await SendAsync(HttpMethod.Post, $"repos/{Repository}/pulls", new JsonObject
            {
                ["title"] = proposal.Title,
                ["head"] = proposal.Branch,
                ["base"] = "main",
                ["body"] = BodyOf(proposal.Body),
                ["draft"] = proposal.Draft,
            }, cancellationToken);
            return pull.RootElement.GetProperty("number").GetInt32();
        }
        catch (Exception failure)
        {
            // Also a stop (SIGTERM) between branch and pull request: the branch must not outlive its proposal.
            try
            {
                (await SendAsync(HttpMethod.Delete, $"repos/{Repository}/git/refs/heads/{proposal.Branch}", null, CancellationToken.None))
                    .Dispose();
            }
            catch (GitHubException cleanup)
            {
                throw new GitHubException($"{failure.Message}; deleting {proposal.Branch} failed as well: {cleanup.Message}");
            }

            throw;
        }
    }

    // GitHub answered 405 "Base branch was modified" to a merge seconds after the previous one, on a branch of the newest
    // main, while it still worked out the pull request; tried again before the next merge changes main, it goes through.
    public async Task MergeAsync(int number, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                (await SendAsync(HttpMethod.Put, $"repos/{Repository}/pulls/{number}/merge", new JsonObject
                {
                    ["merge_method"] = "merge",
                }, cancellationToken)).Dispose();
                return;
            }
            catch (GitHubException ex) when (ex.Status == 405 && attempt < MergeAttempts)
            {
                var wait = mergeRetry ?? TimeSpan.FromSeconds(5);
                log?.Invoke($"pull request {number}: merge answered 405, try {attempt + 1} of {MergeAttempts} in {wait.TotalSeconds:0} s");
                await Task.Delay(wait, cancellationToken);
            }
        }
    }

    public async Task DeleteBranchAsync(string branch, CancellationToken cancellationToken) =>
        (await SendAsync(HttpMethod.Delete, $"repos/{Repository}/git/refs/heads/{branch}", null, cancellationToken)).Dispose();

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Api, path));
        request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubException($"GitHub {method} {path} failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubException($"GitHub {method} {path} timed out");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubException(
                    $"GitHub {method} {path} answered {(int)response.StatusCode}: {Text.Clean(text[..Math.Min(text.Length, 300)])}",
                    (int)response.StatusCode);
            }

            return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        }
    }
}
