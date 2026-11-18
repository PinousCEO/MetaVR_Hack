using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// A glass game out on the desk (MrGlass): the STAGE - a field standing on the desk through the partner's spot and
    /// facing the user, or (a flat game) lying on the desk in front of it - with its pool of pictures and words, its
    /// cards, the rings that show where a hand is, and the sign. Authored under MrCore/GlassStage by
    /// LPSceneBuilder.AuthorGlass (LPSceneGlass.cs); the app places `stage` and drives everything in it.
    /// In `stage`: x = to the user's right, y = up, z = AWAY from the user; the origin is the middle of the floor line
    /// of a standing field (the middle of the far edge of a lying one); lengths are metres (the HUD, the cards and
    /// the result card are laid out in units of their own and scaled by the app).
    /// </summary>
    public sealed class GlassStageView : MonoBehaviour
    {
        [Tooltip("Everything of a game on the desk. Switched on while a game runs here.")]
        public GameObject stage;
        [Tooltip("A strip of paper on the desk along the foot of the field, and a faint sheet behind the field.")]
        public FxQuad floor, veil;
        [Tooltip("The pool of pictures a game's sprite list is drawn with (160: what a scripted game may ask for at once).")]
        public UiSprite[] sprites;
        public BallView[] footballs;
        [Tooltip("Words on the field (32), each with a small strip of paper for when they have no colour of their own.")]
        public StoryLabel[] labels;
        public FxQuad[] labelBgs;

        [Header("The cards of a choice (one unit = the height of a card's paper)")]
        public Transform[] cards;
        public UiSprite[] cardBg, cardIcon;
        public FxQuad[] cardRing;
        public StoryLabel[] cardName, cardNote;

        [Header("What a hand reaches on the field (0 = right, 1 = left), and the bot's hand")]
        public FxQuad[] handRing;
        public FxQuad[] handFill;
        public UiSprite botHand;

        [Header("Aiming at the field from a distance (0 = right, 1 = left): the ring on the field and the ray from the hand")]
        [Tooltip("The holder of each hand's ring: this is moved.")]
        public Transform[] aimCursor;
        [Tooltip("Three for each hand: brown outline ring, cream ring, centre dot.")]
        public FxQuad[] aimParts;
        public FxQuad[] aimRay;

        [Header("Through the glass (MrGlassOut): what a game sends out of its field flies through the room at the user")]
        [Tooltip("The pool of things in the air (8): each a holder that is turned to the eye, with its picture and a soft glow in it.")]
        public Transform[] outThings;
        public UiSprite[] outPicture;
        public FxQuad[] outGlow;
        [Tooltip("Their trails, and the ring on the glass where each came through.")]
        public FxQuad[] outTrail, outRipple;
        [Tooltip("The shield on each open palm (0 = right, 1 = left): a translucent disc and its rim.")]
        public FxQuad[] shieldDisc, shieldRing;
        [Tooltip("The soft red ring round the edge of the view when a thing reached the user.")]
        public FxQuad vignette;

        [Header("The sign above the field (laid out in field units)")]
        public Transform hud;
        public FxQuad hudBg;
        public StoryLabel title, score, level, time;
        public UiSprite[] hearts;
        public UiSprite scoreStar;
        public FxQuad barBg, bar;
        public FxQuad bannerBg;
        public StoryLabel banner;
        public Transform result;
        public FxQuad resultBg;
        public StoryLabel resultTitle, resultLine, resultScore;

        [Header("Pictures")]
        [Tooltip("One picture for each name in GlassArt.All, in that order (Art/Ui/glass-<name>.png).")]
        public Texture2D[] art;
        [Tooltip("The powers of the survival game, in the order of GlassSurvival.AbilityIds.")]
        public Texture2D[] abilityArt;

        [Header("The game is played inside a screen: a small sign near that screen")]
        public GameObject pcSign;
        public FxQuad pcSignBg;
        public StoryLabel pcSignLabel;
    }
}
