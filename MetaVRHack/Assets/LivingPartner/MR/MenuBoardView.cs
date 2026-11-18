using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The menu (MrCore/Panels/MenuFan and MrCore/Panels/PalmChip).
    ///  - THE FAN: what the user owns as stickers WITHOUT tiles, on a piece of a cylinder that is curved towards the
    ///    user - four columns by three rows, the rows a little out of step, every sticker a little tilted and
    ///    bobbing -, each with its name on a strip of paper under it, a soft glow behind it, and a thin warm ray of
    ///    light running to it from one point at the bottom: the open left palm it unfolds from. Over them a ribbon
    ///    with the page's name, a dot per page, a round arrow at each side of the ribbon and the user's stars; under
    ///    them one line that says in words what a press did. It is at HAND scale: about 30 cm wide, the stickers 5 cm.
    ///  - THE STOP CHIP: during a game the open palm carries one round "Stop" sticker instead of the fan.
    /// The parts are authored here by LPSceneBuilder; MrMenuBoard fills and drives them.
    /// </summary>
    public sealed class MenuBoardView : MonoBehaviour
    {
        // ---- the shape of the fan, in metres (the builder authors to these numbers, the app lays out with them)
        /// <summary>The fan was first drawn 1.6 times this large (a board in the room): what was authored for that, times K, is hand scale.</summary>
        public const float K = 0.625f;
        /// <summary>The cylinder the stickers stand on: its axis is upright, FanRadius in front of the middle of the fan (where the user's eyes are).</summary>
        public const float FanRadius = 0.40f;
        /// <summary>The room the fan takes (along the curve, and upright) - for pointing at it and for a finger that comes near it.</summary>
        public const float FanWidth = 0.31f, FanHeight = 0.28f;
        public const int Cols = 4, Rows = 3, Tiles = Cols * Rows;
        /// <summary>From one sticker to the next along the curve / from one row to the next; a sticker itself.</summary>
        public const float PitchX = 0.074f, PitchY = 0.064f, Sticker = 0.047f;
        /// <summary>Heights on the fan (0 = its middle): the ribbon, the page dots, the middle of the rows, the line of words, the point the rays start from (just above the palm).</summary>
        public const float HeaderY = 0.128f, DotsY = 0.105f, GridY = 0.000f, StatusY = -0.110f, RootY = -0.132f;
        /// <summary>In a sticker's own space: where its picture and its name strip are (the button's middle is between them).</summary>
        public const float ArtY = 0.00625f, NameY = -0.0250f;

        /// <summary>A point of the fan: s metres along the curve from its middle (+ to the right), y metres up, lift metres towards the user.</summary>
        public static Vector3 FanPoint(float s, float y, float lift = 0f)
        {
            float a = s / FanRadius, r = FanRadius - lift;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r - FanRadius);
        }
        /// <summary>How something that stands on the fan at s is turned: it faces the axis (the user).</summary>
        public static Quaternion FanTurn(float s) => Quaternion.Euler(0f, s / FanRadius * Mathf.Rad2Deg, 0f);
        /// <summary>A point given in the fan's own space, as (metres along the curve, metres up, metres behind (+) or in front (-) of the curved sheet).</summary>
        public static Vector3 OnFan(Vector3 local)
        {
            float dz = local.z + FanRadius;
            return new Vector3(Mathf.Atan2(local.x, dz) * FanRadius, local.y, Mathf.Sqrt(local.x * local.x + dz * dz) - FanRadius);
        }

        // ---- THE CAROUSEL (2026-11-18: "make it the form that goes round and round like before"): the stickers stand on a
        // ring that revolves above the palm - the one at the front is big and is the one that is pressed. In the panel's
        // own space (x right, y up, z away from the user; the palm's point is at (0, RootY, 0)):
        /// <summary>The ring's radius, and how many stickers stand on it at most (a longer list runs through it like a belt: it changes at the back).</summary>
        public const float RingRadius = 0.090f; public const int RingShown = 9;
        /// <summary>The sticker at the front: its height and how far it stands towards the user; how much higher the one at the back stands (the ring leans to the eyes).</summary>
        public const float RingFrontY = -0.030f, RingFrontZ = -0.020f, RingBackLift = 0.052f;
        /// <summary>How large a sticker is at the front and at the back of the ring (times its authored size).</summary>
        public const float FrontScale = 1.40f, BackScale = 0.58f;
        /// <summary>The two arrows that turn the ring by one sticker; the ribbon and the line of words over the ring.</summary>
        public const float ArrowX = 0.064f, ArrowY = -0.103f, CarHeaderY = 0.104f, CarStatusY = 0.072f;

        /// <summary>Where a sticker stands that is a radians round the ring from the front (+ = to the user's right).</summary>
        public static Vector3 RingPoint(float a)
        {
            float c = Mathf.Cos(a);
            return new Vector3(Mathf.Sin(a) * RingRadius, RingFrontY + RingBackLift * (1f - c) * 0.5f, RingFrontZ + RingRadius * (1f - c));
        }
        /// <summary>How large it is there: it grows as it comes round to the front.</summary>
        public static float RingScale(float a)
        {
            float f = (1f + Mathf.Cos(a)) * 0.5f;
            return BackScale + (FrontScale - BackScale) * f * f * f;
        }

        public UiPanel panel;
        [Header("The carousel: the arrows' pictures, the pictures of the folders")]
        public UiSprite prevArt;
        public UiSprite nextArt;
        public Texture2D texPlaces, texArrow;
        [Header("The Stop chip on the open palm (during a game)")]
        public UiPanel chipPanel;
        public UiButton chip;
        [Header("Header: everything that unfolds together")]
        public Transform header;
        public UiSprite ribbon;
        [Tooltip("The page's name on the ribbon: Bag, Nilo, Tools.")]
        public StoryLabel title;
        [Tooltip("close: only while the fan stands in the room (asked for by voice) - on the palm, closing the hand closes it.")]
        public UiButton prev, next, close;
        public FxQuad starsBg;
        public UiSprite starIcon;
        public StoryLabel stars;
        [Tooltip("One dot per page, under the ribbon.")]
        public FxQuad[] dots;
        [Header("The point the fan unfolds from (the palm), and the rays")]
        public FxQuad rootGlow;
        public UiSprite rootSpark;
        [Tooltip("One ray per sticker: from the point at the bottom to the sticker.")]
        public FxQuad[] rays;
        [Header("Stickers (four by three, row by row)")]
        [Tooltip("Each: the soft glow behind it (bg), its picture (art), its name (label).")]
        public UiButton[] tiles;
        [Tooltip("The strip of paper under each sticker that its name is written on.")]
        public FxQuad[] tileNameBg;
        [Tooltip("The green dot of a thing that is out on the desk.")]
        public FxQuad[] tileDot;
        [Tooltip("The ring round the picture of what is playing / switched on.")]
        public FxQuad[] tileRing;
        [Tooltip("The little flag of a thing that was not in the bag when the fan was last open.")]
        public FxQuad[] tileNewBg;
        public StoryLabel[] tileNew;
        [Tooltip("A place's preview: a disc that runs from its sky colour to its horizon colour, and its hills in its third colour.")]
        public FxQuad[] tilePlace;
        public UiSprite[] tileHills;
        [Tooltip("What the last press did, in words, on a strip of paper under the stickers.")]
        public StoryLabel status;
        public FxQuad statusBg;
        [Header("Pictures by kind")]
        public Texture2D texItem;
        public Texture2D texSkill, texDance, texMusic, texGame, texDemonGate, texKeeper, texSurvival, texTennis, texPop, texRps, texRedLight;
        [Header("Nilo and Tools")]
        public Texture2D texGoIn;
        public Texture2D texComeOut, texHere, texWave, texLook, texCheer, texMarket, texScreen, texFind, texQuiet, texRain, texCancel;
    }
}
