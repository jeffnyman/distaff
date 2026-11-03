using System.Text;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Input;
using Distaff.Scumm.Saves;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// The VGA CD's own menu and banners, drawn by the interpreter over the room while the game waits:
/// F5 opens the menu, to save, load, play on or quit; the pause key and the quit question show a
/// banner until a key or a click. Mirrors ScummVM's rebuilding of the original interface in
/// <c>gfx_gui.cpp</c> for Loom version 4 (<c>showMainMenu</c>, <c>setUpMainMenuControls</c>,
/// <c>drawMainMenuControls</c>, <c>executeMainMenuOperation</c>, <c>drawInternalGUIControl</c>,
/// <c>getInternalGUIControlFromCoordinates</c>, <c>fillSavegameLabels</c>,
/// <c>userWriteLabelRoutine</c>, <c>drawMainMenuTitle</c>, <c>showBannerAndPause</c>,
/// <c>queryQuit</c>, <c>saveSurfacesPreGUI</c> and <c>restoreSurfacesPostGUI</c>), and the keys of
/// <c>processKeyboard</c> that open them. ScummVM waits inside these; here they are a mode the
/// world's steps run instead of the game, one timer tick at a time.
/// </summary>
public sealed partial class GameWorld
{
    // the menu's pages (GUI_PAGE_MAIN, GUI_PAGE_SAVE, GUI_PAGE_LOAD)
    private const int PageMain = 0, PageSave = 1, PageLoad = 2;

    // its controls (GUI_CTRL_*); 0 is the banner's
    private const int CtrlFirstSlot = 1, CtrlLastSlot = 9, CtrlSave = 10, CtrlLoad = 11, CtrlPlay = 12, CtrlQuit = 13, CtrlOk = 14,
        CtrlCancel = 15, CtrlArrowUp = 16, CtrlArrowDown = 17, CtrlPath = 18, CtrlOuterBox = 26, CtrlInnerBox = 27;

    // the game's strings the interface takes its words from (getGUIString, getPlainEngineString)
    private const int GsPause = 4, GsRestart = 5, GsQuitPrompt = 6, GsSave = 7, GsLoad = 8, GsPlay = 9, GsCancel = 10, GsQuit = 11, GsOk = 12,
        GsInsertSaveDisk = 13, GsMustName = 14, GsGameNotLoaded = 16, GsSaving = 17, GsLoading = 18, GsNamePrompt = 19, GsSelectLoadPrompt = 20,
        GsTextSpeedSlider = 25;

    // what a save or load from the menu leaves for the scripts (GAME_PROPER_SAVE, GAME_PROPER_LOAD)
    private const int ProperSave = 201, ProperLoad = 203;

    /// <summary>ScummVM's own English words for a string the game has not set (<c>getStaticResString</c>).</summary>
    private static readonly Dictionary<int, string> GuiFallback = new()
    {
        [4] = "Game paused, press SPACE to continue.  ", [5] = "Are you sure you want to restart? (y/n)y", [6] = "Are you sure you want to quit? (y/n)y",
        [7] = "Save", [8] = "Load", [9] = "Play", [10] = "Cancel", [11] = "Quit", [12] = "OK", [13] = "Insert save/load game disk",
        [14] = "You must enter a name", [15] = "The game was NOT saved (disk full?)", [16] = "The game was NOT loaded",
        [17] = "Saving '%s'", [18] = "Loading '%s'", [19] = "Name your SAVE game", [20] = "Select a game to LOAD",
        [GsTextSpeedSlider] = "Text Speed   Slow  ==========  Fast",
    };

    // the interface's words the game has none of: ScummVM's own, in English for every language (getStaticResString, string 0)
    private static readonly HashSet<int> GuiHardCoded = [GsTextSpeedSlider];

    private readonly GuiControl[] _controls = [.. Enumerable.Range(0, 28).Select(_ => new GuiControl())];
    private Charset? _guiFont;
    private bool _menuActive;
    private int _menuPage;
    private int _savegameLabel;
    private int _firstSaveStateOfList;
    private readonly List<string> _savegameNames = [];
    private int _oldCursorState;
    private int _saveScriptParam;
    private int _guiCursorAnimCounter;
    private (Surface8? Main, Surface8? Verb, Surface8 Text)? _guiSurfaces;
    private bool? _guiWheel;
    private Banner? _banner;

    // a wait in quarter-frames (waitForTimer) and what follows it
    private int _guiWait;
    private Action? _guiThen;

    // the click or key the menu or banner has not taken yet (_leftBtnPressed, _keyPressed)
    private (int X, int Y, bool Right)? _guiClick;
    private (Key Key, char Ascii, KeyModifiers Modifiers)? _guiKey;

    /// <summary>Whether ScummVM's <c>confirm_exit</c> is set: Quit asks first. Off, as ScummVM has it by default.</summary>
    internal bool ConfirmExit { get; set; }

    /// <summary>Whether the menu or a banner is up, and the game waits.</summary>
    internal bool InInterface => _menuActive || _banner is not null || _oldBanner is not null;

    /// <summary>Whether the menu is open (<c>_mainMenuIsActive</c>).</summary>
    public bool MenuActive => _menuActive;

    /// <summary>The words of the banner shown, or null.</summary>
    public string? BannerText => _banner?.Text ?? _oldBanner?.Text;

    /// <summary>The save slots the menu lists, as it labels them (<c>_savegameNames</c>).</summary>
    internal IReadOnlyList<string> SavegameLabels => _savegameNames;

    /// <summary>A control of the interface and its look. Mirrors <c>InternalGUIControl</c>.</summary>
    private sealed class GuiControl
    {
        public int RelativeCenterX = -1, RelativeCenterY, XPos, YPos;
        public int NormalFillColor, NormalTextColor, TopLineColor, BottomLineColor, LeftLineColor, RightLineColor;
        public int HighlightedTextColor, HighlightedFillColor;
        public string Label = "";
        public bool CenterText, DoubleLines;
    }

    /// <summary>A banner waiting for a key or a click: its words, the rows it covers and what they held, and what comes after.</summary>
    private sealed record Banner(string Text, bool OverMenu, int SaveTop, byte[][] Rows, Action<(Key Key, char Ascii, KeyModifiers Modifiers)?> Then)
    {
        /// <summary>The quarter-frames left before the banner goes by itself, or -1 to wait for a key.</summary>
        public int WaitTime { get; set; } = -1;
    }

    /// <summary>
    /// A key while the game runs with its original interface. Mirrors the part of
    /// <c>processKeyboard</c> before the keys reach the game: the pause key shows the pause banner
    /// with the cursor off; the restart key (variable 42), or F8 on the FM Towns, asks whether to
    /// restart; on the VGA CD the menu key (variable 50, which the game sets to F5) opens the menu;
    /// Alt+I on version 3 and Ctrl+R on version 4 switch the screen between snapping to the camera
    /// and scrolling. Returns whether the key was taken.
    /// </summary>
    private bool InterfaceKey(Key key, KeyModifiers modifiers, char ascii)
    {
        int code = key is >= Key.F1 and <= Key.F9 ? key - Key.F1 + 315 : ascii;
        int pauseKey = Engine.Variables[Vars.PauseKey];
        if (pauseKey != 0 && code == pauseKey)
        {
            int oldCursorState = CursorState;
            CursorState = 0;
            ShowOldStyleBanner(GuiString(GsPause), 12, -1, _ => CursorState = oldCursorState);
            return true;
        }

        int restartKey = Engine.Variables[Vars.RestartKey];
        if ((restartKey != 0 && code == restartKey) || (FmTowns && key == Key.F8 && modifiers == KeyModifiers.None))
        {
            QueryRestart();
            return true;
        }

        // "Text Speed  Slow  ==========  Fast", from version 4 on
        if (_loader.Version == 4 && ascii is '+' or '-')
        {
            ChangeTextSpeed(ascii);
            return true;
        }

        // Ctrl+C and Alt+X quit, on every release with the original interface
        if ((key == Key.C && modifiers.HasFlag(KeyModifiers.Ctrl)) || (key == Key.X && modifiers.HasFlag(KeyModifiers.Alt)))
        {
            QuitRequested = true;
            return true;
        }

        if (_loader.Version == 4 && code != 0 && code == Engine.Variables[MainMenuKey] && modifiers == KeyModifiers.None)
        {
            ShowMainMenu();
            return true;
        }

        if ((_loader.Version == 3 && key == Key.I && modifiers == KeyModifiers.Alt)
            || (_loader.Version == 4 && key == Key.R && modifiers == KeyModifiers.Ctrl))
        {
            ToggleSnapScroll();
            return true;
        }

        return false;
    }

    /// <summary>The variables holding the key that opens the menu, and whether the saves are on a fixed disk (<c>VAR_MAINMENU_KEY</c>, <c>VAR_FIXEDDISK</c>, version 4).</summary>
    private const int MainMenuKey = 50, FixedDisk = 51;

    /// <summary>A click or key for the menu or banner, which takes it on the next step.</summary>
    private void InterfaceInput((int X, int Y, bool Right)? click, (Key, char, KeyModifiers)? key)
    {
        _guiClick = click ?? _guiClick;
        _guiKey = key ?? _guiKey;
    }

    /// <summary>
    /// A step while the menu or a banner is up: the cursor's colour moves on every 17 quarter-frames
    /// (<c>waitForBannerInput</c>), a wait counts down, and then the click or key is taken.
    /// </summary>
    private void InterfaceStep()
    {
        int quarterFrames = StepTicks * 4;

        // the CD timer runs on a thread of its own, through the menu unless the pause stopped it
        AdvanceCdTimer(quarterFrames);
        for (int i = 0; i < quarterFrames; i++)
        {
            if ((_guiCursorAnimCounter++ & 16) != 0)
            {
                _guiCursorAnimCounter = 0;
                AnimateCursor();
            }
        }

        if (_oldBanner is { } oldBanner)
        {
            var (bannerClick, bannerKey) = (_guiClick, _guiKey);
            (_guiClick, _guiKey) = (null, null);
            OldStyleBannerStep(oldBanner, quarterFrames, bannerClick, bannerKey);
            return;
        }

        if (_guiWait > 0)
        {
            _guiWait -= quarterFrames;
            if (_guiWait <= 0 && _guiThen is { } then)
            {
                _guiThen = null;
                then();
            }

            return;
        }

        var click = _guiClick;
        var key = _guiKey;
        var wheel = _guiWheel;
        _guiClick = null;
        _guiKey = null;
        _guiWheel = null;
        if (_banner is { } banner)
        {
            if (click is not null || key is not null)
            {
                EndBanner(banner, key);
            }
            else if (banner.WaitTime > 0 && (banner.WaitTime -= quarterFrames) <= 0)
            {
                EndBanner(banner, null);
            }

            return;
        }

        if (_menuActive)
        {
            MenuInput(click, key);
            if (click is null && wheel is { } up)
            {
                MenuWheel(up);
            }
        }
    }

    /// <summary>Waits some quarter-frames, then goes on (<c>waitForTimer</c>).</summary>
    private void WaitThen(int quarterFrames, Action then)
    {
        _guiWait = quarterFrames;
        _guiThen = then;
    }

    /// <summary>
    /// Opens the menu: the sound pauses, the screen as it is is kept to put back, the main page is
    /// drawn, and the cursor shows. Mirrors the start of <c>showMainMenu</c> for Loom version 4,
    /// which leaves the line being spoken where it is.
    /// </summary>
    private void ShowMainMenu()
    {
        PauseSounds(true);
        SaveGuiSurfaces();
        _menuPage = PageMain;
        SetUpMainMenuControls();
        DrawMainMenuControls();
        _oldCursorState = CursorState;
        CursorState = 1;
        _menuActive = true;
        _guiClick = null;
        _guiKey = null;
    }

    /// <summary>
    /// Closes the menu: the cursor comes back as it was unless a game was loaded from it (or, as
    /// in ScummVM, ever has been since, the flag never being cleared in version 4), the screen
    /// comes back unless a game was loaded, and the sound goes on. Mirrors the end of
    /// <c>showMainMenu</c>.
    /// </summary>
    private void ExitMenu(bool hasLoadedState)
    {
        _menuActive = false;
        if (_saveScriptParam != ProperLoad)
        {
            CursorState = _oldCursorState;
        }

        if (hasLoadedState)
        {
            _guiSurfaces = null;
        }
        else
        {
            RestoreGuiSurfaces();
        }

        PauseSounds(false);
        _guiClick = null;
        _guiKey = null;
    }

    /// <summary>
    /// The menu takes a click or key. Mirrors the loop of <c>showMainMenu</c>: on the save page,
    /// once a slot is chosen, keys type its name (<c>userWriteLabelRoutine</c>) and a left click
    /// goes on as a click; elsewhere keys do nothing. A click on a button lights it a moment and
    /// carries it out; a click on a slot chooses it, and on the load page loads it.
    /// </summary>
    private void MenuInput((int X, int Y, bool Right)? click, (Key Key, char Ascii, KeyModifiers Modifiers)? key)
    {
        if (_menuPage == PageSave && _savegameLabel > 0)
        {
            if (key is { } k)
            {
                TypeIntoLabel(k.Key, k.Ascii);
                return;
            }

            if (click is not { Right: false })
            {
                return;
            }
        }

        if (click is not { } c)
        {
            return;
        }

        ClickControl(ControlAt(c.X, c.Y), c.Y);
    }

    /// <summary>A control of the menu clicked, the mouse at a height: a slot is chosen; another control is lit a moment and does what it does.</summary>
    private void ClickControl(int clicked, int mouseY)
    {
        if (clicked == -1)
        {
            return;
        }

        if (clicked is < CtrlFirstSlot or > CtrlLastSlot)
        {
            if (clicked is CtrlOuterBox or CtrlInnerBox)
            {
                return;
            }

            DrawControl(clicked, true);

            // the original lit a button for 120 quarter-frames; ScummVM halves it (shouldHighlightLabelAndWait)
            if (clicked is >= CtrlSave and <= CtrlPath)
            {
                WaitThen(60, () =>
                {
                    DrawControl(clicked, false);
                    ExecuteMenuOperation(clicked, mouseY);
                });
            }
            else
            {
                DrawControl(clicked, false);
                ExecuteMenuOperation(clicked, mouseY);
            }

            return;
        }

        int previous = _savegameLabel;
        _savegameLabel = clicked;
        DrawControl(previous, false);
        DrawControl(_savegameLabel, true);
        if (_menuPage == PageLoad)
        {
            ExecuteMenuOperation(CtrlOk, mouseY);
        }
    }

    /// <summary>
    /// The mouse wheel turned while the menu is up. Mirrors the menu loop's <c>_mouseWheelFlag</c>:
    /// over the save list (its slots, or the box between them within the list's bounds), it is a
    /// click on the arrow that way, if that arrow shows, with the mouse where it is.
    /// </summary>
    private void MenuWheel(bool up)
    {
        int over = ControlAt(MouseX, MouseY);
        var first = _controls[CtrlFirstSlot];
        var last = _controls[CtrlLastSlot];
        if (over == CtrlOuterBox && first.RelativeCenterX != -1 && last.RelativeCenterX != -1)
        {
            over = MouseX >= first.RelativeCenterX && MouseX <= last.XPos && MouseY >= first.RelativeCenterY && MouseY <= last.YPos ? CtrlFirstSlot : -1;
        }

        if (over is < CtrlFirstSlot or > CtrlLastSlot)
        {
            return;
        }

        int arrow = up ? CtrlArrowUp : CtrlArrowDown;
        if (_controls[arrow].RelativeCenterX != -1)
        {
            ClickControl(arrow, MouseY);
        }
    }

    /// <summary>The mouse wheel turned a step, up or down. Only the VGA CD's menu takes it, while it is up (<c>EVENT_WHEELUP</c>, <c>EVENT_WHEELDOWN</c>).</summary>
    public void Wheel(bool up)
    {
        if (_menuActive)
        {
            _guiWheel = up;
        }
    }

    /// <summary>
    /// '+' or '-' makes the lines go faster or slower by one, within 0 to 9, and says so in a
    /// banner ("Text Speed  Slow  ==========  Fast", the speed marked); another '+' or '-' within
    /// 60 quarter-frames goes on, anything else or the time ends it. Mirrors the text speed keys
    /// of <c>processKeyboard</c> for version 4, the banner drawn over a copy of the screen that
    /// comes back when it goes.
    /// </summary>
    private void ChangeTextSpeed(char key)
    {
        int charInc = Math.Clamp(Engine.Variables[Vars.CharInc] + (key == '+' ? -1 : 1), 0, 9);
        TextSpeed = 9 - charInc;
        ShowBanner(SliderString(GsTextSpeedSlider, charInc), next =>
        {
            if (next is { Ascii: '+' or '-' } k)
            {
                ChangeTextSpeed(k.Ascii);
            }
        }, waitTime: 60);
    }

    /// <summary>A slider's words with its bar of ten marks, the one for the value lit (<c>getSliderString</c> for the text speed).</summary>
    private string SliderString(int id, int value)
    {
        var chars = GuiString(id).ToCharArray();
        int at = Array.IndexOf(chars, '=');
        if (at < 0)
        {
            return new string(chars);
        }

        for (int i = at; i < at + 10 && i < chars.Length; i++)
        {
            chars[i] = '\v';
        }

        if (at + 9 - value < chars.Length)
        {
            chars[at + 9 - value] = '\f';
        }

        return new string(chars);
    }

    /// <summary>
    /// A key in the chosen slot of the save page: Enter saves, Backspace takes back a letter but
    /// not the slot's number, and a character from space to 'z' goes on the end, to 39 in all.
    /// Mirrors <c>userWriteLabelRoutine</c>.
    /// </summary>
    private void TypeIntoLabel(Key key, char ascii)
    {
        string label = _savegameNames[_savegameLabel - 1];
        if (key == Key.Return)
        {
            ExecuteMenuOperation(CtrlOk, -1);
            return;
        }

        if (key == Key.Backspace)
        {
            if (label.Length > 4)
            {
                SetLabel(label[..^1]);
            }
        }
        else if (ascii is >= ' ' and <= 'z' && label.Length < 39)
        {
            SetLabel(label + ascii);
        }

        void SetLabel(string text)
        {
            _savegameNames[_savegameLabel - 1] = text;
            _controls[_savegameLabel].Label = text;
            DrawControl(_savegameLabel, true);
        }
    }

    /// <summary>
    /// Carries out a control. Mirrors <c>executeMainMenuOperation</c> for Loom version 4: Save and
    /// Load list the slots on their page; Play closes the menu; Quit asks; OK saves under the
    /// slot's name or loads it, after showing so a moment; Cancel goes back to the main page; the
    /// arrows scroll the list by how far from the list's middle they are pressed.
    /// </summary>
    private void ExecuteMenuOperation(int op, int mouseY)
    {
        switch (op)
        {
            case CtrlSave or CtrlLoad:
                _savegameLabel = 0;
                FillSavegameLabels();
                _menuPage = op == CtrlSave ? PageSave : PageLoad;
                SetUpMainMenuControls();
                DrawMainMenuControls();
                break;
            case CtrlPlay:
                ExitMenu(false);
                break;
            case CtrlQuit:
                QueryQuit();
                break;
            case CtrlOk when _menuPage == PageSave:
                {
                    string name = _savegameLabel > 0 ? _savegameNames[_savegameLabel - 1][4..] : "";
                    if (name.Length == 0)
                    {
                        DrawMainMenuTitle(GuiString(GsMustName));
                        break;
                    }

                    DrawMainMenuTitle(GuiString(GsSaving).Replace("%s", name));
                    int slot = _savegameLabel + _firstSaveStateOfList;
                    WaitThen(60, () => SaveFromMenu(slot, name));
                    break;
                }

            case CtrlOk when _menuPage == PageLoad && _savegameLabel > 0:
                {
                    string label = _savegameNames[_savegameLabel - 1];
                    if (label.Length == 4)
                    {
                        DrawMainMenuControls();
                        break;
                    }

                    DrawMainMenuTitle(GuiString(GsLoading).Replace("%s", label[4..]));
                    int slot = _savegameLabel + _firstSaveStateOfList;
                    WaitThen(60, () => LoadFromMenu(slot));
                    break;
                }

            case CtrlCancel:
                _menuPage = PageMain;
                SetUpMainMenuControls();
                DrawMainMenuControls();
                break;
            case CtrlArrowUp or CtrlArrowDown when _menuPage != PageMain:
                {
                    var main = Screens.Main;
                    _firstSaveStateOfList += op == CtrlArrowUp
                        ? -((main.Height / 2 - mouseY + main.Top + 54) / 14)
                        : (mouseY - main.Height / 2 - main.Top + 45) / 14;
                    _firstSaveStateOfList = Math.Clamp(_firstSaveStateOfList, 0, 90);
                    _savegameLabel = 0;
                    FillSavegameLabels();
                    for (int i = CtrlFirstSlot; i <= CtrlLastSlot; i++)
                    {
                        _controls[i].Label = _savegameNames[i - 1];
                    }

                    DrawMainMenuControls();
                    break;
                }

            case CtrlArrowUp or CtrlArrowDown:
                // the arrows are there on the main page too, not drawn: a click shows them
                DrawControl(CtrlArrowUp, false);
                DrawControl(CtrlArrowDown, false);
                break;
        }
    }

    /// <summary>Saves from the menu with the cursor as it was before the menu, and closes it; a save that fails leaves the menu up.</summary>
    private void SaveFromMenu(int slot, string name)
    {
        CursorState = _oldCursorState;
        if (!WriteSave(slot, name))
        {
            return;
        }

        _oldCursorState = CursorState;
        CursorState = 1;
        _saveScriptParam = ProperSave;
        DrawMainMenuControls();
        ExitMenu(false);
    }

    /// <summary>Loads from the menu and closes it, the loaded game going on as after any load; one that fails says so.</summary>
    private void LoadFromMenu(int slot)
    {
        if (ReadSave(slot) is { } file)
        {
            try
            {
                Load(file);
                _saveScriptParam = ProperLoad;
                ExitMenu(true);
                return;
            }
            catch (InvalidDataException)
            {
            }
        }

        DrawMainMenuTitle(GuiString(GsGameNotLoaded));
    }

    /// <summary>
    /// Quits, or with <see cref="ConfirmExit"/> asks first with the game's question, its last
    /// letter the answer that quits. Mirrors <c>queryQuit</c>.
    /// </summary>
    private void QueryQuit()
    {
        string prompt = GuiString(GsQuitPrompt);
        if (prompt.Length == 0)
        {
            return;
        }

        char yes = prompt[^1];
        if (!ConfirmExit)
        {
            Quit();
            return;
        }

        ShowBanner(prompt[..^1], key =>
        {
            if (key is { } k && (char.ToLowerInvariant(k.Ascii) == char.ToLowerInvariant(yes)
                || (k.Key == Key.C && k.Modifiers == KeyModifiers.Ctrl) || (k.Key == Key.X && k.Modifiers == KeyModifiers.Alt)))
            {
                Quit();
            }
        });

        void Quit()
        {
            QuitRequested = true;
            if (_menuActive)
            {
                ExitMenu(false);
            }
        }
    }

    /// <summary>
    /// The menu's controls for the page shown, around the main band's middle. Mirrors
    /// <c>ScummEngine::setUpMainMenuControls</c> for Loom version 4: the boxes, Save, Load, Play
    /// and Quit on the main page, the arrows on every page, and OK (on the save page), Cancel and
    /// nine slots on the others.
    /// </summary>
    private void SetUpMainMenuControls()
    {
        int y = Screens.Main.Top + Screens.Main.Height / 2;
        foreach (var control in _controls)
        {
            control.RelativeCenterX = -1;
        }

        SetUpControl(CtrlOuterBox, 7, 0, 15, 8, 15, 8, 14, 1, 20, y - 60, 300, y + 60 < 0 ? -120 : y + 60, "", true, true);
        SetUpControl(CtrlInnerBox, 7, 0, 8, 15, 8, 15, 14, 1, 26, y - 47, 202, y - 47 + 102, "", true, true);
        if (_menuPage == PageMain)
        {
            SetUpControl(CtrlSave, 7, 0, 15, 8, 15, 8, 14, 1, 232, y - 23, 292, y - 23 + 12, GuiString(GsSave), true, true);
            SetUpControl(CtrlLoad, 7, 0, 15, 8, 15, 8, 14, 1, 232, y - 8, 292, y - 8 + 12, GuiString(GsLoad), true, true);
            SetUpControl(CtrlPlay, 7, 0, 15, 8, 15, 8, 14, 1, 232, y + 7, 292, y + 19, GuiString(GsPlay), true, true);
            SetUpControl(CtrlQuit, 7, 0, 15, 8, 15, 8, 14, 1, 232, y + 22, 292, y + 34, GuiString(GsQuit), true, true);
        }

        SetUpControl(CtrlArrowUp, 7, 0, 15, 8, 15, 8, 14, 1, 206, y - 43, 222, y - 43 + 47, "\x18", true, true);
        SetUpControl(CtrlArrowDown, 7, 0, 15, 8, 15, 8, 14, 1, 206, y + 7, 222, y + 52, "\x19", true, true);
        if (_menuPage is PageSave or PageLoad)
        {
            if (_menuPage == PageSave)
            {
                SetUpControl(CtrlOk, 7, 0, 15, 8, 15, 8, 14, 1, 232, y - 8, 292, y - 8 + 12, GuiString(GsOk), true, true);
            }

            int cancel = _menuPage == PageLoad ? y - 1 : y + 7;
            SetUpControl(CtrlCancel, 7, 0, 15, 8, 15, 8, 14, 1, 232, cancel, 292, cancel + 12, GuiString(GsCancel), true, true);
            for (int i = CtrlFirstSlot, j = 0; i <= CtrlLastSlot; i++, j += 11)
            {
                SetUpControl(i, 7, 0, 7, 7, 7, 7, 14, 1, 28, y - 45 + j, 200, -9, _savegameNames[i - 1], false, false);
            }
        }
    }

    /// <summary>
    /// Draws the page: the outer box, then the main page's buttons (and a title asking for the save
    /// disk when the saves are not on a fixed one), or the inner box, OK, Cancel, the slots, the
    /// arrows and the page's title; then the chosen slot lit. Mirrors
    /// <c>drawMainMenuControls</c> for Loom version 4.
    /// </summary>
    private void DrawMainMenuControls()
    {
        DrawControl(CtrlOuterBox, false);
        if (_menuPage == PageMain)
        {
            DrawControl(CtrlSave, false);
            DrawControl(CtrlLoad, false);
            DrawControl(CtrlPlay, false);
            DrawControl(CtrlQuit, false);
            if (Engine.Variables[FixedDisk] == 0)
            {
                DrawMainMenuTitle(GuiString(GsInsertSaveDisk));
            }
        }
        else
        {
            DrawControl(CtrlInnerBox, false);
            DrawControl(CtrlPath, false);
            DrawControl(CtrlOk, false);
            DrawControl(CtrlCancel, false);
            for (int i = CtrlFirstSlot; i <= CtrlLastSlot; i++)
            {
                DrawControl(i, false);
            }

            DrawControl(CtrlArrowUp, false);
            DrawControl(CtrlArrowDown, false);
            DrawMainMenuTitle(GuiString(_menuPage == PageSave ? GsNamePrompt : GsSelectLoadPrompt));
        }

        if (_savegameLabel != 0)
        {
            DrawControl(_savegameLabel, true);
        }
    }

    /// <summary>The menu's title line in its strip at the top. Mirrors <c>drawMainMenuTitle</c> for Loom version 4.</summary>
    private void DrawMainMenuTitle(string title)
    {
        int y = Screens.Main.Top + Screens.Main.Height / 2;
        DrawBox(22, y - 57, 298, y - 49, 7);
        DrawGuiText(title, null, 160, y - 57, 0, true);
    }

    /// <summary>
    /// The nine slots from the first shown, numbered, each with its save's name or none. Mirrors
    /// <c>fillSavegameLabels</c> for Loom version 4.
    /// </summary>
    private void FillSavegameLabels()
    {
        _savegameNames.Clear();
        for (int i = CtrlFirstSlot; i <= CtrlLastSlot; i++)
        {
            int slot = i + _firstSaveStateOfList;
            string name = "";
            if (ReadSave(slot) is { } file)
            {
                try
                {
                    name = SaveFile.ReadInfo(file).Name;
                }
                catch (InvalidDataException)
                {
                    name = "WARNING: wrong save version";
                }
            }

            _savegameNames.Add($"{slot,2}. {name}");
        }
    }

    /// <summary>Sets a control up: a negative right or bottom counts from its left or top. Mirrors <c>setUpInternalGUIControl</c>.</summary>
    private void SetUpControl(int id, int normalFill, int normalText, int topLine, int bottomLine, int leftLine, int rightLine,
        int highlightedText, int highlightedFill, int anchorX, int anchorY, int x, int y, string label, bool center, bool doubleLines)
    {
        var c = _controls[id];
        (c.RelativeCenterX, c.RelativeCenterY, c.XPos, c.YPos) = (anchorX, anchorY, x < 0 ? anchorX - x : x, y < 0 ? anchorY - y : y);
        (c.Label, c.CenterText, c.DoubleLines) = (label, center, doubleLines);
        (c.NormalFillColor, c.NormalTextColor, c.HighlightedTextColor, c.HighlightedFillColor) = (normalFill, normalText, highlightedText, highlightedFill);
        (c.TopLineColor, c.BottomLineColor, c.LeftLineColor, c.RightLineColor) = (topLine, bottomLine, leftLine, rightLine);
    }

    /// <summary>
    /// Draws a control that is set up: its box, filled, edged in two lines a side for a raised look
    /// or one for a slot, and its words in charset 1, centred or from its left, a slot's clipped to
    /// it and with a '_' where a name is being typed. Mirrors <c>drawInternalGUIControl</c> for Loom
    /// version 4.
    /// </summary>
    private void DrawControl(int id, bool highlighted)
    {
        var c = _controls[id];
        if (c.RelativeCenterX == -1)
        {
            return;
        }

        int left = c.RelativeCenterX, top = c.RelativeCenterY, x = c.XPos, y = c.YPos;
        int fill = highlighted ? c.HighlightedFillColor : c.NormalFillColor;
        if (c.DoubleLines)
        {
            DrawBox(left + 1, top + 1, x - 1, y - 1, fill);
            DrawLine(left + 1, top, x - 1, top, c.TopLineColor);
            DrawLine(left + 1, y, x - 1, y, c.BottomLineColor);
            DrawLine(left, top + 1, left, y - 1, c.LeftLineColor);
            DrawLine(x, top + 1, x, y - 1, c.RightLineColor);
            DrawLine(left + 1, top + 1, x - 1, top + 1, c.TopLineColor);
            DrawLine(left + 1, y - 1, x - 1, y - 1, c.BottomLineColor);
            DrawLine(left + 1, top + 1, left + 1, y - 1, c.LeftLineColor);
            DrawLine(x - 1, top + 1, x - 1, y - 1, c.RightLineColor);
        }
        else
        {
            DrawBox(left, top, x, y, fill);
            DrawLine(left, top, x, top, c.TopLineColor);
            DrawLine(left, y, x, y, c.BottomLineColor);
            DrawLine(left, top, left, y, c.LeftLineColor);
            DrawLine(x, top, x, y, c.RightLineColor);
        }

        int textX = c.CenterText ? left + (x - left) / 2 : left + 2;
        int textY = top + (y - 8 - top + 2) / 2;
        bool isSlot = id is >= CtrlFirstSlot and <= CtrlLastSlot;
        string label = isSlot && _savegameLabel == id && _menuPage == PageSave ? c.Label + "_" : c.Label;
        DrawGuiText(label, isSlot ? x : null, textX, textY, highlighted ? c.HighlightedTextColor : c.NormalTextColor, c.CenterText);
    }

    /// <summary>The topmost control set up at a point, or -1. Mirrors <c>getInternalGUIControlFromCoordinates</c>.</summary>
    private int ControlAt(int x, int y)
    {
        for (int id = 0; id < _controls.Length; id++)
        {
            var c = _controls[id];
            if (c.RelativeCenterX != -1 && c.RelativeCenterX <= x && c.XPos >= x && c.RelativeCenterY <= y && c.YPos >= y)
            {
                return id;
            }
        }

        return -1;
    }

    /// <summary>Words of the interface in text slot 5 and charset 1, clipped on the right where asked. Mirrors <c>drawGUIText</c>.</summary>
    private void DrawGuiText(string text, int? clipRight, int x, int y, int color, bool center)
    {
        var slot = TextSlots[5];
        int right = slot.Right;
        (slot.X, slot.Y, slot.Right, slot.Center, slot.Color) = (x, y, clipRight ?? ScreenWidth - 1, center, color);
        var font = _font;
        _font = _guiFont ??= _loader.LoadCharset(1) is { } data ? Charset.ParseClassic(data) : null;
        DrawString(5, Encoding.Latin1.GetBytes(text + "\0"));
        _font = font;
        slot.Right = right;
    }

    /// <summary>
    /// A line from one point to another, a pixel at a time, in the band its start is in. Mirrors
    /// <c>drawLine</c> with a colour given.
    /// </summary>
    private void DrawLine(int x1, int y1, int x2, int y2, int color)
    {
        if (Screens.Find(y1) is not { } vs)
        {
            return;
        }

        int effX = x1, effY = y1;
        int width = Math.Abs(x2 - x1), height = Math.Abs(y2 - y1), originalHeight = height;
        if (height <= width)
        {
            height = width;
        }

        int widthAccumulator = 0, heightAccumulator = 0;
        DrawPixel(vs, x1, y1, color);
        for (int strips = height + 1; strips > 0; strips--)
        {
            bool draw = false;
            widthAccumulator += width;
            heightAccumulator += originalHeight;
            if (widthAccumulator > height)
            {
                draw = true;
                widthAccumulator -= height;
                effX += x2 - x1 < 0 ? -1 : 1;
            }

            if (heightAccumulator > height)
            {
                draw = true;
                heightAccumulator -= height;
                effY += y2 - y1 < 0 ? -1 : 1;
            }

            if (draw)
            {
                DrawPixel(vs, effX, effY, color);
            }
        }
    }

    /// <summary>A pixel of a band, at a point of the screen. Mirrors <c>drawPixel</c>.</summary>
    private void DrawPixel(VirtScreen vs, int x, int y, int color)
    {
        if (vs.Front is not { } front || x < 0 || y < 0 || x >= ScreenWidth + 8 || y >= ScreenHeight)
        {
            return;
        }

        int px = x + (vs.Number == VirtScreenNumber.Main ? Camera.Left : 0);
        int py = y - vs.Top;
        if (px < front.Width && py >= 0 && py < front.Height)
        {
            front[px, py] = (byte)color;
        }
    }

    /// <summary>
    /// A banner across the screen's middle with words, until a key or a click; then the rows it
    /// covered come back and what follows is done with the key. The sound pauses meanwhile. Mirrors
    /// <c>showBannerAndPause</c> for version 4, which <c>showOldStyleBannerAndPause</c> is for Loom VGA.
    /// </summary>
    private void ShowBanner(string text, Action<(Key Key, char Ascii, KeyModifiers Modifiers)?> then, int waitTime = -1)
    {
        bool overMenu = _menuActive;
        if (!overMenu)
        {
            SaveGuiSurfaces();
        }

        PauseSounds(true);
        var font = _font;
        _font = _guiFont ??= _loader.LoadCharset(1) is { } data ? Charset.ParseClassic(data) : null;
        int height = (_font?.Height ?? 8) + 5;
        int width = Math.Max(StringWidth(0, Encoding.Latin1.GetBytes(text)), 100);
        _font = font;
        int roundedWidth = width / 2;
        int startX = 156 - roundedWidth, startY = 80;

        // the rows from two above the banner, as many as it is high, in the main band's own rows (_bannerMem)
        int saveTop = startY - 2;
        var rows = new byte[height][];
        var main = Screens.Main.Front;
        for (int i = 0; i < height; i++)
        {
            rows[i] = main is not null && saveTop + i < main.Height
                ? main.Row(saveTop + i).Slice(Camera.Left, Math.Min(ScreenWidth + 8, main.Width - Camera.Left)).ToArray()
                : [];
        }

        SetUpControl(0, 7, 0, 15, 8, 15, 8, 0, 0, startX, startY, roundedWidth + 164, 1 - height, text, true, true);
        DrawControl(0, false);
        _banner = new Banner(text, overMenu, saveTop, rows, then) { WaitTime = waitTime };
        _guiClick = null;
        _guiKey = null;
    }

    /// <summary>The banner goes: its rows come back (<c>clearBanner</c>), then the screen if no menu is up, then the sound.</summary>
    private void EndBanner(Banner banner, (Key Key, char Ascii, KeyModifiers Modifiers)? key)
    {
        _banner = null;
        if (Screens.Main.Front is { } main)
        {
            for (int i = 0; i < banner.Rows.Length && banner.SaveTop + i < main.Height; i++)
            {
                banner.Rows[i].CopyTo(main.Row(banner.SaveTop + i)[Camera.Left..]);
            }
        }

        if (!banner.OverMenu)
        {
            RestoreGuiSurfaces();
        }

        PauseSounds(banner.OverMenu);
        _controls[0].RelativeCenterX = -1;
        banner.Then(key);
    }

    /// <summary>Keeps what the main and verb bands and the text surface show, to put back after. Mirrors <c>saveSurfacesPreGUI</c> for Loom version 4.</summary>
    private void SaveGuiSurfaces()
        => _guiSurfaces = (Copy(Screens.Main.Front), Copy(Screens.Verb.Front), Copy(Screens.TextSurface)!);

    /// <summary>Puts back what <see cref="SaveGuiSurfaces"/> kept. Mirrors <c>restoreSurfacesPostGUI</c>.</summary>
    private void RestoreGuiSurfaces()
    {
        if (_guiSurfaces is not { } saved)
        {
            return;
        }

        _guiSurfaces = null;
        CopyBack(saved.Main, Screens.Main.Front);
        CopyBack(saved.Verb, Screens.Verb.Front);
        CopyBack(saved.Text, Screens.TextSurface);

        static void CopyBack(Surface8? from, Surface8? to)
        {
            if (from is not null && to is not null && from.Pixels.Length == to.Pixels.Length)
            {
                from.Pixels.CopyTo(to.Pixels, 0);
            }
        }
    }

    private static Surface8? Copy(Surface8? surface)
    {
        if (surface is null)
        {
            return null;
        }

        var copy = new Surface8(surface.Width, surface.Height);
        surface.Pixels.CopyTo(copy.Pixels, 0);
        return copy;
    }

    /// <summary>One of the game's strings the interface uses, to its end, or ScummVM's English when the game has not set it (<c>getPlainEngineString</c>).</summary>
    private string GuiString(int id)
    {
        if (GuiHardCoded.Contains(id) || _strings[id] is not { } text)
        {
            return GuiFallback.GetValueOrDefault(id, "");
        }

        int end = Array.IndexOf(text, (byte)0);
        return Encoding.Latin1.GetString(text, 0, end < 0 ? text.Length : end);
    }
}
