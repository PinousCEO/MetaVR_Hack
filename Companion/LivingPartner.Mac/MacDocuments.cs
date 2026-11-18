using System.Text.RegularExpressions;
using LivingPartner.Host;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    string DocumentRoot => Environment.GetEnvironmentVariable("LP_MAC_DOCUMENTS") is {Length: > 0} root
        ? Path.GetFullPath(root) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bambi");
    PointerSample lastSpot;
    double spotAt;
    readonly Dictionary<string,string> pendingDocument = new();
    readonly Dictionary<string,DocItem> finishedDocuments = new();

    bool TryWorkSpoken(string words,out string reply)
    {
        string ask = NameRules.Strip(words).Trim();
        if (TryExportModel(ask, out reply)) return true;
        var meant = documents.Meant();
        var action = DocsRules.Plain(ask,meant != null, documents.Open != null,meant?.kind == DocsRules.Designs);
        bool create = Regex.IsMatch(ask,@"만들|만드|그려|그리|뽑아|제작|생성|\b(make|create|draw|generate|design)\b",RegexOptions.IgnoreCase);
        bool png = Regex.IsMatch(ask,@"png|피엔지|이미지|그림|\b(image|picture)\b",RegexOptions.IgnoreCase);
        if (action == null && create && png) action = new DocAsk {action = DocsRules.MakeDesigns,arg = ask};
        if (action == null && create && Regex.IsMatch(ask,"pdf|피디에프|보고서",RegexOptions.IgnoreCase))
            action = new DocAsk { action = DocsRules.MakeReport,arg = DocsRules.Topic(ask) };
        if (action == null) return TryCreateSpoken(words,out reply);
        switch (action.action)
        {
            case DocsRules.MakeReport:
            case DocsRules.MakeDesigns:
                bool report = action.action == DocsRules.MakeReport;
                bool pointed = !report && Regex.IsMatch(ask,@"(이|저|그)\s*(아이콘|로고|디자인|그림)|\b(this|that)\s+(design|logo|icon|image)\b",RegexOptions.IgnoreCase);
                var spot = lastSpot;
                if (pointed && (spot == null || Now - spotAt > 8 || spot.monitorId != screen?.id))
                { reply = "바꿀 아이콘을 TV에서 가리키면서 다시 말해 줘."; return true; }
                int count = png && !pointed && !Regex.IsMatch(ask,@"시안|여러|options|variants",RegexOptions.IgnoreCase) ? 1 : DocsRules.DesignOptions;
                string kind = report ? DocsRules.Report : DocsRules.Designs;
                string title = report ? action.arg : TruncateTitle(ask);
                reply = QueueWork(kind,title,ask,(job, ct) => MakeDocument(job,report,count,pointed ? spot : null,ct));
                return true;
            case DocsRules.ShowDoc: reply = documents.OpenDoc() ? "문서를 열었어." : "아직 열 수 있는 문서가 없어."; return true;
            case DocsRules.CloseDoc: documents.Close(); reply = "문서를 닫았어."; return true;
            case DocsRules.DeliverDoc: reply = DeliverDocument(meant?.id); return true;
            case DocsRules.DocPage: reply = documents.Page(action.arg,action.n) is int n && n > 0 ? n + "페이지야." : "문서를 먼저 열어 줘."; return true;
            case DocsRules.PickOption: reply = documents.Pick(meant?.id,action.n) != null ? action.n + "번을 골랐어." : "그 번호의 시안이 없어."; return true;
            default: reply = ""; return false;
        }
    }

    static string TruncateTitle(string s) => s.Length <= 44 ? s : s[..43] + "…";

    async Task<Action> MakeDocument(WorkJob job,bool report,int count, PointerSample spot,CancellationToken ct)
    {
        var model = Model ?? throw new InvalidOperationException("Mac에서 생성하려면 OpenAI 키를 설정해 줘.");
        string root = DocumentRoot;
        Directory.CreateDirectory(root);
        string temp = Path.Combine(root,".bambi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        lock (Sync) pendingDocument.Add(job.id,temp);
        var options = new List<DocOption>();
        var files = new List<string>();
        int pages;
        string title = job.title;
        DateTime now = DateTime.Now;
        if (report)
        {
            WorkStep(job,"자료 조사 중",.10f);
            var research = await model.WebResearchAsync(DocsRules.ResearchSystem,DocsRules.ResearchUser(job.title,job.asked,now),ct);
            var sources = research.sources.Select(s => new ReportSource {title = s.title,url = s.url}).ToList();
            bool web = research.searches > 0 && sources.Count != 0;
            WorkStep(job,"보고서 작성 중",.54f);
            bool ko = job.asked.Any(c => c is >= '가' and <= '힣');
            var json = await model.AskJsonAsync(DocsRules.WriterSystem,DocsRules.WriterUser(title,job.asked,ko,research.text,sources,now),
                Array.Empty<(string,byte[],string)>(),DocsRules.ReportSchema(),8000,ct);
            var data = ReportData.From(json,title,ko,web,sources,now);
            if (!data.Usable) throw new InvalidOperationException("보고서 본문을 받지 못했어.");
            title = data.title;
            string pdf = DocsRules.SafeTitle(title) + ".pdf";
            WorkStep(job,"PDF 페이지 만드는 중",.86f);
            pages = MacDocumentPages.Report(data,temp,Path.Combine(temp,pdf));
            files.Add(pdf);
        }
        else
        {
            var pngs = new List<string>();
            string purpose = job.asked;
            if (spot != null)
            {
                WorkStep(job,"가리킨 아이콘 확인 중",.08f);
                var crop = await Displays.CaptureSpotAsync(spot.monitorId,spot.u,spot.v,ct);
                var brief = await model.AskJsonAsync(DocsRules.DesignSystem,
                    "The only image is a close-up centred on the user's pointer. Describe the closest distinct icon or logo's function and four original replacement ideas in the supplied schema. Set found to false if none. Request: " + job.asked,
                    new[]{("image/png",crop,"pointer.png")},DocsRules.DesignSchema(),3000,ct);
                if ((bool?)brief["found"] != true) throw new InvalidOperationException("가리킨 곳에서 아이콘을 찾지 못했어.");
                purpose = DocsRules.Debrand((string)brief["purpose"] ?? job.asked);
                title = (string)brief["title"] ?? title;
            }
            for (int i=0;i<count;i++)
            {
                WorkStep(job,$"이미지 {i+1}/{count} 그리는 중",.14f + .67f*i/count);
                var style = DocsRules.Styles[i];
                string preview = $"option-{i+1}.png";
                string name = count == 1 ? DocsRules.SafeTitle(title) + ".png" : preview;
                string prompt = count == 1 ? job.asked : DocsRules.DrawPrompt(title,purpose,style,"",true);
                byte[] image = await model.GenerateImageAsync(prompt,"1024x1024",count > 1,ct);
                string imagePath = Path.Combine(temp,name);
                await File.WriteAllBytesAsync(imagePath,image,ct);
                MacDocumentPages.Thumbnail(imagePath,Path.Combine(temp,$"option-{i+1}-s.png"));
                pngs.Add(imagePath); files.Add(name);
                options.Add(new DocOption {name=count == 1 ? title : DocsRules.OptionName(i+1,style.label),picture=preview});
            }
            pages = 0;
            if (count > 1)
            {
                WorkStep(job,"미리보기 만드는 중",.89f);
                string pdf = DocsRules.SafeTitle(title) + ".pdf";
                pages = MacDocumentPages.Designs(title,pngs,temp,Path.Combine(temp,pdf));
                files.Insert(0,pdf);
            }
        }
        ct.ThrowIfCancellationRequested();
        return () =>
        {
            string folder = Path.Combine(root,DocsRules.FreeFolder(now,title,n => Path.Exists(Path.Combine(root,n))));
            Directory.Move(temp,folder);
            pendingDocument.Remove(job.id);
            string id = DocsRules.NewId(job.kind);
            var doc = documents.Begin(id,job.kind,title,job.asked,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),"",out _);
            documents.Ready(id,title,pages,options,files.Select(n => new DocFile { name=n,kind=Path.GetExtension(n)[1..],path=Path.Combine(folder,n) }),folder);
            if (!report && count == 1) documents.Pick(id,1);
            finishedDocuments[id] = doc;
            job.title = title; job.resultKind = "doc"; job.resultId = id;
        };
    }

    string DeliverDocument(string id)
    {
        var doc = documents.Meant(id);
        var file = DocsRules.Carried(doc).file;
        if (file == null || !File.Exists(file.path)) return "아직 보낼 파일이 없어.";
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) return "Mac 바탕화면을 찾지 못했어.";
        try
        {
            string n = DocsRules.FreeFile(Path.GetFileNameWithoutExtension(file.name),Path.GetExtension(file.name),s => Path.Exists(Path.Combine(desktop,s)));
            File.Copy(file.path,Path.Combine(desktop,n),false);
            documents.Delivered(doc.id); return n + " 파일을 바탕화면에 뒀어.";
        }
        catch (IOException) { return "바탕화면에 복사하지 못했어. 대기줄에서 파일을 열 수 있어."; }
        catch (UnauthorizedAccessException) { return "바탕화면 쓰기 권한이 필요해. 대기줄에서 파일을 열 수 있어."; }
    }
}
