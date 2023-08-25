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

        public MainWindow()
        {
            InitializeComponent();
            Initialize();
            Closing += Window_Closing;

            uid = UIDGenerator.GenerateUID();

            uidText.Text = "UID: " + uid;

            SaveUidToServerAsync(uid);
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

        private void ShowQuestion(int index)
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
                answerStackPanel.Children.Clear();
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

            //socket.OnDisconnected += (sender, e) => {
            //    Dispatcher.Invoke(() => {
            //        AuthStatusText("Соединение разорвано");
            //        UpdateConnectionStatusIcon(false);
            //    });
            //};

            socket.ConnectAsync();
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
                string serverUrl = "http://localhost:3000/saveUid"; // Замените на ваш адрес сервера
                var content = new StringContent($"{{ \"uid\": \"{uid}\" }}", Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(serverUrl, content);
                    response.EnsureSuccessStatusCode(); // Проверка на успешный статус ответа

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
