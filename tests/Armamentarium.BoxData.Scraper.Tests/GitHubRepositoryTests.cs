using System.Net;
using System.Text;
using System.Text.Json;

namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class GitHubRepositoryTests
{
    private const string Token = "github_pat_SECRET_VALUE";

    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.ToString()));
            return answer(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task GetTable_DecodesContentAndSha()
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"format\": 1}\n"));
        var handler = new Recorder(_ => Json($$"""{"sha":"abc","content":"{{content[..4]}}\n{{content[4..]}}"}"""));

        var (text, sha) = await new GitHubRepository(new HttpClient(handler), null).GetTableAsync(default);

        Assert.AreEqual("{\"format\": 1}\n", text);
        Assert.AreEqual("abc", sha);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/contents/box-contents.json?ref=main", handler.Requests[0].Path);
    }

    [TestMethod]
    public async Task GetTable_OverOneMegabyte_ReadsTheBlob()
    {
        // Over 1 MB the contents API answers without content (measured 2026-10-22 at 1,048,715 bytes).
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"format\": 1}\n"));
        var handler = new Recorder(request => request.RequestUri!.AbsolutePath.Contains("/git/blobs/")
            ? Json($$"""{"sha":"abc","encoding":"base64","content":"{{content[..4]}}\n{{content[4..]}}"}""")
            : Json("""{"sha":"abc","encoding":"none","content":""}"""));

        var (text, sha) = await new GitHubRepository(new HttpClient(handler), null).GetTableAsync(default);

        Assert.AreEqual("{\"format\": 1}\n", text);
        Assert.AreEqual("abc", sha);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/git/blobs/abc", handler.Requests[1].Path);
    }

    [TestMethod]
    public async Task OpenScraperBranches_KeepsOnlyScraperHeads()
    {
        var handler = new Recorder(_ => Json("""[{"head":{"ref":"scraper/60010199001"}},{"head":{"ref":"data/manual"}}]"""));

        var branches = await new GitHubRepository(new HttpClient(handler), null).OpenScraperBranchesAsync(default);

        CollectionAssert.AreEqual(new[] { "scraper/60010199001" }, branches.ToArray());
    }

    [TestMethod]
    public async Task Propose_CreatesBranchCommitAndDraftPullRequest()
    {
        var handler = new Recorder(request => request.Method == HttpMethod.Get
            ? Json("""{"object":{"sha":"main-sha"}}""")
            : Json("""{"number":12}""", HttpStatusCode.Created));

        var number = await new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/60010199002", "data: add Kill Team: B", "body", "{}\n"), "file-sha", default);

        Assert.AreEqual(12, number);
        var r = handler.Requests;
        Assert.AreEqual(4, r.Count);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/git/ref/heads/main", r[0].Path);
        StringAssert.Contains(r[1].Body, "\"refs/heads/scraper/60010199002\"");
        StringAssert.Contains(r[1].Body, "\"main-sha\"");
        Assert.AreEqual(HttpMethod.Put, r[2].Method);
        using (var put = JsonDocument.Parse(r[2].Body!))
        {
            Assert.AreEqual("{}\n", Encoding.UTF8.GetString(Convert.FromBase64String(put.RootElement.GetProperty("content").GetString()!)));
            Assert.AreEqual("file-sha", put.RootElement.GetProperty("sha").GetString());
            Assert.AreEqual("scraper/60010199002", put.RootElement.GetProperty("branch").GetString());
        }

        using (var pull = JsonDocument.Parse(r[3].Body!))
        {
            Assert.IsTrue(pull.RootElement.GetProperty("draft").GetBoolean());
            Assert.AreEqual("main", pull.RootElement.GetProperty("base").GetString());
        }

        Assert.IsTrue(r.All(x => x.Auth == $"Bearer {Token}"));
    }

    [TestMethod]
    public async Task Propose_NotADraft_OpensAReadyPullRequest()
    {
        var handler = new Recorder(request => request.Method == HttpMethod.Get
            ? Json("""{"object":{"sha":"main-sha"}}""")
            : Json("""{"number":3}""", HttpStatusCode.Created));

        await new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/60010199002", "data: add B", "body", "{}\n", Draft: false), "file-sha", default);

        using var pull = JsonDocument.Parse(handler.Requests[3].Body!);
        Assert.IsFalse(pull.RootElement.GetProperty("draft").GetBoolean());
    }

    [TestMethod]
    public async Task Merge_MergesThePullRequest()
    {
        var handler = new Recorder(_ => Json("""{"merged":true}"""));

        await new GitHubRepository(new HttpClient(handler), Token).MergeAsync(12, default);

        var r = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Put, r.Method);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/pulls/12/merge", r.Path);
    }

    [TestMethod]
    public async Task DeleteBranch_DeletesTheRef()
    {
        var handler = new Recorder(_ => Json("", HttpStatusCode.NoContent));

        await new GitHubRepository(new HttpClient(handler), Token).DeleteBranchAsync("scraper/60010199002", default);

        var r = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Delete, r.Method);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/git/refs/heads/scraper/60010199002", r.Path);
    }

    [TestMethod]
    public async Task Merge_Refused_FailsAndKeepsTheBranch()
    {
        var handler = new Recorder(_ => Json("""{"message":"Pull Request is not mergeable"}""", HttpStatusCode.MethodNotAllowed));

        var error = await Assert.ThrowsExactlyAsync<GitHubException>(
            () => new GitHubRepository(new HttpClient(handler), Token, TimeSpan.Zero).MergeAsync(12, default));

        StringAssert.Contains(error.Message, "405");
        Assert.AreEqual(GitHubRepository.MergeAttempts, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Delete));
    }

    // GitHub answered 405 "Base branch was modified" to a merge two seconds after the previous one, on a branch of the newest
    // main; a later try merges (measured, ADR 0004 Addendum ARMAM-603).
    [TestMethod]
    public async Task Merge_BaseBranchWasModified_IsTriedAgain()
    {
        var answers = new Queue<HttpResponseMessage>([
            Json("""{"message":"Base branch was modified. Review and try the merge again."}""", HttpStatusCode.MethodNotAllowed),
            Json("""{"merged":true}"""),
        ]);
        var handler = new Recorder(_ => answers.Dequeue());
        var log = new List<string>();

        await new GitHubRepository(new HttpClient(handler), Token, TimeSpan.Zero, log.Add).MergeAsync(12, default);

        Assert.AreEqual(2, handler.Requests.Count);
        CollectionAssert.AreEqual(new[] { "pull request 12: merge answered 405, try 2 of 3 in 0 s" }, log);
    }

    [TestMethod]
    public async Task Merge_StoppedWhileWaiting_AsksNoMore()
    {
        using var stop = new CancellationTokenSource();
        var handler = new Recorder(_ =>
        {
            stop.Cancel();
            return Json("""{"message":"Base branch was modified."}""", HttpStatusCode.MethodNotAllowed);
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new GitHubRepository(new HttpClient(handler), Token, TimeSpan.FromMinutes(1)).MergeAsync(12, stop.Token));

        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Merge_OtherFailure_IsNotTriedAgain()
    {
        var handler = new Recorder(_ => Json("""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));

        await Assert.ThrowsExactlyAsync<GitHubException>(
            () => new GitHubRepository(new HttpClient(handler), Token, TimeSpan.Zero).MergeAsync(12, default));

        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Propose_FailureAfterTheBranch_DeletesTheBranch()
    {
        var handler = new Recorder(request =>
            request.Method == HttpMethod.Get ? Json("""{"object":{"sha":"main-sha"}}""")
            : request.Method == HttpMethod.Put ? Json("""{"message":"conflict"}""", HttpStatusCode.Conflict)
            : Json("{}", request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.Created));

        await Assert.ThrowsExactlyAsync<GitHubException>(() => new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/60010199002", "data: add B", "body", "{}\n"), "file-sha", default));

        var last = handler.Requests[^1];
        Assert.AreEqual(HttpMethod.Delete, last.Method);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/git/refs/heads/scraper/60010199002", last.Path);
    }

    [TestMethod]
    public async Task Propose_TimeoutAfterTheBranch_DeletesTheBranch()
    {
        var handler = new Recorder(request =>
            request.Method == HttpMethod.Get ? Json("""{"object":{"sha":"main-sha"}}""")
            : request.Method == HttpMethod.Put ? throw new TaskCanceledException("timeout")
            : Json("{}", request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.Created));

        await Assert.ThrowsExactlyAsync<GitHubException>(() => new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/60010199002", "data: add B", "body", "{}"), "file-sha", default));

        Assert.AreEqual(HttpMethod.Delete, handler.Requests[^1].Method);
    }

    [TestMethod]
    public async Task Propose_ExistingBranch_FailsWithoutDeletingIt()
    {
        var handler = new Recorder(request => request.Method == HttpMethod.Get
            ? Json("""{"object":{"sha":"main-sha"}}""")
            : Json("""{"message":"Reference already exists"}""", HttpStatusCode.UnprocessableEntity));

        await Assert.ThrowsExactlyAsync<GitHubException>(() => new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/60010199002", "data: add B", "body", "{}\n"), "file-sha", default));

        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Delete));
    }

    [TestMethod]
    public async Task Failure_NamesStatusButNeverTheToken()
    {
        var handler = new Recorder(_ => Json($$"""{"message":"Bad credentials"}""", HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsExactlyAsync<GitHubException>(
            () => new GitHubRepository(new HttpClient(handler), Token).OpenScraperBranchesAsync(default));

        StringAssert.Contains(error.Message, "401");
        Assert.IsFalse(error.ToString().Contains(Token, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetAliases_DecodesTheFileOnMain()
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"format\": 1, \"aliases\": []}"));
        var handler = new Recorder(_ => Json($$"""{"sha":"abc","content":"{{content}}"}"""));

        var text = await new GitHubRepository(new HttpClient(handler), null).GetAliasesAsync(default);

        Assert.AreEqual("{\"format\": 1, \"aliases\": []}", text);
        Assert.AreEqual("/repos/Strecker-IT/armamentarium-box-data/contents/aliases.json?ref=main", handler.Requests[0].Path);
    }

    [TestMethod]
    public async Task GetAliases_NoFile_IsNullAndAnyOtherFailureThrows()
    {
        Assert.IsNull(await new GitHubRepository(new HttpClient(new Recorder(_ => Json("{}", HttpStatusCode.NotFound))), null)
            .GetAliasesAsync(default));
        await Assert.ThrowsExactlyAsync<GitHubException>(() => new GitHubRepository(
            new HttpClient(new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError))), null).GetAliasesAsync(default));
    }

    [TestMethod]
    public async Task Propose_CutsABodyOverGitHubsLimitAtALine()
    {
        var handler = new Recorder(request => request.Method == HttpMethod.Get
            ? Json("""{"object":{"sha":"main-sha"}}""")
            : Json("""{"number":12}""", HttpStatusCode.Created));
        var lines = Enumerable.Range(0, 2000).Select(i => $"| `box {i}` | `40k` | `name` | `ids` |").ToList();

        await new GitHubRepository(new HttpClient(handler), Token).ProposeAsync(
            new Proposal("scraper/backfill-ids", "data: ids", string.Join("\n", lines) + "\n", "{}\n"), "file-sha", default);

        using var pull = JsonDocument.Parse(handler.Requests[3].Body!);
        var body = pull.RootElement.GetProperty("body").GetString()!;
        Assert.IsTrue(body.Length <= GitHubRepository.MaxBodyLength, $"{body.Length} characters");
        var kept = body.Split('\n').TakeWhile(l => l.StartsWith('|')).Count();
        CollectionAssert.AreEqual(lines.Take(kept).ToArray(), body.Split('\n').Take(kept).ToArray());
        StringAssert.Contains(body, $"{2000 - kept} more line(s)");
    }

    [TestMethod]
    public void BodyOf_KeepsABodyOfExactlyTheLimitWhole()
    {
        var body = new string('x', GitHubRepository.MaxBodyLength - 1) + "\n";

        Assert.AreEqual(body, GitHubRepository.BodyOf(body));
    }

    [TestMethod]
    public void BodyOf_LeavesOutALineLongerThanTheLimit()
    {
        var body = new string('x', GitHubRepository.MaxBodyLength + 1) + "\nsecond\n";

        var cut = GitHubRepository.BodyOf(body);

        Assert.IsTrue(cut.Length <= GitHubRepository.MaxBodyLength);
        Assert.IsFalse(cut.Contains('x'));
        StringAssert.Contains(cut, "2 more line(s)");
    }
}
