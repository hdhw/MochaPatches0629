using System;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using RecRoom.AntiCheat;

namespace Mocha.Patch
{
    // BepInEx plugins don't get a per-frame tick the way a MelonMod does, so the
    // config-poll / maintenance-countdown / scene-cleanup loop that used to live in
    // Core.OnUpdate runs here instead, on a MonoBehaviour Core.Load() adds to the
    // scene.
    public class MochaBehaviour : MonoBehaviour
    {
        private const float CheckIntervalSeconds = 30f;

        private float _pollTimer;
        private bool _fetching;
        private volatile int _polledMinutes = int.MinValue;
        private int _lastConfigMinutes = int.MinValue;
        private DateTime _deadlineUtc;
        private bool _active;
        private int _lastShownMilestone = -1;
        private int _lastSceneIndex = int.MinValue;

        private void Update()
        {
            _pollTimer += Time.deltaTime;
            if (_pollTimer >= CheckIntervalSeconds && !_fetching)
            {
                _pollTimer = 0f;
                _ = FetchAsync();
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex != _lastSceneIndex)
            {
                _lastSceneIndex = active.buildIndex;
                OnSceneLoaded(active);
            }

            if (_active)
            {
                int minutesRemaining = (int)Math.Ceiling((_deadlineUtc - DateTime.UtcNow).TotalMinutes);
                if (minutesRemaining <= 0)
                {
                    Core.ApplyMaintenance(0, show: false);
                    _active = false;
                    _lastShownMilestone = -1;
                }
                else if (Core.IsMilestone(minutesRemaining) && minutesRemaining != _lastShownMilestone)
                {
                    Core.ApplyMaintenance(minutesRemaining, show: true);
                    _lastShownMilestone = minutesRemaining;
                }
            }
        }

        private async Task FetchAsync()
        {
            if (_fetching)
            {
                return;
            }

            _fetching = true;
            try
            {
                using System.Net.Http.HttpResponseMessage response = await Core.Http.GetAsync(Core.ConfigUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                int minutes = root.TryGetProperty("ServerMaintenance", out JsonElement sm)
                    && sm.ValueKind == JsonValueKind.Object
                    && sm.TryGetProperty("StartsInMinutes", out JsonElement m)
                    && m.ValueKind == JsonValueKind.Number
                    ? m.GetInt32()
                    : int.MinValue;

                _polledMinutes = minutes;

                if (minutes != _lastConfigMinutes)
                {
                    _lastConfigMinutes = minutes;
                    if (minutes >= 0)
                    {
                        _deadlineUtc = DateTime.UtcNow.AddMinutes(minutes);
                        _active = true;
                        _lastShownMilestone = -1;
                    }
                    else
                    {
                        Core.ApplyMaintenance(0, show: false);
                        _active = false;
                        _lastShownMilestone = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                MochaLog.Warning("FetchAsync failed: " + ex.Message);
            }
            finally
            {
                _fetching = false;
            }
        }

        private void OnSceneLoaded(Scene scene)
        {
            MochaLog.Msg($"Scene Loaded: {scene.name} (BuildIndex: {scene.buildIndex})");

            bool outsideTitle = scene.name != "TitleScreen" && scene.name != "empty";
            if (outsideTitle)
            {
                CheatManager cheatManager = UnityEngine.Object.FindObjectOfType<CheatManager>();
                UnityEngine.Object.Destroy(cheatManager != null ? cheatManager.gameObject : null);

                EACManager eacManager = UnityEngine.Object.FindObjectOfType<EACManager>();
                UnityEngine.Object.Destroy(eacManager != null ? eacManager.gameObject : null);
            }
        }
    }
}
