﻿namespace UserInterface.Views
{
    using System;
    using System.Reflection;
    using EventArguments;
    using Gtk;
    using System.IO;
    using Utility;
    using Cairo;
    using System.Globalization;
    using System.Linq;
    using System.Collections.Generic;
    using Intellisense;
    using Interfaces;
    using GtkSource;
    using Extensions;

    /// <summary>
    /// This class provides an intellisense editor and has the option of syntax highlighting keywords.
    /// </summary>
    /// <remarks>
    /// This is the .net core/gtk3 version, which uses SourceView.
    /// This class could probably be trimmed down significantly, there's
    /// probably a lot of stuff that's specific to gtk2.
    /// </remarks>
    public class EditorView : ViewBase, IEditorView
    {
        /// <summary>
        /// The find-and-replace form
        /// </summary>
        private FindAndReplaceForm findForm = new FindAndReplaceForm();

        /// <summary>
        /// Scrolled window
        /// </summary>
        private ScrolledWindow scroller;

        /// <summary>
        /// The main text editor
        /// </summary>
        private SourceView textEditor;

        /// <summary>
        /// Settings for search and replace
        /// </summary>
        private SearchSettings searchSettings;

        /// <summary>
        /// Context for search and replace
        /// </summary>
        private SearchContext searchContext;

        /// <summary>
        /// Menu accelerator group
        /// </summary>
        private AccelGroup accel = new AccelGroup();

        /// <summary>
        /// Horizontal scroll position
        /// </summary>
        private int horizScrollPos = -1;

        /// <summary>
        /// Vertical scroll position
        /// </summary>
        private int vertScrollPos = -1;

        /// <summary>
        /// Invoked when the editor needs context items (after user presses '.')
        /// </summary>
        public event EventHandler<NeedContextItemsArgs> ContextItemsNeeded;

        /// <summary>
        /// Invoked when the user changes the text in the editor.
        /// </summary>
        public event EventHandler TextHasChangedByUser;

        /// <summary>
        /// Invoked when the user leaves the text editor.
        /// </summary>
        public event EventHandler LeaveEditor;

        /// <summary>
        /// Invoked when the user changes the style.
        /// </summary>
        public event EventHandler StyleChanged;

        /// <summary>
        /// Gets or sets the text property to get and set the content of the editor.
        /// </summary>
        public string Text
        {
            get
            {
                return textEditor.Buffer.Text;
            }

            set
            {
                if (value != null)
                {
                    textEditor.Buffer.BeginNotUndoableAction();
                    textEditor.Buffer.Text = value;
                    textEditor.Buffer.EndNotUndoableAction();
                }
                //if (Mode == EditorType.ManagerScript)
                //{
                //    textEditor.Completion.AddProvider(new ScriptCompletionProvider(ShowError));
                //}
                //else if (Mode == EditorType.Report)
                //{
                //    if (SyntaxModeService.GetSyntaxMode(textEditor.Document, "text/x-apsimreport") == null)
                //        LoadReportSyntaxMode();
                //    textEditor.Document.MimeType = "text/x-apsimreport";
                //}
            }
        }

        /// <summary>
        /// Performs a one-time registration of the report syntax highlighting rules.
        /// This will only run once, the first time the user clicks on a report node.
        /// </summary>
        private void LoadReportSyntaxMode()
        {
            // tbi: syntax highlighting in report
            //string resource = "ApsimNG.Resources.SyntaxHighlighting.Report.xml";
            //using (System.IO.Stream s = GetType().Assembly.GetManifestResourceStream(resource))
            //{
            //    ProtoTypeSyntaxModeProvider p = new ProtoTypeSyntaxModeProvider(SyntaxMode.Read(s));
            //    SyntaxModeService.InstallSyntaxMode("text/x-apsimreport", p);
            //}
        }

        /// <summary>
        /// Gets or sets the lines in the editor.
        /// </summary>
        public string[] Lines
        {
            get
            {
                string text = Text.TrimEnd("\r\n".ToCharArray());
                return text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            }
            set
            {
                if (value != null)
                    Text = string.Join(Environment.NewLine, value);
            }
        }

        private EditorType editorMode;

        /// <summary>
        /// Controls the syntax highlighting scheme.
        /// </summary>
        public EditorType Mode
        {
            get => editorMode;
            set
            {
                editorMode = value;
                // Initialise completion provider based on editor type.
                if (value == EditorType.ManagerScript)
                    textEditor.Completion.AddProvider(new ScriptCompletionProvider(ShowError, textEditor));
            }
        }

        /// <summary>
        /// Gets or sets the characters that bring up the intellisense context menu.
        /// </summary>
        public string IntelliSenseChars { get; set; }
        
        /// <summary>
        /// Gets the current line number
        /// </summary>
        public int CurrentLineNumber
        {
            get
            {
                return textEditor.Buffer.GetIterAtOffset(textEditor.Buffer.CursorPosition).Line + 1;
            }
        }

        /// <summary>
        /// Get the current column number.
        /// </summary>
        public int CurrentColumnNumber
        {
            get
            {
                return textEditor.Buffer.GetIterAtOffset(textEditor.Buffer.CursorPosition).LineOffset;
            }
        }

        /// <summary>
        /// Controls visibility of the widget.
        /// </summary>
        public bool Visible
        {
            get
            {
                return MainWidget.Visible;
            }
            set
            {
                if (value)
                    MainWidget.ShowAll();
                else
                    MainWidget.Hide();
            }
        }

        /// <summary>
        /// Gets or sets the current location of the caret (column and line) and the current scrolling position
        /// This isn't really a Rectangle, but the Rectangle class gives us a convenient
        /// way to store these values.
        /// 
        /// X is column, Y is line number, width is horizontal scroll position, height is vertical scroll position.
        /// </summary>
        public System.Drawing.Rectangle Location
        {
            get
            {
                int scrollX = Convert.ToInt32(scroller.Hadjustment.Value, CultureInfo.InvariantCulture);
                int scrollY = Convert.ToInt32(scroller.Vadjustment.Value, CultureInfo.InvariantCulture);

                // x is column, y is line number.
                return new System.Drawing.Rectangle(CurrentColumnNumber, CurrentLineNumber, scrollX, scrollY);
            }

            set
            {
                // tbi
                //textEditor.Caret.Location = new DocumentLocation(value.Y, value.X);
                horizScrollPos = value.Width;
                vertScrollPos = value.Height;

                // Unfortunately, we often can't set the scroller adjustments immediately, as they may not have been set up yet
                // We make these calls to set the position if we can, but otherwise we'll just hold on to the values until the scrollers are ready
                Hadjustment_Changed(this, null);
                Vadjustment_Changed(this, null);

                // x is column, y is line number.
                TextIter iter = textEditor.Buffer.GetIterAtLineOffset(value.Y, value.X);
                textEditor.Buffer.PlaceCursor(iter);
            }
        }

        /// <summary>
        /// Offset of the caret from the beginning of the text editor.
        /// </summary>
        public int Offset
        {
            get
            {
                return textEditor.Buffer.CursorPosition;
            }
        }

        /// <summary>
        /// Returns true iff this text editor has the focus
        /// (ie it can receive keyboard input).
        /// </summary>
        public bool HasFocus
        {
            get
            {
                return textEditor.HasFocus;
            }
        }

        public bool ShowLineNumbers
        {
            get
            {
                return textEditor.ShowLineNumbers;
            }
            set
            {
                textEditor.ShowLineNumbers = value;
            }
        }

        public string Language
        {
            get
            {
                return textEditor.Buffer.Language.Name;
            }
            set
            {
                textEditor.Buffer.Language = LanguageManager.Default.GetLanguage(value);
            }
        }

        public EditorView() { }

        /// <summary>
        /// Default constructor that configures the Completion form.
        /// </summary>
        /// <param name="owner">The owner view</param>
        public EditorView(ViewBase owner) : base(owner)
        {
            scroller = new ScrolledWindow();
            textEditor = new SourceView();
            searchSettings = new SearchSettings();
            searchContext = new SearchContext(textEditor.Buffer, searchSettings);
            scroller.Add(textEditor);
            InitialiseWidget();
        }

        protected override void Initialise(ViewBase ownerView, GLib.Object gtkControl)
        {
            base.Initialise(ownerView, gtkControl);
            Container parent = (Container)gtkControl;
            mainWidget = parent;
            scroller = new ScrolledWindow();
            textEditor = new SourceView();
            scroller.Add(textEditor);
            parent.Add(scroller);
            InitialiseWidget();
        }

        private void InitialiseWidget()
        {
            textEditor.Monospace = true;
            textEditor.HighlightCurrentLine = true;
            textEditor.AutoIndent = true;
            textEditor.TabWidth = 4; // Should probably be in css?
            textEditor.ShowLineNumbers = true;

            // Move to the first/last non-whitespace char on the first
            // press of home/end keys, and to the beginning/end of the
            // line on the second press.
            textEditor.SmartHomeEnd = SmartHomeEndType.Before;

            mainWidget = scroller;
            textEditor.Buffer.Changed += OnTextHasChanged;
            textEditor.FocusInEvent += OnTextBoxEnter;
            textEditor.FocusOutEvent += OnTextBoxLeave;
            textEditor.KeyPressEvent += OnKeyPress;
            scroller.Hadjustment.Changed += Hadjustment_Changed;
            scroller.Vadjustment.Changed += Vadjustment_Changed;
            mainWidget.Destroyed += _mainWidget_Destroyed;

            // Attempt to load a style scheme from the user settings.
            StyleScheme style = StyleSchemeManager.Default.GetScheme(Configuration.Settings.EditorStyleName);
            if (style == null)
            {
                // If there's no style scheme specified in user settings (or if it's an unknown
                // scheme), then fallback to adwaita, or adwaita-dark if dark mode is active.
                if (Configuration.Settings.DarkTheme)
                    style = StyleSchemeManager.Default.GetScheme("Adwaita-dark");
                else
                    style = StyleSchemeManager.Default.GetScheme("Adwaita");
            }
            if (style != null)
                textEditor.Buffer.StyleScheme = style;

            AddContextActionWithAccel("Find", OnFind, "Ctrl+F");
            AddContextActionWithAccel("Replace", OnReplace, "Ctrl+H");
            AddMenuItem("Change Style", OnChangeStyle);

            textEditor.Realized += OnRealized;
            IntelliSenseChars = ".";
        }

        /// <summary>
        /// Context menu items aren't actually added to the context menu until the
        /// user requests the context menu (ie via right clicking). Keyboard shortcuts
        /// (accelerators) won't work until this occurs. Therefore, we now manually
        /// fire off a populate-popup signal to cause the context menu to be populated.
        /// (This doesn't actually cause the context menu to be displayed.)
        ///
        /// We wait until the widget is realized so that the owner of the view has a
        /// chance to add context menu items.
        /// </summary>
        /// <param name="sender">Sender object (the SourceView widget).</param>
        /// <param name="args">Event data.</param>
        private void OnRealized(object sender, EventArgs args)
        {
            try
            {
                textEditor.Realized -= OnRealized;
                GLib.Signal.Emit(textEditor, "populate-popup", new Menu());
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Cleanup events
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void _mainWidget_Destroyed(object sender, EventArgs e)
        {
            try
            {
                foreach (ICompletionProvider completion in textEditor.Completion.Providers)
                    textEditor.Completion.RemoveProvider(completion);

                textEditor.Buffer.Changed -= OnTextHasChanged;
                textEditor.FocusInEvent -= OnTextBoxEnter;
                textEditor.FocusOutEvent -= OnTextBoxLeave;
                textEditor.KeyPressEvent -= OnKeyPress;
                scroller.Hadjustment.Changed -= Hadjustment_Changed;
                scroller.Vadjustment.Changed -= Vadjustment_Changed;
                mainWidget.Destroyed -= _mainWidget_Destroyed;

                // It's good practice to disconnect all event handlers, as it makes memory leaks
                // less likely. However, we may not "own" the event handlers, so how do we 
                // know what to disconnect?
                // We can do this via reflection. Here's how it currently can be done in Gtk#.
                // Windows.Forms would do it differently.
                // This may break if Gtk# changes the way they implement event handlers.
                textEditor.DetachAllHandlers();
                accel.Dispose();
                textEditor.Dispose();
                textEditor = null;
                owner = null;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Called when the user wants to change the editor style.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="e">Event arguments.</param>
        private void OnChangeStyle(object sender, EventArgs e)
        {
            try
            {
                using (Dialog popup = new Dialog("Choose a Style", mainWidget.Toplevel as Window, DialogFlags.Modal, new object[] { "Cancel", ResponseType.Cancel, "OK", ResponseType.Ok }))
                {
                    StyleSchemeChooserWidget styleChooser = new StyleSchemeChooserWidget();
                    popup.ContentArea.PackStart(styleChooser, true, true, 0);
                    popup.ShowAll();
                    int response = popup.Run();
                    if (response == (int)ResponseType.Ok)
                    {
                        textEditor.Buffer.StyleScheme = styleChooser.StyleScheme;
                        Configuration.Settings.EditorStyleName = styleChooser.StyleScheme.Id;
                    }
                }
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The vertical position has changed
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void Vadjustment_Changed(object sender, EventArgs e)
        {
            try
            {
                if (vertScrollPos > 0 && vertScrollPos < scroller.Vadjustment.Upper)
                    scroller.Vadjustment.Value = vertScrollPos;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The horizontal position has changed
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void Hadjustment_Changed(object sender, EventArgs e)
        {
            try
            {
                if (horizScrollPos > 0 && horizScrollPos < scroller.Hadjustment.Upper)
                    scroller.Hadjustment.Value = horizScrollPos;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Preprocesses key strokes so and emits the show-completion signal on
        /// the gtksourceview widget when the user presses the '.' key.
        /// </summary>
        /// <remarks>
        /// Our custom GtkSourceCompletionProvider uses the user-requested activation
        /// mode, which means that by default it only activates when the user presses
        /// control + space. In order to trigger a completion request at other times
        /// (such as when the user presses '.' in this case), we need to emit the
        /// show-completion signal on the GtkSourceView widget.
        /// </remarks>
        /// <param name="sender">Sending object</param>
        /// <param name="args">Event data.</param>
        [GLib.ConnectBefore] // Otherwise this is handled internally, and we won't see it
        private void OnKeyPress(object sender, KeyPressEventArgs args)
        {
            try
            {
                if (args.Event.Key == Gdk.Key.F3)
                {
                    if (string.IsNullOrEmpty(findForm.LookFor))
                        findForm.ShowFor(textEditor, searchContext, false);
                    else
                        findForm.FindNext(true, (args.Event.State & Gdk.ModifierType.ShiftMask) == 0, string.Format("Search text «{0}» not found.", findForm.LookFor));
                    args.RetVal = true;
                    return;
                }
                char previousChar = 'x';
                if (textEditor.Buffer.CursorPosition > 0)
                {
                    TextIter prevIter = textEditor.Buffer.GetIterAtOffset(textEditor.Buffer.CursorPosition - 1);
                    previousChar = prevIter.Char.ToCharArray().FirstOrDefault();
                }
                char keyChar = (char)Gdk.Keyval.ToUnicode(args.Event.KeyValue);
                if (keyChar == '.' && !char.IsDigit(previousChar))
                {
                    if (ContextItemsNeeded != null)
                    {
                        ContextItemsNeeded.Invoke(this, new NeedContextItemsArgs()
                        {
                            Coordinates = GetPositionOfCursor(),
                            Code = Text,
                            Offset = this.Offset,
                            ControlSpace = false,
                            ControlShiftSpace = false,
                            LineNo = CurrentLineNumber,
                            ColNo = CurrentColumnNumber
                        });
                    }
                    else
                        GLib.Signal.Emit(textEditor, "show-completion");
                }
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Gets the location (in screen coordinates) of the cursor.
        /// </summary>
        /// <returns>Tuple, where item 1 is the x-coordinate and item 2 is the y-coordinate.</returns>
        public System.Drawing.Point GetPositionOfCursor()
        {
            TextIter iter = textEditor.Buffer.GetIterAtOffset(Offset);
            if (iter.Equals(TextIter.Zero))
                return new System.Drawing.Point(0, 0);
            // Get the location of the cursor. This rectangle's x and y properties will be
            // the current line and column number.
            Gdk.Rectangle location = textEditor.GetIterLocation(iter);

            // Convert the buffer coordinates (line/col numbers) to actual cartesian
            // coordinates (note that these are relative to the origin of the GtkSourceView
            // widget's GdkWindow, not the GtkWindow itself).
            textEditor.BufferToWindowCoords(TextWindowType.Text, location.X, location.Y, out int x, out int y);

            // Now, convert these coordinates to be relative to the GtkWindow's origin.
            textEditor.TranslateCoordinates(mainWidget.Toplevel, x, y, out int windowX, out int windowY);

            // Don't forget to account for the offset of the window within the screen.
            // (Remember that the screen is made up of multiple monitors, so this is
            // what accounts for which particular monitor the on which the window is
            // physically displayed.)
            MasterView.MainWindow.GetOrigin(out int frameX, out int frameY);

            // Also add on the line height of the current line. Arguably, this doesn't
            // belong here - the method is called GetPositionOfCursor(), which doesn't
            // really imply anything about accounting for the line height. However,
            // all of the surrounding infrastructure really assumes that it *does* in
            // fact account for line height, as the intellisense popup (the only thing
            // which really uses these coods) will be displayed at this locataion.
            textEditor.GetLineYrange(iter, out int _, out int lineHeight);

            return new System.Drawing.Point(frameX + windowX, frameY + windowY + lineHeight);
        }

        /// <summary>
        /// Redraws the text editor.
        /// </summary>
        public void Refresh()
        {
            //textEditor.Options.ColorScheme = Configuration.Settings.EditorStyleName;
            textEditor.QueueDraw();
        }

        /// <summary>
        /// Display a list of completion options to the user.
        /// </summary>
        public void ShowCompletionItems(List<NeedContextItemsArgs.ContextItem> completionOptions)
        {

        }

        /// <summary>
        /// Hide the completion window.
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void HideCompletionWindow(object sender, EventArgs e)
        {
            try
            {
                //textEditor.Document.ReadOnly = false;
                textEditor.GrabFocus();
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Inserts a new completion option at the caret, potentially overwriting a partially-completed word.
        /// </summary>
        /// <param name="triggerWord">
        /// Word to be overwritten. May be empty.
        /// This function will overwrite the last occurrence of this word before the caret.
        /// </param>
        /// <param name="completionOption">Completion option to be inserted.</param>
        public void InsertCompletionOption(string completionOption, string triggerWord)
        {
            // todo - this shouldn't be necessary. need to overhaul the whole completion
            // mechanism with the new method built into the sourceview.
            if (string.IsNullOrEmpty(completionOption))
                return;

            // If no trigger word provided, insert at caret.
            if (string.IsNullOrEmpty(triggerWord))
            {
                int offset = Offset + completionOption.Length;
                textEditor.Buffer.InsertAtCursor(completionOption);
                // fixme
                //textEditor.Buffer.CursorPosition = offset;
                return;
            }

            // If trigger word is entire text, replace the entire text.
            if (Text == triggerWord)
            {
                Text = completionOption;
                //Offset = completionOption.Length;
                return;
            }

            // Overwrite the last occurrence of this word before the caret.
            int index = Text.Substring(0, Offset).LastIndexOf(triggerWord);
            if (index < 0)
                // If text does not contain trigger word, isnert at caret.
                InsertAtCaret(completionOption);

            string textBeforeTriggerWord = Text.Substring(0, index);

            string textAfterTriggerWord = "";
            if (Text.Length > index + triggerWord.Length)
                textAfterTriggerWord = Text.Substring(index + triggerWord.Length);

            // Changing the text property of the text editor will reset the scroll
            // position. To work around this, we record the scroll position before
            // we change the text then reset it manually afterwards.
            double verticalPosition = scroller.Vadjustment.Value;
            double horizontalPosition = scroller.Hadjustment.Value;

            Text = textBeforeTriggerWord + completionOption + textAfterTriggerWord;

            scroller.Vadjustment.Value = verticalPosition;
            scroller.Hadjustment.Value = horizontalPosition;
        }

        /// <summary>
        /// Insert the currently selected completion item into the text box.
        /// </summary>
        /// <param name="text">The text to be inserted.</param>
        public void InsertAtCaret(string text)
        {
            textEditor.Buffer.InsertAtCursor(text);
        }

        /// <summary>
        /// User has changed text. Invoke our OnTextChanged event.
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnTextHasChanged(object sender, EventArgs e)
        {
            try
            {
                TextHasChangedByUser?.Invoke(sender, e);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Entering the textbox event
        /// </summary>
        /// <param name="o">The calling object</param>
        /// <param name="args">The arguments</param>
        private void OnTextBoxEnter(object o, FocusInEventArgs args)
        {
            try
            {
                ((o as Widget).Toplevel as Gtk.Window).AddAccelGroup(accel);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Leaving the textbox event
        /// </summary>
        /// <param name="o">The calling object</param>
        /// <param name="e">The event arguments</param>
        private void OnTextBoxLeave(object o, EventArgs e)
        {
            try
            {
                ((o as Widget).Toplevel as Gtk.Window).RemoveAccelGroup(accel);
                if (LeaveEditor != null)
                    LeaveEditor.Invoke(this, e);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        #region Code related to Edit menu

        /// <summary>
        /// Add a menu item to the menu
        /// </summary>
        /// <param name="menuItemText">Menu item caption</param>
        /// <param name="onClick">Event handler</param>
        /// <returns>The menu item that was created</returns>
        public void AddMenuItem(string menuItemText, System.EventHandler onClick)
        {
            textEditor.PopulatePopup += (sender, args) =>
            {
                if (args.Popup is Menu menu)
                {
                    MenuItem item = new MenuItem(menuItemText);
                    if (onClick != null)
                        item.Activated += onClick;
                    item.ShowAll();
                    menu.Append(item);
                }
            };
        }

        /// <summary>
        /// Add an action (on context menu) on the series grid.
        /// </summary>
        public MenuItem AddContextSeparator()
        {
            textEditor.PopulatePopup += (sender, args) =>
            {
                if (args.Popup is Menu menu)
                {
                    MenuItem item = new SeparatorMenuItem();
                    item.ShowAll();
                    menu.Append(item);
                }
            };
            return null;
        }

        /// <summary>
        /// Add an action (on context menu) on the text area.
        /// </summary>
        /// <param name="menuItemText">The text of the menu item</param>
        /// <param name="onClick">The event handler to call when menu is selected</param>
        /// <param name="shortcut">The shortcut string</param>
        public MenuItem AddContextActionWithAccel(string menuItemText, System.EventHandler onClick, string shortcut)
        {
            textEditor.PopulatePopup += (sender, args) =>
            {
                if (args.Popup is Menu menu)
                {
                    ImageMenuItem item = new ImageMenuItem(menuItemText);
                    if (!string.IsNullOrEmpty(shortcut))
                    {
                        string keyName = string.Empty;
                        Gdk.ModifierType modifier = Gdk.ModifierType.None;
                        string[] keyNames = shortcut.Split(new char[] { '+' });
                        foreach (string name in keyNames)
                        {
                            if (name == "Ctrl")
                                modifier |= Gdk.ModifierType.ControlMask;
                            else if (name == "Shift")
                                modifier |= Gdk.ModifierType.ShiftMask;
                            else if (name == "Alt")
                                modifier |= Gdk.ModifierType.Mod1Mask;
                            else if (name == "Del")
                                keyName = "Delete";
                            else
                                keyName = name;
                        }
                        try
                        {
                            Gdk.Key accelKey = (Gdk.Key)Enum.Parse(typeof(Gdk.Key), keyName, false);
                            item.AddAccelerator("activate", accel, (uint)accelKey, modifier, AccelFlags.Visible);
                        }
                        catch
                        {
                        }
                    }
                    if (onClick != null)
                        item.Activated += onClick;
                    item.ShowAll();
                    menu.Append(item);
                }
            };
            return null;
        }

        /// <summary>
        /// The cut menu handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnCut(object sender, EventArgs e)
        {
            try
            {
                // todo: test this
                textEditor.Buffer.CutClipboard(Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", true)), true);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Copy menu handler 
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnCopy(object sender, EventArgs e)
        {
            try
            {
                // todo: test this
                textEditor.Buffer.CopyClipboard(Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", true)));
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Past menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnPaste(object sender, EventArgs e)
        {
            try
            {
                // todo: test this
                textEditor.Buffer.PasteClipboard(Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", true)));
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Undo menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnUndo(object sender, EventArgs e)
        {
            try
            {
                // tbi (do we even need this?)
                //MiscActions.Undo(textEditor.TextArea.GetTextEditorData());
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Redo menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnRedo(object sender, EventArgs e)
        {
            try
            {
                // tbi (do we even need this?)
                //MiscActions.Redo(textEditor.TextArea.GetTextEditorData());
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Find menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnFind(object sender, EventArgs e)
        {
            try
            {
                findForm.ShowFor(textEditor, searchContext, false);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// The Replace menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnReplace(object sender, EventArgs e)
        {
            try
            {
                findForm.ShowFor(textEditor, searchContext, true);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Changing the editor style menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnChangeEditorStyle(object sender, EventArgs e)
        {
            try
            {
                MenuItem subItem = (MenuItem)sender;
                string caption = ((Gtk.Label)(subItem.Children[0])).LabelProp;

                foreach (CheckMenuItem item in ((Menu)subItem.Parent).Children)
                {
                    item.Activated -= OnChangeEditorStyle;  // stop recursion
                    item.Active = (string.Compare(caption, ((Gtk.Label)item.Children[0]).LabelProp, true) == 0);
                    item.Activated += OnChangeEditorStyle;
                }

                Utility.Configuration.Settings.EditorStyleName = caption;
                //textEditor.Options.ColorScheme = caption;
                textEditor.QueueDraw();

                StyleChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Handle other changes to editor options. All we're really interested in 
        /// here at present is keeping track of the editor zoom level.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        private void EditorOptionsChanged(object sender, EventArgs e)
        {
            try
            {
                //Utility.Configuration.Settings.EditorZoom = textEditor.Options.Zoom;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }
        #endregion
    }
}
