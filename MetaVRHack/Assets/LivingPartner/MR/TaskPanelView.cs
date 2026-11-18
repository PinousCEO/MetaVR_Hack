using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>The result of the selected change (MrCore/Panels/TaskPanel): real files, test output, explicit publish approval.</summary>
    public sealed class TaskPanelView : MonoBehaviour
    {
        public UiPanel panel;
        public UiButton approve, preview, close;
    }
}