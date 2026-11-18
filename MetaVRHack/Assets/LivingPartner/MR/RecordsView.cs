using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The record picker (MrCore/Panels/Records): a small sheet of paper that comes up over the record player when it
    /// is pressed - six records a page, each a tile with a little vinyl disc (its label in a colour of its own) and
    /// its name; the one that is playing is marked and its disc turns; "Stop", and a button to put the sheet away.
    /// The parts are authored here; MrRecords fills them from the records the PC lists (WorldState.songs).
    /// </summary>
    public sealed class RecordsView : MonoBehaviour
    {
        public UiPanel panel;
        [Tooltip("Six records, row by row. Each: paper (bg), name (label).")]
        public UiButton[] tiles;
        [Tooltip("The vinyl disc on each tile: this holder is turned while its record plays.")]
        public Transform[] discs;
        [Tooltip("The paper label in the middle of each disc: its colour comes from the record's name.")]
        public FxQuad[] discLabels;
        [Tooltip("Earlier / later records, when there are more than six.")]
        public UiButton prev;
        public UiButton next;
        public StoryLabel pageLabel;
        public UiButton stop, close;
    }
}
