using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // GLASS GAMES: games that are played the same way on both sides of a display's glass.
    //   INSIDE  the partner is in the PC. The PC client draws the game on that monitor, and the user plays from the
    //           outside with their hands: the headset says where the hands are in front of the glass (GlassHand) and the
    //           PC draws them as SHADOWS inside the screen. The shadow is the cursor - the user sees where the PC thinks
    //           the hand is and steers by that, so a few centimetres of calibration error do not matter.
    //   OUTSIDE the partner is on the desk. The headset draws the same game on an upright "stage" standing on the desk
    //           through the partner, and the user's real hands are the input.
    // One simulation (GlassGame, pure C#, no engine types) serves both: it is stepped by the side that draws it.
    // The field is 2D: x to the right 0..W (W = the aspect, 16:9 -> 1.78), y up 0..1, the floor at y = 0.

    /// <summary>Headset -> host -> PC client, about 30 times a second while a hand is near a display or pointing at one.</summary>
    public sealed class GlassHand
    {
        public string monitorId = "";
        public double t;                        // host time
        public GlassHandPoint[] hands = { new GlassHandPoint(), new GlassHandPoint() };   // 0 = right, 1 = left
        // The spot the user is POINTING at from further away (the ray of the hand met this display): drawn by the PC as
        // a ring, so that what will be read / where the partner will go is seen on the real screen.
        public bool pointValid; public float pointU, pointV;
        // The hand that points: 0 = right, 1 = left, and whether its thumb and finger are closed just now - the TRIGGER of a
        // game that is aimed at from a distance (the PC takes the moment it closes as GlassInput.fire).
        public int pointHand; public bool pointPinch;
    }

    public sealed class GlassHandPoint
    {
        public bool valid;                      // the hand is tracked and is over this display, within 0.45 m of its glass
        public float u, v;                      // the middle of the palm, dropped straight onto the glass (0..1 from the top-left of the lit area; may be a little outside)
        public float dist;                      // metres in front of the glass
        public float spread = 1f;               // 0 = a fist, 1 = an open hand (how large its shadow is)
        public float roll;                      // degrees the hand is turned in the plane of the glass (0 = fingers up)
        public bool pinch;
        // What the hand SHOWS and how fast it moves, for games that read them (GlassInput.fingers / speed):
        public int fingers = -1;                // the stretched fingers as bits: 2 index, 4 middle, 8 ring, 16 little (1 = the thumb); -1 = not known
        public float speed;                     // metres a second of the palm, smoothed over about 0.15 s
        // seen: the hand is tracked at all. While a game is played inside this display a hand that is too far from the glass to
        // act (valid is false) is still reported, with u, v, dist, fingers and speed: such a game may ask what it shows.
        public bool seen;
    }

    /// <summary>Any client -> host.</summary>
    public sealed class GlassCommand
    {
        public string cmd = "";                 // start (game = a pack id, a title or a template) | stop | choose (pick = 0..2)
        public string game = "";
        public int pick;
        public string how = "";                 // who asked ("the bag", "voice" ...), for the log
    }

    /// <summary>
    /// Which glass game is on, and how it stands. The HOST decides game / packId / side / runId when a game is started
    /// (side "pc" when the partner is inside the PC and a PC client is connected, otherwise "mr") and sends it to all;
    /// the side that runs the simulation reports the rest about five times a second (and at once when the phase changes),
    /// and the host passes it on. Also carried in every snapshot.
    /// </summary>
    public sealed class GlassState
    {
        public string game = "";                // the template: survival | umbrella | wipe  ("" = no game)
        public string packId = "";
        public string title = "";
        public string side = "";                // pc | mr
        public string monitorId = "";           // side pc: the display it is played on
        public int runId;                       // goes up with every start
        public string phase = "";               // intro | play | choose | over
        public int score, lives, level, best;
        public float seconds;                   // played so far
        public string[] choices = new string[0];    // phase choose: the names of what is offered
        public string[] choiceNotes = new string[0];
        public string banner = "";              // one short line ("Level 3!", "Pick one")
        public string result = "";              // phase over: one line for the partner to say
    }

    /// <summary>
    /// Something the partner is visibly doing inside the PC, set by the host and drawn by the PC client (the headset
    /// may draw its share): the link between what was asked in the room and what happens on the computer.
    ///   look     it reads a part of a display: monitorId and the rectangle u, v, w, h (fractions, from the top-left)
    ///            are framed on the real screen while it reads
    ///   publish  it puts a thing up in the marketplace: a little market stall, the thing on the shelf, its price tag
    ///   deliver  a thing that was bought arrives: a parcel comes out of the screen
    ///   operate  it OPERATES the computer for the user (round 4): it walks to the spot u, v (fractions of monitorId) it is about
    ///            to click / type at; name = one short English line of the step ("Clicking the second video"); stage = click | type | scroll | look | done
    /// </summary>
    public sealed class ShowInfo
    {
        public string kind = "";                // "" | look | publish | deliver
        public int seq;                         // goes up with every show
        public string monitorId = "";
        public float u, v, w, h;
        public string name = "";                // the thing's name
        public string icon = "";                // its picture: PNG under /world/
        public int price;
        public double startedAt;                // host time
        public float seconds = 4f;              // how long it lasts
        public string stage = "";               // publish: going | stall | listed | back
    }

    // ------------------------------------------------------------------ THROUGH the glass
    // A game may send things OUT of its field toward the user - out of the monitor into the room when it is played
    // inside the PC (the partner shoots at the user from inside the screen), up from the desk stage otherwise. The side
    // that runs the game launches them (GlassGame.outs); the HEADSET flies them through the room toward the user's head
    // and decides what became of each: "blocked" (an open hand was in the way: the user's shield), "hit" (it reached the
    // user), "missed" (it flew past). The verdict goes back to the game (GlassGame.OutResult).
    //   glass.out          the running side -> host -> headsets, body GlassOut      (side mr: the headset does it by itself)
    //   glass.out.result   headset -> host -> the running side, body GlassOutResult
    public sealed class GlassOut
    {
        public int runId, id;                   // id: unique within the run
        public float x, y;                      // where on the field it leaves the glass (field units)
        public float speed = 1.2f;              // metres a second through the room
        public string art = "orb";              // a GlassArt name or "pack:<name>"
        public int color = -1;                  // 0xRRGGBB, -1 = as the picture is
        public float size = 0.06f;              // metres across in the room
    }
    public sealed class GlassOutResult { public int runId, id; public string what = ""; }      // blocked | hit | missed

    // ------------------------------------------------------------------ the simulation

    /// <summary>The user's hands on the field, as the side that draws the game sees them.</summary>
    public sealed class GlassInput
    {
        public readonly bool[] valid = new bool[2];
        public readonly float[] x = new float[2], y = new float[2];     // field units
        public readonly float[] dist = new float[2];                    // metres from the plane of the field (inside: from the glass)
        public readonly float[] spread = { 1f, 1f };
        // AIMING from a distance (a finger gun): the hand's pointing ray meets the field at (aimX, aimY); fire[i] is true in the
        // one frame in which that hand pinched (the trigger). Inside the PC this is where the pointing ring is on the screen.
        public readonly bool[] aimValid = new bool[2];
        public readonly float[] aimX = new float[2], aimY = new float[2];
        public readonly bool[] fire = new bool[2];
        // EVERY hand the cameras see, also one that is too far from the field to act (valid is false; x, y, dist are filled all
        // the same): a game may read what it SHOWS and how fast it MOVES - rock paper scissors, red light. A valid hand counts
        // as seen even when a renderer does not say so.
        public readonly bool[] seen = new bool[2];
        public readonly int[] fingers = { -1, -1 };                     // the stretched fingers as bits: 2 index, 4 middle, 8 ring, 16 little; -1 = not known (GlassGames.Shape)
        public readonly float[] speed = new float[2];                   // metres a second of the palm, smoothed over about 0.15 s (HandSpeed)
    }

    /// <summary>One thing to draw this frame. The renderers keep a pool of pictures and place them by this list.</summary>
    public struct GlassSprite
    {
        public int id;                          // stays the same for one thing for as long as it lives (for smooth drawing)
        public string art;                      // GlassArt.*
        public float x, y;                      // its middle, field units
        public float size;                      // its height, field units
        public float rot;                       // degrees, counter-clockwise
        public float alpha;                     // 0..1
        public int frame;                       // a variant of the picture (a facing: 0 = right, 1 = left; a level ...)
        public string text;                     // for art == Label / Card: what is written on it
        public string text2;                    // Card: its second, smaller line
        public float fill;                      // Card: 0..1, how far the "hold to pick" ring has filled
        public float w;                         // its width, field units (0 = from the picture's own shape: size is then its height and the width follows)
        public int color;                       // 0xRRGGBB to tint it with (plain shapes and labels need one); -1 = as the picture is. NOTE: default(GlassSprite) has color 0 = black, set it.
    }

    /// <summary>Screen-relative bounds shared by the display renderers and the headset.</summary>
    public static class GlassDisplay
    {
        public const float Floor = 0.035f, FlatScale = 0.86f, FlatBottom = 0.02f, ToolScale = 0.85f;
        public static bool IsTool(GamePackDef pack) => pack != null && !string.IsNullOrEmpty(pack.script)
            && pack.script.IndexOf("button(", StringComparison.Ordinal) >= 0 && pack.script.IndexOf("lives(", StringComparison.Ordinal) < 0;
        public static float Scale(GamePackDef pack, bool flat) => flat ? FlatScale : IsTool(pack) ? ToolScale : 1f;
    }

    public static class GlassArt
    {
        public const string Enemy = "enemy", Flyer = "flyer", Brute = "brute", Gem = "gem", Shot = "shot", Orb = "orb", Ring = "ring", Bolt = "bolt",
            Puff = "puff", Heart = "heart", Drop = "drop", Blob = "blob", Gold = "gold", Splash = "splash", Star = "star", Sparkle = "sparkle",
            Card = "card", Label = "label", Frost = "frost",
            Rect = "rect", Disc = "disc", RingShape = "hoop",      // plain shapes for scripted games, tinted with GlassSprite.color: a rectangle w x size, a filled circle, a circle's outline
            Board = "board", StoneA = "stone-a", StoneB = "stone-b", Cell = "cell",      // a board game: the board (size = its side), the user's stone, the partner's stone, the cell under the hand (fill = hold progress)
            Rock = "rock", PaperSign = "paper-sign", Scissors = "scissors";              // the three hand signs, as stickers (PaperSign: "paper" is the UI kit's box, below - a script calls the sticker 'paper')
        public static readonly string[] All = { Enemy, Flyer, Brute, Gem, Shot, Orb, Ring, Bolt, Puff, Heart, Drop, Blob, Gold, Splash, Star, Sparkle, Card, Label, Frost, Rect, Disc, RingShape, Board, StoneA, StoneB, Cell, Rock, PaperSign, Scissors };
        /// <summary>
        /// The paper the UI kit of scripted tools is made of (GlassScript: panel / button / display / toggle): a filled ROUNDED
        /// rectangle w x size in GlassSprite.color (always set), corner radius = GlassSprite.fill * the shorter side (fill 0..0.5;
        /// 0.5 = a pill / a circle), never turned. It has NO texture and no outline of its own (an outline is an ink paper under
        /// a smaller one): draw it with a rounded-rectangle shader or nine-sliced, so that the corners stay round at any w x h.
        /// It is NOT in All (there is no glass-paper.png). GlassScript only uses it when its renderer said it can draw it
        /// (GlassScript.Rounded); otherwise the same boxes come out as Rect.
        /// </summary>
        public const string Paper = "paper";
        /// <summary>A picture of the pack itself: art = PackPrefix + the name given in GamePackDef.art ("pack:stone").</summary>
        public const string PackPrefix = "pack:";
        public static bool IsFootball(string art) => art == "football" || string.Equals(art, "pack:soccerball", StringComparison.OrdinalIgnoreCase) || string.Equals(art, "pack:football", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A glass game. Start, then Step every frame with the hands; after each Step read what to draw and to play.
    /// Deterministic for a given seed and the same inputs (it has its own random numbers), so that it can be tested.
    /// </summary>
    public abstract class GlassGame
    {
        public float W = 16f / 9f;                              // the width of the field (its height is 1)
        public string phase = "intro";                          // intro | play | choose | over
        public int score, lives = 3, level = 1;
        public float seconds;                                   // played so far (the clock stands still while choosing)
        public string banner = "";
        public string result = "";
        public readonly List<GlassSprite> sprites = new List<GlassSprite>();    // rebuilt by every Step
        public readonly List<string> sounds = new List<string>();              // SfxSynth names raised by this Step (cleared at the start of the next)
        public string[] choices = new string[0], choiceNotes = new string[0];

        // The partner: where it stands (its feet; y is 0 on the floor), which way it faces and what it is doing.
        public float petX, petY;
        public bool petFacesRight = true;
        public string petPose = "idle";                         // idle | walk | run | cheer | hurt | pick | wipe | cower | down
        public float petHurt;                                   // > 0 for a moment after it was hit (blink it)

        // How far an OPEN hand reaches on the field right now (a fist reaches 0.6 of it; see GlassGames.HandRadius).
        // A game may grow it ("Big Hand"): draw the hand's shadow to this size, so that what it touches is what is seen.
        public float handRadius = 0.085f;

        // The fog of the "wipe" game (null in the others): fogCols x fogRows values 0 (clear) .. 1 (thick), row 0 at the BOTTOM.
        public float[] fog; public int fogCols, fogRows;
        public int fogRevision;                                 // goes up when the fog changed

        // BOARD games (template "board": five in a row, three in a row, four in a row with gravity). boardCols = 0 in the
        // other games. The board is a square region of the field: its bottom-left corner is (boardX, boardY) and its
        // side boardSide (field units); the middle of cell (col, row) is at
        //   x = boardX + (col + 0.5) * boardSide / boardCols,   y = boardY + (row + 0.5) * boardSide / boardCols     (row 0 at the bottom)
        // The user plays the stones 1, the partner the stones 2. A renderer may draw the board from these fields instead
        // of from the sprite list (the headset lays it flat on the desk) and place a stone directly with Place(col, row);
        // through the glass of a monitor the hand's shadow rests on a cell until hoverFill has filled.
        public int boardCols, boardRows;
        public int[] board;                                     // boardCols * boardRows, index = row * boardCols + col: 0 empty, 1 the user's, 2 the partner's
        public int boardRevision;                               // goes up with every stone
        public float boardX, boardY, boardSide;
        public bool boardGravity;                               // stones fall to the lowest free cell of their column (four in a row)
        public int turn;                                        // 1 = the user's turn, 2 = the partner's, 0 = nobody's (not started, or over)
        public int hoverCol = -1, hoverRow = -1;                // the free cell an acting hand is over (-1 = none)
        public float hoverFill;                                 // 0..1: how far "hold to place" has filled on that cell
        public int lastCol = -1, lastRow = -1, lastWho;         // the stone that was placed last
        public int[] winLine = new int[0];                      // the indices of the cells of the winning line (empty until someone has won)
        public int winner;                                      // 0 = nobody yet, 1 = the user, 2 = the partner, 3 = a draw
        /// <summary>The user's stone goes here. False when it is not the user's turn or the cell is not free.</summary>
        public virtual bool Place(int col, int row) { return false; }

        // A FLAT game lies on the desk when the headset draws it (x to the user's right, y away from the user; the partner
        // walks on it: petX, petY is where it stands) and every index fingertip is a hand whose dist is its height above
        // the desk in metres. Inside a monitor a flat game is drawn like any other. Scripted games choose (GlassScript).
        public bool flat;

        // Scripted games (GlassScript), for the renderers:
        //   touch          SET BY THE RENDERER before Step: true when the hands are fingertips over a surface that can really be
        //                  touched (the headset's flat field on the desk; dist = height above it). A tap is then the fingertip coming
        //                  down. False (through a monitor's glass, the upright stage): a tap is a hand that rests on a spot.
        //   petLine        a short line the partner says (a speech bubble at the partner); petLineLeft seconds are left ("" = nothing).
        //   petCustom      true while a motion that the script defined is playing: petCustomPose is then the pose to show
        //                  INSTEAD of the one petPose names (evaluated by every Step; null in games that never have one).
        public bool touch;
        public string petLine = "";
        public float petLineLeft;
        public bool petCustom;
        public Pose petCustomPose;

        // Things sent out toward the user in this Step (see GlassOut). The renderer / the running side takes them and clears the list.
        public readonly List<GlassOut> outs = new List<GlassOut>();
        /// <summary>What became of something that was sent out: blocked | hit | missed.</summary>
        public virtual void OutResult(int id, string what) { }
        // True when the game is being played inside a monitor (the PC client runs it), false on the desk. Set by the side that runs it, before Start.
        public bool inside;

        public abstract string Template { get; }
        public abstract void Start(GamePackDef pack, int seed);
        public abstract void Step(float dt, GlassInput input);
        /// <summary>Phase choose: take the i-th of what is offered (the hands can also pick by resting on a card).</summary>
        public virtual void Choose(int i) { }

        public void FillState(GlassState s)
        {
            s.phase = phase; s.score = score; s.lives = lives; s.level = level; s.seconds = seconds;
            s.choices = choices; s.choiceNotes = choiceNotes; s.banner = banner; s.result = result;
        }
    }
}
