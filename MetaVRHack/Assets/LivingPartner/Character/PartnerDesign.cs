using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// One look of the partner: a body mesh skinned to the shared skeleton plus where its eyes sit. There is one, made
    /// from the owner's character model; a second look would be another asset of this kind, skinned to the SAME skeleton
    /// so that poses, the portal crossing and the clip plane work identically.
    /// Assets live in Art/Characters and are listed on the Partner prefab (PartnerRig.designs).
    /// </summary>
    public sealed class PartnerDesign : ScriptableObject
    {
        [Tooltip("Stable id used in the shared state (CharacterInfo.design), e.g. \"bunny\".")]
        public string id = "momo";
        public string displayName = "Nilo";
        public Mesh bodyMesh;
        [Tooltip("Eye positions in the Head bone's space, and the scale of the eye objects (1 = as modelled).")]
        public Vector3 eyeL, eyeR, eyeScale = Vector3.one;
    }
}
