# AlphaQuantum.ShadowAITools

Find the AI applications your users already reach, using logs you already keep. This .NET 8 library reads an export from a DNS server, proxy or firewall, pulls out the hostnames, and returns the ones that belong to AI products, each with its category and the vendor's stance on training. It is the code-level companion to [shadow AI discovery tools for IT and compliance](https://www.shadowaitools.com).

```bash
dotnet add package AlphaQuantum.ShadowAITools
```

## The whole workflow in a console app

```csharp
using AlphaQuantum.ShadowAITools;

var client = new ShadowAIToolsClient(Environment.GetEnvironmentVariable("AQ_API_KEY")!);
var findings = await client.ScanAsync(@"C:\exports\proxy-week38.csv");

foreach (var f in findings.Where(f => f["blocked"].GetBoolean()))
    Console.WriteLine($"{f["domain"].GetString(),-35} {f["primary_category"].GetString()}");
```

`ScanAsync` returns `IReadOnlyList<Dictionary<string, JsonElement>>`, one entry per unique host. To look up a single host, use `CheckAsync(host)`, which returns one dictionary of the same shape.

## How the file is read

`ScanAsync` reads every line and splits it on spaces, tabs, commas and semicolons. Each piece is treated as a URL, with `https://` added when there is no scheme. If it parses and its host contains a dot, the host goes into a case-insensitive set. Each distinct host is then checked once, one after another.

That approach works directly on:

- proxy access logs (Squid, most secure web gateway exports)
- firewall traffic CSVs with a destination hostname column
- Pi-hole and dnsmasq query logs
- a plain text file with one domain per line

## Windows DNS Server logs need one extra step

Windows DNS debug logging writes names in wire format, for example `(7)copilot(9)microsoft(3)com(0)`. Tokens like that do not parse as hostnames, so convert them first. PowerShell handles it in one line:

```powershell
Get-Content dns.log |
  Select-String '\(\d+\)[\w-]+(\(\d+\)[\w-]+)*\(0\)' -AllMatches |
  ForEach-Object { $_.Matches.Value -replace '^\(\d+\)','' -replace '\(\d+\)','.' -replace '\.$','' } |
  Sort-Object -Unique | Set-Content hosts.txt
```

Then run `ScanAsync("hosts.txt")`. The same tip applies to any log that stores names in an unusual form: normalise to plain hostnames, then scan.

## What each finding contains

For AI tools, `blocked` is `true` and the entry names the main category (`primary_category`), the AI type (`ai_type`) and every category that applies (`categories`). The vendor's position on training appears as `trains_on_data`, with `opt_out_available`, `enterprise_no_training` and `api_no_training` alongside, and `terms_checked` shows when someone last read the terms. Ordinary sites return `blocked: false` and nothing more to act on.

Treat `unstated` carefully. It means the vendor's terms say nothing on that point. For a compliance review, that is usually a reason to ask the vendor, not a reason to relax.

## Producing a report people will read

Security teams want one line per tool, not per host, grouped by risk:

```csharp
var report = findings
    .Where(f => f["blocked"].GetBoolean())
    .GroupBy(f => f.TryGetValue("matched_domain", out var m) ? m.GetString() : f["domain"].GetString())
    .Select(g => new
    {
        Tool = g.Key,
        Category = g.First()["primary_category"].GetString(),
        Trains = g.First()["trains_on_data"].GetString(),
        Hosts = g.Count()
    })
    .OrderBy(x => x.Trains == "no")
    .ThenBy(x => x.Tool);
```

Write it out with `System.Text.Json` for a dashboard, or with any CSV library for Excel.

## Running it every week

A scheduled task, or a `BackgroundService` in a worker host, can scan the newest export each week and store the result. Comparing this week's set of tools with last week's shows what is new:

```csharp
var added = thisWeek.Except(lastWeek, StringComparer.OrdinalIgnoreCase);
```

New tools are where policy conversations start, and catching them early keeps those conversations small.

## Performance and quota

Lookups run one at a time, so load stays even and each unique host costs exactly one call. A week of proxy logs from a few hundred users usually contains a few thousand unique hosts. To save quota on repeat runs, remember hosts you have already checked and scan only new ones. `CheckAsync` in your own loop makes that easy.

## When a lookup fails

`ScanAsync` does not catch errors. The first failing call ends the scan with an exception and no partial list. For big files, loop over hosts yourself and decide per error:

- **`ApiException`** with `StatusCode` 429: pause, then continue.
- **`ApiException`** with 401 or 403: stop, because the key or quota needs attention.
- **`TaskCanceledException`**: a timeout. Log it and move on.

File problems raise the usual `IOException` family. An empty key or host raises `ArgumentException`.

## Data protection

The library sends hostnames only. Client IPs, usernames and timestamps never leave your machine, because they are never part of a lookup. Join findings back to the original log locally to see which users or departments used each tool. Keep that joined file under the same access rules as the log itself.

## Why an inventory matters

The EU AI Act expects organisations to know which AI systems they deploy. ISO/IEC 42001 asks for an inventory within an AI management system. NIST's AI Risk Management Framework starts with mapping AI use. A dated scan result is a practical first piece of evidence for all three.

## Data sources

Findings come from the AI tool register and feed straight into an [AI AUP](https://www.aitoolsblocklist.com/ai-acceptable-use-policy.php). Everything the scan clears as non-AI can still be labelled from the [IAB categories list](https://www.urlcategorizationdatabase.com/taxonomy.php). If some of what you find is your own automation, an [AI agent allow list for enterprise copilots](https://www.aiagentallowlist.com/enterprise.php) is the next step.

Prefer another stack? There is [shadowaitools-go for command-line tools](https://pkg.go.dev/github.com/explainableaixai/shadowaitools-go) and [a Python release for notebooks](https://pypi.org/project/shadowaitools/).

## License

MIT
