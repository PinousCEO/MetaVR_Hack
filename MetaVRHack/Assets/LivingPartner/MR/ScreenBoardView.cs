using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// A display of the PC brought into the room (MrCore/Panels/ScreenBoard): its picture in a paper frame, hanging in
    /// the air beside the desk, with a line under it once the partner has looked. The picture is fetched from the PC
    /// (GET /screen/&lt;revision&gt;.jpg) whenever the snapshot says there is a newer one.
    /// </summary>
    public sealed class ScreenBoardView : MonoBehaviour
    {
        public UiPanel panel;
        public UiSprite picture;
        public FxQuad captionBg;
        public StoryLabel caption;
        public UiButton close;
        [Tooltip("Shown until the first picture has arrived.")]
        public Texture2D placeholder;
    }
}
