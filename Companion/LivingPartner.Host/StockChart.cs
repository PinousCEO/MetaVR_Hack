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
        return symbols.Length == 0 ? new[] { "META" } : symbols;
    }

    public static GamePackDef Find(IEnumerable<GamePackDef> games, string words)
    {
        var requested = NamedSymbols(words);
        return games.LastOrDefault(pack => IsPack(pack) && requested.All(symbol =>
            pack.script.Split('\n', 2)[0][Marker.Length..].Split(',').Contains(symbol)));
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

    public static async Task<GamePackDef> MakeAsync(string words, CancellationToken ct)
        => Build(await Task.WhenAll(Symbols(words).Select(s => Download(s, ct))));

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
  if W>1.65 or #stocks==1 then text('DAILY CLOSE',W-.31,.90,.04,muted) end
  local s=stocks[stock]; local d=s.days; local n=#d
  for i,count in ipairs(periods) do
    if button(count..'D', W/2+(i-2)*.25,.10,.22,.11,i==period and 'honey' or 'white') then period=i; picked=nil end
  end
  local first=math.max(1,n-periods[period]+1)
  local left,right,bottom,top=.11,W-.26,.29,.64
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
  local color=d[n][2]>=d[first][2] and leaf or rose
  text(string.format('$%.2f',d[ix][2]), W*.22,.751,.093,ink)
  text(string.format('%+.2f%%',delta), W*.52,.756,.059,delta>=0 and leaf or rose)
  text(d[ix][1], W*.80,.753,.048,muted)
  for j=0,2 do
    local v=lo+(hi-lo)*j/2
    line(left,y(v),right,y(v),.002,'brown',.23)
    text(string.format('%.2f',v),W-.13,y(v),.035,muted)
  end
  for i=first+1,n do line(x(i-1),y(d[i-1][2]),x(i),y(d[i][2]),.009,color) end
  line(x(ix),bottom,x(ix),y(d[ix][2]),.003,ink,.48)
  disc(x(ix),y(d[ix][2]),.014,color)
  text(string.sub(d[first][1],6),left+.07,.245,.037,muted)
  text('Yahoo Finance  /  USD close',W*.49,.214,.036,muted)
  text(string.sub(d[n][1],6),right-.05,.245,.037,muted)
end
""";
}
