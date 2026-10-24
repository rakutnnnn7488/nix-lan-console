using System;

namespace NixLanConsole
{
    internal enum RoleScene
    {
        Daily,
        DailyNight,
        ListeningToMusic,
        HostOffline,
        GameOpening,
        LiveGaming,
        LiveGamingFever,
        AfterGame
    }

    internal enum RoleAnimationCategory
    {
        Unknown,
        AmeNormal,
        AmeHappy,
        AmeAnxiety,
        AmeIraira,
        KangelGame,
        KangelGameFever,
        KangelBright,
        KangelCute,
        KangelGaoo
    }

    internal static class RoleAnimationCatalog
    {
        // The local game asset manifest contains seven variants of each Ame idle
        // family and five Kangel character clips. Unknown entries are excluded.
        internal static RoleAnimationCategory Classify(string name)
        {
            if (String.IsNullOrEmpty(name)) return RoleAnimationCategory.Unknown;
            string[] ameFamilies = { "normal", "happy", "anxiety", "iraira" };
            RoleAnimationCategory[] ameCategories = {
                RoleAnimationCategory.AmeNormal, RoleAnimationCategory.AmeHappy,
                RoleAnimationCategory.AmeAnxiety, RoleAnimationCategory.AmeIraira
            };
            for (int i = 0; i < ameFamilies.Length; i++)
            {
                string prefix = "stream_ame_idle_" + ameFamilies[i] + "_";
                if (name.Length == prefix.Length + 1 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Char.ToLowerInvariant(name[name.Length - 1]) >= 'a' && Char.ToLowerInvariant(name[name.Length - 1]) <= 'g')
                    return ameCategories[i];
            }
            switch (name.ToLowerInvariant())
            {
                case "stream_cho_game": return RoleAnimationCategory.KangelGame;
                case "stream_cho_game_fever": return RoleAnimationCategory.KangelGameFever;
                case "stream_cho_akaruku": return RoleAnimationCategory.KangelBright;
                case "stream_cho_kawaiku": return RoleAnimationCategory.KangelCute;
                case "stream_cho_gaoo": return RoleAnimationCategory.KangelGaoo;
                default: return RoleAnimationCategory.Unknown;
            }
        }

        // Weights are per clip. Seven Ame variants therefore share the family
        // probability, while each Kangel clip has its own deliberate frequency.
        internal static int Weight(RoleScene scene, RoleAnimationCategory category)
        {
            switch (scene)
            {
                case RoleScene.Daily:
                    return category == RoleAnimationCategory.AmeNormal ? 7 : category == RoleAnimationCategory.AmeHappy ? 3 : 0;
                case RoleScene.DailyNight:
                    return category == RoleAnimationCategory.AmeNormal ? 9 : category == RoleAnimationCategory.AmeHappy ? 1 : 0;
                case RoleScene.ListeningToMusic:
                    return category == RoleAnimationCategory.AmeHappy ? 8 : category == RoleAnimationCategory.AmeNormal ? 2 : 0;
                case RoleScene.HostOffline:
                    return category == RoleAnimationCategory.AmeAnxiety ? 7 : category == RoleAnimationCategory.AmeIraira ? 3 : 0;
                case RoleScene.GameOpening:
                    return category == RoleAnimationCategory.KangelCute ? 7 : category == RoleAnimationCategory.KangelBright ? 3 : 0;
                case RoleScene.LiveGaming:
                    return category == RoleAnimationCategory.KangelGame ? 7 :
                        category == RoleAnimationCategory.KangelBright ? 2 :
                        category == RoleAnimationCategory.KangelCute || category == RoleAnimationCategory.KangelGaoo ? 1 : 0;
                case RoleScene.LiveGamingFever:
                    return category == RoleAnimationCategory.KangelGameFever ? 6 :
                        category == RoleAnimationCategory.KangelGame ? 3 :
                        category == RoleAnimationCategory.KangelGaoo ? 1 : 0;
                case RoleScene.AfterGame:
                    return category == RoleAnimationCategory.KangelBright ? 5 :
                        category == RoleAnimationCategory.KangelCute ? 4 :
                        category == RoleAnimationCategory.KangelGaoo ? 1 : 0;
                default: return 0;
            }
        }

        internal static bool IsKangel(RoleScene scene)
        {
            return scene == RoleScene.GameOpening || scene == RoleScene.LiveGaming ||
                scene == RoleScene.LiveGamingFever || scene == RoleScene.AfterGame;
        }

        internal static RoleScene Resolve(bool online, bool kangel, bool gaming, bool musicPlaying,
            bool gameOpening, bool highLoad, int localHour)
        {
            if (!online) return RoleScene.HostOffline;
            if (gaming && kangel)
                return gameOpening ? RoleScene.GameOpening : highLoad ? RoleScene.LiveGamingFever : RoleScene.LiveGaming;
            if (kangel) return RoleScene.AfterGame;
            if (musicPlaying) return RoleScene.ListeningToMusic;
            return localHour >= 23 || localHour < 6 ? RoleScene.DailyNight : RoleScene.Daily;
        }

        internal static string Caption(RoleScene scene)
        {
            switch (scene)
            {
                case RoleScene.DailyNight: return "糖糖 · 深夜待机 ♡";
                case RoleScene.ListeningToMusic: return "糖糖 · 听歌中 ♡";
                case RoleScene.HostOffline: return "糖糖 · 等待主机 ♡";
                case RoleScene.GameOpening: return "超天酱 · 游戏开场 ♡";
                case RoleScene.LiveGaming: return "超天酱 · 游戏运行中 ♡";
                case RoleScene.LiveGamingFever: return "超天酱 · 高能游戏中 ♡";
                case RoleScene.AfterGame: return "超天酱 · 游戏结束后 ♡";
                default: return "糖糖 · 日常待机 ♡";
            }
        }
    }
}
