using System;
using System.Collections.Generic;

namespace Unlikely.Core
{
    /// <summary>
    /// Donnie's pockets. Items are picked with the number keys (1 = first item, ...); the equipped one shows
    /// at the bottom centre of the screen. Some actions need the right item in hand (mop, coin, cable).
    /// </summary>
    public static class Inventory
    {
        public sealed class Item
        {
            public readonly string id, name;
            public Item(string id, string name) { this.id = id; this.name = name; }
        }

        private static readonly List<Item> items = new List<Item>();

        public static IReadOnlyList<Item> Items => items;
        public static int EquippedIndex { get; private set; } = -1;
        public static Item Equipped => EquippedIndex >= 0 && EquippedIndex < items.Count ? items[EquippedIndex] : null;

        /// <summary>Raised whenever items or the equipped slot change (the HUD redraws).</summary>
        public static event Action Changed;

        public static bool Has(string id) => items.Exists(i => i.id == id);
        public static bool IsEquipped(string id) => Equipped != null && Equipped.id == id;

        public static void Add(string id, string name, bool equip = true)
        {
            if (!Has(id)) items.Add(new Item(id, name));
            if (equip) EquippedIndex = items.FindIndex(i => i.id == id);
            Changed?.Invoke();
        }

        /// <summary>Vibe's Theory of Everything: nothing in the story takes it away.</summary>
        public const string Permanent = "theory";

        public static void Remove(string id)
        {
            if (id == Permanent) return;
            int idx = items.FindIndex(i => i.id == id);
            if (idx < 0) return;
            items.RemoveAt(idx);
            if (EquippedIndex == idx) EquippedIndex = items.Count > 0 ? 0 : -1;
            else if (EquippedIndex > idx) EquippedIndex--;
            Changed?.Invoke();
        }

        public static void Equip(int index)
        {
            if (index < 0 || index >= items.Count) return;
            EquippedIndex = index;
            Changed?.Invoke();
        }

        /// <summary>New game: empty pockets and forget old listeners (destroyed HUDs from the last play).</summary>
        public static void ResetStatic()
        {
            items.Clear();
            EquippedIndex = -1;
            Changed = null;
        }

        public static void Clear()
        {
            items.Clear();
            EquippedIndex = -1;
            Changed?.Invoke();
        }
    }
}
