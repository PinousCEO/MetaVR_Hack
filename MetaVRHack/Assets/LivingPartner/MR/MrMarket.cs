using System;
using System.Collections;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Networking;

namespace LivingPartner.MR
{
    /// <summary>
    /// The marketplace in the room, and what comes out of it.
    ///  - THE SHOP BOARD: the catalogue the PC keeps (the same one its web page shows) as a sheet of paper in front of
    ///    the user - tabs, eight cards a page, the picked card large beside them with what the partner gains, and one
    ///    button. Getting something sends a shooting star to the partner; the thing itself (a thing, a skill or a
    ///    game pack - all of them in its bag) arrives with the next snapshot.
    ///  - GAME PACKS: a game is data (GamePackDef) on one of the ways of playing the headset knows. A pack whose way
    ///    of playing is a GLASS GAME (survival, umbrella, wipe) is started by the PC - glass.command start - and is
    ///    played on the side of the glass the partner is on: on the desk (MrGlass) or inside the screen.
    ///  - THE SCREEN BOARD: a display of the PC brought into the room - its picture in a paper frame beside the desk,
    ///    flown out of the real screen, with the partner looking up at it.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Marketplace")]
        [SerializeField] MarketBoardView _shop;
        [SerializeField] ScreenBoardView _screenBoard;

        static readonly MarketView NoMarket = new MarketView();
        MarketView Market => _snap != null && _snap.market != null ? _snap.market : NoMarket;

        void SendMarket(string cmd, string id = "", string text = "", int price = 0) => _link.Send(Msg.Market, new MarketCommand { cmd = cmd, id = id, text = text, price = price });

        string HostUrl(string path)
        {
            string ws = _link.CurrentUrl ?? "";
            if (ws.Length == 0) return "";
            string http = ws.Replace("wss://", "https://").Replace("ws://", "http://");
            int cut = http.LastIndexOf("/ws", StringComparison.Ordinal);
            if (cut > 0) http = http.Substring(0, cut);
            return http + path;
        }

        // ------------------------------------------------------------------ the shop board
        static readonly string[] ShopTabKinds = { "", ModuleKinds.Item, ModuleKinds.Skill, ModuleKinds.Game, ModuleKinds.Ambience, ModuleKinds.Ability, "mine" };
        // selling: the price the user is setting for the picked thing of their own, and a purchase that waits for its second tap
        int _shopPrice = 10; string _shopPriceFor = "", _shopArmFor = ""; float _shopArmUntil;

        /// <summary>What the one button under the picked thing does just now.</summary>
        enum ShopDo { None, Remove, Install, Get, Buy, BuyArmed, Need, Offline, Sell, SellArmed, StopSelling, Have }
        // what the button meant, and for which thing, when that was last looked at (UpdateShop)
        ShopDo _shopActWas = ShopDo.None; string _shopActFor = "";

        /// <summary>The tab "Mine" is shown: the one place things are SOLD from (ShopPress, Shared/Market.cs).</summary>
        bool ShopSellTab => ShopTabKinds[Mathf.Clamp(_shopTab, 0, ShopTabKinds.Length - 1)] == "mine";

        ShopDo ShopWhat(MarketListing d, MarketView m)
        {
            if (d == null) return ShopDo.None;
            // The user's own: a press NEVER sells what is not in the bag - it goes back into the bag (2026-11-18: "I pressed
            // Fish Fishing - it was already there - and it said it PUT IT UP"); selling is the tab "Mine" only (ShopPress).
            if (d.mine)
            {
                string press = ShopPress.What(d, m.stars, m.online, ShopSellTab);
                if (press == ShopPress.Install) return ShopDo.Install;
                if (press == ShopPress.Have) return ShopDo.Have;
                return press == ShopPress.StopSelling ? ShopDo.StopSelling : _shopArmFor == d.id && Time.time < _shopArmUntil ? ShopDo.SellArmed : ShopDo.Sell;      // putting up for sale takes two taps, like paying
            }
            if (d.installed) return ShopDo.Remove;
            if (d.owned) return ShopDo.Install;
            if (d.price <= 0) return ShopDo.Get;
            if (d.listed && !m.online) return ShopDo.Offline;
            if (d.price > m.stars) return ShopDo.Need;
            return _shopArmFor == d.id && Time.time < _shopArmUntil ? ShopDo.BuyArmed : ShopDo.Buy;      // paying takes two taps
        }

        void ShopPriceStep(int step)
        {
            var d = _shopPicked;
            if (!ShopPress.SellControls(d, ShopSellTab)) return;
            _shopPrice = Mathf.Clamp(_shopPrice + step, 0, 995); _shopShown = "";
            if (d.listed) SendMarket("setPrice", d.id, "", _shopPrice);      // already on sale: the new price counts at once
        }
        static readonly Color Paper = new Color(1.00f, 0.985f, 0.945f, 0.98f), PaperDeep = new Color(0.985f, 0.945f, 0.875f, 0.98f);
        static readonly Color HoneyPaper = new Color(1.00f, 0.90f, 0.62f, 0.98f), MintPaper = new Color(0.86f, 0.95f, 0.84f, 0.98f), GreyPaper = new Color(0.88f, 0.86f, 0.82f, 0.98f);
        bool _shopOpen; string _shopHostOpen = "", _shopPick = "", _shopShown = "", _shopNoticeSeen = "";
        int _shopTab, _shopPage; float _shopNoticeUntil; Vector3 _shopAnchor;
        readonly List<MarketListing> _shopList = new List<MarketListing>();
        readonly MarketListing[] _shopCard = new MarketListing[8];
        MarketListing _shopPicked;

        void InitMarket()
        {
            var v = _shop;
            for (int i = 0; i < v.tabs.Length; i++) { int k = i; v.tabs[i].press = () => { _shopTab = k; _shopPage = 0; _shopPick = ""; _shopShown = ""; }; }
            for (int i = 0; i < v.cards.Length; i++) { int k = i; v.cards[i].press = () => { if (_shopCard[k] != null) { _shopPick = _shopCard[k].id; _shopShown = ""; } }; }
            v.prev.press = () => { _shopPage--; _shopPick = ""; _shopShown = ""; };
            v.next.press = () => { _shopPage++; _shopPick = ""; _shopShown = ""; };
            v.close.press = CloseShop;
            v.action.press = ShopAct;
            v.minus.press = () => ShopPriceStep(-5);
            v.plus.press = () => ShopPriceStep(+5);
            v.panel.gameObject.SetActive(false);
            _screenBoard.close.press = () => SendMarket("screen", "", "close");
            _screenBoard.panel.gameObject.SetActive(false);
        }

        void OpenShop(bool tell)
        {
            if (!_shopOpen) { _shopOpen = true; _shopAnchor = Vector3.zero; _shopShown = ""; Debug.Log("[MR] market: the shop board is open"); }
            if (tell) SendMarket("open", "", "mr");
        }

        void CloseShop()
        {
            if (_shopOpen) Debug.Log("[MR] market: the shop board is closed");
            _shopOpen = false;
            SendMarket("open", "", "");
        }

        void OnSnapshotMarket(StateSnapshot s)
        {
            // asked for by voice ("상점 열어줘"): the PC says the board should be open - or closed again
            string open = s.market != null ? s.market.open ?? "" : "";
            if (open == _shopHostOpen) return;
            _shopHostOpen = open;
            if (open == "mr") OpenShop(false); else _shopOpen = false;
        }

        Texture2D ShopIcon(MarketListing l, out bool own)
        {
            var v = _shop;
            var t = Icon(l.icon);
            own = t != null;
            if (own) return t;
            switch (l.kind)
            {
                case ModuleKinds.Game: return l.template == SwarmRules.Template ? v.texDemonGate : l.template == "keeper" ? v.texKeeper : l.template == "tennis" ? v.texTennis : l.template == "rps" ? v.texRps : l.template == "pop" ? v.texPop : l.template == "redlight" ? v.texRedLight : l.template == "survival" ? v.texSurvival : v.texGame;
                case ModuleKinds.Skill: return l.id.IndexOf("dance", StringComparison.OrdinalIgnoreCase) >= 0 ? v.texDance : l.id.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0 ? v.texMusic : v.texSkill;
                case ModuleKinds.Ambience: return v.texAmbience;
                case ModuleKinds.Ability: return v.texAbility;
                default: return v.texItem;
            }
        }

        /// <summary>Writes s into the label, cut short (with an ellipsis) until it is no wider than maxWidth.</summary>
        static void FitInto(StoryLabel l, string s, float maxWidth)
        {
            s = s ?? "";
            // Measured, not tried out: the label's mesh is built once, for the text that fits. (It was built anew for
            // every character taken away.) Measure looks at each candidate by itself - with the font that candidate
            // will be drawn in - so the result is what trying it out gave.
            string fit = s;
            for (int n = s.Length - 1; n > 1 && l.Measure(fit) > maxWidth; n--) fit = s.Substring(0, n).TrimEnd() + "…";
            l.text = fit;
        }

        static string KindName(MarketListing l) => l.kind == ModuleKinds.Game ? "Game pack" : l.kind == ModuleKinds.Skill ? "Skill" : l.kind == ModuleKinds.Ambience ? "Surroundings" : l.kind == ModuleKinds.Ability ? "Ability" : "Furniture";
        static Color KindColor(string kind) => kind == ModuleKinds.Game ? new Color(0.79f, 0.92f, 0.71f) : kind == ModuleKinds.Skill ? new Color(0.86f, 0.80f, 0.97f) : kind == ModuleKinds.Ambience ? new Color(0.75f, 0.89f, 0.98f) : kind == ModuleKinds.Ability ? new Color(0.74f, 0.92f, 0.89f) : new Color(1f, 0.89f, 0.72f);

        void UpdateShop(float dt)
        {
            var v = _shop; var p = v.panel;
            if (!_shopOpen || _calibrating) { p.Hide(); return; }
            var m = Market;
            float t = Time.time;
            // which cards, and which of them is the picked one
            string kind = ShopTabKinds[Mathf.Clamp(_shopTab, 0, ShopTabKinds.Length - 1)];
            _shopList.Clear();
            foreach (var l in m.list) if (kind == "mine" ? l.mine : kind.Length == 0 || l.kind == kind) _shopList.Add(l);
            int pages = Mathf.Max(1, (_shopList.Count + v.cards.Length - 1) / v.cards.Length);
            _shopPage = (_shopPage % pages + pages) % pages;
            _shopPicked = null;
            for (int i = 0; i < v.cards.Length; i++)
            {
                int idx = _shopPage * v.cards.Length + i;
                _shopCard[i] = idx < _shopList.Count ? _shopList[idx] : null;
                if (_shopCard[i] != null && _shopCard[i].id == _shopPick) _shopPicked = _shopCard[i];
            }
            if (_shopPicked == null && _shopCard[0] != null) { _shopPicked = _shopCard[0]; _shopPick = _shopPicked.id; }
            // The one button means something else from one moment to the next (Get -> Remove when the thing has arrived,
            // Sell -> Stop selling, Buy -> "Tap again to buy"), and so does it when another card is picked. Whenever it
            // changes it takes no press for 1.2 s: a finger still in it, or already on its way, pressed the new meaning.
            {
                var act = ShopWhat(_shopPicked, m); string actFor = _shopPicked != null ? _shopPicked.id : "";
                if (act != _shopActWas || actFor != _shopActFor) { _shopActWas = act; _shopActFor = actFor; v.action.cool = Mathf.Max(v.action.cool, 1.2f); }
            }

            // the words, when something changed
            if (_shopPicked != null && _shopPicked.mine && _shopPriceFor != _shopPicked.id) { _shopPriceFor = _shopPicked.id; _shopPrice = _shopPicked.listed ? _shopPicked.price : 10; }
            string key = m.revision + "/" + m.stars + "/" + _shopTab + "/" + _shopPage + "/" + _shopPick + "/" + _shopList.Count + "/" + _shopPrice + "/" + (Time.time < _shopArmUntil ? _shopArmFor : "") + "/" + m.online + "/" + m.nickname;
            if (key != _shopShown)
            {
                _shopShown = key;
                v.stars.text = m.stars.ToString();
                FitInto(v.nickname, m.nickname ?? "", 0.19f);
                v.pageLabel.text = (_shopPage + 1) + " / " + pages;
                for (int i = 0; i < v.cards.Length; i++)
                {
                    var c = v.cards[i]; var l = _shopCard[i];
                    if (c.gameObject.activeSelf != (l != null)) c.gameObject.SetActive(l != null);
                    if (l == null) continue;
                    FitInto(c.label, l.name, 0.082f);
                    v.cardPrice[i].text = l.mine ? (l.listed ? "On sale  " + (l.price <= 0 ? "free" : l.price.ToString()) : "Not on sale") : l.installed ? "Yours" : l.owned ? "Owned" : l.price <= 0 ? "Free" : l.price + " stars";
                    v.cardPrice[i].color = l.mine ? new Color(0.62f, 0.40f, 0.10f) : l.installed || l.owned ? new Color(0.25f, 0.50f, 0.30f) : Ui.Pal.TextDim;
                    bool picked = l == _shopPicked;
                    c.color = picked ? HoneyPaper : l.installed ? MintPaper : Paper;
                    c.color2 = picked ? new Color(1f, 0.84f, 0.50f, 0.98f) : l.installed ? new Color(0.80f, 0.92f, 0.79f, 0.98f) : PaperDeep;
                    v.cardDisc[i].SetColors(KindColor(l.kind), KindColor(l.kind));
                }
                for (int i = 0; i < v.tabs.Length; i++)
                {
                    bool on = i == _shopTab;
                    v.tabs[i].color = on ? HoneyPaper : Paper; v.tabs[i].color2 = on ? new Color(1f, 0.84f, 0.50f, 0.98f) : PaperDeep;
                }
                bool more = pages > 1;
                if (v.prev.gameObject.activeSelf != more) { v.prev.gameObject.SetActive(more); v.next.gameObject.SetActive(more); }
                var d = _shopPicked;
                bool any = d != null;
                if (v.action.gameObject.activeSelf != any) v.action.gameObject.SetActive(any);
                bool selling = ShopPress.SellControls(d, ShopSellTab);      // (the price stepper: only where things are sold from - the tab "Mine")
                if (v.minus.gameObject.activeSelf != selling) { v.minus.gameObject.SetActive(selling); v.plus.gameObject.SetActive(selling); }
                v.priceLabel.text = selling ? (_shopPrice <= 0 ? "Free" : _shopPrice + " stars") : "";
                v.detailIcon.enabled = any;
                if (!any)
                {
                    v.detailName.text = _snap == null || m.list.Count == 0 ? "The shop is empty" : "Nothing here yet";
                    v.detailBy.text = "";
                    v.detailText.text = m.list.Count == 0 ? "The PC app has no catalogue to show.\nIs it up to date?" : "Make one with words:\n\"Bambi, make a ... and put it\nin the shop\"";
                    v.detailDisc.enabled = false;
                }
                else
                {
                    FitInto(v.detailName, d.name, 0.166f);
                    FitInto(v.detailBy, KindName(d) + "  -  by " + (string.IsNullOrEmpty(d.seller) ? d.author : d.seller) + (d.installs > 0 ? "  -  " + d.installs + " got" : ""), 0.166f);
                    var b = v.detailText;
                    b.text = d.gives + " " + d.summary;                      // the whole text once: the label settles on the font that can draw it
                    var sb = new System.Text.StringBuilder();
                    WrapInto(b, sb, d.gives, 0.164f, selling ? 2 : 3, "");       // (the price stepper takes the room of two lines)
                    int givesLines = sb.ToString().Split('\n').Length - 1;
                    if (!selling && !string.IsNullOrEmpty(d.summary) && d.summary != d.gives && givesLines <= 2) WrapInto(b, sb, d.summary, 0.164f, 4 - givesLines, "");
                    b.text = sb.ToString().TrimEnd('\n');
                    var what = ShopWhat(d, m);
                    Color grey = new Color(0.82f, 0.80f, 0.76f), hue = Ui.Pal.Mint;
                    switch (what)
                    {
                        case ShopDo.Remove: v.action.label.text = "Remove"; hue = Ui.Pal.Pink; break;
                        case ShopDo.Install: v.action.label.text = "Install"; break;
                        case ShopDo.Get: v.action.label.text = "Get  -  Free"; break;
                        case ShopDo.Buy: v.action.label.text = "Buy  -  " + d.price + " stars"; break;
                        case ShopDo.BuyArmed: v.action.label.text = "Tap again to buy"; hue = Ui.Pal.Orange; break;
                        case ShopDo.Need: v.action.label.text = "Need " + (d.price - m.stars) + " more"; hue = grey; break;
                        case ShopDo.Offline: v.action.label.text = "Shop is offline"; hue = grey; break;
                        case ShopDo.Sell: v.action.label.text = _shopPrice <= 0 ? "Put up for free" : "Sell for " + _shopPrice + " stars"; break;
                        case ShopDo.SellArmed: v.action.label.text = "Tap again to sell for " + (_shopPrice <= 0 ? "free" : _shopPrice.ToString()); hue = Ui.Pal.Orange; break;
                        case ShopDo.Have: v.action.label.text = "In your bag"; hue = grey; break;
                        default: v.action.label.text = "Stop selling"; hue = Ui.Pal.Pink; break;
                    }
                    v.action.color = Color.Lerp(hue, Color.white, 0.18f); v.action.color2 = hue * 0.96f; v.action.color.a = v.action.color2.a = 0.98f;
                }
            }
            // the pictures (they arrive from the PC one by one)
            for (int i = 0; i < v.cards.Length; i++)
            {
                var l = _shopCard[i];
                if (l == null) continue;
                var c = v.cards[i];
                c.art.SetTexture(ShopIcon(l, out bool own));
                c.art.transform.localScale = Vector3.one * (own ? 0.056f : 0.078f);      // a badge picture has its own edge and shadow round it
                if (v.cardDisc[i].enabled != own) v.cardDisc[i].enabled = own;
                float cycle = Mathf.Repeat(t * 0.3f - i * 0.37f, 3.2f);
                c.art.SetLook(M.Smooth(c.hover) * 0.6f, l == _shopPicked && cycle < 1f ? cycle : -1f);
            }
            if (_shopPicked != null)
            {
                v.detailIcon.SetTexture(ShopIcon(_shopPicked, out bool own));
                v.detailIcon.transform.localScale = Vector3.one * (own ? 0.082f : 0.112f) * (1f + 0.025f * Mathf.Sin(t * 2.2f));
                v.detailIcon.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.3f) * 4f);
                if (v.detailDisc.enabled != own) v.detailDisc.enabled = own;
                if (own) v.detailDisc.SetColors(KindColor(_shopPicked.kind), KindColor(_shopPicked.kind));
            }
            // what just happened, for a few seconds
            string note = m.notice ?? "";
            if (note != _shopNoticeSeen)
            {
                _shopNoticeSeen = note;
                if (note.Length > 0) { _shopNoticeUntil = t + 4.5f; _sparks.Burst(v.detailIcon.transform.position, p.transform.right, Vector3.up, 0.07f, 16); }
            }
            string shownNote = t < _shopNoticeUntil ? Shorten(note, 44) : "";
            if (v.notice.text != shownNote) v.notice.text = shownNote;
            v.starIcon.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.7f) * 10f);

            p.Show();
            // in front of the user, where it was opened (it does not follow the head)
            if (_shopAnchor == Vector3.zero || Vector3.Distance(_shopAnchor, Input.HeadPos) > 1.3f)
            {
                Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                _shopAnchor = Input.Simulated ? Input.HeadPos + Input.HeadRot * new Vector3(0f, 0.01f, 0.80f) : Input.HeadPos + fwd * 0.56f + Vector3.down * 0.07f;
            }
            FaceUser(p.transform, _shopAnchor);
            UpdateButtons(p, dt);
        }

        void ShopAct()
        {
            var d = _shopPicked;
            if (d == null) return;
            switch (ShopWhat(d, Market))
            {
                case ShopDo.Remove: SendMarket("remove", d.id); break;
                case ShopDo.Install:
                case ShopDo.Get:
                    SendMarket("install", d.id);
                    MenuComet(_shop.detailIcon.transform.position);
                    Debug.Log($"[MR] market: getting '{d.name}' ({d.kind}, free or owned)");
                    break;
                case ShopDo.Buy: _shopArmFor = d.id; _shopArmUntil = Time.time + 3.5f; _shopShown = ""; break;      // the second tap pays
                case ShopDo.BuyArmed:
                    _shopArmUntil = 0f; _shopShown = "";
                    SendMarket("buy", d.id);
                    Sfx2D("coin", 0.7f);
                    MenuComet(_shop.detailIcon.transform.position);
                    Debug.Log($"[MR] market: buying '{d.name}' ({d.kind}, {d.price} stars) from {(string.IsNullOrEmpty(d.seller) ? d.author : d.seller)}");
                    break;
                case ShopDo.Need: Caption("Not enough stars for that one yet.", 3f); Sfx2D("error", 0.5f); break;
                case ShopDo.Offline: Caption("The shop cannot be reached just now.", 3f); Sfx2D("error", 0.5f); break;
                case ShopDo.Sell: _shopArmFor = d.id; _shopArmUntil = Time.time + 3.5f; _shopShown = ""; break;      // the second tap puts it up
                case ShopDo.SellArmed:
                    _shopArmUntil = 0f; _shopShown = "";
                    SendMarket("publish", d.id, ShopPress.SellMark, _shopPrice);      // (the mark: this is the explicit Sell control - the PC sells nothing from a headset without it)
                    Sfx2D("coin", 0.7f);
                    _sparks.Burst(_shop.detailIcon.transform.position, _shop.panel.transform.right, Vector3.up, 0.07f, 14);
                    Debug.Log($"[MR] market: putting '{d.name}' up for sale at {_shopPrice} stars");
                    break;
                case ShopDo.StopSelling: SendMarket("unpublish", d.id); break;
                case ShopDo.Have: Caption("It is in your bag already. To sell it, open the Mine tab.", 3.5f); break;
            }
        }

        bool OnDebugMarket(DebugCommand d)
        {
            // (captures and checks from the PC: lpctl debug shop 1, shopTab 3, shopPick 2, shopAct, shopPage 1)
            if (d.cmd == "shop") { if (d.value > 0.5f) OpenShop(true); else CloseShop(); return true; }
            if (d.cmd == "shopTab") { _shopTab = Mathf.Clamp((int)d.value, 0, ShopTabKinds.Length - 1); _shopPage = 0; _shopPick = ""; _shopShown = ""; return true; }
            if (d.cmd == "shopPage") { _shopPage = (int)d.value; _shopPick = ""; _shopShown = ""; return true; }
            if (d.cmd == "shopPick") { var l = _shopCard[Mathf.Clamp((int)d.value, 0, _shopCard.Length - 1)]; if (l != null) { _shopPick = l.id; _shopShown = ""; } return true; }
            if (d.cmd == "shopAct") { ShopAct(); return true; }
            if (d.cmd == "shopPrice") { ShopPriceStep((int)d.value); return true; }
            return false;
        }

        // ------------------------------------------------------------------ the screen board
        string _scrMon = ""; int _scrRev = -1; Texture2D _scrTex; bool _scrFetching, _scrHave; float _scrShownAt, _scrRetryAt; Vector3 _scrAnchor, _scrFrom; string _scrCaption = "\u0001";

        IEnumerator FetchUrl(string url, Action<byte[]> done)
        {
            if (url.Length == 0) { done(null); yield break; }
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log($"[MR] could not fetch {url}: {req.error}"); done(null); }
                else done(req.downloadHandler.data);
            }
        }

        /// <summary>
        /// A picture (of the PC, or a file) as a texture: read and decoded away from the main thread and handed to the
        /// graphics card - it cannot be read back afterwards. (Texture2D.LoadImage decodes on the main thread: a display
        /// brought over stuttered at every new picture, a report at every page.) mips: with its smaller copies.
        /// done(null, 0) when it could not be fetched or is not a picture; otherwise the texture is the caller's to destroy.
        /// </summary>
        IEnumerator FetchTexture(string url, bool mips, Action<Texture2D, int> done)
        {
            if (url.Length == 0) { done(null, 0); yield break; }
            var how = DownloadedTextureParams.Default; how.readable = false; how.mipmapChain = mips;
            using (var req = UnityWebRequestTexture.GetTexture(url, how))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                Texture2D tex = null;
                if (req.result != UnityWebRequest.Result.Success) Debug.Log($"[MR] could not fetch {url}: {req.error}");
                else
                {
                    try { tex = DownloadHandlerTexture.GetContent(req); }
                    catch (Exception e) { Debug.Log($"[MR] {url} is not a picture: {e.Message}"); }
                }
                done(tex, tex != null ? (int)(req.downloadedBytes / 1024) : 0);
            }
        }

        void UpdateScreenBoard(float dt)
        {
            var sb = _snap != null ? _snap.screen : null;
            var v = _screenBoard; var p = v.panel;
            bool on = sb != null && !string.IsNullOrEmpty(sb.monitorId) && !_calibrating;
            if (!on) { p.Hide(); _scrMon = ""; _scrRev = -1; return; }
            float t = Time.time;
            if (sb.monitorId != _scrMon)
            {
                // a display is brought over: the board flies out of the real screen to a place beside the desk
                _scrMon = sb.monitorId; _scrRev = -1; _scrHave = false; _scrShownAt = t; _scrCaption = "\u0001";
                Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                _scrAnchor = Input.Simulated ? Input.HeadPos + Input.HeadRot * new Vector3(-0.02f, 0.03f, 0.74f) : Input.HeadPos + fwd * 0.52f - right * 0.30f + Vector3.down * 0.08f;
                var mv = Mon(sb.monitorId);
                _scrFrom = mv != null ? mv.Origin : _scrAnchor;
                v.picture.SetTexture(v.placeholder);
                _sparks.Burst(_scrFrom, right, Vector3.up, 0.10f, 18);
                if (_ch.owner == Env.Mr)
                {
                    _emoteForced = Emote.Think; _emoteT = 3.5f;
                    if (_mode == Mode.Free && !_motor.HasTarget) _motor.Face(YawTo(_scrAnchor - _pos));
                }
                Debug.Log($"[MR] screen board: display {sb.monitorId} brought into the room");
            }
            if (sb.revision != _scrRev && !_scrFetching && t >= _scrRetryAt)
            {
                int rev = sb.revision; string mon = _scrMon;
                _scrFetching = true;
                // (decoded away from the main thread; the picture that is showing stays until the new one is ready, then it is destroyed)
                StartCoroutine(FetchTexture(HostUrl("/screen/" + rev + ".jpg"), false, (tex, kb) =>
                {
                    _scrFetching = false;
                    if (tex == null) { _scrRetryAt = Time.time + 2f; return; }
                    if (mon != _scrMon) { Destroy(tex); return; }
                    _scrRev = rev;
                    tex.name = "Screen board"; tex.wrapMode = TextureWrapMode.Clamp;      // picture data, not a scene object
                    var old = _scrTex; _scrTex = tex;
                    if (!_scrHave) { _scrHave = true; Debug.Log($"[MR] screen board: picture {rev} arrived ({_scrTex.width}x{_scrTex.height}, {kb} kB)"); }
                    v.picture.SetTexture(_scrTex); v.picture.Apply();
                    if (old != null) Destroy(old);
                }));
            }
            // the frame follows the picture's shape
            float aspect = _scrHave && _scrTex.height > 0 ? Mathf.Clamp(_scrTex.width / (float)_scrTex.height, 0.5f, 3.6f) : 16f / 9f;
            float w = 0.46f, h = w / aspect;
            if (aspect < 1f) { h = 0.40f; w = h * aspect; }                // a display standing on end
            string cap = sb.caption ?? "";
            if (cap != _scrCaption)
            {
                _scrCaption = cap;
                var c = v.caption;
                c.text = cap;
                var sbd = new System.Text.StringBuilder();
                WrapInto(c, sbd, cap, Mathf.Max(0.22f, w - 0.03f), 3, "");
                c.text = sbd.ToString().TrimEnd('\n');
                v.captionBg.enabled = cap.Length > 0;      // no line under the picture until the partner has something to say about it
            }
            float capH = cap.Length > 0 ? v.caption.Height + 0.016f : 0f, total = h + 0.028f + capH + (cap.Length > 0 ? 0.006f : 0f);
            p.width = w + 0.028f; p.height = total;
            Ui.SizePanel(p.bg, p.width, total, 0.018f, 0.0018f);
            p.glow.transform.localScale = new Vector3(p.width * 1.2f, total * 1.28f, 1f);
            float top = total * 0.5f;
            v.picture.transform.localPosition = new Vector3(0f, top - 0.014f - h * 0.5f, -0.003f);
            v.picture.transform.localScale = _scrHave ? new Vector3(w, h, 1f) : Vector3.one * 0.12f;
            v.caption.transform.localPosition = new Vector3(0f, -top + 0.006f + capH * 0.5f, -0.004f);
            Ui.SizePanel(v.captionBg, w, capH, 0.008f, 0.0010f);
            v.captionBg.transform.localPosition = new Vector3(0f, -top + 0.006f + capH * 0.5f, -0.002f);
            v.close.home = new Vector3(p.width * 0.5f - 0.012f, top - 0.004f, -0.006f); v.close.homeSet = true;
            p.Show();
            float u = Mathf.Clamp01((t - _scrShownAt) / 0.7f), e = u * u * (3f - 2f * u);
            Vector3 at = Vector3.Lerp(_scrFrom, _scrAnchor, e) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.05f + Mathf.Sin(t * 1.1f) * 0.003f);
            FaceUser(p.transform, at);
            UpdateButtons(p, dt);
        }

        // ------------------------------------------------------------------ game packs
        GamePackDef _pack;      // the pack being played (null: a game started by its plain name)

        /// <summary>The badge that carries the game's own name, for the four the partner came with (null for any other pack).</summary>
        Texture2D PlainTile(GamePackDef g)
        {
            if (g.template == SwarmRules.Template) return _menu.texDemonGate;
            if (g.template == "tennis") return _menu.texTennis;
            if (g.template == "keeper") return _menu.texKeeper;
            return null;
        }

        static string GameOf(string template) => template == "rps" || template == "pop" || template == "redlight" ? template : "";

        bool PackRunning(GamePackDef g)
        {
            if (g.template == "keeper") return (_tn.active && _tn.mode == "keeper") || (_tnPendingAt >= 0f && _tnMode == "keeper");
            if (g.template == "tennis") return (_tn.active && _tn.mode != "keeper") || (_tnPendingAt >= 0f && _tnMode != "keeper");
            if (g.template == SwarmRules.Template) return _swOn;
            // a game at the glass runs when the PC says so - on the desk or inside the screen
            if (GlassGames.Is(g.template)) { var gl = GlassNow; return gl.game.Length > 0 && gl.packId == g.id; }
            if (GameOf(g.template).Length == 0) return false;      // a way of playing this app does not know is never running
            return _game.Length > 0 && _game == GameOf(g.template) && (_pack == null || _pack.id == g.id);
        }

        GamePackDef FindPack(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var games = World.games;
            if (games == null) return null;
            foreach (var g in games) if (string.Equals(g.id, name, StringComparison.OrdinalIgnoreCase)) return g;
            foreach (var g in games) if (string.Equals(g.title, name, StringComparison.OrdinalIgnoreCase)) return g;
            foreach (var g in games) if (!string.IsNullOrEmpty(g.title) && (g.title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(g.title, StringComparison.OrdinalIgnoreCase) >= 0)) return g;
            return null;
        }

        /// <summary>The installed pack that is the plain game of this name (so that "play rock paper scissors" uses the pack's settings), or null.</summary>
        GamePackDef PackOfTemplate(string template)
        {
            var games = World.games;
            if (games == null) return null;
            foreach (var g in games) if (g.template == template && PlainTile(g) != null) return g;
            return null;
        }

        void StartPack(GamePackDef g, string how)
        {
            if (g == null) return;
            if (g.template == "gomoku") { RequestGomoku(); return; }
            if (g.template == "keeper") { StopGames("a ball game"); StartTennis(how, "keeper"); return; }
            if (g.template == "tennis") { StopGames("a ball game"); StartTennis(how, ""); return; }
            if (g.template == SwarmRules.Template) { StartSwarm(how); return; }      // Screen Scamps: the headset's own, with or without the PC (MrSwarm)
            // a game at the glass: the PC starts it, on the side of the glass the partner is on (it works inside the screen too)
            if (GlassGames.Is(g.template)) { GlassAsk(g.id, how); return; }
            // a way of playing this app does not know (a pack made for a newer one): say so. (It used to start tennis.)
            if (GameOf(g.template).Length == 0) { Caption("That game needs a newer app.", 4f); Sfx2D("error", 0.5f); return; }
            StartLocal(g.template, how, g);
        }

        void TogglePack(GamePackDef g)
        {
            if (g == null) return;
            if (g.template == "tennis") { ToggleTennis("the bag"); return; }
            if (PackRunning(g)) StopGames("the menu"); else StartPack(g, "the menu");
        }
    }
}
