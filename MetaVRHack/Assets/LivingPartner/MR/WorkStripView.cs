using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// THE WORK STRIP (MrCore/Panels/WorkStrip): the partner's work queue in the room - a small stack of paper cards
    /// under a honey handle. One card per job: its title, where it is worked ("in the room" / "in the PC workshop"),
    /// one line of what is being done, a bar with the percentage; a finished one has a tick and is pressed to open
    /// what came of it; an open one has a small round button that cancels it. The user takes it by the handle
    /// (thumb and finger) and puts it anywhere. Authored by LPSceneBuilder (LPSceneU1.cs), driven by MrWorkStrip.
    /// </summary>
    public sealed class WorkStripView : MonoBehaviour
    {
        public const int Rows = 3;
        /// <summary>Metres: the strip's width, its handle, a card, the gap between cards, the margin, the bar of a card.</summary>
        public const float W = 0.236f, HandleH = 0.027f, RowH = 0.052f, Gap = 0.005f, Pad = 0.007f, BarW = 0.150f, BarH = 0.0066f;
        public static float Height(int rows) => Pad + HandleH + Mathf.Max(1, rows) * (RowH + Gap) + Pad;

        public UiPanel panel;
        [Tooltip("The handle and what is written on it: moved up and down as the stack grows.")]
        public Transform top;
        public FxQuad handle;
        public StoryLabel title, more;
        [Header("The cards (from the top)")]
        public Transform[] rows;
        [Tooltip("The card itself: a finished job is pressed here to open what came of it.")]
        public UiButton[] rowBtn;
        [Tooltip("The small round button of an open job: cancel.")]
        public UiButton[] cancel;
        public StoryLabel[] rowTitle, rowStep, rowWhere, rowPct;
        public FxQuad[] rowWhereBg, rowBarBg, rowBar, rowMark;
        [Tooltip("The tick of a finished job (two strokes on the mark).")]
        public GameObject[] rowTick;
    }
}
