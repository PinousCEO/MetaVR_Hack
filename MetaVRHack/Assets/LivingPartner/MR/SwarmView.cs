using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// MrCore/Swarm: everything SCREEN SCAMPS shows (MR/MrSwarm.cs runs the game, Shared/Swarm.cs is the game). Pools of
    /// real objects authored by Editor/LPSceneSwarm.cs - the monsters (a mesh renderer each: their shapes are mesh data
    /// built at start, SwarmMeshes), their shadows, the bolts, the gems, rings, puffs - the storybook window they come
    /// out of when there is no screen, the sign with the hearts, the three augment cards and the result card.
    /// </summary>
    public sealed class SwarmView : MonoBehaviour
    {
        public GameObject root;
        public UiSprite gate;
        [Header("Where they come out: a storybook window on the desk (no screen), and the sheet on the glass")]
        public GameObject window;
        public FxQuad windowPane, windowFrame, windowSill, glassVeil, glassGlow;
        [Header("Monsters")]
        public Transform[] monsters;
        public MeshFilter[] monsterMesh;
        public MeshRenderer[] monsterRend;
        public FxQuad[] shadows;
        [Header("Bolts, gems, effects")]
        public FxQuad[] boltGlow;
        public FxQuad[] boltStreak;
        public UiSprite[] gems;
        public FxQuad[] ripples, puffs, chains, orbitStars;
        public FxQuad aimLine, reticle, reticleDot, shieldDisc, shieldRing, vignette, mist, ouch;
        [Header("The sign over the glass")]
        public Transform hud;
        public FxQuad hudBg, barBg, bar, bossBarBg, bossBar, bannerBg;
        public UiSprite[] hearts;
        public StoryLabel level, clock, banner, bossName;
        [Header("The augment cards")]
        public UiPanel[] cards;
        public UiButton[] cardButtons;
        public UiSprite[] cardIcons;
        public FxQuad[] cardBadges;
        public StoryLabel[] cardNames, cardLines, cardLevels;
        public Texture2D[] augArt;
        [Header("The result")]
        public UiPanel result;
        public StoryLabel resultTitle, resultLine, resultScore;
        public UiButton again, close;
    }
}
