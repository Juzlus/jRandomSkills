using CounterStrikeSharp.API.Core;
using src.utils;

namespace jRandomSkills.src.utils
{
    public enum KillfeedIcons
    {
        // Pistols
        Deagle,
        Elite,
        FiveSeven,
        Glock,
        Hkp2000,
        P2000,
        P250,
        Tec9,
        Cz75a,
        Revolver,
        UspSilencer,
        UspSilencerOff,

        // Assault Rifles
        Ak47,
        Aug,
        Famas,
        Galilar,
        M4A4,
        M4A1,
        M4A1_off,
        Sg556,

        // Sniper Rifles
        AWP,
        G3SG1,
        Scar20,
        Ssg,

        // Machine Guns
        M249,
        Negev,

        // Submachine Guns
        Bizon,
        Mac10,
        Mp5,
        Mp7,
        Mp9,
        P90,
        Ump45,

        // Shotguns
        Mag7,
        Nova,
        Sawedoff,
        Xm1014,

        // Knives
        Bayonet,
        Knife,
        KnifeBowie,
        KnifeButterfly,
        KnifeCanis,
        KnifeCord,
        KnifeCss,
        KnifeFalchion,
        KnifeFlip,
        KnifeGut,
        KnifeGypsyJackknife,
        KnifeKarambit,
        KnifeKukri,
        KnifeM9Bayonet,
        KnifeOutdoor,
        KnifePush,
        KnifeSkeleton,
        KnifeStiletto,
        KnifeSurvivalBowie,
        KnifeT,
        KnifeTactical,
        KnifeTwinblade,
        KnifeUrsus,
        KnifeWidowmaker,
        Knifegg,

        // Grenades and Explosives
        C4,
        Decoy,
        Flashbang,
        FlashbangAssist,
        Grenadepack,
        Grenadepack2,
        Hegrenade,
        Incgrenade,
        Fire,
        Molotov,
        Smokegrenade,

        // Equipment and Armor
        Armor,
        ArmorHelmet,
        Assaultsuit,
        AssaultsuitHelmetOnly,
        Defuser,
        HeavyArmor,
        Helmet,
        Taser,
        Healthshot,
        Ammobox,
        AmmoboxThreepack,

        // Environmental and Other Damage
        Disconnect,
        Sentry,
        MeleeKnife,
        Fist,
        Explosion,
        Leg,

        // Cosmetics and Effects
        ClothingHands,
        Player,
        Medal,
        Spray
    }

    public static class KillfeedIconsExtensions
    {
        public static string? ToIcon(this KillfeedIcons icon)
        {
            return icon switch
            {
                // Pistols
                KillfeedIcons.Deagle => "deagle",
                KillfeedIcons.Elite => "elite",
                KillfeedIcons.FiveSeven => "fiveseven",
                KillfeedIcons.Glock => "glock",
                KillfeedIcons.Hkp2000 => "hkp2000",
                KillfeedIcons.P2000 => "p2000",
                KillfeedIcons.P250 => "p250",
                KillfeedIcons.Tec9 => "tec9",
                KillfeedIcons.Cz75a => "cz75a",
                KillfeedIcons.Revolver => "revolver",
                KillfeedIcons.UspSilencer => "usp_silencer",
                KillfeedIcons.UspSilencerOff => "usp_silencer_off",

                // Assault Rifles
                KillfeedIcons.Ak47 => "ak47",
                KillfeedIcons.Aug => "aug",
                KillfeedIcons.Famas => "famas",
                KillfeedIcons.Galilar => "galilar",
                KillfeedIcons.M4A4 => "m4a1",
                KillfeedIcons.M4A1 => "m4a1_silencer",
                KillfeedIcons.M4A1_off => "m4a1_silencer_off",
                KillfeedIcons.Sg556 => "sg556",

                // Sniper Rifles
                KillfeedIcons.AWP => "awp",
                KillfeedIcons.G3SG1 => "g3sg1",
                KillfeedIcons.Scar20 => "scar20",
                KillfeedIcons.Ssg => "ssg08",

                // Machine Guns
                KillfeedIcons.M249 => "m249",
                KillfeedIcons.Negev => "negev",

                // Submachine Guns
                KillfeedIcons.Bizon => "bizon",
                KillfeedIcons.Mac10 => "mac10",
                KillfeedIcons.Mp5 => "mp5sd",
                KillfeedIcons.Mp7 => "mp7",
                KillfeedIcons.Mp9 => "mp9",
                KillfeedIcons.P90 => "p90",
                KillfeedIcons.Ump45 => "ump45",

                // Shotguns
                KillfeedIcons.Mag7 => "mag7",
                KillfeedIcons.Nova => "nova",
                KillfeedIcons.Sawedoff => "sawedoff",
                KillfeedIcons.Xm1014 => "xm1014",

                // Knives
                KillfeedIcons.Bayonet => "bayonet",
                KillfeedIcons.Knife => "knife",
                KillfeedIcons.KnifeBowie => "knife_bowie",
                KillfeedIcons.KnifeButterfly => "knife_butterfly",
                KillfeedIcons.KnifeCanis => "knife_canis",
                KillfeedIcons.KnifeCord => "knife_cord",
                KillfeedIcons.KnifeCss => "knife_css",
                KillfeedIcons.KnifeFalchion => "knife_falchion",
                KillfeedIcons.KnifeFlip => "knife_flip",
                KillfeedIcons.KnifeGut => "knife_gut",
                KillfeedIcons.KnifeGypsyJackknife => "knife_gypsy_jackknife",
                KillfeedIcons.KnifeKarambit => "knife_karambit",
                KillfeedIcons.KnifeKukri => "knife_kukri",
                KillfeedIcons.KnifeM9Bayonet => "knife_m9_bayonet",
                KillfeedIcons.KnifeOutdoor => "knife_outdoor",
                KillfeedIcons.KnifePush => "knife_push",
                KillfeedIcons.KnifeSkeleton => "knife_skeleton",
                KillfeedIcons.KnifeStiletto => "knife_stiletto",
                KillfeedIcons.KnifeSurvivalBowie => "knife_survival_bowie",
                KillfeedIcons.KnifeT => "knife_t",
                KillfeedIcons.KnifeTactical => "knife_tactical",
                KillfeedIcons.KnifeTwinblade => "knife_twinblade",
                KillfeedIcons.KnifeUrsus => "knife_ursus",
                KillfeedIcons.KnifeWidowmaker => "knife_widowmaker",
                KillfeedIcons.Knifegg => "knifegg",

                // Grenades and Explosives
                KillfeedIcons.C4 => "c4",
                KillfeedIcons.Decoy => "decoy",
                KillfeedIcons.Flashbang => "flashbang",
                KillfeedIcons.FlashbangAssist => "flashbang_assist",
                KillfeedIcons.Grenadepack => "grenadepack",
                KillfeedIcons.Grenadepack2 => "grenadepack2",
                KillfeedIcons.Hegrenade => "hegrenade",
                KillfeedIcons.Incgrenade => "incgrenade",
                KillfeedIcons.Fire => "inferno",
                KillfeedIcons.Molotov => "molotov",
                KillfeedIcons.Smokegrenade => "smokegrenade",

                // Equipment and Armor
                KillfeedIcons.Armor => "armor",
                KillfeedIcons.ArmorHelmet => "armor_helmet",
                KillfeedIcons.Assaultsuit => "assaultsuit",
                KillfeedIcons.AssaultsuitHelmetOnly => "assaultsuit_helmet_only",
                KillfeedIcons.Defuser => "defuser",
                KillfeedIcons.HeavyArmor => "heavy_armor",
                KillfeedIcons.Helmet => "helmet",
                KillfeedIcons.Taser => "taser",
                KillfeedIcons.Healthshot => "healthshot",
                KillfeedIcons.Ammobox => "ammobox",
                KillfeedIcons.AmmoboxThreepack => "ammobox_threepack",

                // Environmental and Other Damage
                KillfeedIcons.Disconnect => "disconnect",
                KillfeedIcons.Sentry => "dronegun",
                KillfeedIcons.MeleeKnife => "melee",
                KillfeedIcons.Fist => "movelinear",
                KillfeedIcons.Explosion => "prop_exploding_barrel",
                KillfeedIcons.Leg => "stomp_damage",

                // Cosmetics and Effects
                KillfeedIcons.ClothingHands => "clothing_hands",
                KillfeedIcons.Player => "customplayer",
                KillfeedIcons.Medal => "flair0",
                KillfeedIcons.Spray => "spray0",

                _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null)
            };
        }

        public static KillfeedIcons? FromWeapon(CBasePlayerWeapon? weapon)
        {
            string weaponName = SkillUtils.GetDesignerName(weapon);
            return FromWeaponName(weaponName);
        }

        public static KillfeedIcons? FromWeaponName(string? weaponName)
        {
            if (string.IsNullOrWhiteSpace(weaponName))
                return null;

            return weaponName.ToLowerInvariant() switch
            {
                "weapon_deagle" => KillfeedIcons.Deagle,
                "weapon_revolver" => KillfeedIcons.Revolver,
                "weapon_glock" => KillfeedIcons.Glock,
                "weapon_usp_silencer" => KillfeedIcons.UspSilencer,
                "weapon_cz75a" => KillfeedIcons.Cz75a,
                "weapon_fiveseven" => KillfeedIcons.FiveSeven,
                "weapon_p250" => KillfeedIcons.P250,
                "weapon_tec9" => KillfeedIcons.Tec9,
                "weapon_elite" => KillfeedIcons.Elite,
                "weapon_hkp2000" => KillfeedIcons.Hkp2000,
                "weapon_mp9" => KillfeedIcons.Mp9,
                "weapon_mac10" => KillfeedIcons.Mac10,
                "weapon_bizon" => KillfeedIcons.Bizon,
                "weapon_mp7" => KillfeedIcons.Mp7,
                "weapon_ump45" => KillfeedIcons.Ump45,
                "weapon_p90" => KillfeedIcons.P90,
                "weapon_mp5sd" => KillfeedIcons.Mp5,
                "weapon_sg556" => KillfeedIcons.Sg556,
                "weapon_famas" => KillfeedIcons.Famas,
                "weapon_galilar" => KillfeedIcons.Galilar,
                "weapon_m4a4" => KillfeedIcons.M4A4,
                "weapon_m4a1_silencer" => KillfeedIcons.M4A1,
                "weapon_ak47" => KillfeedIcons.Ak47,
                "weapon_aug" => KillfeedIcons.Aug,
                "weapon_ssg08" => KillfeedIcons.Ssg,
                "weapon_awp" => KillfeedIcons.AWP,
                "weapon_scar20" => KillfeedIcons.Scar20,
                "weapon_g3sg1" => KillfeedIcons.G3SG1,
                "weapon_nova" => KillfeedIcons.Nova,
                "weapon_xm1014" => KillfeedIcons.Xm1014,
                "weapon_mag7" => KillfeedIcons.Mag7,
                "weapon_sawedoff" => KillfeedIcons.Sawedoff,
                "weapon_m249" => KillfeedIcons.M249,
                "weapon_negev" => KillfeedIcons.Negev,
                "weapon_taser" => KillfeedIcons.Taser,
                "weapon_molotov" => KillfeedIcons.Molotov,
                "weapon_incgrenade" => KillfeedIcons.Incgrenade,
                "weapon_flashbang" => KillfeedIcons.Flashbang,
                "weapon_smokegrenade" => KillfeedIcons.Smokegrenade,
                "weapon_decoy" => KillfeedIcons.Decoy,
                "weapon_hegrenade" => KillfeedIcons.Hegrenade,
                "weapon_knife" => KillfeedIcons.Knife,
                "weapon_bayonet" => KillfeedIcons.Bayonet,
                "weapon_c4" => KillfeedIcons.C4,

                _ => null
            };
        }
    }
}
