using AlphaQuantum.ShadowAITools;var client=new ShadowAIToolsClient(Environment.GetEnvironmentVariable("AQ_API_KEY")!);Console.WriteLine(await client.ScanAsync("dns-export.csv"));
