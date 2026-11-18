using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The record picker. "When I press the record player I must be able to choose among several tunes - there is no
    /// tune selection at all" (the user, 2026-11-17). Pressing a thing that plays music (MrPoke) brings a small sheet
    /// up over it, turned to the user: the records the partner has (WorldState.songs - an older PC app lists none:
    /// then the three it came with), six a page; the one that is playing is marked and its disc turns. Pressing a
    /// record asks the PC to play it (world command "music" with the record's id), "Stop" stops the tune. The sheet
    /// goes away with its x, after 12 s without a press, when the thing is pressed again or put back into the bag.
    /// While a tune plays and a thing for music stands on the desk, a few notes rise from it.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Records")]
        [SerializeField] RecordsView _records;
        [Tooltip("Notes that rise from the thing that plays the tune (a head, a stem and a flag each).")]
        [SerializeField] Transform[] _notes;
        [SerializeField] FxQuad[] _noteParts;

        const float RecordsStay = 12f;      // seconds without a press before the sheet goes away by itself
        bool _recOpen, _recordsWasShown; string _recItem = "", _recShown = "", _recWant = "";
        int _recPage; float _recTouchedAt, _recWantAt, _recSpin; Vector3 _recAnchor;
        readonly List<SongDef> _recList = new List<SongDef>();
        // what an older PC app does not list: the three tunes the partner came with, by their ids
        static readonly SongDef[] RecBuiltin = { new SongDef { id = "song-club", name = "Club" }, new SongDef { id = "song-lofi", name = "Lo-fi" }, new SongDef { id = "song-lullaby", name = "Lullaby" } };

        void InitRecords()
        {
            var v = _records;
            for (int i = 0; i < v.tiles.Length; i++) { int k = i; v.tiles[i].press = () => RecordPick(_recPage * _records.tiles.Length + k); }
            v.prev.press = () => { _recPage--; _recShown = ""; _recTouchedAt = Time.time; };
            v.next.press = () => { _recPage++; _recShown = ""; _recTouchedAt = Time.time; };
            v.stop.press = () => RecordPick(-1);
            v.close.press = () => CloseRecords("its close button");
            v.panel.gameObject.SetActive(false);
            foreach (var q in _noteParts) q.enabled = false;
        }

        /// <summary>The records to choose from: what the PC lists, or the three the partner came with.</summary>
        List<SongDef> RecordList()
        {
            _recList.Clear();
            var songs = World.songs;
            if (songs != null) foreach (var s in songs) if (s != null && !string.IsNullOrEmpty(s.id)) _recList.Add(s);
            if (_recList.Count == 0) _recList.AddRange(RecBuiltin);
            return _recList;
        }

        /// <summary>The id of the record that is playing (or was just asked for), "" when there is silence.</summary>
        string RecordPlaying()
        {
            if (_recWant.Length > 0 && Time.time - _recWantAt < 2.5f) return _recWant == "stop" ? "" : _recWant;      // (until the PC's answer is here, what was pressed counts)
            var s = World.song;
            return s != null ? s.id ?? "" : "";
        }

        void OpenRecords(ItemDef d)
        {
            _recOpen = true; _recItem = d != null ? d.id : ""; _recShown = ""; _recTouchedAt = Time.time;
            // the page the playing record is on
            var list = RecordList(); string playing = RecordPlaying(); _recPage = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].id == playing) _recPage = i / _records.tiles.Length;
            _records.panel.Hide();      // (so that it pops up anew)
            Debug.Log($"[MR] records: the picker is open over {(d != null ? "'" + d.name + "'" : "the partner's spot (no thing for music is on the desk)")}: {list.Count} record(s){(World.songs == null || World.songs.Count == 0 ? " - the PC lists none, these are the three the partner came with" : "")}, playing: {(playing.Length > 0 ? playing : "nothing")}");
        }

        /// <summary>The PC is gone: the sheet and the notes are put away (nothing is behind their buttons).</summary>
        void RecordsHostLost()
        {
            CloseRecords("the PC is gone");
            _records.panel.Hide();
            foreach (var q in _noteParts) if (q.enabled) q.enabled = false;
        }

        void CloseRecords(string why)
        {
            if (!_recOpen) return;
            _recOpen = false;
            Debug.Log("[MR] records: the picker is closed (" + why + ")");
        }

        /// <summary>A record was picked (its place in the whole list), or -1: stop the tune.</summary>
        void RecordPick(int index)
        {
            _recTouchedAt = Time.time; _recShown = "";
            var list = RecordList();
            if (index < 0)
            {
                SendWorld("music", "", "stop");
                _recWant = "stop"; _recWantAt = Time.time;
                Debug.Log("[MR] records: Stop (world command music, text stop)");
                return;
            }
            if (index >= list.Count) { Debug.Log($"[MR] records: there is no record {index} (there are {list.Count})"); return; }
            var s = list[index];
            SendWorld("music", "", s.id);
            _recWant = s.id; _recWantAt = Time.time;
            int tile = index - _recPage * _records.tiles.Length;
            if (_recOpen && tile >= 0 && tile < _records.tiles.Length) _sparks.Burst(_records.discs[tile].position, _records.panel.transform.right, Vector3.up, 0.03f, 7);
            Debug.Log($"[MR] records: '{s.name}' picked (world command music, text {s.id})");
        }

        /// <summary>lpctl debug records 1: the picker over the first thing for music on the desk (over the partner's spot when there is none).</summary>
        void DebugOpenRecords()
        {
            foreach (var p in _props)
            {
                var d = p.itemId.Length > 0 ? Item(p.itemId) : null;
                if (d != null && d.uses != null && Array.IndexOf(d.uses, ItemUses.Music) >= 0) { OpenRecords(d); return; }
            }
            OpenRecords(null);
        }

        /// <summary>A pastel of its own for every record, from its name.</summary>
        static Color RecordColor(string name)
        {
            uint h = 2166136261;
            foreach (char c in name ?? "") { h ^= c; h *= 16777619; }
            return Color.HSVToRGB((h % 360u) / 360f, 0.42f, 0.96f);
        }

        /// <summary>Once a frame while the PC is there (MrUi.UpdateUi).</summary>
        void UpdateRecords(float dt)
        {
            UpdateNotes(dt);
            var v = _records; var p = v.panel;
            PropView prop = null; ItemDef thing = null;
            if (_recOpen)
            {
                if (_recItem.Length > 0) { thing = Item(_recItem); prop = PropOf(_recItem); }
                if (_recItem.Length > 0 && (thing == null || !thing.inRoom || prop == null)) CloseRecords("the thing was put away");
                else if (_calibrating) CloseRecords("the screens are being found");
                else if (Time.time - _recTouchedAt > RecordsStay) CloseRecords(RecordsStay + " s without a press");
            }
            if (!_recOpen) { p.Hide(); return; }

            var list = RecordList();
            int per = v.tiles.Length, pages = Mathf.Max(1, (list.Count + per - 1) / per);
            _recPage = (_recPage % pages + pages) % pages;
            string playing = RecordPlaying();
            string key = World.revision + "/" + list.Count + "/" + _recPage + "/" + playing;
            if (key != _recShown)
            {
                _recShown = key;
                for (int i = 0; i < per; i++)
                {
                    int idx = _recPage * per + i; var s = idx < list.Count ? list[idx] : null;
                    var b = v.tiles[i];
                    if (b.gameObject.activeSelf != (s != null)) b.gameObject.SetActive(s != null);
                    if (s == null) continue;
                    FitInto(b.label, string.IsNullOrEmpty(s.name) ? s.id : s.name, 0.080f);
                    bool on = s.id == playing;
                    b.color = on ? HoneyPaper : Paper; b.color2 = on ? new Color(1f, 0.84f, 0.50f, 0.98f) : PaperDeep;
                    Color c = RecordColor(string.IsNullOrEmpty(s.name) ? s.id : s.name);
                    v.discLabels[i].SetColors(c, Color.Lerp(c, Color.white, 0.25f));
                    if (!on) v.discs[i].localRotation = Quaternion.identity;
                }
                bool more = pages > 1;
                if (v.prev.gameObject.activeSelf != more) { v.prev.gameObject.SetActive(more); v.next.gameObject.SetActive(more); }
                v.pageLabel.text = more ? (_recPage + 1) + " / " + pages : "";
                Color hue = playing.Length > 0 ? Ui.Pal.Pink : new Color(0.86f, 0.84f, 0.80f);      // nothing to stop: greyed
                v.stop.color = Color.Lerp(hue, Color.white, 0.18f); v.stop.color2 = hue * 0.96f; v.stop.color.a = v.stop.color2.a = 0.98f;
            }
            // the disc of the record that is playing turns (a record's 33 turns a minute would look still: a little faster)
            _recSpin = Mathf.Repeat(_recSpin + dt * 150f, 360f);
            for (int i = 0; i < per; i++)
            {
                int idx = _recPage * per + i;
                if (idx < list.Count && list[idx].id == playing) v.discs[i].localRotation = Quaternion.Euler(0f, 0f, -_recSpin);
            }

            // over the thing, turned to the user (over the partner's spot when it was opened without a thing)
            if (prop != null && thing != null) _recAnchor = prop.shown + Vector3.up * (PokeTall(prop, thing) + 0.035f + p.height * 0.5f);
            else if (_homeSet) _recAnchor = new Vector3(_home.x, _deskY + 0.16f + p.height * 0.5f, _home.z);
            else _recAnchor = Input.HeadPos + Input.HeadRot * new Vector3(0f, -0.05f, 0.5f);
            p.Show();
            FaceUser(p.transform, _recAnchor);
            UpdateButtons(p, dt);
        }

        /// <summary>A few notes rising from the thing for music while a tune plays.</summary>
        void UpdateNotes(float dt)
        {
            PropView from = null; ItemDef thing = null;
            if (_songNow != null && _music.isPlaying && _musicVol > 0.05f && _worldFrame)
                foreach (var p in _props)
                {
                    var d = p.itemId.Length > 0 && p.Visible ? Item(p.itemId) : null;
                    if (d != null && d.status != "making" && d.uses != null && Array.IndexOf(d.uses, ItemUses.Music) >= 0) { from = p; thing = d; break; }
                }
            bool on = from != null;
            foreach (var q in _noteParts) if (q.enabled != on) q.enabled = on;
            if (!on) return;
            Vector3 top = from.shown + Vector3.up * PokeTall(from, thing), head = Input.HeadPos;
            Vector3 right = Input.HeadRot * Vector3.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            for (int i = 0; i < _notes.Length; i++)
            {
                float u = Mathf.Repeat(Time.time * 0.42f + i / (float)_notes.Length, 1f);
                Vector3 at = top + Vector3.up * (0.014f + u * 0.085f) + right * ((i - (_notes.Length - 1) * 0.5f) * 0.011f + Mathf.Sin(u * 6f + i * 1.9f) * 0.010f);
                // it pops out of the thing, floats up swaying, and shrinks away at the top
                float size = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.30f);
                _notes[i].SetPositionAndRotation(at, Quaternion.LookRotation(at - head, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(u * 7f + i) * 14f));
                _notes[i].localScale = Vector3.one * Mathf.Max(0.001f, size);
            }
        }
    }
}
