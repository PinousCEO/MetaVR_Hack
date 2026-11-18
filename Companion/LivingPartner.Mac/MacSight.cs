using LivingPartner.Host;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    Peer photoPeer;
    string photoId;
    TaskCompletionSource<EyeFrame> pendingPhoto;

    public int PacketLimit(Peer peer)
    {
        if (!Authenticated(peer)) return 8192;
        return photoPeer == peer && pendingPhoto != null ? (SightRules.MaxPictureBytes + 2) / 3 * 4 + 8192 : 2 * 1024 * 1024;
    }

    async Task<byte[]> TakeObjectPhoto(string id, CancellationToken ct)
    {
        var pending = new TaskCompletionSource<EyeFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Sync)
        {
            ct.ThrowIfCancellationRequested();
            if (headset == null) throw new InvalidOperationException("The headset is disconnected.");
            photoPeer = headset; pendingPhoto = pending; photoId = id;
            Send(headset, Msg.EyeRequest, new EyeRequest { id = id, maxSide = 1280 });
        }
        try
        {
            EyeFrame frame;
            try { frame = await pending.Task.WaitAsync(TimeSpan.FromSeconds(SightRules.FrameSeconds), ct); }
            catch (TimeoutException) { throw new InvalidOperationException("No picture arrived. Check headset camera access."); }
            if (!frame.ok) throw new InvalidOperationException(frame.why == "denied" ? "Enable the headset camera to show me an object." : "The headset could not send a photo.");
            if (string.IsNullOrEmpty(frame.jpeg) || frame.jpeg.Length > (SightRules.MaxPictureBytes + 2) / 3 * 4 ||
                frame.width is < 1 or > 1280 || frame.height is < 1 or > 1280) throw new InvalidOperationException("Unable to read the image dimensions.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(frame.jpeg); }
            catch (FormatException) { throw new InvalidOperationException("Unable to read the camera image."); }
            if (bytes.Length is < 200 or > SightRules.MaxPictureBytes || bytes[0] != 0xff || bytes[1] != 0xd8 ||
                bytes[^2] != 0xff || bytes[^1] != 0xd9) throw new InvalidOperationException("Unable to read the JPEG.");
            return bytes;
        }
        finally
        {
            lock (Sync) if (pendingPhoto == pending) { photoId = null; photoPeer = null; pendingPhoto = null; }
        }
    }

    void GotPhoto(Peer peer, EyeFrame frame)
    {
        if (peer == photoPeer && frame != null && frame.id == photoId) pendingPhoto?.TrySetResult(frame);
    }

    void CameraDisconnected(Peer peer)
    {
        if (peer == photoPeer) pendingPhoto?.TrySetException(new InvalidOperationException("The headset disconnected during capture."));
    }

    bool TryExportModel(string words, out string reply)
    {
        reply = "";
        if (Matches(words, @"pdf|png|그림|문서|보고서|이미지|아이콘")) return false;
        bool file = Matches(words, @"glb|3\s*[d디]\s*(모델|오브젝트|물건)?\s*(파일|저장|내보)|\b3d (file|model)\b");
        if (!file && !Matches(words, @"맥북|맥으로|맥에|컴퓨터|바탕화면|데스크톱|pc|desktop|macbook|my mac")) return false;
        if (!Matches(words, @"저장|내보내|보내|옮겨|가져|넣어|\b(export|save|send|carry|put|copy|bring)\b")) return false;
        var made = bag.World.items.Where(i => IsCreationId(i.id, "item")).Reverse().ToArray();
        var named = made.FirstOrDefault(i => words.Contains(i.name, StringComparison.OrdinalIgnoreCase));
        if (!file && named == null && !Matches(words, @"방금\s*만든\s*(물건|모델|오브젝트)|(?:this|that|last) (object|shape|model)")) return false;
        var item = named ?? made.FirstOrDefault();
        if (item == null || GeneratedModelFile(item.id) is not {} src) { reply = "Make a 3D object first."; return true; }
        reply = QueueWork("task", item.name + " GLB export", words, (job, ct) =>
        {
            WorkStep(job, "Copying the verified GLB to Documents", .5f);
            return Task.FromResult<Action>(() =>
            {
                ct.ThrowIfCancellationRequested();
                string target = ExportToDocuments(item, src);
                job.resultKind = "file"; job.resultId = target; job.note = Path.GetRelativePath(DocumentRoot, target);
                Say(item.name + " saved to Documents/Yonder/Models.");
            });
        });
        return true;
    }

    string ExportToDocuments(ItemDef item, DocFile source)
    {
        string root = Path.Combine(DocumentRoot, "Models");
        Directory.CreateDirectory(root);
        string name = SightRules.FolderName(DateTime.Now, item.name, n => Path.Exists(Path.Combine(root, n)));
        string pending = Path.Combine(root, ".bambi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            File.Copy(source.path, Path.Combine(pending, source.name));
            Directory.Move(pending, Path.Combine(root, name));
        }
        finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
        return Path.Combine(root, name, source.name);
    }
}
