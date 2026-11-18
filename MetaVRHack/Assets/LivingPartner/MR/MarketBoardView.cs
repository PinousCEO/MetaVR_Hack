using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The marketplace as a board in the room (MrCore/Panels/MarketBoard): a sheet of paper with a ribbon, the user's
    /// stars, a row of tabs, eight product cards and - beside them - the card that was picked, large, with what the
    /// partner gains and one button (Get / Remove). The parts are authored here; MrMarket fills them from the catalogue
    /// the PC sends (the same one its web page shows).
    /// </summary>
    public sealed class MarketBoardView : MonoBehaviour
    {
        public UiPanel panel;
        public UiSprite ribbon, starIcon;
        public StoryLabel stars, pageLabel, notice;
        [Tooltip("All, Things, Skills, Games, Places, Powers, Mine.")]
        public UiButton[] tabs;
        [Tooltip("Eight cards, row by row. Each: paper (bg), picture (art), name (label).")]
        public UiButton[] cards;
        [Tooltip("Under each card's name: its price, or that the partner has it.")]
        public StoryLabel[] cardPrice;
        [Tooltip("The round sticker edge behind each card's picture.")]
        public FxQuad[] cardDisc;
        public UiButton prev, next, close, action;
        [Tooltip("For something of the user's own: the price it is (or would be) sold for, one step down / up.")]
        public UiButton minus;
        public UiButton plus;
        public StoryLabel priceLabel;
        [Tooltip("Who the user is in the marketplace.")]
        public StoryLabel nickname;
        [Header("The picked card, large")]
        public FxQuad detailBg;
        public FxQuad detailDisc;
        public UiSprite detailIcon;
        public StoryLabel detailName, detailBy, detailText;
        [Header("Pictures by kind, for modules without one of their own")]
        public Texture2D texItem;
        public Texture2D texSkill, texDance, texMusic, texAmbience, texAbility, texGame, texDemonGate, texKeeper, texSurvival, texTennis, texRps, texPop, texRedLight;
    }
}
