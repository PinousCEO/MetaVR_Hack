using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.Windows
{
    /// <summary>
    /// PcCharacter/Swarm: the monsters of SCREEN SCAMPS while they are BEHIND THE GLASS of a monitor (Windows/PcSwarm.cs
    /// draws them from what the headset sends). A pool of mesh renderers with the one prop material, the rings that
    /// spread where one goes out through the glass, and a soft glow. Authored by Editor/LPSceneSwarm.cs, on a layer of
    /// its own (PcApp.SwarmLayer): only PcSwarm's window shows it.
    /// </summary>
    public sealed class PcSwarmView : MonoBehaviour
    {
        public GameObject root;
        public UiSprite gate;
        public Transform[] monsters;
        public MeshFilter[] monsterMesh;
        public MeshRenderer[] monsterRend;
        public FxQuad[] rings;
        public FxQuad glow;
    }
}
