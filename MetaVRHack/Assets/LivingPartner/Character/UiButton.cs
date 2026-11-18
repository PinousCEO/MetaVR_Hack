using System;
using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// A hand-first pill button on a spatial panel: poke it with an index fingertip or point-and-pinch from a distance.
    /// The object, its gradient quad and its label are authored in the panel prefab; only the action is bound in code.
    /// </summary>
    public sealed class UiButton : MonoBehaviour
    {
        public FxQuad bg;
        public StoryLabel label;
        [Tooltip("The picture of a tile button (hand menu). Pill buttons have none: they are drawn by bg and label.")]
        public UiSprite art;
        [Tooltip("Activation radius in metres (half the button width).")]
        public float radius = 0.02f;
        [Tooltip("A button that is not round: half its width and half its height in metres. Zero = round, hit by its radius.")]
        public Vector2 half;
        public Color color = Color.white, color2 = Color.white;
        [Tooltip("How much it grows under a finger or a pointing ray (0.10 = a tenth).")]
        public float hoverGrow = 0.10f;

        /// <summary>What the button does. Bound once by the owning controller.</summary>
        [NonSerialized] public Action press;
        [NonSerialized] public float cool, hover, punch;
        /// <summary>Pop-in of this one button (0..1) and a small float up and down, set by a panel that animates its buttons.</summary>
        [NonSerialized] public float intro = 1f, bob;
        /// <summary>Its size (1 = as authored), set by a panel whose buttons grow and shrink (the menu's carousel: the sticker at the front is the big one).</summary>
        [NonSerialized] public float size = 1f;
        [NonSerialized] public Vector3 home; [NonSerialized] public bool homeSet;
        /// <summary>
        /// The hands whose fingertip has gone through the face of the button and has not come back out yet (bit 0 the
        /// right hand, bit 1 the left). Such a finger cannot press it again: it has to be drawn back first.
        /// </summary>
        [NonSerialized] public int pokedBy;
    }
}
