using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using NppPluginNET;
using System.Collections.Generic;
using System.Diagnostics;

namespace GoAutocomplete
{
    class Main
    {
        #region " Fields "
        internal const string PluginName = "GoAutocomplete";
        static string iniFilePath = null;
        static bool someSetting = false;
        static frmMyDlg frmMyDlg = null;
        static int idMyDlg = -1;
        static Bitmap tbBmp = Properties.Resources.star;
        static Bitmap tbBmp_tbTab = Properties.Resources.star_bmp;
        static Icon tbIcon = null;
        #endregion

        #region " StartUp/CleanUp "
        internal static void CommandMenuInit()
        {
            StringBuilder sbIniFilePath = new StringBuilder(Win32.MAX_PATH);
            Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_GETPLUGINSCONFIGDIR, Win32.MAX_PATH, sbIniFilePath);
            iniFilePath = sbIniFilePath.ToString();
            if (!Directory.Exists(iniFilePath)) Directory.CreateDirectory(iniFilePath);
            iniFilePath = Path.Combine(iniFilePath, PluginName + ".ini");
            someSetting = (Win32.GetPrivateProfileInt("SomeSection", "SomeKey", 0, iniFilePath) != 0);

            PluginBase.SetCommand(0, "MyMenuCommand", myMenuFunction, new ShortcutKey(false, false, false, Keys.None));
            PluginBase.SetCommand(1, "MyDockableDialog", myDockableDialog); idMyDlg = 1;
            PluginBase.SetCommand(2, "Go (Golang) Autocomplete", autocompleteGolang, new ShortcutKey(false, true, false, Keys.Space));
        }
        internal static void SetToolBarIcon()
        {
            toolbarIcons tbIcons = new toolbarIcons();
            tbIcons.hToolbarBmp = tbBmp.GetHbitmap();
            IntPtr pTbIcons = Marshal.AllocHGlobal(Marshal.SizeOf(tbIcons));
            Marshal.StructureToPtr(tbIcons, pTbIcons, false);
            Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_ADDTOOLBARICON, PluginBase._funcItems.Items[idMyDlg]._cmdID, pTbIcons);
            Marshal.FreeHGlobal(pTbIcons);
        }
        internal static void PluginCleanUp()
        {
            Win32.WritePrivateProfileString("SomeSection", "SomeKey", someSetting ? "1" : "0", iniFilePath);
        }
        #endregion

        #region " Menu functions "

        //
        // Low-level hooks to get the pixel position of the main NPP window
        //

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;        // x position of upper-left corner  
            public int Top;         // y position of upper-left corner  
            public int Right;       // x position of lower-right corner  
            public int Bottom;      // y position of lower-right corner  
        }


        //
        // Custom WinForms class for the autocomplete popup box
        //

        public class AutocompleteForm : Form
        {
            private int _wordStartPosition;
            private string _originalWord;
            private ListBox _suggestions;
            public ListBox Suggestions
            {
                get
                {
                    return this._suggestions;
                }
            }
            private void _suggestions_SelectedIndexChanged(object sender, System.EventArgs e)
            {
                try
                {
                    string selectedItem = _suggestions.SelectedItem.ToString();
                    string[] tokens = selectedItem.Split('\t');
                    if (tokens != null && tokens.Length >= 1 && !String.IsNullOrEmpty(tokens[1]))
                    {
                        Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_SETANCHOR, _wordStartPosition, 0);
                        Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_REPLACESEL, 0, tokens[0]);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("An error occurred with the GoAutocomplete plugin: " + ex.Message);
                }
            }

            public AutocompleteForm()
            {
                try
                {
                    // Get the position of the overall window
                    RECT mainWindowPosition;
                    GetWindowRect(PluginBase.GetCurrentScintilla(), out mainWindowPosition);
                    // Get the cursor postion, offset from the window position
                    int currentPos = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETCURRENTPOS, 0, 0);
                    int caretOffsetX = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_POINTXFROMPOSITION, 0, currentPos);
                    int caretOffsetY = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_POINTYFROMPOSITION, 0, currentPos);
                    // Get the height of each line in pixels, so the autocomplete pop-up can be offset to fall underneath the current line 
                    int lineHeight = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_TEXTHEIGHT, 0, 0);

                    // Determine coordinates for placing the autocomplete popup, by adding aLl the offsets to the NPP window coordinates
                    int positionX = mainWindowPosition.Left + caretOffsetX;
                    int positionY = mainWindowPosition.Top + caretOffsetY + lineHeight;

                    // Configure WinForms attributes
                    MaximizeBox = false;
                    MinimizeBox = false;
                    FormBorderStyle = FormBorderStyle.None;
                    Size = new Size(400, 100);
                    StartPosition = FormStartPosition.Manual;
                    Location = new System.Drawing.Point(positionX, positionY);
                    AutoScroll = true;
                    //KeyPreview = true;

                    // Add a ListBox for autocomplete suggestions
                    _suggestions = new ListBox();
                    _suggestions.Dock = System.Windows.Forms.DockStyle.Fill;
                    _suggestions.SelectedIndexChanged += new EventHandler(_suggestions_SelectedIndexChanged);
                    Controls.Add(_suggestions);

                    // Store the original word and its starting point, so that it can later be replaced or restored
                    int cursorPosition = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETCURRENTPOS, 0, 0);
                    _wordStartPosition = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_WORDSTARTPOSITION, cursorPosition, 1);
                    _originalWord = "";
                    int bufferCapacity = 1024;
                    string currentWord = "";
                    using (Sci_TextRange textRange = new Sci_TextRange(_wordStartPosition, cursorPosition, bufferCapacity))
                    {
                        Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETTEXTRANGE, 0, textRange.NativePointer);
                        _originalWord = textRange.lpstrText;
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("An error occurred with the GoAutocomplete plugin: " + e.Message);
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                try
                {
                    if (keyData == Keys.Escape)
                    {
                        // Restore the original word, and close the popup
                        Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_SETANCHOR, _wordStartPosition, 0);
                        Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_REPLACESEL, 0, _originalWord);
                        this.Close();
                        return true;
                    }
                    else if (keyData == Keys.Enter)
                    {
                        // The currently-selected suggestion should already be inserted temporarily into the text by 
                        // the "_suggestions_SelectedIndexChanged" event handler, so just close the pop-up and let 
                        // that temporary change become fixed in place.
                        this.Close();
                        return true;
                    }
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                catch (Exception e)
                {
                    MessageBox.Show("An error occurred with the GoAutocomplete plugin: " + e.Message);
                    return false;
                }
            }

            // First attempt at a drill-down, rebuilding the popup box as the user types characters while its open.
            // Running into problems differentiating between key presses we want to intercept, vs. those we don't.
            // Re-visit later.
            /*
            protected override bool ProcessKeyPreview(ref Message msg)
            {
                KeyEventArgs keyEventArgs = new KeyEventArgs(((Keys)((int)((long)msg.WParam))) | ModifierKeys);
                if(keyEventArgs.KeyCode >= Keys.A && keyEventArgs.KeyCode <= Keys.Z)
                {
                    string charTyped = keyEventArgs.KeyCode.ToString();
                    if(keyEventArgs.Shift)
                    {
                        charTyped = charTyped.ToUpper();
                    }
                    Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_REPLACESEL, 0, charTyped);
                }
                else
                {
                    this.Close();
                }
                return true;
            }
            */
        }


        //
        // Handler method for the Go autocomplete command
        //

        static void autocompleteGolang()
        {
            try
            {
                StringBuilder path = new StringBuilder(Win32.MAX_PATH);
                Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_GETFULLCURRENTPATH, 0, path);
                bool isDocTypeGolang = path.ToString().ToLower().EndsWith(".go");

                if (isDocTypeGolang)
                {
                    // Get the text from the working file's in-memory state, so the user doesn't have to save the file to disk 
                    // for "gocode.exe" to see its current state
                    int documentLength = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETTEXTLENGTH, 0, 0);
                    byte[] documentBytes = new byte[documentLength];
                    string documentText = "";
                    unsafe
                    {
                        fixed (byte* p = documentBytes)
                        {
                            IntPtr ptr = (IntPtr)p;
                            Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETTEXT, documentLength, ptr);
                        }
                        documentText = System.Text.Encoding.UTF8.GetString(documentBytes).TrimEnd('\0');
                    }


                    // Run "gocode.exe" on the current text and collect its output
                    StringBuilder sbNppPath = new StringBuilder(Win32.MAX_PATH);
                    Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_GETNPPDIRECTORY, Win32.MAX_PATH, sbNppPath);
                    string nppPath = sbNppPath.ToString();
                    string gocodePath = "\"" + nppPath + "\\plugins\\gocode.exe\"";
                    int cursorPosition = (int)Win32.SendMessage(PluginBase.GetCurrentScintilla(), SciMsg.SCI_GETCURRENTPOS, 0, 0);
                    gocodeStart(gocodePath);
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = gocodePath,
                            Arguments = "-f csv autocomplete " + cursorPosition,
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    bool started = proc.Start();
                    proc.StandardInput.WriteLine(documentText);
                    proc.StandardInput.Write((char)26);
                    proc.StandardInput.Flush();
                    proc.StandardInput.Close();
                    proc.WaitForExit();
                    List<string> output = new List<string>();
                    while (proc.StandardOutput.ReadLine() != null)
                    {
                        output.Add(proc.StandardOutput.ReadLine());
                    }

                    // Render the "gocode.exe" output in an autocomplete pop-up
                    using (AutocompleteForm popup = new AutocompleteForm())
                    {
                        foreach (string line in output)
                        {
                            string type = "";
                            string suggestion = "";
                            string description = "";
                            if (!String.IsNullOrEmpty(line))
                            {
                                string[] tokens = line.Split(',');
                                if (tokens != null)
                                {
                                    if (tokens.Length > 0 && !String.IsNullOrEmpty(tokens[0]))
                                    {
                                        type = tokens[0];
                                    }
                                    if (tokens.Length > 2 && !String.IsNullOrEmpty(tokens[2]))
                                    {
                                        suggestion = tokens[2];
                                    }
                                    if (tokens.Length > 4 && !String.IsNullOrEmpty(tokens[4]))
                                    {
                                        description = tokens[4];
                                    }
                                }
                            }
                            popup.Suggestions.Items.Add(suggestion + "\t" + type + "\t" + description + "\n");
                        }
                        popup.ShowDialog();
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("An error occurred with the GoAutocomplete plugin: " + e.Message);
            }
        }

        private static void gocodeStart(string gocodePath)
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gocodePath,
                    Arguments = "set propose-builtins true",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            bool started = proc.Start();
            proc.WaitForExit();
        }





        //
        // Handler methods that came with the template.  To be removed before public release.
        //

        internal static void myMenuFunction()
        {
            MessageBox.Show("Hello N++!");
        }
        internal static void myDockableDialog()
        {
            if (frmMyDlg == null)
            {
                frmMyDlg = new frmMyDlg();

                using (Bitmap newBmp = new Bitmap(16, 16))
                {
                    Graphics g = Graphics.FromImage(newBmp);
                    ColorMap[] colorMap = new ColorMap[1];
                    colorMap[0] = new ColorMap();
                    colorMap[0].OldColor = Color.Fuchsia;
                    colorMap[0].NewColor = Color.FromKnownColor(KnownColor.ButtonFace);
                    ImageAttributes attr = new ImageAttributes();
                    attr.SetRemapTable(colorMap);
                    g.DrawImage(tbBmp_tbTab, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, attr);
                    tbIcon = Icon.FromHandle(newBmp.GetHicon());
                }

                NppTbData _nppTbData = new NppTbData();
                _nppTbData.hClient = frmMyDlg.Handle;
                _nppTbData.pszName = "My dockable dialog";
                _nppTbData.dlgID = idMyDlg;
                _nppTbData.uMask = NppTbMsg.DWS_DF_CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR;
                _nppTbData.hIconTab = (uint)tbIcon.Handle;
                _nppTbData.pszModuleName = PluginName;
                IntPtr _ptrNppTbData = Marshal.AllocHGlobal(Marshal.SizeOf(_nppTbData));
                Marshal.StructureToPtr(_nppTbData, _ptrNppTbData, false);

                Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_DMMREGASDCKDLG, 0, _ptrNppTbData);
            }
            else
            {
                Win32.SendMessage(PluginBase.nppData._nppHandle, NppMsg.NPPM_DMMSHOW, 0, frmMyDlg.Handle);
            }
        }
        #endregion
    }
}