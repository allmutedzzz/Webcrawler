using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.CommandLine;
using HtmlAgilityPack;

class WebCrawler
{
    private static HashSet<string> visitedUrls = new HashSet<string>();
    private static HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static async Task Main(string[] args)
    {
        var rootCommand = new RootCommand("WebCrawler");

        var urlOption = new Option<string>(
            new[] { "-u", "--url" }, 
            description: "Начальный URL для загрузки"
        ) { IsRequired = true };

        var depthOption = new Option<int>(
            new[] { "-d", "--depth" }, 
            getDefaultValue: () => 2,
            description: "Глубина рекурсивного сканирования"
        );

        var outputOption = new Option<string>(
            new[] { "-o", "--output" }, 
            getDefaultValue: () => "downloads",
            description: "Директория для сохранения"
        );

        var maxPagesOption = new Option<int>(
            new[] { "-m", "--max-pages" }, 
            getDefaultValue: () => 10,
            description: "Максимальное количество страниц для загрузки"
        );

        rootCommand.AddOption(urlOption);
        rootCommand.AddOption(depthOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(maxPagesOption);

        rootCommand.SetHandler(async (url, depth, output, maxPages) => 
        {
            try 
            {
                Directory.CreateDirectory(Path.GetFullPath(output));

                visitedUrls.Clear();

                await CrawlWebsite(url, output, depth, maxPages);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }
        }, 
        urlOption, depthOption, outputOption, maxPagesOption);

        await rootCommand.InvokeAsync(args);
    }

    static async Task CrawlWebsite(string startUrl, string outputDir, int maxDepth, int maxPages)
    {
        var queue = new Queue<(string url, int depth)>();
        queue.Enqueue((startUrl, 0));

        while (queue.Count > 0 && visitedUrls.Count < maxPages)
        {
            var (currentUrl, currentDepth) = queue.Dequeue();

            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out _))
                continue;

            if (visitedUrls.Contains(currentUrl) || currentDepth > maxDepth)
                continue;

            try 
            {
                var pageContent = await DownloadPage(currentUrl, outputDir);

                if (!string.IsNullOrEmpty(pageContent))
                {
                    visitedUrls.Add(currentUrl);
                    Console.WriteLine($"Загружена страница: {currentUrl}");

                    if (currentDepth < maxDepth)
                    {
                        var links = ExtractLinks(pageContent, currentUrl);
                    
                        foreach (var link in links)
                        {
                            if (!visitedUrls.Contains(link))
                            {
                                queue.Enqueue((link, currentDepth + 1));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке {currentUrl}: {ex.Message}");
            }
        }

        Console.WriteLine($"Сканирование завершено. Загружено страниц: {visitedUrls.Count}");
    }

    static async Task<string> DownloadPage(string url, string outputDir)
    {
        try 
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            var response = await httpClient.GetStringAsync(url);
            
            var uri = new Uri(url);
            var fileName = SanitizeFileName(uri.Host + uri.AbsolutePath) + ".html";
            var fullPath = Path.Combine(outputDir, fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            await File.WriteAllTextAsync(fullPath, response);

            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки {url}: {ex.Message}");
            return string.Empty;
        }
    }

    static List<string> ExtractLinks(string pageContent, string baseUrl)
    {
        var links = new List<string>();

        try 
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(pageContent);

            var linkNodes = htmlDoc.DocumentNode.SelectNodes("//a[@href]");
            
            if (linkNodes != null)
            {
                foreach (var link in linkNodes)
                {
                    string href = link.GetAttributeValue("href", "");
                    
                    href = NormalizeUrl(href, baseUrl);

                    if (!string.IsNullOrEmpty(href) && 
                        href.StartsWith("http") && 
                        !visitedUrls.Contains(href))
                    {
                        links.Add(href);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка парсинга ссылок: {ex.Message}");
        }

        return links.Distinct().Take(10).ToList();
    }

    static string NormalizeUrl(string url, string baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;

        if (url.StartsWith("//"))
            return "https:" + url;

        if (url.StartsWith("/"))
        {
            var uri = new Uri(baseUrl);
            return $"{uri.Scheme}://{uri.Host}{url}";
        }

        if (!url.StartsWith("http"))
        {
            try 
            {
                url = new Uri(new Uri(baseUrl), url).AbsoluteUri;
            }
            catch { return null; }
        }

        return url;
    }

    static string SanitizeFileName(string fileName)
    {
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

        return Regex.Replace(fileName, invalidRegStr, "_")
                    .Replace(" ", "_")
                    .ToLower();
    }
}