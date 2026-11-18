using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    public sealed partial class MrApp
    {
        const string GardenSave = "Yonder.StarGarden.v1";
        string GardenKey => Input.Simulated && !string.IsNullOrEmpty(LocalFile) ? GardenSave + "." + LocalFile : GardenSave;
        GameObject _gardenRoot;
        RadishGarden _garden;
        readonly MeshFilter[] _pots = new MeshFilter[RadishGarden.Plots];
        ResourceRequest _gardenArtRequest;
        StarGardenMeshes _gardenArt;
        readonly Transform[] _fruit = new Transform[RadishGarden.Plots];
        readonly StoryLabel[] _moduleSigns = new StoryLabel[3];
        StoryLabel _gardenWallet;
        bool GardenOpen => _gardenRoot != null && _gardenRoot.activeSelf;

        void ToggleGarden()
        {
            if (GardenOpen) { _gardenRoot.SetActive(false); _mbDirty = true; return; }
            if (GameOn || _calibrating) { Caption("Finish the game or screen setup to open your garden.", 2f); return; }
            if (!Input.HeadTracked || !_homeSet) { Caption("Look towards the desk to place your garden.", 2f); return; }
            if (_gardenRoot == null)
            {
                _gardenArt = _gardenArtRequest?.asset as StarGardenMeshes;
                if (_gardenArt == null) _gardenArt = Resources.Load<StarGardenMeshes>("StarGarden");
                if (_gardenArt == null) { Caption("The garden could not be loaded.", 3f); return; }
                BuildGarden();
            }
            var forward = Vector3.ProjectOnPlane(Input.HeadRot * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = _worldFwd;
            Vector3 at = Input.HeadPos + forward * 0.38f;
            at.y = _deskY;
            _gardenRoot.transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward));
            _gardenRoot.SetActive(true);
            _mbDirty = true;
            CloseBoard("garden");
            Debug.Log($"[MR] garden: opened at {at:F3}; middle pot {_pots[1].transform.position:F3}");
            Caption("Pinch the soil to plant. Pinch again to water.", 4f);
        }

        void BuildGarden()
        {
            bool migrate = !PlayerPrefs.HasKey(GardenKey);
            string saved = PlayerPrefs.GetString(migrate && GardenKey == GardenSave ? "Bambi.RadishGarden.v1" : GardenKey, "{}");
            _garden = JsonUtility.FromJson<RadishGarden>(saved) ?? new RadishGarden();
            if (_garden.beds == null || _garden.beds.Length != RadishGarden.Plots) _garden = new RadishGarden();
            if (migrate) _garden.stars = Math.Max(0, _garden.harvested) * 3;
            _gardenRoot = new GameObject("Star Garden");
            _gardenRoot.transform.SetParent(transform, false);
            var clay = _props[0].GetComponent<MeshRenderer>().sharedMaterial;
            for (int i = 0; i < _pots.Length; i++)
            {
                var pot = new GameObject("Radish pot " + (i + 1));
                pot.transform.SetParent(_gardenRoot.transform, false);
                pot.transform.localPosition = new Vector3((i - 1) * 0.112f, 0, i == 1 ? 0.018f : 0);
                pot.transform.localScale = Vector3.one * 0.092f;
                pot.transform.localRotation = Quaternion.Euler(0, i * 29f, 0);
                _pots[i] = pot.AddComponent<MeshFilter>();
                pot.AddComponent<MeshRenderer>().sharedMaterial = clay;
                var fruit = new GameObject("Star fruit");
                fruit.transform.SetParent(pot.transform, false);
                fruit.AddComponent<MeshFilter>().sharedMesh = _gardenArt.fruit;
                fruit.AddComponent<MeshRenderer>().sharedMaterial = clay;
                _fruit[i] = fruit.transform;
                _moduleSigns[i] = GardenSign(new Vector3((i - 1) * .127f, .053f, .143f), .12f, .052f);
            }
            _gardenWallet = GardenSign(new Vector3(0, .110f, .166f), .17f, .030f);
            RefreshGardenSigns();
        }

        StoryLabel GardenSign(Vector3 at, float width, float height)
        {
            var label = Instantiate(_introLine, _gardenRoot.transform, false);
            label.transform.localPosition = at;
            label.transform.localRotation = Quaternion.Euler(26, 0, 0);
            label.transform.localScale = Vector3.one;
            label.characterSize = _introLine.characterSize * .90f;
            label.Mesh.alignment = TextAlignment.Center;
            label.color = Ui.Pal.Ink;
            var panel = Instantiate(_introBg, _gardenRoot.transform, false);
            panel.transform.SetPositionAndRotation(label.transform.position + label.transform.forward * .0018f, label.transform.rotation);
            panel.enabled = true;
            panel.SetColors(new Color(1f,.93f,.72f), new Color(.96f,.77f,.54f));
            Ui.SizePanel(panel, width, height, .009f, .0013f);
            return label;
        }

        void RefreshGardenSigns()
        {
            _gardenWallet.text = "Star Garden  ·  " + _garden.stars + " stars";
            for (int i = 0; i < _moduleSigns.Length; i++)
                _moduleSigns[i].text = RadishGarden.ModuleNames[i] + "\n" + (_garden.Owns(i) ? "Installed" : RadishGarden.Prices[i] + " stars");
        }

        void UpdateGarden()
        {
            if (!GardenOpen) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (int i = 0; i < _pots.Length; i++)
            {
                var pot = _pots[i];
                var stage = _garden.Stage(i, now);
                var mesh = _gardenArt.stages[(int)stage];
                if (pot.sharedMesh != mesh) pot.sharedMesh = mesh;
                var fruit = _fruit[i];
                bool ripe = stage == RadishStage.Ripe;
                if (fruit.gameObject.activeSelf != ripe) fruit.gameObject.SetActive(ripe);
                if (ripe)
                {
                    fruit.localPosition = new Vector3(0, .89f + .028f*Mathf.Sin(Time.time*2f+i), 0);
                    fruit.localRotation = Quaternion.Euler(0, Time.time * 50f + i * 27f, 8f);
                }
                if (_mbOpen || _mbClosing || _calibrating || !Input.HeadTracked || GameOn || !_introDone) continue;
                for (int h = 0; h < 2; h++)
                {
                    var hand = UiHand(h);
                    if (!hand.tracked || !hand.pinchDown || HandBusy(h, hand)) continue;
                    var contact = (hand.indexTip + hand.thumbTip) * 0.5f;
                    if (i == 0 && GardenShopPinch(contact)) return;
                    var delta = pot.transform.InverseTransformPoint(contact);
                    if (delta.y < 0.18f || delta.y > 1.50f || delta.x * delta.x + delta.z * delta.z > .31f) continue;
                    if (!_garden.Tend(i, now))
                    {
                        Caption("Harvest in " + Math.Max(1, _garden.GrowthTime - (now - _garden.beds[i])) + "s", 1.3f);
                        return;
                    }
                    PlayerPrefs.SetString(GardenKey, JsonUtility.ToJson(_garden));
                    PlayerPrefs.Save();
                    RefreshGardenSigns();
                    Debug.Log($"[MR] garden: pot {i + 1} {stage} -> {_garden.Stage(i, now)}; harvested {_garden.harvested}");
                    pot.sharedMesh = _gardenArt.stages[(int)_garden.Stage(i, now)];
                    Sfx(stage == RadishStage.Soil ? "tap" : stage == RadishStage.Seed ? "drop" : "pop", contact, .62f);
                    if (stage == RadishStage.Ripe)
                    {
                        _sparks.Burst(contact, _gardenRoot.transform.right, Vector3.up, .052f, 10);
                        Caption("+" + _garden.Yield + " stars", 2.5f);
                    }
                    return;
                }
            }
        }

        bool GardenShopPinch(Vector3 point)
        {
            for (int i = 0; i < _moduleSigns.Length; i++)
            {
                Vector3 hit = _moduleSigns[i].transform.InverseTransformPoint(point);
                if (Mathf.Abs(hit.x) > .06f || Mathf.Abs(hit.y) > .027f || Mathf.Abs(hit.z) > .028f) continue;
                if (_garden.Buy(i))
                {
                    PlayerPrefs.SetString(GardenKey, JsonUtility.ToJson(_garden));
                    PlayerPrefs.Save();
                    RefreshGardenSigns();
                    Debug.Log($"[MR] garden: installed {RadishGarden.ModuleNames[i]}; {_garden.stars} stars left");
                    Sfx("sparkle", point, .65f);
                    _sparks.Burst(point, _gardenRoot.transform.right, Vector3.up, .05f, 12);
                    Caption(RadishGarden.ModuleNames[i] + " installed · " + RadishGarden.Benefits[i], 3f);
                }
                else Caption(_garden.Owns(i) ? RadishGarden.Benefits[i] : "Harvest " + (RadishGarden.Prices[i] - _garden.stars) + " more stars", 2f);
                return true;
            }
            return false;
        }
    }
}
