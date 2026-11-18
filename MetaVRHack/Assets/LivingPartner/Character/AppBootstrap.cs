using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingPartner.Character
{
    /// <summary>
    /// Entry scene of the Windows build (Scenes/Main.unity). The one shared project holds one scene per client, each with
    /// its objects authored in the hierarchy; this picks the scene for the role given on the command line:
    ///   -lp-role pc     -> PcCharacter  (the transparent desktop character client)
    ///   -lp-role mrsim  -> MrSimulator  (the desktop MR simulator, a development tool)
    /// The Quest build starts directly in the QuestMR scene and does not use this.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] string _pcScene = "PcCharacter";
        [SerializeField] string _simScene = "MrSimulator";
        [Tooltip("Role used when pressing Play on this scene in the Editor.")]
        [SerializeField] string _editorRole = "mrsim";

        public static string ActiveRole { get; private set; } = "";

        void Awake()
        {
            string role = Application.isEditor ? _editorRole : Ui.Arg("-lp-role", "pc");
            ActiveRole = role;
            string scene = role == "mrsim" ? _simScene : _pcScene;
            Debug.Log($"[LivingPartner] bootstrap role={role} platform={Application.platform} -> scene {scene}");
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
    }
}
