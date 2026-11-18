using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The documents the partner made, as objects in the room (authored by LPSceneDocs.cs, filled and placed by MrDocs.cs):
    ///  - the REPORT board: an upright sheet in A4 proportion with one page of the report as a picture, its title, the
    ///    page number, earlier / later, "Send to PC" and a button that puts it away;
    ///  - the DESIGNS board: the original on the left, four options as cards to pick from, "Send to PC" and close;
    ///  - the MAKING strip: a small strip of paper beside the partner while it works - title, what it is doing, a bar;
    ///  - the SHEET the partner holds over its head while it carries the file to the screen.
    /// </summary>
    public sealed class DocBoardView : MonoBehaviour
    {
        [Header("Report: a page of it on an upright board")]
        public UiPanel report;
        [Tooltip("The strip along the top that the board is moved by.")]
        public FxQuad reportBar;
        public StoryLabel reportTitle, reportPage, reportNote;
        [Tooltip("The page's picture (a quad of exactly the page's size).")]
        public UiSprite page;
        public FxQuad pageEdge;
        public UiButton prev, next, send, close;

        [Header("Designs: the original and the options")]
        public UiPanel designs;
        public FxQuad designsBar;
        public StoryLabel designsTitle, designsNote;
        public UiSprite original;
        public UiButton[] cards;
        [Tooltip("The mark of the picked card: a honey frame round it and a tick on its corner.")]
        public GameObject[] cardPicked;
        public UiButton designsSend, designsClose;

        [Header("Making: the strip beside the partner")]
        public UiPanel strip;
        public StoryLabel stripTitle, stripStage;
        public FxQuad stripBarBg, stripBar;
        public UiSprite stripIcon;

        [Header("The sheet it carries to the screen")]
        public UiSprite sheet;
        [Header("The sheet as the file it really is: the picture of its first page, a paper edge, its name (2026-11-18)")]
        public UiSprite sheetPic;
        public FxQuad sheetPaper, sheetNameBg;
        public StoryLabel sheetName;

        [Header("Pictures")]
        public Texture2D texPdf;
        public Texture2D texPic, texBlankPage, texBlankCard;

        // sizes the app needs (metres), set by the builder
        public float pageW = 0.30f, pageH = 0.424f, pageY = 0.0f, barH = 0.034f, stripBarW = 0.13f;
    }
}
