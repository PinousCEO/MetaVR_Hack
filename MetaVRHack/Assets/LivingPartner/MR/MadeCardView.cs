using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// "Nilo made this!" (MrCore/Panels/MadeCard): the card that comes up when the partner has finished making
    /// something - the thing's picture, its name, and the question whether it should go up in the marketplace, with a
    /// price to step up and down and two buttons. The parts are authored here; MrCraft fills them from the offer the
    /// PC sends (MarketView.offerId) and sends the answer back.
    /// </summary>
    public sealed class MadeCardView : MonoBehaviour
    {
        public UiPanel panel;
        [Tooltip("The round sticker edge behind a picture of the thing's own.")]
        public FxQuad disc;
        public UiSprite icon;
        [Tooltip("The thing's name, and under it what kind of thing it is.")]
        public StoryLabel thingName;
        public StoryLabel kind;
        [Tooltip("The price it would be sold for, one step down / up.")]
        public StoryLabel priceLabel;
        public UiButton minus, plus;
        public UiButton sell, keep;
    }
}
