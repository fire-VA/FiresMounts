using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresMounts
{
    /// <summary>
    /// 0.1.121 (Fire 10-08): the ground mounts' own items.
    /// - TAMING: a summon item crafted from the creature's trophy (workbench) calls a tame one beside you: use it from the hotbar or
    ///   the inventory. "Eikthyr's call" (TrophyEikthyr), "Bjorn's call" (TrophyBjorn), "Brute's call" (TrophyGoblinBrute).
    /// - SADDLE: the "Beast saddle" for Eikthyr, Bjorn and the brute (the Sky saddle stays the flyers').
    /// </summary>
    internal static partial class FlyerFactory
    {
        private static GameObject _beastSaddle;
        private static readonly List<GameObject> _calls = new List<GameObject>();
        private static readonly List<Recipe> _groundRecipes = new List<Recipe>();

        internal sealed class Call
        {
            public string Item, Trophy, Mount, Name;
        }

        internal static readonly Call[] Calls =
        {
            new Call { Item = MountNames.EikthyrCallItem, Trophy = "TrophyEikthyr", Mount = MountNames.EikthyrPrefab, Name = "Eikthyr's call" },
            new Call { Item = MountNames.BjornCallItem, Trophy = "TrophyBjorn", Mount = MountNames.BjornPrefab, Name = "Bjorn's call" },
            new Call { Item = MountNames.BruteCallItem, Trophy = "TrophyGoblinBrute", Mount = MountNames.BrutePrefab, Name = "Brute's call" },
            new Call { Item = MountNames.SerpentCallItem, Trophy = "TrophySerpent", Mount = MountNames.SerpentPrefab, Name = "Serpent's call" },
            new Call { Item = MountNames.BonemawCallItem, Trophy = "TrophyBonemawSerpent", Mount = MountNames.BonemawPrefab, Name = "Bonemaw's call" },
        };

        internal static GameObject BeastSaddle => _beastSaddle;

        private static void BuildGroundItems(ObjectDB db)
        {
            if (MountConfig.GroundMountsEnabled == null || !MountConfig.GroundMountsEnabled.Value) return;
            if (_beastSaddle == null)
            {
                var src = FindVanilla("SaddleLox", db);
                if (src != null)
                {
                    _beastSaddle = CloneUnderHolder(src, MountNames.BeastSaddleItem);
                    var shared = _beastSaddle.GetComponent<ItemDrop>().m_itemData.m_shared;
                    shared.m_name = "Beast saddle";
                    shared.m_description = "A broad, heavy saddle. Put it on a tamed Eikthyr, Bjorn or Fuling brute to ride it.";
                    UseIcon(shared, MountNames.BeastSaddleItem);
                }
            }
            if (_calls.Count > 0) return;
            foreach (Call call in Calls)
            {
                var src = FindVanilla(call.Trophy, db);
                if (src == null) { FiresLogger.LogWarning($"no vanilla '{call.Trophy}': {call.Name} not made"); continue; }
                var go = CloneUnderHolder(src, call.Item);
                var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_name = call.Name;
                shared.m_description = "Use it to call a tame one to your side. It answers once, and the call is spent.";
                shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
                shared.m_consumeStatusEffect = null;
                shared.m_maxStackSize = 1;
                shared.m_value = 0;
                shared.m_teleportable = true;
                _calls.Add(go);
            }
        }

        private static void RegisterGroundItems(ObjectDB db)
        {
            bool changed = false;
            foreach (var go in GroundItems()) if (go != null && !db.m_items.Contains(go)) { db.m_items.Add(go); changed = true; }
            if (changed) s_updateRegisters?.Invoke(db, null);
            if (db.m_recipes == null) return;
            if (_groundRecipes.Count == 0)
            {
                var station = FindVanilla("piece_workbench", db);
                if (station == null) return;   // retried on the next ObjectDB pass
                if (_beastSaddle != null)
                {
                    var reqs = new List<Piece.Requirement>();
                    AddReq(reqs, db, "DeerHide", 10);
                    AddReq(reqs, db, "LeatherScraps", 10);
                    AddReq(reqs, db, "BronzeNails", 10);
                    if (reqs.Count == 3) _groundRecipes.Add(MakeRecipe(_beastSaddle, station, 2, reqs));
                }
                for (int i = 0; i < _calls.Count; i++)
                {
                    Call call = System.Array.Find(Calls, c => c.Item == _calls[i].name);
                    if (call == null) continue;
                    var reqs = new List<Piece.Requirement>();
                    AddReq(reqs, db, call.Trophy, 1);
                    AddReq(reqs, db, "LeatherScraps", 5);
                    if (reqs.Count == 2) _groundRecipes.Add(MakeRecipe(_calls[i], station, 1, reqs));
                }
            }
            foreach (var r in _groundRecipes) if (!db.m_recipes.Contains(r)) db.m_recipes.Add(r);
        }

        private static Recipe MakeRecipe(GameObject item, GameObject station, int level, List<Piece.Requirement> reqs)
        {
            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + item.name;
            recipe.m_item = item.GetComponent<ItemDrop>();
            recipe.m_amount = 1;
            recipe.m_craftingStation = station.GetComponent<CraftingStation>();
            recipe.m_minStationLevel = level;
            recipe.m_resources = reqs.ToArray();
            Object.DontDestroyOnLoad(recipe);
            return recipe;
        }

        private static IEnumerable<GameObject> GroundItems()
        {
            if (_beastSaddle != null) yield return _beastSaddle;
            foreach (var c in _calls) yield return c;
        }

        // A call used (hotbar or inventory): a tame one appears 4 m ahead, following you; the call is spent.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        internal static class UseCall
        {
            [HarmonyPrefix]
            private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
            {
                if (!(__instance is Player player) || player != Player.m_localPlayer || item?.m_dropPrefab == null) return true;
                Call call = System.Array.Find(Calls, c => c.Item == item.m_dropPrefab.name);
                if (call == null) return true;
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(call.Mount) : null;
                if (prefab == null) { player.Message(MessageHud.MessageType.Center, call.Name + ": nothing answers ([Mounts.Flyers] GroundMounts is off)"); return false; }
                Vector3 dir = player.transform.forward; dir.y = 0f;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                Vector3 at = player.transform.position + dir * 4f;
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out float ground)) at.y = ground + 0.3f;
                // The water pair answers only in the water, at its surface.
                bool water = call.Mount == MountNames.SerpentPrefab || call.Mount == MountNames.BonemawPrefab;
                if (water)
                {
                    WaterVolume volume = null;
                    float sea = Floating.GetWaterLevel(at, ref volume);   // the live surface (Galaxies' tide moves it off m_waterLevel)
                    if (at.y > sea - 1.5f) { player.Message(MessageHud.MessageType.Center, call.Name + ": it answers only in deep water"); return false; }
                    at.y = sea;
                }
                GameObject go = Object.Instantiate(prefab, at, Quaternion.LookRotation(-dir));
                var ai = go.GetComponent<MonsterAI>();
                if (ai != null) { ai.MakeTame(); ai.SetFollowTarget(player.gameObject); }
                else go.GetComponent<Character>()?.SetTamed(true);
                (inventory ?? player.GetInventory()).RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, $"{go.GetComponent<Character>()?.GetHoverName() ?? call.Mount} answers your call");
                FiresMountsPlugin.Log.LogDebug($"[FiresMounts] [Call] {call.Item} used: {call.Mount} tamed at {at}, following {player.GetPlayerName()}");
                return false;
            }
        }
    }
}
