using Hardcodet.Wpf.TaskbarNotification;
using SocketIOClient;
using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WpfApp1.Classes;
using WpfApp1.Pages;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private SocketIO socket;
        private DispatcherTimer timer;
        private Questionnaire questionnaire;
        private int currentQuestionIndex;
        private int correctAnswers;
        private TaskbarIcon taskbarIcon;
        private string uid;


        private bool isKeyboardLocked = false;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (isKeyboardLocked)
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void LockKeyboard()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
            isKeyboardLocked = true;
        }

        private void UnlockKeyboard()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                isKeyboardLocked = false;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Initialize();
            Closing += Window_Closing;
            uid = UIDGenerator.GenerateUID();
            uidText.Text = "UID: " + uid;
            SaveUidToServerAsync(uid);
            SetHighPriority();
        }

        private void Initialize()
        {
            ShowSplashScreen();
            ConnectToServer(true);
            InitializeSocket();
            questionnaire = new Questionnaire();
            InitializeTaskbarIcon();
            HideToTray();
        }

        private void SetHighPriority()
        {
            Process currentProcess = Process.GetCurrentProcess();
            if (currentProcess != null)
            {
                currentProcess.PriorityClass = ProcessPriorityClass.High;
            }
        }

        private void ShowSplashScreen()
        {
            Pages.SplashScreen splashScreen = new Pages.SplashScreen();
            splashScreen.ShowDialog();
        }

        private void InitializeTaskbarIcon()
        {
            taskbarIcon = new TaskbarIcon
            {
                Icon = Properties.Resources.icon,
                ToolTipText = "Родительский контроль"
            };
            taskbarIcon.TrayMouseDoubleClick += TaskbarIcon_DoubleClick;
        }

        private void TaskbarIcon_DoubleClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
            taskbarIcon.Visibility = Visibility.Collapsed;
        }

        private void HideToTray()
        {
            this.Hide();
            taskbarIcon.Visibility = Visibility.Visible;
        }

        private async void ShowQuestion(int index)
        {
            if (index < questionnaire.Questions.Count)
            {
                Question question = questionnaire.Questions[index];
                textBlock.Text = question.Text;

                answerStackPanel.Children.Clear();

                StackPanel buttonStackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 20)
                };

                foreach (string option in question.Options)
                {
                    Button button = new Button
                    {
                        Content = option,
                        Style = (Style)FindResource("ColoredButtonStyle"),
                        Tag = option
                    };
                    button.Click += AnswerButton_Click;
                    buttonStackPanel.Children.Add(button);
                }

                answerStackPanel.Children.Add(buttonStackPanel);
            }
            else
            {
                textBlock.Text = $"Тест завершен и уведомление было отправлено";

                HttpClient client = new HttpClient();
                var response = await client.GetAsync("http://localhost:3000/notify");

                answerStackPanel.Children.Clear();

                LockKeyboard();
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray();
            }
            else if (WindowState == WindowState.Normal)
            {
                taskbarIcon.Visibility = Visibility.Collapsed;
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (timer != null && timer.IsEnabled)
            {
                e.Cancel = true;
                MessageBox.Show("Пожалуйста, дождитесь завершения таймера.");
            }
            else
            {
                e.Cancel = true;
                HideToTray();
            }
            base.OnClosing(e);
        }


        private void AnswerButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            Question question = questionnaire.Questions[currentQuestionIndex];

            if (question.Options.IndexOf(button.Tag.ToString()) == question.CorrectIndex)
            {
                correctAnswers++;
            }

            currentQuestionIndex++;
            ShowQuestion(currentQuestionIndex);
        }

        private async void ConnectToServer(bool connect)
        {
            using (HttpClient client = new HttpClient())
            {
                string action = connect ? "connect" : "disconnect";
                Uri url = new Uri($"http://localhost:3000?action={action}");
                await client.GetAsync(url);

                Console.WriteLine(connect ? "Connected" : "Disconnected");
            }
        }

        private void InitializeSocket()
        {
            socket = new SocketIO("http://localhost:3000");

            socket.On("time-received", (response) => {
                int timeInSeconds = response.GetValue<int>();
                Dispatcher.Invoke(() => {
                    StartTimer(timeInSeconds);
                });
            });

            socket.On("uid-authorized", (data) => {
                Dispatcher.Invoke(() => {
                    AuthStatusText("Соединение установлено");
                    UpdateConnectionStatusIcon(true);
                });
            });

            socket.On("continue-work", (data) =>
            {
                HandleAppMinimize();
            });

            socket.On("finish-work", (data) =>
            {
                HandleAppFinish();
            });

            //socket.OnDisconnected += (sender, e) => {
            //    Dispatcher.Invoke(() => {
            //        AuthStatusText("Соединение разорвано");
            //        UpdateConnectionStatusIcon(false);
            //    });
            //};

            socket.ConnectAsync();
        }

        private void HandleAppMinimize()
        {
            this.Dispatcher.Invoke(() =>
            {
                UnlockKeyboard();
                this.WindowState = WindowState.Minimized;
            });
        }

        private void HandleAppFinish()
        {
            this.Dispatcher.Invoke(() =>
            {
                UnlockKeyboard();
                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
            });
        }

        private void AuthStatusText(string text)
        {
            AuthText.Text = text;
        }

        private void UpdateConnectionStatusIcon(bool isConnected)
        {
            if (isConnected)
            {
                ConnectionStatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.SmartphoneLink;
            }
            else
            {
                ConnectionStatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.SmartphoneLinkOff;
            }
        }

        private void StartTimer(int timeInSeconds)
        {
            if (timer != null && timer.IsEnabled)
            {
                timer.Stop();
            }

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            int remainingTime = timeInSeconds;

            timer.Tick += (sender, e) => {
                UpdateTextBlock($"{TimeSpan.FromSeconds(remainingTime).ToString("hh':'mm':'ss")}");
                remainingTime--;

                if (remainingTime < 0)
                {
                    this.Show();
                    this.Activate();
                    timer.Stop();
                    UpdateTextBlock("Время вышло!");

                    socket.EmitAsync("timer-finished");

                    currentQuestionIndex = 0;
                    correctAnswers = 0;
                    ShowQuestion(currentQuestionIndex);

                    WindowState = WindowState.Maximized;

                    Topmost = true;
                }
            };

            timer.Start();
        }

        private void UpdateTextBlock(string text)
        {
            textBlock.Text = text;
        }

        protected override void OnClosed(EventArgs e)
        {
            ConnectToServer(false);
            base.OnClosed(e);
        }

        public MainWindow(IntPtr hWnd) : this()
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            helper.Owner = hWnd;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Topmost = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;

            HideToTray();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception)
            {
            }
        }

        private void MinimizeBtn(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseBtn(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void SaveUidToServerAsync(string uid)
        {
            using (HttpClient client = new HttpClient())
            {
                string serverUrl = "http://localhost:3000/saveUid";
                var content = new StringContent($"{{ \"uid\": \"{uid}\" }}", Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(serverUrl, content);
                    response.EnsureSuccessStatusCode();

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("Ответ сервера: " + responseContent);

                    if (responseContent == "UID уже существует")
                    {
                        Console.WriteLine("UID уже был сохранен на сервере");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine("Ошибка HTTP запроса: " + ex.Message);
                }
            }
        }

        private async void PackIcon_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

            Clipboard.SetText(uid);

            copiedTextBlock.Visibility = Visibility.Visible;

            await Task.Delay(1000);
            copiedTextBlock.Visibility = Visibility.Collapsed;

        }




    }
}
