using UnityEngine;

namespace Unlikely.UI
{
    /// <summary>
    /// Which icon each item shows (Art/Processed/icons, cut from art_raw/item_icons.png by tools/slice_icons.py).
    /// Note-type items (codes, logins, the slip) also get their information written on the icon.
    /// </summary>
    public static class ItemIcons
    {
        /// <summary>The icon name ("icon_key"...) for an item id, or null.</summary>
        public static string For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("key_")) return "icon_key";
            if (id.StartsWith("keycard_")) return "icon_keycard";
            if (id == "code_enc") return "icon_note_lock";
            if (id.StartsWith("code_") || id == "creds") return "icon_note";
            switch (id)
            {
                case "usb": return "icon_cable";
                case "slip": return "icon_slip";
                case "budget":
                case "keymap": return "icon_printout";
                case "coin":
                case "coins": return "icon_coin";
                case "coffee": return "icon_coffee";
                case "crisps": return "icon_crisps";
                case "flashlight": return "icon_torch";
                case "pen_drive": return "icon_pendrive";
                case "tape": return "icon_tape";
                case "restructure": return "icon_folder";
                case "magicorp": return "icon_hand";
                case Unlikely.Core.Inventory.Permanent: return "icon_book";
            }
            return null;
        }

        /// <summary>A tint so look-alike items differ (the CEO's keycard is gold).</summary>
        public static Color Tint(string id) => id == "keycard_ceo" ? new Color(1f, 0.85f, 0.45f) : Color.white;

        /// <summary>
        /// What's written on a note icon: the part after the colon ("Office B code: 3172" -> "3172"); null for other items.
        /// </summary>
        public static string Writing(string id, string name)
        {
            string icon = For(id);
            if (icon != "icon_note" && icon != "icon_slip" && icon != "icon_note_lock") return null;
            int colon = name.IndexOf(':');
            string after = colon >= 0 ? name.Substring(colon + 1).Trim() : "";
            if (id == "slip") after = after.Replace("vendor", "").Trim();
            return after.Length > 0 ? after : null;
        }

        /// <summary>The card's short label: the name before the colon ("Office B code: 3172" -> "Office B code").</summary>
        public static string ShortName(string name)
        {
            int colon = name.IndexOf(':');
            string s = colon > 0 ? name.Substring(0, colon) : name;
            int paren = s.IndexOf(" (");
            return paren > 0 ? s.Substring(0, paren) : s;
        }
    }
}
