using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>The pairing numpad (MrCore/Panels/Pairing): the six-digit code from the PC tray is typed by hand.</summary>
    public sealed class PairingView : MonoBehaviour
    {
        public UiPanel panel;
        [Tooltip("Digit buttons, index = digit (0..9).")]
        public UiButton[] digits = new UiButton[10];
        public UiButton erase, connect;
    }
}