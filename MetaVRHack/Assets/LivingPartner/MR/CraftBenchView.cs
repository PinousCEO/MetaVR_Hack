using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The partner's little workbench (MrCore/CraftBench): it comes up beside the partner while it makes something.
    ///  - root: the bench itself - a plank on two legs, drawn as a small paper cut-out that stands on the desk and is
    ///    turned to the user (about the vertical only). Its parts are authored in metres for a partner of 10 cm; the
    ///    app scales the root.
    ///  - shadow, grip: lie flat on the desk under it (the soft shadow; the dashed ring that says "this can be taken").
    ///  - hammer: the hammer in the partner's right hand.
    ///  - card: the paper card over the bench - what is being made, HOW FAR IT IS in large figures with a bar, and one
    ///    line that says what is being done just now (read from the work queue).
    /// The thing in the making is the ball of light on the plank (MrApp._lightBall / _lightHalo). MrCraft drives all of it.
    /// </summary>
    public sealed class CraftBenchView : MonoBehaviour
    {
        /// <summary>How high the plank's upper side is over the desk, for a partner of 10 cm.</summary>
        public const float Top = 0.027f;
        /// <summary>The card: its height, the least and the most it is wide.</summary>
        public const float CardH = 0.050f, CardMinW = 0.080f, CardMaxW = 0.130f;

        public Transform root;
        public FxQuad shadow, grip;
        public UiSprite hammer;
        [Header("The card over the bench")]
        public Transform card;
        public FxQuad cardBg, barBg, barFill;
        public StoryLabel title, percent, step;
    }
}
