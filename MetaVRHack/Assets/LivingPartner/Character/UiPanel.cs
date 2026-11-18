using System;
using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// A spatial panel in the product style: a soft coloured glow, a deep glass gradient with a bright rim, a small accent
    /// bar under the title and gradient pill buttons. Authored as a prefab / scene object; all parts are serialized
    /// references. It pops in with a little overshoot every time it is shown.
    /// </summary>
    public sealed class UiPanel : MonoBehaviour
    {
        public FxQuad bg, glow, bar;
        public StoryLabel title, body;
        public UiButton[] buttons = Array.Empty<UiButton>();
        public float width = 0.2f, height = 0.1f;

        /// <summary>Which item (timer module, proposal ...) this pooled panel currently shows. Empty = free.</summary>
        [NonSerialized] public string key = "";
        /// <summary>Pop-in progress. Negative values delay the pop (used to stagger a row of cards).</summary>
        [NonSerialized] public float appear;
        /// <summary>Where the panel was in the last frame: a panel that is still coming up or being moved does not take presses.</summary>
        [NonSerialized] public Vector3 lastPos;

        public bool Shown => gameObject.activeSelf;

        public void Show(float delay = 0f)
        {
            if (gameObject.activeSelf) return;
            appear = -delay;
            transform.localScale = Vector3.one * 0.001f;
            gameObject.SetActive(true);
        }

        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        /// <summary>Advances the pop-in and the moving sheen. Call once per frame while shown.</summary>
        public void Animate(float dt, float time)
        {
            appear = Mathf.Min(1f, appear + dt / 0.42f);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, Ui.Pop(appear));
            bg.SetPhase(time);
        }
    }
}
