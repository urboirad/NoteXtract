using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Windows.Forms;
using System.Threading;

public static class DragDropHelper
{
    private static IntPtr _windowHandle;
    private static bool _isDragging;
    private const uint WM_LBUTTONUP = 0x0202;

    public static string[] GetDroppedFiles()
    {
        List<string> files = new List<string>();

        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = GetWindowHandle();
            if (_windowHandle == IntPtr.Zero)
            {
                return files.ToArray();
            }
        }

        MSG msg;
        while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            switch (msg.message)
            {
                case WM_DROPFILES:
                    HDROP hDrop = new HDROP(msg.wParam);
                    uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0); // Use 0xFFFFFFFF to represent -1 as uint
                    for (uint i = 0; i < count; i++) // Use uint for the loop variable
                    {
                        uint length = DragQueryFile(hDrop, i, null, 0); // Cast i to uint
                        StringBuilder sb = new StringBuilder((int)(length + 1)); // Cast length to int
                        DragQueryFile(hDrop, i, sb, length + 1); // Cast i and length to uint and int, respectively
                        files.Add(sb.ToString());
                    }
                    DragFinish(hDrop);
                    _isDragging = false;
                    break;

                case WM_MOUSEMOVE:
                    if (_isDragging)
                    {
                        break;
                    }

                    POINT cursorPos;
                    GetCursorPos(out cursorPos);
                    if (WindowFromPoint(cursorPos) == _windowHandle)
                    {
                        DoDragEnter();
                        _isDragging = true;
                    }
                    break;

                case WM_LBUTTONUP:
                    if (_isDragging)
                    {
                        DoDragLeave();
                        _isDragging = false;
                    }
                    break;
            }
        }

        return files.ToArray();
    }

    private static IntPtr GetWindowHandle()
    {
        foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
        {
            EnumThreadWindows(thread.Id, (hwnd, lParam) =>
            {
                StringBuilder sb = new StringBuilder(256);
                GetClassName(hwnd, sb, sb.Capacity);
                if (sb.ToString() == "MonoGameWindow")
                {
                    _windowHandle = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        return _windowHandle;
    }

    private static void DoDragEnter()
    {
        DragAcceptFiles(_windowHandle, true);
    }

    private static void DoDragLeave()
    {
        DragAcceptFiles(_windowHandle, false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("shell32.dll")]
    private static extern uint DragQueryFile(HDROP hDrop, uint iFile, StringBuilder lpszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(HDROP hDrop);

    [DllImport("user32.dll")]
    private static extern bool DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    private const uint PM_REMOVE = 0x0001;
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hWnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;

        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HDROP
    {
        public IntPtr hMem;

        public HDROP(IntPtr hMem)
        {
            this.hMem = hMem;
        }

        public static implicit operator IntPtr(HDROP d)
        {
            return d.hMem;
        }
    }
}

class SpecializedOpenFileDialog
{
    private OpenFileDialog ofd = new OpenFileDialog();
    public SpecializedOpenFileDialog()
    {
        ofd.Multiselect = false;
        ofd.Filter = "*.html";
    }
    public DialogResult ShowDialog()
    {
        return ofd.ShowDialog();
    }
    public string FileName
    {
        get { return ofd.FileName; }
    }
}

namespace NoteXtract
{
    public class Game1 : Game
    {

        Texture2D logoTexture;
        Vector2 logoPosition;

        Texture2D background;
        Color backgroundColor;

        Texture2D statusBox;
        Rectangle statusBoxRec;
        Color statusBoxColor;

        Rectangle screenRec;

        SpriteFont MainFont;

        String statusText;

        private string _pdfText;

        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        private bool isFileDialogOpen = false;
        private bool isFileDialogClosed = false;
        private bool isTextSaved = false;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

        }

        protected override void Initialize()
        {
            // TODO: Add your initialization logic here

            screenRec = new Rectangle(0, 0, _graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);

            logoPosition = new Vector2(_graphics.PreferredBackBufferWidth / 2, _graphics.PreferredBackBufferHeight / 2);

            background = new Texture2D(GraphicsDevice, 1, 1);
            background.SetData(new[] { Color.White });
            backgroundColor = new Color(219, 190, 161);

            statusBox = new Texture2D(GraphicsDevice, 1, 1);
            statusBox.SetData(new[] { Color.White });
            statusBoxColor = new Color(63, 41, 43);
            statusBoxRec = new Rectangle(0, (_graphics.PreferredBackBufferHeight - 30), _graphics.PreferredBackBufferWidth, 30);

            statusText = new String("Click to select PDF...");

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // TODO: use this.Content to load your game content here

            logoTexture = Content.Load<Texture2D>("logo");

            MainFont = Content.Load<SpriteFont>("Fonts/Windows32");

        }

        protected override void Update(GameTime gameTime)
        {

            // TODO: Add your update logic here

            var keystate = Keyboard.GetState();

            // Check for left mouse button press
            var mouseState = Mouse.GetState();
            if (mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                if (!isFileDialogOpen) // Check if the file dialog is already open
                {
                    isFileDialogOpen = true; // Set the flag to true
                                             // Create a separate thread to show the file dialog
                    Thread uiThread = new Thread(ShowFileDialog);
                    uiThread.SetApartmentState(ApartmentState.STA);
                    uiThread.Start();
                }
            }

            if (isFileDialogOpen && isFileDialogClosed && !isTextSaved)
            {
                isFileDialogOpen = false;

                // Show a save file dialog for saving the extracted text
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Title = "Save Extracted Text",
                    Filter = "Text Files|*.txt",
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string textFilePath = saveFileDialog.FileName;
                    SaveTextToFile(_pdfText, textFilePath);
                    statusText = "Text saved to a file. Click again to open PDF.";
                    isTextSaved = true;
                }
            }

            base.Update(gameTime);
        }

        private void SaveTextToFile(string text, string fileName)
        {
            File.WriteAllText(fileName, text);
        }

        private string ExtractTextFromPdf(string filePath)
        {
            StringBuilder text = new StringBuilder();

            using (PdfReader reader = new PdfReader(filePath))
            {
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    text.Append(PdfTextExtractor.GetTextFromPage(reader, i));
                }
            }

            return text.ToString();
        }

        [STAThread]
        private void ShowFileDialog()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Open PDF File",
                Filter = "PDF Files|*.pdf",
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string pdfPath = openFileDialog.FileName;
                string pdfText = ExtractTextFromPdf(pdfPath);

                if (!string.IsNullOrEmpty(pdfText))
                {
                    _pdfText = pdfText;
                    statusText = "Text extracted from PDF. Click again to save it.";
                    Console.Write(pdfText);
                }

                // Set isFileDialogClosed to true when the file dialog is closed
                isFileDialogClosed = true;
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // TODO: Add your drawing code here

            _spriteBatch.Begin();

            _spriteBatch.Draw(background, screenRec, backgroundColor);

            _spriteBatch.Draw(statusBox, statusBoxRec, statusBoxColor);

            _spriteBatch.Draw(
                logoTexture,
                logoPosition,
                null,
                Color.White,
                0f,
                new Vector2(logoTexture.Width / 2, logoTexture.Height / 2),
                0.4f,
                SpriteEffects.None,
                0f
            );

            _spriteBatch.DrawString(MainFont, statusText, new Vector2(10, statusBoxRec.Y + 5), backgroundColor);

            _spriteBatch.End();

            base.Draw(gameTime);
        }

    }
}