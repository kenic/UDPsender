using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;

namespace WindowsFormsApplication1
{
    public partial class Form1 : Form
    {
        // 送信タイマのオブジェクト
        Timer timer = new Timer();

        // JPGエンコーダー関係
        ImageCodecInfo jpgEncoder;
        EncoderParameter encParam;
        EncoderParameters encParams;
        
        // 画面サイズ
        Int32 xmax, ymax;

        // 送信ブロックサイズ
        Int32 blockSize;
        Byte bSize;
        Byte bNum;

        // cmd用op定数
        public const Byte END = 0;
        public const Byte PARTW = 1;
        public const Byte FULLW = 2;
        public const Byte NOIMG = 3;
        public const Byte MUTE = 4;
        public const Byte VDWN = 5;
        // cmd長
        public const Byte CMDLEN = 5;

        // フルスクリーンモードフラグ
        public Boolean isFullScreenMode;

        // キャプチャー元のディスプレイ指定
        // primary = 0, secondary = 1
        Byte SourceDisplay;

        // partial capture用
        Graphics g;
        Bitmap bmp;

        // full capture用
        Graphics fg;
        Bitmap fbmp;

        Int32 capw, caph;

        // 送信用
        //Graphics sg;
        Bitmap sbmp;

        // 総送信量, エラーの回数
        Int64 totalSend;
        Int32 totalErr;
        Int32 totalSuc;

        // Socket
        UdpClient sendClient;
        IPEndPoint endPoint;

        //Debug
        Stopwatch sw = new Stopwatch(); 

        public Form1()
        {
            InitializeComponent();

            // Partialモード用
            capw = caph = 400; // 400px
            bmp = new Bitmap(capw, caph);
            
            // 送信画面サイズ (blockSize) * (blockSize)
            bNum = 4;
            bSize = 100;
            blockSize = bSize*bNum; 

            // 初期値はPartialモード
            isFullScreenMode = false;

            // 初期値はセカンダリディスプレイ
            SourceDisplay = 1;

            // Formはリサイズ禁止
            this.FormBorderStyle = FormBorderStyle.FixedSingle;

            // ダブルクリックによる最大化禁止
            this.MaximizeBox = false;

            // Formは常に手前
            this.TopMost = true;

            // ディスプレイのサイズ取得
            //ディスプレイの高さ
            //ymax = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
            ymax = System.Windows.Forms.Screen.AllScreens[SourceDisplay].Bounds.Height;
            //ディスプレイの幅
            //xmax = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            xmax = System.Windows.Forms.Screen.AllScreens[SourceDisplay].Bounds.Width;

            // Jpeg エンコーダー関連
            Int64 quality = 20; // 品質レベル: 0-100

            // JPEG用のエンコーダの取得
            foreach (ImageCodecInfo ici
                  in ImageCodecInfo.GetImageEncoders())
            {
                if (ici.FormatID == ImageFormat.Jpeg.Guid)
                {
                    jpgEncoder = ici;
                    break;
                }
            }

            // エンコーダに渡すパラメータの作成
            encParam = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            // パラメータを配列に格納
            encParams = new EncoderParameters(1);
            encParams.Param[0] = encParam;

            // タイマースタート
            //this.Size = new Size(capw, caph);
            //this.MaximumSize = new Size(capw, caph);
            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = 100; // ミリ秒
        }

        // 送信開始//停止ボタン
        private void button1_Click(object sender, EventArgs e)
        {
            if (this.button1.Text == "start")
            {
                this.button1.Text = "stop";
                // ソケットのオープン
                try
                {
                    sendClient = new UdpClient();
                    endPoint = new IPEndPoint(IPAddress.Broadcast, 11006);
                }
                catch (Exception er)
                {
                    MessageBox.Show("ソケットをオープンできませんでした。");
                    Environment.Exit(0);
                }
                // start timer
                timer.Enabled = true;
                timer.Start();
            }else{
                this.button1.Text = "start";
                // タイマー停止
                timer.Enabled = false;
                timer.Stop();
                sendEndMsg();
                // ソケットのクローズ
                sendClient.Close();
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (isFullScreenMode)
            {   // full screen mode
                sw.Restart();
                timer.Interval = 300;
                fullCap();
                sw.Stop();
                this.label3.Text = sw.ElapsedMilliseconds + "ms";
            }
            else
            {   // partial window mode
                timer.Interval = 100;
                sw.Restart();
                partialCap();
                sw.Stop();
                this.label3.Text = sw.ElapsedMilliseconds + "ms";
            }
        }

        private void fullCap()
        {
            Int32 x, y, hotx, hoty;
            Int64 sentBytes;

            x = y = hotx = hoty = 0;

            // 画面キャプチャ用のビットマップとグラフィックスのオブジェクト
            fbmp = new Bitmap(xmax, ymax);
            fg = this.CreateGraphics();
            fg = Graphics.FromImage(fbmp);

            // get cursor image and it's coord of upper-right corner and host spot.
            Bitmap cbmp = CaptureCursor(ref x, ref y, ref hotx, ref hoty);

            // Screen.AllScreens[1].Bounds の添え字が
            // 0がプライマリ、1がセカンダリディスプレイ
            // あとでこのあたりを見る http://d.hatena.ne.jp/sensepicker/20091025/1256439647
            // 画面の座標はこんな感じ http://yamatyuu.net/computer/program/sdk/base/enumdisplay/index.html
            Rectangle rect = Screen.AllScreens[SourceDisplay].Bounds;


            // Capture whole screen
            //fg.CopyFromScreen(0, 0, 0, 0, new Size(xmax, ymax));
            fg.CopyFromScreen(rect.X, rect.Y, 0, 0, new Size(xmax, ymax));

            if (cbmp != null) // カーソルが取得できたら
            {
                // draw mouse cursor
                if (SourceDisplay == 0) // when the display is primary
                {
                    fg.DrawImage(cbmp, x - hotx, y - hoty);
                }
                else // when it's secondary
                {
                    fg.DrawImage(cbmp, x + System.Math.Abs(rect.X) - hotx, y + System.Math.Abs(rect.Y) - hoty);
                }
                cbmp.Dispose();
            }

            // 送信用ビットマップの準備
            sbmp = new Bitmap(blockSize, blockSize);
            // 送信用ビットマップのGraphicsオブジェクトを作成する
            //sg = Graphics.FromImage(sbmp);

            Byte i, j;
            for (i = 0; i < ((double)xmax / (double)blockSize); i++)
            {
                for (j = 0; j < ((double)ymax / (double)blockSize); j++)
                {

                    //切り取る部分の範囲、始点x 始点y 幅 高さ
                    Int32 bw = blockSize; 
                    Int32 bh = blockSize;

                    if (blockSize * i + blockSize > xmax)
                        bw = xmax - blockSize * i;
                    if (blockSize * j + blockSize > ymax)
                        bh = ymax - blockSize * j;
                     
                    // 切りとる矩形を作成
                    Rectangle srcRect = new Rectangle(blockSize * i, blockSize * j, bw, bh);

                    //画像の一部をコピー
                    sbmp = fbmp.Clone(srcRect, fbmp.PixelFormat);

                    // 画像を送信
                    sentBytes = SendMessage(bNum, i, j);
                    totalSend += sentBytes;
                }
            }

            fg.Dispose();
            fbmp.Dispose();
            bmp.Dispose();

            if (totalSend < 1024)
                this.label1.Text = totalSend.ToString() + "B sent";
            else if (totalSend < 1024 * 1024)
                this.label1.Text = (totalSend / 1024).ToString() + "KB sent";
            else if (totalSend < 1024 * 1024 * 1024)
                this.label1.Text = Math.Round(((double)totalSend / 1024 / 1024), 2, MidpointRounding.AwayFromZero).ToString() + "MB sent";

            this.label3.Text = sw.ElapsedMilliseconds + "ms";

            this.label2.Text = "err: " + totalErr.ToString() + "/" + totalSuc.ToString();
        
        }

        private void partialCap()
        {
            Int32 x, y, hotx, hoty;
            Int32 capx, capy; // キャプチャー領域始点座標
            Int32 poffx, poffy; // ポインタオフセット
            Int32 sentBytes;
            Int32 boundminx, boundminy, boundmaxx, boundmaxy; // 画面の境界

            x = y = hotx = hoty = 0;

            boundminx = Screen.AllScreens[SourceDisplay].Bounds.X;
            boundminy = Screen.AllScreens[SourceDisplay].Bounds.Y;
            boundmaxx = boundminx + Screen.AllScreens[SourceDisplay].Bounds.Width;
            boundmaxy = boundminy + Screen.AllScreens[SourceDisplay].Bounds.Height;

            bmp = new Bitmap(capw, caph);
            g = this.CreateGraphics();
            g = Graphics.FromImage(bmp);

            // get cursor image and it's coord of upper-right corner and host spot.
            Bitmap cbmp = CaptureCursor(ref x, ref y, ref hotx, ref hoty);

            // if the capture rectangle contains outside of screen, omit it.
            if ((x - capw / 2) < boundminx) // 左端に達した
            {
                capx = boundminx;
                poffx = System.Math.Abs(boundminx - x) - hotx;
            }
            else if ((x + capw / 2) > boundmaxx) // 右端に達した
            {
                capx = boundmaxx - capw;
                poffx = capw - (boundmaxx - x) - hotx;
            }
            else //どっちでもなければ真ん中
            {
                capx = x - capw / 2;
                poffx = capw / 2 - hotx;
            }
            if ((y - caph / 2) < boundminy) // 上端に達した
            {
                capy = boundminy;
                poffy = y - hoty;
            }
            else if ((y + caph / 2) > boundmaxy) // 下端に達した
            {
                capy = boundmaxy - caph;
                poffy = caph - (boundmaxy - y) - hoty;
            }
            else
            {
                capy = y - caph / 2;
                poffy = caph / 2 - hoty;
            }
            // Capture screen
            g.CopyFromScreen(capx, capy, 0, 0, new Size(capw, caph));

            if (cbmp != null) // カーソルが取得できてたら
            {
                // draw mouse cursor
                g.DrawImage(cbmp, poffx, poffy);
                // dispose
                cbmp.Dispose();
            }

            // 送信用ビットマップの準備
            sbmp = new Bitmap(blockSize, blockSize);

            // 分割して送信
            Byte i, j;
            for (i = 0; i < ((double)caph / (double)blockSize); i++)
            {
                for (j = 0; j < ((double)capw / (double)blockSize); j++)
                {
                    //切り取る部分の範囲、始点x 始点y 幅 高さ
                    Int32 bw = blockSize;
                    Int32 bh = blockSize;

                    if (blockSize * i + blockSize > xmax)
                        bw = xmax - blockSize * i;
                    if (blockSize * j + blockSize > ymax)
                        bh = ymax - blockSize * j;

                    // 切り取る矩形のセット
                    Rectangle srcRect = new Rectangle(blockSize * i, blockSize * j, bw, bh);

                    // 画像の送信部分を切り取り
                    sbmp = bmp.Clone(srcRect, bmp.PixelFormat);

                    // 画像の送信
                    sentBytes = SendMessage(bNum, j, i);
                    totalSend += sentBytes;
                }
            }
            g.Dispose();
            sbmp.Dispose();
            bmp.Dispose();

            if (totalSend < 1024)
                this.label1.Text = totalSend.ToString() + "B sent";
            else if (totalSend < 1024*1024)
                this.label1.Text = (totalSend/1024).ToString() + "KB sent";
            else if (totalSend < 1024 * 1024 * 1024)
                this.label1.Text = Math.Round(((double)totalSend / 1024 / 1024), 2, MidpointRounding.AwayFromZero).ToString() + "MB sent";

            this.label2.Text = "err: " + totalErr.ToString() + "/" + totalSuc.ToString();
            }

        private int SendMessage(Byte blockSize, Byte x, Byte y)
        {
            // 送信画像用ms
            MemoryStream ms = new MemoryStream();

            // 送信画像をjpgでmsに保存
            sbmp.Save(ms, jpgEncoder, encParams);

            // ms を byte[]に変換
            Byte[] message = ms.GetBuffer();

            // モードによってopの値をセット
            Byte op;
            if (isFullScreenMode)
                op = FULLW;
            else
                op = PARTW;

            // コマンド列を作成
            Byte[] cmd = new Byte[CMDLEN] { op, bSize, bNum, x, y };

            //送信データ用の新しい配列を作る
            Byte[] senddata = new Byte[cmd.Length + message.Length];

            //マージする配列のデータをコピーする
            Array.Copy(cmd, senddata, cmd.Length); // cmd
            Array.Copy(message, 0, senddata, cmd.Length, message.Length); // data

            try
            {
                // データの送出
                sendClient.Send(senddata, senddata.Length, endPoint);
            }
            catch (Exception e)
            {
                //MessageBox.Show(e.Message);
                totalErr += 1;
            }

            totalSuc += 1;
            return message.Length;

        }

        /// <summary>
        /// 終了メッセージの送信
        /// </summary>
        private void sendEndMsg()
        {
            UdpClient sendClient = new UdpClient();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 11006);

            Byte[] cmd = new Byte[5] { END, bNum, 0, 0, 0 };

            Byte i;
            for (i = 0; i < 3; i++)
            {
                try
                {
                    sendClient.Send(cmd, cmd.Length, endPoint);
                    sendClient.Close();
                }
                catch (Exception e)
                {
                    //MessageBox.Show(e.Message);
                }
                System.Threading.Thread.Sleep(200); // ミリ秒
            }
        }

        /// <summary>
        /// MUTEメッセージの送信
        /// </summary>
        private void sendMuteMsg()
        {
            UdpClient sendClient = new UdpClient();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 11006);

            Byte[] cmd = new Byte[5] { MUTE, bNum, 0, 0, 0 };

            Byte i;
            for (i = 0; i < 3; i++)
            {
                try
                {
                    sendClient.Send(cmd, cmd.Length, endPoint);
                    sendClient.Close();
                }
                catch (Exception e)
                {
                    //MessageBox.Show(e.Message);
                }
                System.Threading.Thread.Sleep(200); // ミリ秒
            }
        }

        /// <summary>
        /// VDWNメッセージの送信 (ボリュームダウン)
        /// </summary>
        private void sendVdwnMsg()
        {
            UdpClient sendClient = new UdpClient();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 11006);

            Byte[] cmd = new Byte[5] { VDWN, bNum, 0, 0, 0 };

            Byte i;
            for (i = 0; i < 3; i++)
            {
                try
                {
                    sendClient.Send(cmd, cmd.Length, endPoint);
                    sendClient.Close();
                }
                catch (Exception e)
                {
                    //MessageBox.Show(e.Message);
                }
                System.Threading.Thread.Sleep(200); // ミリ秒
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
                SourceDisplay = 0; // Primary
            else
                SourceDisplay = 1; // Secondary

            // ディスプレイのサイズを再取得
            //ディスプレイの高さ
            ymax = System.Windows.Forms.Screen.AllScreens[SourceDisplay].Bounds.Height;
            //ディスプレイの幅
            xmax = System.Windows.Forms.Screen.AllScreens[SourceDisplay].Bounds.Width;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            sendMuteMsg();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            sendVdwnMsg();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            // キャプチャモードの切り替え
            isFullScreenMode = checkBox1.Checked;
        }

        static Bitmap CaptureCursor(ref int x, ref int y, ref int hotx, ref int hoty)
        {
            // かなりややこしいんだが、画面キャプチャでカーソルはとれないので、
            // 別途用意しなくちゃいけない

            CursorInfo cursorInfo = new CursorInfo();

            cursorInfo.Size = Marshal.SizeOf(cursorInfo.GetType());
            if (!Win32Api.GetCursorInfo(out cursorInfo))
            {
                return null;
            }

            if (cursorInfo.Flags != Win32Api.CURSOR_SHOWING)
            {
                x = cursorInfo.Position.X;
                y = cursorInfo.Position.Y;
                hotx = 0;
                hoty = 0;
                return null;
            }

            IntPtr hicon = Win32Api.CopyIcon(cursorInfo.Handle);
            if (hicon == IntPtr.Zero)
                return null;
            
            IconInfo iconInfo;
            if (!Win32Api.GetIconInfo(hicon, out iconInfo))
                return null;

            // x = cursorInfo.Position.X - ((int)iconInfo.xHotspot);
            // y = cursorInfo.Position.Y - ((int)iconInfo.yHotspot);

            x = cursorInfo.Position.X;
            y = cursorInfo.Position.Y;
            hotx = ((int)iconInfo.xHotspot);
            hoty = ((int)iconInfo.yHotspot);

            using (Bitmap maskBitmap = Bitmap.FromHbitmap(iconInfo.hbmMask))
            {

                // Is this a monochrome cursor?
                if (maskBitmap.Height == maskBitmap.Width * 2)
                {
                    Bitmap resultBitmap = new Bitmap(maskBitmap.Width, maskBitmap.Width);

                    Graphics desktopGraphics = Graphics.FromHwnd(Win32Api.GetDesktopWindow());
                    IntPtr desktopHdc = desktopGraphics.GetHdc();

                    IntPtr maskHdc = Win32Api.CreateCompatibleDC(desktopHdc);
                    IntPtr oldPtr = Win32Api.SelectObject(maskHdc, maskBitmap.GetHbitmap());

                    using (Graphics resultGraphics = Graphics.FromImage(resultBitmap))
                    {
                        IntPtr resultHdc = resultGraphics.GetHdc();

                        // These two operation will result in a black cursor over a white background.
                        // Later in the code, a call to MakeTransparent() will get rid of the white background.
                        Win32Api.BitBlt(resultHdc, 0, 0, 32, 32, maskHdc, 0, 32, Win32Api.TernaryRasterOperations.SRCCOPY);
                        Win32Api.BitBlt(resultHdc, 0, 0, 32, 32, maskHdc, 0, 0, Win32Api.TernaryRasterOperations.SRCINVERT);

                        resultGraphics.ReleaseHdc(resultHdc);
                    }

                    IntPtr newPtr = Win32Api.SelectObject(maskHdc, oldPtr);

                    Win32Api.DeleteDC(maskHdc);
                    //Win32Api.DeleteDC(desktopHdc);
                    //Win32Api.DeleteObject(oldPtr);
                    Win32Api.DeleteObject(newPtr);
                    
                    /*
                     * Win32Api.DeleteDC(oldPtr);
                    Win32Api.DeleteDC(newPtr);
                    Win32Api.DeleteDC(maskHdc);
                    desktopGraphics.ReleaseHdc(desktopHdc);
                    desktopGraphics.Dispose();
                    */

                    // Remove the white background from the BitBlt calls,
                    // resulting in a black cursor over a transparent background.
                    resultBitmap.MakeTransparent(Color.White);

                    //goto END;

                    //maskBitmap.Dispose();
                    //resultBitmap.Dispose();
                    //desktopGraphics.Dispose();

                    Win32Api.DeleteObject(iconInfo.hbmColor);
                    Win32Api.DeleteObject(iconInfo.hbmMask);
                    Win32Api.DestroyIcon(hicon);

                    return resultBitmap;
                }
            }

            // リソースの解放
            Win32Api.DeleteObject(iconInfo.hbmColor);
            Win32Api.DeleteObject(iconInfo.hbmMask);

            Icon icon = Icon.FromHandle(hicon);            

            Bitmap bmp = icon.ToBitmap();

            Win32Api.DestroyIcon(hicon);
            icon.Dispose();

            return bmp;
        }

    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Handle;
        public Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public bool fIcon;         // Specifies whether this structure defines an icon or a cursor. A value of TRUE specifies 
        public Int32 xHotspot;     // Specifies the x-coordinate of a cursor's hot spot. If this structure defines an icon, the hot 
        public Int32 yHotspot;     // Specifies the y-coordinate of the cursor's hot spot. If this structure defines an icon, the hot 
        public IntPtr hbmMask;     // (HBITMAP) Specifies the icon bitmask bitmap. If this structure defines a black and white icon, 
        public IntPtr hbmColor;    // (HBITMAP) Handle to the icon color bitmap. This member can be optional if this 
    }

    /// Win32API の extern 宣言クラス
    public class Win32Api
    {
        public const Int32 CURSOR_SHOWING = 0x00000001;

        public enum TernaryRasterOperations : uint
        {
            /// <summary>dest = source</summary>
            SRCCOPY = 0x00CC0020,
            /// <summary>dest = source OR dest</summary>
            SRCPAINT = 0x00EE0086,
            /// <summary>dest = source AND dest</summary>
            SRCAND = 0x008800C6,
            /// <summary>dest = source XOR dest</summary>
            SRCINVERT = 0x00660046,
            /// <summary>dest = source AND (NOT dest)</summary>
            SRCERASE = 0x00440328,
            /// <summary>dest = (NOT source)</summary>
            NOTSRCCOPY = 0x00330008,
            /// <summary>dest = (NOT src) AND (NOT dest)</summary>
            NOTSRCERASE = 0x001100A6,
            /// <summary>dest = (source AND pattern)</summary>
            MERGECOPY = 0x00C000CA,
            /// <summary>dest = (NOT source) OR dest</summary>
            MERGEPAINT = 0x00BB0226,
            /// <summary>dest = pattern</summary>
            PATCOPY = 0x00F00021,
            /// <summary>dest = DPSnoo</summary>
            PATPAINT = 0x00FB0A09,
            /// <summary>dest = pattern XOR dest</summary>
            PATINVERT = 0x005A0049,
            /// <summary>dest = (NOT dest)</summary>
            DSTINVERT = 0x00550009,
            /// <summary>dest = BLACK</summary>
            BLACKNESS = 0x00000042,
            /// <summary>dest = WHITE</summary>
            WHITENESS = 0x00FF0062,
            /// <summary>
            /// Capture window as seen on screen.  This includes layered windows
            /// such as WPF windows with AllowsTransparency="true"
            /// </summary>
            CAPTUREBLT = 0x40000000
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(out CursorInfo info);

        [DllImport("user32.dll")]
        public static extern IntPtr CopyIcon(IntPtr handle);

        [DllImport("user32.dll", EntryPoint = "GetIconInfo")]
        public static extern bool GetIconInfo(IntPtr hIcon, out IconInfo info);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, PreserveSig = true, SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }

}

