using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using LivingPartner.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingPartner.Host;

/// <summary>The stock-chart tool packs dated daily closes into an ordinary portable Glass program.</summary>
public static class StockChart
{
    static readonly HttpClient Feed = new() { Timeout = TimeSpan.FromSeconds(12) };
    static readonly (string ticker, string alias)[] Stocks =
    {
        ("META", @"\b(meta|facebook)\b|메타|페이스북"), ("AAPL", @"\b(apple|aapl)\b|애플"),
        ("MSFT", @"\b(microsoft|msft)\b|마이크로소프트"), ("NVDA", @"\b(nvidia|nvda)\b|엔비디아"),
        ("GOOGL", @"\b(google|googl|alphabet)\b|구글"), ("TSLA", @"\b(tesla|tsla)\b|테슬라"),
    };
    const string Topic = @"\b(?:stock|share price|stock price)\s+(?:price\s+)?charts?\b|(?:주식|주가)\s*(?:차트|그래프)";
    const string Marker = "-- Yonder stock chart: ";
    public const string ReadyNote = "100% complete";
    const string OtherRequest = @"\b(don['’]t|do not|never|pdf|report|screenshot|logo|icon|image|document|png)\b|만들지\s*마|보고서|문서|이미지|스크린샷";
    public static bool IsChart(string words) => Regex.IsMatch(words ?? "", Topic, RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(words, OtherRequest, RegexOptions.IgnoreCase);
    public static bool CreateRequest(string words) => IsChart(words) &&
        Regex.IsMatch(words, @"\b(make|create|build|generate)\b|만들|제작", RegexOptions.IgnoreCase);
    public static bool OpenRequest(string words) => IsChart(words) && Regex.IsMatch(words, @"^(?:please\s+)?(?:open|show|launch)\b|열어|보여", RegexOptions.IgnoreCase);
    public static bool IsPack(GamePackDef pack) => pack?.script?.StartsWith(Marker, StringComparison.Ordinal) == true;
    public sealed record Day(string date, decimal close);
    public sealed record Series(string ticker, List<Day> days);

    static string[] NamedSymbols(string words)
    {
        var found = Stocks.Select(s => (s.ticker, match: Regex.Match(words, s.alias, RegexOptions.IgnoreCase)))
            .Where(s => s.match.Success).Select(s => (s.match.Index, symbol: s.ticker)).ToList();
        foreach (Match m in Regex.Matches(words, @"(?:\$|(?i:\bticker)\s+)([a-zA-Z]{1,5}(?:[.-][a-zA-Z])?)\b"))
            found.Add((m.Index, m.Groups[1].Value.ToUpperInvariant()));
        foreach (Match m in Regex.Matches(words, @"(?i:\b(?:for|and|of)\s+)([A-Z]{1,5}(?:[.-][A-Z])?)\b|\b([A-Z]{1,5}(?:[.-][A-Z])?)\s+(?i:stock chart)\b"))
        {
            string ticker = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!Stocks.Any(s => Regex.IsMatch(ticker, s.alias, RegexOptions.IgnoreCase)) &&
                ticker is not ("A" or "THE" or "MY" or "ME" or "STOCK" or "YOUR" or "THIS" or "THAT" or "OUR" or "PRICE" or "SHARE"))
                found.Add((m.Index, ticker));
        }
        return found.OrderBy(m => m.Index).Select(m => m.symbol).Distinct().ToArray();
    }

    internal static string[] Symbols(string words)
    {
        var symbols = NamedSymbols(words);
        if (symbols.Length > 3) throw new InvalidDataException("Choose up to three stocks for one chart.");
        if (symbols.Length == 0 && Regex.IsMatch(words, @"\b(for|of|about)\s+(?!me\b)\w",RegexOptions.IgnoreCase))
            throw new InvalidDataException("Say the ticker, for example: make a stock chart for ticker META.");
        return symbols.Length == 0 ? new[] { "META", "AAPL", "NVDA" } : symbols;
    }

    public static GamePackDef Find(IEnumerable<GamePackDef> games, string words)
    {
        var requested = NamedSymbols(words);
        return games.LastOrDefault(pack => IsPack(pack) && requested.All(symbol =>
            pack.script.Split('\n', 2)[0][Marker.Length..].Split(',').Contains(symbol)));
    }

    public static WorkJob InProgress(IEnumerable<WorkJob> jobs, string words)
    {
        var requested = NamedSymbols(words);
        return jobs.FirstOrDefault(job =>
        {
            if (job.kind != "tool" || job.status is not ("working" or "queued") || !IsChart(job.asked)) return false;
            try { return requested.Length == 0 || !requested.Except(Symbols(job.asked)).Any(); }
            catch (InvalidDataException) { return false; }
        });
    }

    internal static Series Read(string symbol, JObject body, DateTimeOffset now)
    {
        var data = body.SelectToken("chart.result[0]");
        var meta = data?["meta"];
        if (!string.Equals((string)meta?["symbol"], symbol, StringComparison.OrdinalIgnoreCase) || (string)meta?["currency"] != "USD" ||
            data?["timestamp"] is not JArray timestamps || data.SelectToken("indicators.quote[0].close") is not JArray closes)
            throw new InvalidDataException($"No USD closing prices available for {symbol}.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById((string)meta["exchangeTimezoneName"]);
        DateOnly LocalDay(long epoch) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(epoch), zone).DateTime);
        var today = LocalDay(now.ToUnixTimeSeconds());
        long? end = (long?)meta.SelectToken("currentTradingPeriod.regular.end");
        bool closed = end.HasValue && LocalDay(end.Value) == today && now.ToUnixTimeSeconds() >= end.Value;
        var sessions = new SortedDictionary<DateOnly,decimal>();
        for (int i = 0; i < Math.Min(timestamps.Count, closes.Count); i++)
        {
            if (timestamps[i].Type != JTokenType.Integer || closes[i].Type is not (JTokenType.Float or JTokenType.Integer)) continue;
            var day = LocalDay((long)timestamps[i]);
            decimal price = decimal.Round((decimal)closes[i], 2, MidpointRounding.AwayFromZero);
            if (price > 0 && (day < today || day == today && closed)) sessions[day] = price;
        }
        if (sessions.Count < 5) throw new InvalidDataException($"Not enough completed trading days for {symbol}.");
        return new(symbol, sessions.TakeLast(63).Select(d => new Day(d.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),d.Value)).ToList());
    }

    static async Task<Series> Download(string symbol, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=6mo&interval=1d");
        request.Headers.UserAgent.ParseAdd("Yonder/1.0");
        using var reply = await Feed.SendAsync(request, ct);
        if (!reply.IsSuccessStatusCode) throw new InvalidDataException($"Price history for {symbol} is unavailable. Try again later.");
        return Read(symbol, JObject.Parse(await reply.Content.ReadAsStringAsync(ct)), DateTimeOffset.UtcNow);
    }

    // Minimum screen time per milestone keeps the real work queue readable during a take.
    // A slow download or check counts toward its own milestone. Cancellation interrupts every hold.
    internal static async Task<GamePackDef> MakeAsync(string words, Action<string, float> step, CancellationToken ct,
        Func<string, CancellationToken, Task<Series>> load = null)
    {
        var symbols = Symbols(words);
        var elapsed = Stopwatch.StartNew();
        async Task ShowFor(int seconds)
        {
            var remaining = TimeSpan.FromSeconds(seconds) - elapsed.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
            ct.ThrowIfCancellationRequested();
            elapsed.Restart();
        }

        ct.ThrowIfCancellationRequested();
        step("Loading " + string.Join(" / ", symbols) + " daily closes", .08f);
        load ??= Download;
        var data = await Task.WhenAll(symbols.Select(symbol => load(symbol, ct)));
        await ShowFor(5);

        step("Building chart and time ranges", .35f);
        var pack = Build(data);
        await ShowFor(5);

        step("Testing chart controls", .65f);
        var check = GlassScriptCheck.Run(pack.script, pack);
        if (!check.ok || !check.drew) throw new InvalidDataException("The chart could not be verified.");
        await ShowFor(7);

        step("Chart checks passed", .95f);
        await ShowFor(5);
        return pack;
    }

    internal static GamePackDef Build(IReadOnlyList<Series> series)
    {
        // Only ticker, ISO date and numbers enter Lua, never the utterance or provider text.
        string data = string.Join(",\n",series.Select(s => "{symbol=" + JsonConvert.SerializeObject(s.ticker) +
            ",days={" + string.Join(",", s.days.Select(d => "{" + JsonConvert.SerializeObject(d.date) + "," +
            d.close.ToString(CultureInfo.InvariantCulture) + "}")) + "}}"));
        return new GamePackDef { title = string.Join(" + ", series.Select(s => s.ticker)) + " Stock Chart",
            template = GlassScript.TemplateName, howTo = "Choose a range or touch the line for a dated close. Data from Yahoo Finance, saved when made.",
            script = Marker + string.Join(",",series.Select(s => s.ticker)) + "\nlocal stocks = {" + data + "}\n" + View };
    }

    // Upgrade our original stock-chart layout without changing saved prices or custom Lua.
    public static void UpgradeSaved(IEnumerable<GamePackDef> games)
    {
        if (games == null) return;
        foreach (var pack in games.Where(IsPack))
        {
            var script = pack.script.Replace("\r\n", "\n");
            int view = script.IndexOf("\nlocal stock, period, picked", StringComparison.Ordinal);
            if (view < 0) continue;
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(script[(view+1)..]));
            if (Convert.ToHexString(hash) == "05AB4E4966A10D19ACA8FD3DF45BB1849CC9BA4AF1DECED3125C3ED8D16C4FD6")
                pack.script = script[..(view+1)] + View;
        }
    }

    const string View = """
local stock, period, picked = 1, 2, nil
local periods = {5, 21, 63}
local ink, muted, leaf, rose = '#392D35', '#716760', '#267965', '#A64454'
function start() pet_goto(.10) end
function update(dt)
  panel(W/2, .50, W-.04, .97, '', 'cream')
  local bw = math.min(.28,(W-.18)/math.max(3,#stocks))
  for i,s in ipairs(stocks) do
    if button(s.symbol, .09+(i-.5)*bw, .90, bw-.02, .105, i==stock and 'mint' or 'white') then stock=i; picked=nil end
  end
  if W>1.65 or #stocks==1 then text('Daily close',W-.29,.90,.052,muted) end
  local s=stocks[stock]; local d=s.days; local n=#d
  for i,count in ipairs(periods) do
    if button(count..'D', W/2+(i-2)*.25,.10,.22,.11,i==period and 'honey' or 'white') then period=i; picked=nil end
  end
  local first=math.max(1,n-periods[period]+1)
  local left,right,bottom,top=.11,W-.28,.285,.645
  for _,t in ipairs(taps()) do
    if t.x>=left and t.x<=right and t.y>=bottom and t.y<=top then
      picked=first+math.floor((t.x-left)/(right-left)*(n-first)+.5)
    end
  end
  local ix=picked or n
  local lo,hi=d[first][2],d[first][2]
  for i=first+1,n do lo=math.min(lo,d[i][2]); hi=math.max(hi,d[i][2]) end
  local pad=math.max((hi-lo)*.10,.01); lo=lo-pad; hi=hi+pad
  local function x(i) return left+(right-left)*(i-first)/(n-first) end
  local function y(v) return bottom+(top-bottom)*(v-lo)/(hi-lo) end
  local delta=(d[ix][2]/d[first][2]-1)*100
  local color=d[n][2]>=d[first][2] and '#BCE7AE' or '#FFC8A2'
  text(string.format('$%.2f',d[ix][2]), W*.21,.765,.104,ink)
  text(string.format('%+.2f%%',delta), W*.51,.765,.066,delta>=0 and leaf or rose)
  text(d[ix][1], W*.80,.765,.055,muted)
  rect(W/2,.46,W-.10,.446,'#264C47')
  for j=0,2 do
    local v=lo+(hi-lo)*j/2
    line(left,y(v),right,y(v),.003,'#DFEEDB',.35)
    text(string.format('%.2f',v),W-.165,y(v),.039,'#DFEEDB')
  end
  for i=first+1,n do line(x(i-1),y(d[i-1][2]),x(i),y(d[i][2]),.012,color) end
  line(x(ix),bottom,x(ix),y(d[ix][2]),.004,'#FFFFFF',.58)
  disc(x(ix),y(d[ix][2]),.023,'#FAF3DA')
  disc(x(ix),y(d[ix][2]),.012,'#477265')
  text(string.sub(d[first][1],6),left+.07,.206,.043,muted)
  text('Yahoo Finance',W*.5,.206,.039,muted)
  text(string.sub(d[n][1],6),W-.19,.206,.043,muted)
end
""";
}
