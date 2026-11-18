using System;
using System.Collections.Generic;

namespace LivingPartner.Shared
{
    // DOCUMENTS the partner makes for the user - the bridge from talking in the room to real files on the PC:
    //   report    "look into X and make me a PDF": the partner researches, writes a report, and it becomes a PDF
    //   designs   "that icon is not good, make me a new one": the partner looks at what the user points at on a screen
    //             and draws several options (PNG each, and one PDF sheet of all of them)
    // The host makes them (the language / image model, the PDF) and keeps the files in a folder of its own on the PC
    // (Documents\Bambi\...). The headset shows a document on a board in the room (the pages as pictures) and the options
    // as cards to pick from; when the user asks, the partner carries the file INTO the PC: the PC client shows it in the
    // partner's hands, and a click on it there opens the real file.

    public sealed class DocFile
    {
        public string name = "";            // "Meta VR Start - notes.pdf"
        public string kind = "pdf";         // pdf | png | glb (a 3D model the partner sculpted: DocItem.kind "model")
        public string path = "";            // where it is on the PC (shown to the user; only the host opens it)
    }

    public sealed class DocOption
    {
        public string name = "";            // "Option 2 - rounder, warmer"
        public string picture = "";         // PNG served by the host under /docs/<doc id>/<picture>
        public bool picked;
    }

    public sealed class DocItem
    {
        public string id = "";
        public string kind = "report";      // report | designs | model (a sculpted thing as a .glb file: only ever carried into the PC, never on the board; its preview is /docs/<id>/page-1-s.png)
        public string title = "";
        public string asked = "";           // what the user said
        public string status = "making";    // making | ready | failed
        public string stage = "";           // one line while it is being made ("Reading about it ...", "Drawing option 3 of 4 ...")
        public float progress;              // 0..1
        public int pages;                   // report: the pages are pictures /docs/<id>/page-<n>.png, n = 1..pages
        public List<DocOption> options = new List<DocOption>();     // designs
        public List<DocFile> files = new List<DocFile>();           // what was written on the PC (the PDF first)
        public string folder = "";          // the folder on the PC it was saved in
        public bool delivered;              // the partner has carried it into the PC (the PC client shows it to click on)
        public long madeUnixMs;
    }

    /// <summary>In every snapshot: the documents of this sitting (the newest last) and which one is open in the room.</summary>
    public sealed class DocsView
    {
        public int revision;
        public List<DocItem> items = new List<DocItem>();
        public string openId = "";          // the document on the board in the room ("" = none)
        public int page = 1;                // report: the page shown
    }

    /// <summary>Client -> host (Msg.Doc).</summary>
    public sealed class DocCommand
    {
        // open (id) | close | page (n) | pick (id, n = the option) | deliver (id: carry it into the PC) | reveal (id: open its folder
        // on the PC - sent by the PC client when the user clicks the file in the partner's hands) | delete (id)
        public string cmd = "";
        public string id = "";
        public int n;
    }
}
