using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>One timer module panel. A small pool of these is authored under MrCore/Timers (prefab: TimerPanel).</summary>
    public sealed class TimerPanelView : MonoBehaviour
    {
        public UiPanel panel;
        [Tooltip("Radial progress ring (LPFx mode 4).")]
        public FxQuad ring;
        public UiButton toggle, reset, plus, minus, display;
    }
}