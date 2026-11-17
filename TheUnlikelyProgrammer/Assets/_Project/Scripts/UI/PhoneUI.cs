using System;
using System.Collections.Generic;
using System.Text;
using Unlikely.Core;
using Unlikely.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unlikely.UI
{
    /// <summary>
    /// Donnie's phone (Tab, or gamepad Select). Home screen apps: Messages, Calls (won't open), and Manual once
    /// it's downloaded from the kidnappers' message. Also shows incoming calls: the phone rings until answered (E).
    /// Keys: ↑↓ choose, Enter/E open, Esc/Backspace back, Tab close.
    /// Floor 3: a forced software update (<see cref="SoftwareUpdate"/>) fixes Calls and adds the Bank app
    /// (Donnie's savings, and shares in GyaraCorp / MagiCorp; MagiCorp can only be bought once he knows why).
    /// </summary>
    public class PhoneUI : MonoBehaviour
    {
        private enum Screen { Home, Threads, Thread, Manual, Lesson, Ringing, Gyara, Updating, Calls, Bank }

        // the Bank app (Floor 3). Donnie's savings, all of it, and two shares he could put it in.
        public const string Savings = "£3,412.18";
        public const string SavingsAfter = "£12.18";
        private static readonly string[] BankOptions = { "Buy GyaraCorp (GYRA)", "Buy MagiCorp (MAGI)" };

        /// <summary>Raised when Donnie puts his savings into MagiCorp.</summary>
        public event Action StocksBought;

        private float updateT = -1f;
        private string updateText;
        private Action onUpdated;

        private static readonly string[] FloorNames = { "General", "Administration", "Human Resources", "Executive Offices", "Server Room" };

        public static PhoneUI Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public bool IsRinging => IsOpen && screen == Screen.Ringing;

        /// <summary>Raised when the Manual app finishes installing.</summary>
        public event Action ManualInstalled;

        private GameObject root;
        private RectTransform shell;
        private Text clock, battery, statusLine, header, body, footer;
        private RectTransform viewport;
        private float scroll;          // how far the screen's text is scrolled down (px)
        private int listCount = -1;    // list screens: number of entries (keeps the selection on screen)
        private Screen lastScreen;
        private Image progressFill;
        private GameObject progressBar;
        private GameObject homeGrid;
        private float openedAt = -10f;
        private static readonly Dictionary<string, (string glyph, Color32 color)> AppLooks = new Dictionary<string, (string, Color32)>
        {
            { "Messages", ("\u2709", new Color32(0x3C, 0xA8, 0x5A, 0xFF)) },
            { "Calls", ("\u260E", new Color32(0x2F, 0x7F, 0xD8, 0xFF)) },
            { "GyaraCorp", ("", new Color32(0x22, 0x26, 0x2E, 0xFF)) },   // the logo from the building (ArtLibrary "logo_g")
            { "KODEX", (">_", new Color32(0x1A, 0x20, 0x14, 0xFF)) },
            { "INFI Bank", ("\u221E", new Color32(0x1E, 0x2C, 0x6E, 0xFF)) },
        };

        private Screen screen;
        private int selected, openThread, openLesson;
        private string note;
        private string caller;
        private Action onAnswer;
        private bool answered;
        private float downloadT = -1f;
        private PhoneData.Thread downloading;

        private void Awake()
        {
            Instance = this;
            Build();
            root.SetActive(false);
            PhoneData.MessageArrived += OnMessage;
        }

        private void OnDestroy()
        {
            PhoneData.MessageArrived -= OnMessage;
            if (Instance == this) Instance = null;
        }

        private void OnMessage(PhoneData.Thread t)
        {
            HudUI.Instance?.SetPhoneUnread(PhoneData.AnyUnread);
            if (!t.messages[t.messages.Count - 1].fromMe) HudUI.Instance?.Toast("New message: " + t.contact);
            if (IsOpen) Refresh();
        }

        /// <summary>Make the phone ring. It opens on the call screen; answering runs onAnswered.</summary>
        public void Ring(string from, Action onAnswered)
        {
            caller = from;
            onAnswer = onAnswered;
            root.SetActive(true);
            screen = Screen.Ringing;
            Sfx.Ring(true);
            Refresh();
        }

        /// <summary>Answer a ringing call (same as pressing E).</summary>
        public void Answer()
        {
            if (IsRinging) answered = true;
        }

        public void Open()
        {
            screen = Screen.Home;
            selected = 0;
            note = null;
            if (!root.activeSelf) { openedAt = Time.unscaledTime; Sfx.Play(Sfx.Id.Confirm, 0.5f, 1.2f); }
            root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (screen == Screen.Ringing) return;
            root.SetActive(false);
            HudUI.Instance?.SetPhoneUnread(PhoneData.AnyUnread);
        }

        private List<string> HomeApps()
        {
            var apps = new List<string> { "Messages", "Calls", "GyaraCorp" };
            if (GameState.HasManual) apps.Add("KODEX");
            if (GameState.Has("bank_app")) apps.Add("INFI Bank");
            return apps;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool toggle = (kb != null && kb.tabKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame);

            if (downloadT >= 0f) TickDownload();
            if (updateT >= 0f) { TickUpdate(); return; }

            if (!IsOpen)
            {
                bool blocked = UiKit.AnyModalOpen || (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen)
                               || (World.RoomManager.Instance != null && World.RoomManager.Instance.IsTransitioning)
                               || Player.PlayerController.Frozen;
                if (toggle && !blocked) Open();
                return;
            }

            // ringing pulse
            if (screen == Screen.Ringing)
            {
                shell.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 40f) * (Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.6f ? 2f : 0f), 0);
                bool answer = (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                              || (pad != null && pad.buttonSouth.wasPressedThisFrame);
                if (answered)
                {
                    // one frame after the key press, so the same E doesn't also skip the first line of the call
                    answered = false;
                    Sfx.Ring(false);
                    if (caller == "Unknown number") Music.KidnapperCall();   // The Usual Suspects / Three's Company
                    Sfx.Play(Sfx.Id.Confirm, 0.6f);
                    shell.anchoredPosition = Vector2.zero;
                    screen = Screen.Home;
                    root.SetActive(false);
                    var cb = onAnswer;
                    onAnswer = null;
                    cb?.Invoke();
                }
                else if (answer) answered = true;
                return;
            }

            if (toggle && screen != Screen.Updating) { Close(); return; }
            if (kb == null) return;

            bool back = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            bool ok = kb.enterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            int move = (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame ? 1 : 0);
            if (screen != Screen.Ringing)
            {
                if (move != 0) Sfx.Play(Sfx.Id.Move, 0.7f);
                if (ok) Sfx.Play(Sfx.Id.Confirm, 0.6f);
                if (back) Sfx.Play(Sfx.Id.Back, 0.6f);
            }

            switch (screen)
            {
                case Screen.Home:
                {
                    var apps = HomeApps();
                    if (back) { Close(); return; }
                    if (move != 0) { selected = (selected + move + apps.Count) % apps.Count; note = null; }
                    if (ok)
                    {
                        string app = apps[selected];
                        if (app == "Messages") { screen = Screen.Threads; selected = 0; }
                        else if (app == "Calls")
                        {
                            if (GameState.Has("phone_updated")) screen = Screen.Calls;
                            else note = "The call app won't open. It never does when I need it.";
                        }
                        else if (app == "INFI Bank") { screen = Screen.Bank; selected = 0; }
                        else if (app == "KODEX") { screen = Screen.Manual; selected = 0; }
                        else if (app == "GyaraCorp") screen = Screen.Gyara;
                    }
                    break;
                }
                case Screen.Threads:
                    if (back) { screen = Screen.Home; selected = 0; break; }
                    if (PhoneData.Threads.Count == 0) break;
                    if (move != 0) selected = (selected + move + PhoneData.Threads.Count) % PhoneData.Threads.Count;
                    if (ok) { openThread = selected; PhoneData.Threads[openThread].unread = false; screen = Screen.Thread; HudUI.Instance?.SetPhoneUnread(PhoneData.AnyUnread); }
                    break;
                case Screen.Thread:
                    if (back) { screen = Screen.Threads; selected = openThread; break; }
                    scroll += move * 40f;
                    if (ok && downloadT < 0f && PendingAttachment(PhoneData.Threads[openThread]) != null)
                    {
                        downloading = PhoneData.Threads[openThread];
                        downloadT = 0f;
                    }
                    break;
                case Screen.Manual:
                {
                    var lessons = ManualLessons.Unlocked();
                    if (back) { screen = Screen.Home; selected = HomeApps().IndexOf("KODEX"); break; }
                    if (lessons.Count == 0) break;
                    if (move != 0) selected = (selected + move + lessons.Count) % lessons.Count;
                    if (ok) { openLesson = selected; screen = Screen.Lesson; }
                    break;
                }
                case Screen.Lesson:
                    if (back) { screen = Screen.Manual; selected = openLesson; }
                    else scroll += move * 40f;
                    break;
                case Screen.Gyara:
                    if (back) { screen = Screen.Home; selected = HomeApps().IndexOf("GyaraCorp"); }
                    else scroll += move * 40f;
                    break;
                case Screen.Calls:
                    if (back) { screen = Screen.Home; selected = HomeApps().IndexOf("Calls"); }
                    break;
                case Screen.Bank:
                    if (back) { screen = Screen.Home; selected = HomeApps().IndexOf("INFI Bank"); note = null; break; }
                    if (move != 0) { selected = (selected + move + BankOptions.Length) % BankOptions.Length; note = null; }
                    if (ok) Buy(selected);
                    break;
            }
            if (move != 0 || ok || back) Refresh();
        }

        /// <summary>Does Donnie know enough to bet on MagiCorp? Ryker's MagiCorp login (Floor 2) and the executives' mail.</summary>
        public static bool KnowsAboutMagiCorp => GameState.Has("f2_magicorp") && GameState.Has("f3_mails_read");

        /// <summary>The Bank app only ever turns up if Ryker was discovered (the lie on Floor 2): it's the capitalist ending's door.</summary>
        public static bool BankAllowed => GameState.Has("d2_lie") || GameState.Has("f2_magicorp") || GameState.Has("ryker_discovered");

        /// <summary>Bank app: 0 = GyaraCorp, 1 = MagiCorp (also used by tests).</summary>
        public void Buy(int option)
        {
            if (GameState.Has("f3_stocks")) { note = "It's all in MagiCorp already. Twelve quid left. Not buying a sandwich with that."; return; }
            if (!KnowsAboutMagiCorp)
            {
                note = option == 0
                    ? "Put Betty's college money into the company that's holding her hostage? No."
                    : "MagiCorp's going under. I'm not betting Betty's college money on a hunch.";
                return;
            }
            if (option == 0) { note = "No. Not GyaraCorp. The other one's the one waiting for a push."; return; }
            GameState.Set("f3_stocks");
            HudUI.Instance?.Toast("Bought: MagiCorp shares");
            root.SetActive(false);
            StocksBought?.Invoke();
        }

        /// <summary>
        /// The phone restarts itself, insists on an update, installs it. Afterwards Calls opens and there's a Bank app.
        /// Keys do nothing until it's done; then the phone closes and onDone runs.
        /// </summary>
        public void SoftwareUpdate(Action onDone)
        {
            onUpdated = onDone;
            updateT = 0f;
            root.SetActive(true);
            screen = Screen.Updating;
            Refresh();
        }

        public bool IsUpdating => updateT >= 0f;

        private void TickUpdate()
        {
            updateT += Time.unscaledDeltaTime;
            string before = updateText;
            if (updateT < 1.6f) updateText = "\n\n\n<size=18><b>Restarting...</b></size>";
            else if (updateT < 4.4f)
                updateText = "\n\n<size=16><b>Software update required</b></size>\n\n<size=12>PhoneOS 14.2.1\nSecurity fixes. Call app stability." +
                             (BankAllowed ? "\nNew: INFI Bank, pre-installed.\n(Cannot be removed.)" : "") + "</size>\n\nInstalling...";
            else if (updateT < 6.2f) updateText = "\n\n\n<size=16><b>Update complete</b></size>\n\n<size=12>" + (BankAllowed ? "New app: INFI Bank" : "Calls: fixed") + "</size>";
            else
            {
                updateT = -1f;
                progressBar.SetActive(false);
                GameState.Set("phone_updated");
                if (BankAllowed) GameState.Set("bank_app");
                screen = Screen.Home;
                root.SetActive(false);
                HudUI.Instance?.Toast(BankAllowed ? "New app: INFI Bank" : "Phone updated");
                var cb = onUpdated;
                onUpdated = null;
                cb?.Invoke();
                return;
            }
            progressBar.SetActive(updateT >= 1.6f && updateT < 4.4f);
            progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((updateT - 1.6f) / 2.8f), 1);
            if (before != updateText) Refresh();
        }

        private static PhoneData.Message PendingAttachment(PhoneData.Thread t)
        {
            foreach (var m in t.messages)
                if (m.attachment == "manual" && !GameState.HasManual) return m;
            return null;
        }

        private void TickDownload()
        {
            downloadT += Time.unscaledDeltaTime / 2.2f;
            progressBar.SetActive(IsOpen && screen == Screen.Thread);
            progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(downloadT), 1);
            if (downloadT < 1f) return;
            downloadT = -1f;
            progressBar.SetActive(false);
            GameState.Set("has_manual");
            HudUI.Instance?.Toast("KODEX installed");
            ManualInstalled?.Invoke();
            if (IsOpen) Refresh();
        }

        private void LateUpdate()
        {
            // the phone slides up into view when it's taken out
            if (!IsOpen || screen == Screen.Ringing) return;
            float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / 0.16f);
            float y = -36f * (1f - t) * (1f - t);
            if (shell.anchoredPosition.y != y) shell.anchoredPosition = new Vector2(0f, y);
        }

        private void Refresh()
        {
            clock.text = GameClock.Text;
            battery.text = "▂▄▆  " + GameClock.Battery + "%";
            int c = Mathf.Clamp(GameState.Clearance, 0, FloorNames.Length - 1);
            statusLine.text = "Clearance: " + FloorNames[c];
            progressBar.SetActive((downloadT >= 0f && screen == Screen.Thread) || (screen == Screen.Updating && updateT >= 1.6f && updateT < 4.4f));

            var sb = new StringBuilder();
            if (screen != lastScreen) { scroll = 0f; lastScreen = screen; }
            listCount = -1;
            homeGrid.SetActive(false);   // only the home screen shows the app tiles
            switch (screen)
            {
                case Screen.Updating:
                    header.text = "SYSTEM";
                    sb.Append(updateText ?? "");
                    footer.text = "Please don't turn off your phone";
                    break;

                case Screen.Calls:
                    header.text = "CALLS";
                    sb.Append("<b>Unknown number</b>\n<size=11><color=#6B665C>incoming  ·  today  ·  more than I'd like</color></size>\n\n");
                    sb.Append("<b>Betty</b>\n<size=11><color=#6B665C>outgoing  ·  last night  ·  1 min</color></size>\n\n");
                    sb.Append("<b>Dentist</b>\n<size=11><color=#6B665C>missed  ·  3 weeks ago</color></size>\n\n");
                    sb.Append("<i><color=#6B665C>It works now. Of course it does.</color></i>");
                    footer.text = "Esc  back      Tab  close";
                    break;

                case Screen.Bank:
                {
                    bool bought = GameState.Has("f3_stocks");
                    header.text = "INFI BANK";
                    sb.Append("<size=11>CURRENT ACCOUNT  ·  D. RAYMOND</size>\n<size=20><b>").Append(bought ? SavingsAfter : Savings).Append("</b></size>\n");
                    sb.Append("<size=11><color=#6B665C>\"Betty - college\"  (savings pot)</color></size>\n\n");
                    sb.Append("<size=11><b>MARKETS</b></size>\n");
                    sb.Append("GYRA  GyaraCorp      <b>412.50</b>  <color=#2E7D3A>▲ 0.4%</color>\n");
                    sb.Append("MAGI  MagiCorp         <b>0.83</b>  <color=#C0392B>▼ 61%</color>\n\n");
                    if (bought) sb.Append("<size=11><b>MY SHARES</b></size>\nMAGI  ×  4,098\n\n");
                    for (int i = 0; i < BankOptions.Length; i++) Line(sb, BankOptions[i], i == selected);
                    if (note != null) sb.Append("<i><color=#6B665C>").Append(note).Append("</color></i>");
                    footer.text = "↑↓  choose      Enter  buy      Esc  back";
                    break;
                }

                case Screen.Ringing:
                    header.text = "INCOMING CALL";
                    sb.Append("\n\n\n<size=20><b>").Append(caller).Append("</b></size>\n\ncalling...");
                    footer.text = "E  answer";
                    break;

                case Screen.Home:
                {
                    header.text = "HOME";
                    var apps = HomeApps();
                    ShowHomeGrid(apps);
                    // the grid takes the top of the page; a note (e.g. the call app refusing) goes under it
                    if (note != null) sb.Append(new string('\n', 2 + 6 * ((apps.Count + 2) / 3))).Append("<i><color=#6B665C>").Append(note).Append("</color></i>");
                    footer.text = "W/S  choose      Enter  open      Tab  close";
                    break;
                }

                case Screen.Threads:
                    header.text = "MESSAGES";
                    if (PhoneData.Threads.Count == 0) sb.Append("<i>No messages.</i>");
                    for (int i = 0; i < PhoneData.Threads.Count; i++)
                    {
                        var t = PhoneData.Threads[i];
                        string last = t.messages[t.messages.Count - 1].text;
                        if (last.Length > 26) last = last.Substring(0, 25) + "…";
                        Line(sb, (t.unread ? "<color=#D0453A>●</color> " : "") + t.contact + "\n      <size=11><color=#6B665C>" + last + "</color></size>", i == selected);
                    }
                    footer.text = "↑↓  choose      Enter  open      Esc  back";
                    break;

                case Screen.Thread:
                {
                    var t = PhoneData.Threads[openThread];
                    t.unread = false;   // on screen = read
                    header.text = t.contact.ToUpperInvariant();
                    foreach (var m in t.messages)
                    {
                        sb.Append(m.fromMe ? "<color=#2F5E9E><b>Me:</b> " : "").Append(m.text).Append(m.fromMe ? "</color>" : "").Append("\n");
                        if (m.attachment == "manual")
                            sb.Append(GameState.HasManual ? "<color=#3C8A4A>[ KODEX.apk  installed ]</color>\n" : "<color=#B07A1F><b>[ KODEX.apk  -  Enter to download ]</b></color>\n");
                        sb.Append("\n");
                    }
                    footer.text = "Esc  back      Tab  close";
                    break;
                }

                case Screen.Manual:
                {
                    header.text = "KODEX";
                    var lessons = ManualLessons.Unlocked();
                    if (lessons.Count == 0) sb.Append("<i>Nothing learned yet. Plug into an employee terminal.</i>");
                    for (int i = 0; i < lessons.Count; i++) Line(sb, lessons[i].title, i == selected);
                    listCount = lessons.Count;
                    footer.text = "↑↓  choose      Enter  open      Esc  back";
                    break;
                }

                case Screen.Gyara:
                {
                    // the company's staff app: Donnie's badge and when it lets him onto each floor
                    header.text = "GYARACORP  ›  MY BADGE";
                    sb.Append("<b>Donald Raymond</b>\n<size=11>Facilities  ·  ").Append(Access.JanitorNumber)
                      .Append("  ·  Badge ").Append(Access.BadgeId).Append("</size>\n\n");
                    sb.Append("<size=11><b>MY ACCESS</b></size>\n");
                    for (int i = 0; i < Access.FloorNames.Length; i++)
                    {
                        bool ok = Access.Allowed(i) && (i < 4 || GameState.Clearance >= 4);
                        sb.Append(ok ? "<color=#2E7D3A>●</color> " : "<color=#9A968C>○</color> ")
                          .Append(i < 4 ? i + "  " : "").Append(Access.FloorNames[i]).Append("\n")
                          .Append("      <size=11><color=#6B665C>").Append(Access.Schedule(i)).Append("</color></size>\n");
                    }
                    sb.Append("\n<size=10><i><color=#8A857A>Facilities runs janitors on rotating slots. Badges outside their slot are refused at the elevator.</color></i></size>");
                    footer.text = "Esc  back      Tab  close";
                    break;
                }

                case Screen.Lesson:
                {
                    var lessons = ManualLessons.Unlocked();
                    var l = lessons[Mathf.Clamp(openLesson, 0, lessons.Count - 1)];
                    header.text = "KODEX  ›  " + l.title.ToUpperInvariant();
                    sb.Append(l.body).Append("\n\n<b>Example</b>\n<color=#2F5E9E>").Append(l.example).Append("</color>");
                    footer.text = "↑↓  scroll      Esc  back      Tab  close";
                    break;
                }
            }
            body.text = sb.ToString();
            ApplyScroll();
            HudUI.Instance?.SetPhoneUnread(PhoneData.AnyUnread);
        }

        /// <summary>Long pages scroll (W/S); long lists scroll to keep the selected entry on screen.</summary>
        private void ApplyScroll()
        {
            float h = body.preferredHeight;
            body.rectTransform.sizeDelta = new Vector2(0f, h);
            float view = viewport.rect.height;
            if (listCount > 0)
            {
                float item = h / listCount;
                if (selected * item < scroll) scroll = selected * item;
                if ((selected + 1) * item > scroll + view) scroll = (selected + 1) * item - view;
            }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, h - view));
            body.rectTransform.anchoredPosition = new Vector2(0f, scroll);
        }

        /// <summary>The home screen: app tiles, three across. The selected one gets a ring; Messages shows an unread dot.</summary>
        private void ShowHomeGrid(List<string> apps)
        {
            foreach (Transform c in homeGrid.transform) Destroy(c.gameObject);
            homeGrid.SetActive(true);
            const float tile = 50f, cellW = 76f, cellH = 82f;
            // three columns, the block centred on the screen (x is each cell's left edge, from the centre)
            for (int i = 0; i < apps.Count; i++)
            {
                AppLooks.TryGetValue(apps[i], out var look);
                float x = -1.5f * cellW + (i % 3) * cellW, y = -(i / 3) * cellH;
                float pad = (cellW - tile) / 2f;
                bool sel = i == selected;
                var top = new Vector2(0.5f, 1);
                if (sel)
                    UiKit.RoundBox("Ring", homeGrid.transform, new Color32(0xB0, 0x7A, 0x1F, 0xFF), top, top,
                        new Vector2(x + pad - 3, y - tile - 9), new Vector2(x + pad + tile + 3, y - 3), 1.2f);
                var t = UiKit.RoundBox("App" + i, homeGrid.transform, look.glyph != null ? (Color)look.color : new Color(0.3f, 0.3f, 0.35f), top, top,
                    new Vector2(x + pad, y - tile - 6), new Vector2(x + pad + tile, y - 6), 1.2f);
                var logo = apps[i] == "GyaraCorp" ? ArtLibrary.Get("logo_g") : null;
                if (logo != null)
                {
                    var img = UiKit.Box("Logo", t.transform, Color.white, Vector2.zero, Vector2.one, new Vector2(7, 7), new Vector2(-7, -7));
                    img.sprite = logo;
                    img.preserveAspect = true;
                }
                else
                {
                    var g = UiKit.Label("Glyph", t.transform, GlyphFont, apps[i] == "KODEX" ? 17 : 24, apps[i] == "KODEX" ? (Color)UiKit.Lime : Color.white,
                        Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, FontStyle.Bold);
                    g.text = string.IsNullOrEmpty(look.glyph) ? apps[i].Substring(0, 1) : look.glyph;
                }
                if (apps[i] == "Messages" && PhoneData.AnyUnread)
                    UiKit.RoundBox("Unread", t.transform, new Color32(0xD0, 0x45, 0x3A, 0xFF), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(4, 4), 1f);
                var l = UiKit.Label("Name", homeGrid.transform, UiKit.UiFont, 12,sel ? new Color32(0x7A, 0x52, 0x10, 0xFF) : new Color32(0x3A, 0x3C, 0x44, 0xFF),
                    top, top, TextAnchor.UpperCenter, sel ? FontStyle.Bold : FontStyle.Normal,
                    new Vector2(x, y - tile - 26), new Vector2(x + cellW, y - tile - 10));
                l.text = apps[i];
            }
        }

        private static Font glyphFont;
        /// <summary>Symbols (envelope, handset) live in the system symbol fonts, not the UI font.</summary>
        private static Font GlyphFont => glyphFont != null ? glyphFont
            : (glyphFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Segoe UI", "Apple Symbols", "DejaVu Sans", "Noto Sans Symbols2", "Symbola", "Arial Unicode MS", "Arial" }, 24));

        private static void Line(StringBuilder sb, string text, bool sel) =>
            sb.Append(sel ? "<color=#B07A1F><b>›  " : "    ").Append(text).Append(sel ? "</b></color>" : "").Append("\n\n");

        private void Build()
        {
            var canvas = UiKit.MakeCanvas(transform, "PhoneCanvas", 130);
            root = UiKit.Box("Phone", canvas.transform, new Color(0, 0, 0, 0.55f), Vector2.zero, Vector2.one).gameObject;

            var holder = new GameObject("Holder", typeof(RectTransform));
            holder.transform.SetParent(root.transform, false);
            shell = UiKit.Rect(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-135, -250), new Vector2(135, 250));

            var body0 = UiKit.Framed("Body", holder.transform, new Color32(0x1C, 0x1D, 0x22, 0xFF), new Color32(0x45, 0x46, 0x4E, 0xFF),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 3f);
            UiKit.Box("Earpiece", body0.transform, new Color32(0x0C, 0x0C, 0x0F, 0xFF), new Vector2(0.38f, 1), new Vector2(0.62f, 1), new Vector2(0, -12), new Vector2(0, -7));
            UiKit.Box("Crack", body0.transform, new Color(1, 1, 1, 0.12f), new Vector2(0.7f, 0.08f), new Vector2(0.71f, 0.3f)); // it's had a life
            var screenImg = UiKit.Box("Screen", body0.transform, new Color32(0xF2, 0xEF, 0xE8, 0xFF), Vector2.zero, Vector2.one, new Vector2(10, 22), new Vector2(-10, -22));

            var bar = UiKit.Box("StatusBar", screenImg.transform, new Color32(0x1C, 0x1D, 0x22, 0xFF), new Vector2(0, 1), Vector2.one, new Vector2(0, -18), Vector2.zero);
            clock = UiKit.Label("Clock", bar.transform, UiKit.UiFont, 11, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(8, 0), Vector2.zero);
            battery = UiKit.Label("Signal", bar.transform, UiKit.UiFont, 11, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleRight, FontStyle.Normal, Vector2.zero, new Vector2(-8, 0));

            var head = UiKit.Box("Header", screenImg.transform, new Color32(0x2A, 0x2F, 0x3A, 0xFF), new Vector2(0, 1), Vector2.one, new Vector2(0, -52), new Vector2(0, -18));
            header = UiKit.Label("Title", head.transform, UiKit.UiFont, 14, Color.white, Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(10, 0), Vector2.zero);

            statusLine = UiKit.Label("Clearance", screenImg.transform, UiKit.UiFont, 11, new Color32(0x6B, 0x66, 0x5C, 0xFF), new Vector2(0, 1), Vector2.one,
                TextAnchor.MiddleLeft, FontStyle.Italic, new Vector2(12, -74), new Vector2(-12, -54));
            // the text sits in a masked window so long pages can scroll
            var vp = new GameObject("BodyViewport", typeof(RectTransform), typeof(RectMask2D));
            vp.transform.SetParent(screenImg.transform, false);
            viewport = UiKit.Rect(vp, Vector2.zero, Vector2.one, new Vector2(12, 34), new Vector2(-12, -80));
            body = UiKit.Label("Body", vp.transform, UiKit.UiFont, 13, new Color32(0x22, 0x24, 0x2B, 0xFF), new Vector2(0, 1), new Vector2(1, 1),
                TextAnchor.UpperLeft, FontStyle.Normal, Vector2.zero, Vector2.zero);
            body.rectTransform.pivot = new Vector2(0.5f, 1f);
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.lineSpacing = 1.05f;
            footer = UiKit.Label("Footer", screenImg.transform, UiKit.UiFont, 10, new Color32(0x8A, 0x85, 0x7A, 0xFF), Vector2.zero, new Vector2(1, 0),
                TextAnchor.MiddleCenter, FontStyle.Normal, new Vector2(6, 6), new Vector2(-6, 28));

            var grid = new GameObject("HomeGrid", typeof(RectTransform));
            grid.transform.SetParent(screenImg.transform, false);
            UiKit.Rect(grid, Vector2.zero, Vector2.one, new Vector2(0, 34), new Vector2(0, -90));
            homeGrid = grid;

            var pb = UiKit.Framed("Download", screenImg.transform, new Color32(0xDD, 0xD8, 0xCC, 0xFF), new Color32(0x8A, 0x85, 0x7A, 0xFF),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 40), new Vector2(-14, 52));
            progressBar = pb.transform.parent.gameObject;
            progressFill = UiKit.Box("Fill", pb.transform, new Color32(0x3C, 0x8A, 0x4A, 0xFF), Vector2.zero, new Vector2(0, 1));
            progressBar.SetActive(false);
        }
    }
}
