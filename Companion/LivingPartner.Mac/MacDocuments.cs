using System.Text.RegularExpressions;
using LivingPartner.Host;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    string DocumentRoot => Environment.GetEnvironmentVariable("LP_MAC_DOCUMENTS") is {Length: > 0} root
        ? Path.GetFullPath(root) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nilo");
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
                { reply = "Point at the icon on the TV and ask again."; return true; }
                int count = png && !pointed && !Regex.IsMatch(ask,@"시안|여러|options|variants",RegexOptions.IgnoreCase) ? 1 : DocsRules.DesignOptions;
                string kind = report ? DocsRules.Report : DocsRules.Designs;
                string title = report ? action.arg : TruncateTitle(ask);
                reply = QueueWork(kind,title,ask,(job, ct) => MakeDocument(job,report,count,pointed ? spot : null,ct));
                return true;
            case DocsRules.ShowDoc: reply = documents.OpenDoc() ? "Document opened." : "There are no documents yet."; return true;
            case DocsRules.CloseDoc: documents.Close(); reply = "Document closed."; return true;
            case DocsRules.DeliverDoc: reply = DeliverDocument(meant?.id); return true;
            case DocsRules.DocPage: reply = documents.Page(action.arg,action.n) is int n && n > 0 ? n + " is the current page." : "Open a document first."; return true;
            case DocsRules.PickOption: reply = documents.Pick(meant?.id,action.n) != null ? action.n + " selected." : "That option is unavailable."; return true;
            default: reply = ""; return false;
        }
    }

    static string TruncateTitle(string s) => s.Length <= 44 ? s : s[..43] + "…";

    async Task<Action> MakeDocument(WorkJob job,bool report,int count, PointerSample spot,CancellationToken ct)
    {
        var model = Model ?? throw new InvalidOperationException("Set an OpenAI key on your Mac to create files.");
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
            WorkStep(job,"Researching",.10f);
            var research = await model.WebResearchAsync(DocsRules.ResearchSystem,DocsRules.ResearchUser(job.title,job.asked,now),ct);
            var sources = research.sources.Select(s => new ReportSource {title = s.title,url = s.url}).ToList();
            bool web = research.searches > 0 && sources.Count != 0;
            WorkStep(job,"Writing the report",.54f);
            bool ko = job.asked.Any(c => c is >= '가' and <= '힣');
            var json = await model.AskJsonAsync(DocsRules.WriterSystem,DocsRules.WriterUser(title,job.asked,ko,research.text,sources,now),
                Array.Empty<(string,byte[],string)>(),DocsRules.ReportSchema(),8000,ct);
            var data = ReportData.From(json,title,ko,web,sources,now);
            if (!data.Usable) throw new InvalidOperationException("The report came back empty.");
            title = data.title;
            string pdf = DocsRules.SafeTitle(title) + ".pdf";
            WorkStep(job,"Building PDF pages",.86f);
            pages = MacDocumentPages.Report(data,temp,Path.Combine(temp,pdf));
            files.Add(pdf);
        }
        else
        {
            var pngs = new List<string>();
            string purpose = job.asked;
            if (spot != null)
            {
                WorkStep(job,"Checking the selected icon",.08f);
                var crop = await Displays.CaptureSpotAsync(spot.monitorId,spot.u,spot.v,ct);
                var brief = await model.AskJsonAsync(DocsRules.DesignSystem,
                    "The only image is a close-up centred on the user's pointer. Describe the closest distinct icon or logo's function and four original replacement ideas in the supplied schema. Set found to false if none. Request: " + job.asked,
                    new[]{("image/png",crop,"pointer.png")},DocsRules.DesignSchema(),3000,ct);
                if ((bool?)brief["found"] != true) throw new InvalidOperationException("No icon was found at that spot.");
                purpose = DocsRules.Debrand((string)brief["purpose"] ?? job.asked);
                title = (string)brief["title"] ?? title;
            }
            for (int i=0;i<count;i++)
            {
                WorkStep(job,$"Drawing image {i+1}/{count}",.14f + .67f*i/count);
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
                WorkStep(job,"Preparing previews",.89f);
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
        if (file == null || !File.Exists(file.path)) return "There are no files to send yet.";
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop)) return "Unable to locate your Desktop.";
        try
        {
            string n = DocsRules.FreeFile(Path.GetFileNameWithoutExtension(file.name),Path.GetExtension(file.name),s => Path.Exists(Path.Combine(desktop,s)));
            File.Copy(file.path,Path.Combine(desktop,n),false);
            documents.Delivered(doc.id); return n + " is on your Desktop.";
        }
        catch (IOException) { return "Open the file from the queue. It could not be copied to your Desktop."; }
        catch (UnauthorizedAccessException) { return "Allow Desktop access, or open the file from the queue."; }
    }
}
