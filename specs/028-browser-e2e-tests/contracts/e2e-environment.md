# Contract: E2E environment, skip behavior and project layout

## Environment variables

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `E2E_BASE_URL` | to run | (unset = every test skipped) | MVC base URL of the running compose stack, e.g. `http://localhost:5256` |
| `E2E_ADMIN_USERNAME` | when running | - | the 023 bootstrap admin user name |
| `E2E_ADMIN_PASSWORD` | when running | - | its password (never logged, never in artifacts) |
| `E2E_HEADED` | no | unset (headless) | `1` shows the browser |
| `E2E_SLOWMO_MS` | no | 0 | slows Playwright actions for debugging |
| `E2E_RESULTS_DIR` | no | `test-results` | failure artifacts directory (screenshots, traces, text) |

## Behavior matrix

| State | Result |
|---|---|
| `E2E_BASE_URL` unset or whitespace | every `[E2EFact]` skipped, reason `Set E2E_BASE_URL (and E2E_ADMIN_USERNAME / E2E_ADMIN_PASSWORD) to run browser tests`; no fixture, no browser, no network |
| set, admin variable missing | fixture throws `InvalidOperationException("E2E_ADMIN_USERNAME is not set")` (name only, never the value) -> tests fail |
| set, `GET {base}/` fails or is not 2xx/3xx | fixture throws with `E2E: cannot reach <base> (<error>). Start the stack: docker compose -f docker-compose.yml -f docker-compose.e2e.yml up -d --build` -> tests fail |
| set and reachable | tests run |
| a page shows the rate limit response (HTTP 429 body) during a step | `Steps` throws `Rate limited by the API: start the stack with docker-compose.e2e.yml` |

## Project file rules (`CategoryProduct.E2ETests.csproj`)

- `net10.0`, `ImplicitUsings`, `Nullable` enabled, `IsPackable=false`, `<Using Include="Xunit" />`.
- Package references with EXACT versions (no `*`, no ranges): `Microsoft.Playwright` (pinned at T003), `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4. `coverlet.collector` is not needed.
- No `ProjectReference`.
- Not listed in `CategoryProduct.slnx`; CI builds it by path.
- `AssemblyInfo.cs`: `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.

## Attribute and base class sketch (names are binding, bodies are the implementer's)

```csharp
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute() { if (!E2EEnvironment.IsConfigured) Skip = E2EEnvironment.SkipReason; }
}

[CollectionDefinition("e2e")] public sealed class E2ECollection : ICollectionFixture<E2EFixture> { }

[Collection("e2e")]
public abstract class BrowserTestBase(E2EFixture fixture)
{
    protected Task RunAsync(Func<IPage, Task> body, [CallerMemberName] string test = "");
}
```

`E2EFixture : IAsyncLifetime` creates `IPlaywright`, launches Chromium (`Headless = !Headed`, `SlowMo`), runs the preflight and exposes `Browser`, `BaseUrl`, admin credentials; `DisposeAsync` closes everything.

## Failure artifacts

`<ResultsDir>/<Class>.<Method>.png` (full page), `.zip` (Playwright trace), `.txt` (final URL, console messages, failed requests, responses with status >= 500; no cookies, no bodies, no passwords). Created only when a test throws. `test-results/` is git-ignored.
