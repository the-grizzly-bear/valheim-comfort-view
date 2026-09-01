using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ComfortRadius
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ComfortRadiusPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "mishka.valheim.comfortradius";
        public const string PluginName = "ComfortRadius";
        public const string PluginVersion = "1.0.0";

        // Matches SE_Rested's private c_ComfortRadius - the game checks this exact
        // distance from the player to each comfort piece, so the ring has to match it
        // or it lies about what actually counts.
        private const float ComfortRadius = 10f;

        // How far out from the player we bother spawning rings, so you can see one
        // before you've actually walked into range.
        private const float ScanRadius = 20f;

        private const float RefreshInterval = 1f;

        private static CircleProjector s_markerTemplate;
        private static readonly List<Piece> s_scanBuffer = new List<Piece>();
        private static readonly List<Piece> s_staleBuffer = new List<Piece>();

        private ConfigEntry<KeyboardShortcut> toggleKey;
        private bool visible;
        private float refreshTimer;
        private readonly Dictionary<Piece, GameObject> activeRings = new Dictionary<Piece, GameObject>();

        private void Awake()
        {
            toggleKey = Config.Bind("General", "Toggle Radius", new KeyboardShortcut(KeyCode.F6),
                "Key used to toggle the comfort radius rings on and off.");

            new Harmony(PluginGUID).PatchAll();
        }

        // Ward stones ship a CircleProjector-based ground ring (m_areaMarker) for their
        // own build-permission radius. Rather than build ring rendering from scratch, we
        // clone that same component onto every nearby comfort piece and just override its
        // radius - same trick StaffOfWisps uses to clone existing prefabs instead of
        // authoring new assets.
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class CaptureMarkerTemplate
        {
            private static void Postfix(ZNetScene __instance)
            {
                s_markerTemplate = null;
                foreach (GameObject prefab in __instance.m_prefabs)
                {
                    PrivateArea privateArea = prefab.GetComponentInChildren<PrivateArea>(true);
                    if (privateArea != null && privateArea.m_areaMarker != null)
                    {
                        s_markerTemplate = privateArea.m_areaMarker;
                        break;
                    }
                }
            }
        }

        private void Update()
        {
            if (toggleKey.Value.IsDown())
            {
                visible = !visible;
                if (!visible)
                {
                    ClearRings();
                }
            }

            if (!visible)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || s_markerTemplate == null)
            {
                ClearRings();
                return;
            }

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshRings(player);
            }
        }

        private void RefreshRings(Player player)
        {
            s_scanBuffer.Clear();
            Piece.GetAllComfortPiecesInRadius(player.transform.position, ScanRadius, s_scanBuffer);

            s_staleBuffer.Clear();
            foreach (KeyValuePair<Piece, GameObject> kv in activeRings)
            {
                if (kv.Key == null || !s_scanBuffer.Contains(kv.Key))
                {
                    s_staleBuffer.Add(kv.Key);
                }
            }
            foreach (Piece stale in s_staleBuffer)
            {
                if (activeRings.TryGetValue(stale, out GameObject ring) && ring != null)
                {
                    Destroy(ring);
                }
                activeRings.Remove(stale);
            }

            foreach (Piece piece in s_scanBuffer)
            {
                if (piece == null || activeRings.ContainsKey(piece))
                {
                    continue;
                }

                GameObject ring = Instantiate(s_markerTemplate.gameObject, piece.transform.position, Quaternion.identity, piece.transform);
                CircleProjector projector = ring.GetComponent<CircleProjector>();
                if (projector != null)
                {
                    projector.m_radius = ComfortRadius;
                }
                ring.SetActive(true);
                activeRings[piece] = ring;
            }
        }

        private void ClearRings()
        {
            foreach (GameObject ring in activeRings.Values)
            {
                if (ring != null)
                {
                    Destroy(ring);
                }
            }
            activeRings.Clear();
        }
    }
}
