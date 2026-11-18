using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // THE MARKETPLACE. Everything the partner can be given is a MODULE: a piece of furniture it learns to use, a skill, a
    // tune with its surroundings, an ability, a whole game. A module is data (what World.cs describes) plus a listing -
    // name, picture, author, price. Installing one is the moment the partner GAINS something: the thing appears in its
    // bag, the skill among its skills, the game on its Play page. What the user makes with words can be published as a
    // module; what came with the partner (tennis, dancing ...) is recorded as modules too, already installed.
    // The same catalogue is shown on the PC (a web page the host serves) and in the headset (a shop board).

    public static class ModuleKinds
    {
        public const string Item = "item", Skill = "skill", Game = "game", Ambience = "ambience", Ability = "ability";
        public static readonly string[] All = { Item, Skill, Game, Ambience, Ability };
    }

    public static class StarterGames
    {
        public const string TennisId = "game-tennis", KeeperId = "game-keeper", GomokuId = "game-gomoku";
        public static readonly string[] RetiredIds = { "game-dustbunnies", "game-wipe", "game-umbrella", "game-vampire", "game-rps", "game-pop", "game-redlight", "game-catch", "game-pairs", "game-focus", "game-tag", "game-tv-rally", "game-shooter", "game-calculator", "game-omok" };
        public static bool Retired(string id) => Array.IndexOf(RetiredIds, id) >= 0;
        public static bool IsStarter(string id) => id == TennisId || id == KeeperId || id == SwarmRules.PackId || id == GomokuId;
        public static GamePackDef Tennis() => new GamePackDef { id = TennisId, template = "tennis", title = "Tennis", howTo = "Rally with Bambi by returning the ball with either hand." };
        public static GamePackDef Keeper() => new GamePackDef { id = KeeperId, template = "keeper", title = "Goalkeeper", howTo = "Face the goal and save five shots with your hands." };
        public static GamePackDef Gomoku() => new GamePackDef { id = GomokuId, template = "gomoku", title = "오목", howTo = "두 사람이 TV의 같은 바둑판을 보고 번갈아 돌을 놓아요." };
        public static GamePackDef Defense() => new GamePackDef { id = SwarmRules.PackId, template = SwarmRules.Template, title = SwarmRules.Title, howTo = "Raise your left palm to guard. Aim your right finger to cast. Choose a charm each level." };
    }

    /// <summary>
    /// A game as a pack. template says which way of playing it is - the engine for each template is in the headset -
    /// and the rest dresses and tunes it, so that a new game can be made with words.
    ///   tennis | rps | pop | redlight   the four ways of playing that come with the partner
    ///   survival                        creatures close in on the partner from all sides; the user's fingertip
    ///                                   knocks them back, gems are picked up, the clock runs ("vampire survivors")
    ///   umbrella                        ink rain falls on the partner while it gathers stars; the user's hand is the
    ///                                   umbrella that shelters it (the gold drops are let through)
    /// survival and umbrella are GLASS GAMES (Glass.cs): played inside the PC screen when the partner is in the PC (the
    /// user's hands enter as shadows), on the desk otherwise. On the old template path a game made with words is one of
    /// survival | umbrella | pop; a pack carries title, howTo, enemyName, pickupName, speed, spawn, lives, seconds.
    /// (There is no "wipe" and no "board" way of playing: the wipe game is gone, and no board game is shipped - a game
    /// of another kind is a SCRIPT the partner writes, see `script` below. `rule` / `skill` are unused.)
    /// </summary>
    public sealed class GamePackDef
    {
        public string id = "";
        public string template = "pop";
        public string title = "";
        public string howTo = "";           // one line shown when it starts
        public string enemyName = "", pickupName = "";
        public string enemyIcon = "", pickupIcon = "";   // PNG files under /world/ ("" = the template's own picture)
        public string icon = "";            // the pack's own picture in the bag: a sticker in the same style as every thing's (PNG under /world/, "" = none yet)
        public float seconds = 45f;         // how long a round lasts
        public float speed = 1f;            // how fast things move (1 = the template's own pace)
        public float spawn = 1f;            // how many things appear (1 = the template's own rate)
        public int lives = 3;
        // template "board": which game (omok = five in a row on a 9 x 9 board | tictactoe = three in a row on 3 x 3 | connect4 = four in a row, 7 x 6, the stones fall),
        // and how well the partner plays (1 = a beginner .. 3 = it tries hard)
        public string rule = "";
        public int skill = 2;
        // template "script": the game (or toy, or little tool) IS this small Lua program, written by the partner when the user
        // asked for it. It runs in a sandbox inside the app (GlassScript): it draws shapes, pictures and words, reads the
        // user's hands, moves the partner, keeps the score. art = the pictures drawn for it (PNG files under /world/).
        public string script = "";
        public List<PackArt> art = new List<PackArt>();
        public AmbienceDef ambience;        // the surroundings while it is played (null = none)
        public SongDef song;                // the tune while it is played (null = none)
    }

    /// <summary>A picture that belongs to a scripted pack: the script draws it as "pack:" + name.</summary>
    public sealed class PackArt { public string name = ""; public string file = ""; }

    /// <summary>
    /// Something the partner knows how to do with words alone: instructions that join its own (a calculator that
    /// shows its working, a translator, a brainstorm partner ...). No code: the language model follows them.
    /// </summary>
    public sealed class AbilityDef
    {
        public string id = "";
        public string name = "";
        public string prompt = "";          // the instructions, in English, written to the partner ("When the user asks ... you ...")
        public string[] examples = new string[0];   // things the user might say
    }

    /// <summary>A module: its listing in the marketplace and what installing it gives. Exactly one payload is set (by kind).</summary>
    // (Named MarketModule, not ModuleDef: Modules.cs already has a ModuleDef in this namespace - the timer - and two
    // classes of one name do not compile. This one never travels to a client under its type name, so nothing else changes.)
    public sealed class MarketModule
    {
        public string id = "";
        public string kind = ModuleKinds.Item;
        public string name = "";
        public string summary = "";         // one or two lines for the shop
        public string gives = "";           // one line: what the partner gains ("Bambi can sit down - and study, when there is a desk")
        public string author = "";          // "Bambi Studio" for what came with the partner, "You" for what the user made
        public string icon = "";            // PNG file under /world/ ("" = a picture by kind)
        public int price;                   // in stars, the shop's own play money (0 = free)
        public string version = "1.0";
        public string[] tags = new string[0];
        public bool builtin;                // came with the partner
        public long publishedUnixMs;
        public int installs;
        public ItemDef item;
        public SkillDef skill;
        public AmbienceDef ambience;
        public SongDef song;                // for an ambience module that has a tune of its own
        public GamePackDef game;
        public AbilityDef ability;
    }

    /// <summary>One line of the catalogue, as the clients show it (the payloads stay on the PC).</summary>
    public sealed class MarketListing
    {
        public string id = "", kind = "", name = "", summary = "", gives = "", author = "", icon = "";
        public int price;
        public bool installed, mine, builtin;
        public string template = "";        // games: which way of playing
        // THE REAL MARKETPLACE (the hub: a server that many people's PCs talk to)
        public string seller = "";          // the nickname of who sells it ("Bambi Studio" for what the makers of the partner sell)
        public bool listed;                 // it is on sale on the hub (false: something of the user's that is not on sale yet - it can be put up)
        public bool owned;                  // the user has bought (or got) it on the hub, whether or not it is installed just now
        public int installs, likes;         // how many people got it, how many liked it
        public bool liked;                  // by this user
    }

    /// <summary>The marketplace as every client sees it, sent with each snapshot.</summary>
    public sealed class MarketView
    {
        public int revision;
        public int stars;                   // the user's balance
        public List<MarketListing> list = new List<MarketListing>();
        public string open = "";            // "" | "mr": the shop board is open in the headset (asked for by voice or from the menu)
        public string notice = "";          // one line about what just happened ("Installed: Chair"), shown once
        // the user on the hub
        public string nickname = "";        // "" = no nickname chosen yet: nothing can be sold or bought until there is one
        public bool online;                 // the hub can be reached just now
        public string hub = "";             // where the hub is (its address), for the page that shows it
        public int sales, earned;           // how many times the user's things were bought, and the stars that brought in
        // "I made it! Shall I put it up in the marketplace?": right after the partner has finished making something it
        // offers to publish it. offerId = that thing (an entry of list with mine = true, listed = false), offerPrice = the
        // price the partner suggests, offerSeq goes up with every new offer. "" = nothing is on offer.
        public string offerId = "";
        public int offerPrice, offerSeq;
    }

    /// <summary>From a client (the headset's shop board, the web page, lpctl): what to do in the marketplace.</summary>
    public sealed class MarketCommand
    {
        // install (id) | remove (id) | publish (id of an item or skill of the user's, price) | unpublish (id) | open (text: "mr" | "pc" | "") | make (text: a module described in words; kind may be given in id)
        // screen (text: "1" | "2" | "3" | "this" | "close", optionally followed by " analyze") | song (text)
        // buy (id: pays the seller on the hub and installs) | setPrice (id, price) | like (id) | nickname (text) |
        // export (id: writes the module as a .bambi file) | import (text: the path of a .bambi file)
        // offer (id, text "yes" | "no", price): the answer to the partner's "shall I put it up for sale?" - yes publishes
        //   it at that price, no keeps it; either way the offer is over
        // hub (text: the address of another hub, "http://host:47620"; "" = this PC's own again)
        public string cmd = "";
        public string id = "";
        public string text = "";
        public int price;
    }

    /// <summary>
    /// What the ONE button under the picked card of the shop board does (the headset draws it; the tests hold it to this).
    /// The user (2026-11-18): "I pressed Fish Fishing in the marketplace - it was already there - and it said it PUT IT UP
    /// in the marketplace." Until then every card of the user's own had Sell as its only button, on every tab. Now:
    ///   the user's own, not in the bag   Install - it goes back into the bag, on every tab; a press never sells it
    ///   the user's own, in the bag       Have ("In your bag") - except on the tab "Mine", the one place things are sold
    ///                                    from: Sell (two taps, with the price stepper) / StopSelling
    /// A `publish` the board sends carries SellMark in MarketCommand.text: the host takes a publish from a headset
    /// without it for a plain press (it installs what is not in the bag and sells nothing).
    /// </summary>
    public static class ShopPress
    {
        public const string None = "", Remove = "remove", Install = "install", Get = "get", Buy = "buy", Need = "need", Offline = "offline",
            Sell = "sell", StopSelling = "stopSelling", Have = "have";
        /// <summary>MarketCommand.text of a publish that comes from the board's explicit Sell control.</summary>
        public const string SellMark = "sell";

        /// <summary>d: the picked card; stars, online: the user's balance and whether the hub can be reached; sellTab: the tab "Mine" is shown.</summary>
        public static string What(MarketListing d, int stars, bool online, bool sellTab)
        {
            if (d == null) return None;
            if (d.mine)
            {
                if (!d.installed) return Install;
                if (!sellTab) return Have;
                return d.listed ? StopSelling : Sell;
            }
            if (d.installed) return Remove;
            if (d.owned) return Install;
            if (d.price <= 0) return Get;
            if (d.listed && !online) return Offline;
            if (d.price > stars) return Need;
            return Buy;
        }

        /// <summary>The price stepper and the sell button are shown for this card (the user's own, in the bag, on the tab "Mine").</summary>
        public static bool SellControls(MarketListing d, bool sellTab) => d != null && d.mine && d.installed && sellTab;
    }

    /// <summary>The screen of the PC brought into the room: which display is shown on the board the partner looks at.</summary>
    public sealed class ScreenBoard
    {
        public string monitorId = "";       // "" = none
        public int revision;                // changes when a new picture is worth fetching (GET /screen/<revision>.jpg)
        public string caption = "";         // what is on it, once the partner has looked
    }
}
