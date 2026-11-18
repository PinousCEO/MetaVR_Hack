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
            if (headset == null) throw new InvalidOperationException("헤드셋이 연결되지 않았어.");
            photoPeer = headset; pendingPhoto = pending; photoId = id;
            Send(headset, Msg.EyeRequest, new EyeRequest { id = id, maxSide = 1280 });
        }
        try
        {
            EyeFrame frame;
            try { frame = await pending.Task.WaitAsync(TimeSpan.FromSeconds(SightRules.FrameSeconds), ct); }
            catch (TimeoutException) { throw new InvalidOperationException("헤드셋에서 카메라 사진을 받지 못했어. 카메라 권한을 확인해 줘."); }
            if (!frame.ok) throw new InvalidOperationException(frame.why == "denied" ? "카메라 권한이 꺼져 있어 물건을 볼 수 없어." : "헤드셋 카메라가 물건 사진을 보내지 못했어.");
            if (string.IsNullOrEmpty(frame.jpeg) || frame.jpeg.Length > (SightRules.MaxPictureBytes + 2) / 3 * 4 ||
                frame.width is < 1 or > 1280 || frame.height is < 1 or > 1280) throw new InvalidOperationException("사진의 크기를 읽을 수 없어.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(frame.jpeg); }
            catch (FormatException) { throw new InvalidOperationException("카메라 사진을 읽을 수 없어."); }
            if (bytes.Length is < 200 or > SightRules.MaxPictureBytes || bytes[0] != 0xff || bytes[1] != 0xd8 ||
                bytes[^2] != 0xff || bytes[^1] != 0xd9) throw new InvalidOperationException("카메라의 JPEG 사진을 읽을 수 없어.");
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
        if (peer == photoPeer) pendingPhoto?.TrySetException(new InvalidOperationException("촬영 중에 헤드셋 연결이 끊겼어."));
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
        if (item == null || GeneratedModelFile(item.id) is not {} src) { reply = "먼저 가방에 3D 오브젝트를 만들어 줘."; return true; }
        reply = QueueWork("task", item.name + " GLB 내보내기", words, (job, ct) =>
        {
            WorkStep(job, "검증한 GLB를 Mac 문서 폴더로 복사", .5f);
            return Task.FromResult<Action>(() =>
            {
                ct.ThrowIfCancellationRequested();
                string target = ExportToDocuments(item, src);
                job.resultKind = "file"; job.resultId = target; job.note = Path.GetRelativePath(DocumentRoot, target);
                Say(item.name + " 3D 파일을 Mac 문서의 Bambi/Models에 저장했어.");
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
